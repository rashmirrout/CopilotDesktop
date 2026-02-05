using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Implementation of skills service for managing agent skills.
/// Supports both SDK format (skill.json) and markdown format (SKILL.md).
/// Scans both ~/CopilotAgent/Skills and ~/.copilot/skills directories.
/// </summary>
public class SkillsService : ISkillsService
{
    private readonly ILogger<SkillsService> _logger;
    private readonly List<SkillDefinition> _skills = new();
    private readonly object _skillsLock = new();
    private readonly string _personalSkillsFolder;
    private readonly string _sdkSkillsFolder;

    public event EventHandler? SkillsReloaded;

    public SkillsService(ILogger<SkillsService> logger)
    {
        _logger = logger;
        
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        // Personal skills folder (app-specific)
        _personalSkillsFolder = Path.Combine(userProfile, "CopilotAgent", "Skills");
        
        // SDK skills folder (shared with Copilot CLI/SDK)
        _sdkSkillsFolder = Path.Combine(userProfile, ".copilot", "skills");
        
        // Ensure folders exist
        Directory.CreateDirectory(_personalSkillsFolder);
        // Don't create SDK folder - let user or SDK create it
    }

    public IReadOnlyList<SkillDefinition> GetSkills()
    {
        lock (_skillsLock)
        {
            return _skills.ToList().AsReadOnly();
        }
    }

    public IReadOnlyList<SkillDefinition> GetSkillsForPath(string? folderPath)
    {
        lock (_skillsLock)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                // Return only personal, SDK, and built-in skills
                return _skills.Where(s => 
                    s.Source == SkillSource.Personal || 
                    s.Source == SkillSource.BuiltIn)
                    .ToList().AsReadOnly();
            }

            // Return personal, built-in, and repository skills that match the path
            return _skills.Where(s => 
                s.Source == SkillSource.Personal || 
                s.Source == SkillSource.BuiltIn ||
                (s.Source == SkillSource.Repository && 
                 s.FilePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase)))
                .ToList().AsReadOnly();
        }
    }

    public SkillDefinition? GetSkill(string skillId)
    {
        lock (_skillsLock)
        {
            return _skills.FirstOrDefault(s => s.Id == skillId);
        }
    }

    public SkillDefinition? GetSkillByName(string skillName)
    {
        lock (_skillsLock)
        {
            return _skills.FirstOrDefault(s => 
                s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<SkillDefinition> GetEnabledSkills(Session session)
    {
        // A skill is enabled if it's NOT in the DisabledSkills list
        var disabledSkills = session.DisabledSkills ?? new List<string>();
        
        lock (_skillsLock)
        {
            return _skills
                .Where(s => !disabledSkills.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
    }

    public IReadOnlyList<SkillDefinition> GetDisabledSkills(Session session)
    {
        // A skill is disabled if it IS in the DisabledSkills list
        var disabledSkills = session.DisabledSkills ?? new List<string>();
        
        lock (_skillsLock)
        {
            return _skills
                .Where(s => disabledSkills.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
    }

    public bool IsSkillEnabled(Session session, string skillName)
    {
        // null DisabledSkills = ALL skills are disabled (default for new sessions)
        // Empty list = ALL skills are enabled
        // Non-empty list = specific skills disabled
        if (session.DisabledSkills == null)
        {
            return false; // All disabled by default when not initialized
        }
        
        return !session.DisabledSkills.Contains(skillName, StringComparer.OrdinalIgnoreCase);
    }

    public bool SetSkillEnabled(Session session, string skillName, bool enabled)
    {
        session.DisabledSkills ??= new List<string>();
        
        var existingIndex = session.DisabledSkills
            .FindIndex(s => s.Equals(skillName, StringComparison.OrdinalIgnoreCase));
        
        if (enabled)
        {
            // Enable = remove from disabled list
            if (existingIndex >= 0)
            {
                session.DisabledSkills.RemoveAt(existingIndex);
                _logger.LogInformation("Enabled skill '{SkillName}' for session {SessionId}", 
                    skillName, session.SessionId);
                return true;
            }
            return false; // Already enabled
        }
        else
        {
            // Disable = add to disabled list
            if (existingIndex < 0)
            {
                session.DisabledSkills.Add(skillName);
                _logger.LogInformation("Disabled skill '{SkillName}' for session {SessionId}", 
                    skillName, session.SessionId);
                return true;
            }
            return false; // Already disabled
        }
    }

    public void InitializeSessionDisabledSkills(Session session)
    {
        // Initialize with ALL skill names disabled
        lock (_skillsLock)
        {
            session.DisabledSkills = _skills
                .Select(s => s.Name)
                .ToList();
        }
        
        _logger.LogInformation("Initialized session {SessionId} with {Count} disabled skills (all disabled by default)", 
            session.SessionId, session.DisabledSkills.Count);
    }

    public List<string> GetDisabledSkillNames(Session session)
    {
        // Return the disabled skills list for SDK config
        // If null, return all skill names (all disabled by default)
        if (session.DisabledSkills == null)
        {
            lock (_skillsLock)
            {
                return _skills.Select(s => s.Name).ToList();
            }
        }
        
        return session.DisabledSkills.ToList();
    }

    public List<string> GetSkillDirectories()
    {
        var directories = new List<string>();
        
        // Add personal skills folder if it exists
        if (Directory.Exists(_personalSkillsFolder))
        {
            directories.Add(_personalSkillsFolder);
        }
        
        // Add SDK skills folder if it exists
        if (Directory.Exists(_sdkSkillsFolder))
        {
            directories.Add(_sdkSkillsFolder);
        }
        
        return directories;
    }

    [Obsolete("SDK handles skill prompt injection. This is kept for backward compatibility.")]
    public string GenerateSkillPrompt(Session session)
    {
        var enabledSkills = GetEnabledSkills(session);
        if (enabledSkills.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Active Skills");
        sb.AppendLine();

        foreach (var skill in enabledSkills)
        {
            sb.AppendLine($"## {skill.EffectiveDisplayName}");
            if (!string.IsNullOrEmpty(skill.Description))
            {
                sb.AppendLine($"_{skill.Description}_");
                sb.AppendLine();
            }
            sb.AppendLine(skill.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task ScanSkillsAsync()
    {
        var newSkills = new List<SkillDefinition>();

        try
        {
            _logger.LogInformation("Scanning skills from {Personal} and {Sdk}", 
                _personalSkillsFolder, _sdkSkillsFolder);

            // Scan personal skills folder
            await ScanFolderAsync(_personalSkillsFolder, SkillSource.Personal, newSkills);
            
            // Scan SDK skills folder
            await ScanFolderAsync(_sdkSkillsFolder, SkillSource.Personal, newSkills);

            // Add built-in skills
            AddBuiltInSkills(newSkills);

            lock (_skillsLock)
            {
                _skills.Clear();
                _skills.AddRange(newSkills);
            }

            _logger.LogInformation("Scanned {Count} skills ({SdkFormat} SDK format, {MdFormat} markdown)", 
                newSkills.Count, 
                newSkills.Count(s => s.Format == SkillFormat.SdkJson),
                newSkills.Count(s => s.Format == SkillFormat.Markdown));
            
            SkillsReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan skills");
        }
    }

    public string GetPersonalSkillsFolder() => _personalSkillsFolder;
    
    public string GetSdkSkillsFolder() => _sdkSkillsFolder;

    public async Task<SkillDefinition?> CreateSkillAsync(string name, string description, string content, string? targetFolder = null)
    {
        try
        {
            var folder = targetFolder ?? _personalSkillsFolder;
            Directory.CreateDirectory(folder);

            // Create SDK format skill
            var skillDirName = SanitizeFileName(name);
            var skillDir = Path.Combine(folder, skillDirName);
            Directory.CreateDirectory(skillDir);
            
            // Create prompts directory
            var promptsDir = Path.Combine(skillDir, "prompts");
            Directory.CreateDirectory(promptsDir);
            
            // Write prompt file
            var promptFileName = "main.md";
            var promptPath = Path.Combine(promptsDir, promptFileName);
            await File.WriteAllTextAsync(promptPath, content);
            
            // Write skill.json manifest
            var manifest = new SkillJsonManifest
            {
                Name = skillDirName,
                DisplayName = name,
                Description = description,
                Version = "1.0.0",
                Prompts = new List<string> { $"prompts/{promptFileName}" }
            };
            
            var jsonPath = Path.Combine(skillDir, "skill.json");
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(manifest, jsonOptions));

            var skill = new SkillDefinition
            {
                Id = GenerateSkillId(skillDir),
                Name = skillDirName,
                DisplayName = name,
                Description = description,
                FilePath = skillDir,
                Content = content,
                Source = folder == _personalSkillsFolder ? SkillSource.Personal : SkillSource.Repository,
                Format = SkillFormat.SdkJson,
                Version = "1.0.0",
                Prompts = manifest.Prompts,
                LastModified = DateTime.UtcNow
            };

            lock (_skillsLock)
            {
                _skills.Add(skill);
            }

            _logger.LogInformation("Created skill: {Name} at {Path}", name, skillDir);
            return skill;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create skill: {Name}", name);
            return null;
        }
    }

    private async Task ScanFolderAsync(string folder, SkillSource source, List<SkillDefinition> skills)
    {
        if (!Directory.Exists(folder))
        {
            _logger.LogDebug("Skills folder does not exist: {Folder}", folder);
            return;
        }

        try
        {
            // First, scan for SDK format skills (directories with skill.json)
            foreach (var dir in Directory.EnumerateDirectories(folder))
            {
                var skillJsonPath = Path.Combine(dir, "skill.json");
                if (File.Exists(skillJsonPath))
                {
                    try
                    {
                        var skill = await ParseSdkSkillAsync(dir, source);
                        if (skill != null)
                        {
                            skills.Add(skill);
                            _logger.LogDebug("Found SDK skill: {Name} at {Path}", skill.Name, dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse SDK skill at: {Dir}", dir);
                    }
                }
            }

            // Then scan for markdown format skills (SKILL.md, *.skill.md)
            var patterns = new[] { "SKILL.md", "*.skill.md", "skill.md" };
            
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    // Skip if this file is inside an SDK skill directory (already processed)
                    var fileDir = Path.GetDirectoryName(file);
                    if (fileDir != null && File.Exists(Path.Combine(fileDir, "skill.json")))
                    {
                        continue;
                    }
                    
                    try
                    {
                        var skill = await ParseMarkdownSkillAsync(file, source);
                        if (skill != null)
                        {
                            skills.Add(skill);
                            _logger.LogDebug("Found markdown skill: {Name} at {Path}", skill.Name, file);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse markdown skill: {File}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan folder: {Folder}", folder);
        }
    }

    private async Task<SkillDefinition?> ParseSdkSkillAsync(string skillDir, SkillSource source)
    {
        var jsonPath = Path.Combine(skillDir, "skill.json");
        var json = await File.ReadAllTextAsync(jsonPath);
        
        var manifest = JsonSerializer.Deserialize<SkillJsonManifest>(json);
        if (manifest == null || string.IsNullOrEmpty(manifest.Name))
        {
            _logger.LogWarning("Invalid skill.json at {Path}: missing name", jsonPath);
            return null;
        }

        // Load and combine prompt content
        var contentBuilder = new StringBuilder();
        if (manifest.Prompts != null)
        {
            foreach (var promptFile in manifest.Prompts)
            {
                var promptPath = Path.Combine(skillDir, promptFile);
                if (File.Exists(promptPath))
                {
                    var promptContent = await File.ReadAllTextAsync(promptPath);
                    contentBuilder.AppendLine(promptContent);
                    contentBuilder.AppendLine();
                }
                else
                {
                    _logger.LogWarning("Prompt file not found: {Path}", promptPath);
                }
            }
        }

        var dirInfo = new DirectoryInfo(skillDir);
        
        return new SkillDefinition
        {
            Id = GenerateSkillId(skillDir),
            Name = manifest.Name,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description ?? string.Empty,
            FilePath = skillDir,
            Content = contentBuilder.ToString(),
            Source = source,
            Format = SkillFormat.SdkJson,
            Tags = manifest.Tags,
            Version = manifest.Version,
            Author = manifest.Author,
            Prompts = manifest.Prompts,
            Tools = manifest.Tools,
            LastModified = dirInfo.LastWriteTimeUtc
        };
    }

    private async Task<SkillDefinition?> ParseMarkdownSkillAsync(string filePath, SkillSource source)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var fileInfo = new FileInfo(filePath);

        // Parse metadata from YAML front matter or first heading
        var (name, displayName, description, tags, version, author) = ParseSkillMetadata(content, filePath);

        // Extract the main content (without front matter)
        var mainContent = ExtractMainContent(content);

        return new SkillDefinition
        {
            Id = GenerateSkillId(filePath),
            Name = name,
            DisplayName = displayName,
            Description = description,
            FilePath = filePath,
            Content = mainContent,
            Source = source,
            Format = SkillFormat.Markdown,
            Tags = tags,
            Version = version,
            Author = author,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    private (string name, string? displayName, string description, List<string>? tags, string? version, string? author) 
        ParseSkillMetadata(string content, string filePath)
    {
        // Generate a technical name from the file path
        string name = GenerateSkillNameFromPath(filePath);
        string? displayName = null;
        string description = string.Empty;
        List<string>? tags = null;
        string? version = null;
        string? author = null;

        // Check for YAML front matter
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3);
            if (endIndex > 0)
            {
                var frontMatter = content.Substring(3, endIndex - 3);
                
                // Parse simple YAML-like front matter
                foreach (var line in frontMatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = line.Substring(0, colonIndex).Trim().ToLower();
                        var value = line.Substring(colonIndex + 1).Trim();

                        switch (key)
                        {
                            case "name":
                                name = SanitizeSkillName(value);
                                displayName = value;
                                break;
                            case "title":
                            case "displayname":
                                displayName = value;
                                break;
                            case "description":
                                description = value;
                                break;
                            case "version":
                                version = value;
                                break;
                            case "author":
                                author = value;
                                break;
                            case "tags":
                                tags = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .ToList();
                                break;
                        }
                    }
                }
            }
        }
        else
        {
            // Try to extract name from first heading
            var headingMatch = Regex.Match(content, @"^#\s+(.+?)$", RegexOptions.Multiline);
            if (headingMatch.Success)
            {
                var headingText = headingMatch.Groups[1].Value.Trim();
                displayName = headingText;
                name = SanitizeSkillName(headingText);
            }

            // Try to extract description from first paragraph after heading
            var descMatch = Regex.Match(content, @"^#[^\n]+\n+([^#\n][^\n]+)", RegexOptions.Multiline);
            if (descMatch.Success)
            {
                description = descMatch.Groups[1].Value.Trim();
                // Limit description length
                if (description.Length > 200)
                {
                    description = description.Substring(0, 197) + "...";
                }
            }
        }

        return (name, displayName, description, tags, version, author);
    }

    private string ExtractMainContent(string content)
    {
        // Remove YAML front matter if present
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3);
            if (endIndex > 0)
            {
                return content.Substring(endIndex + 3).TrimStart();
            }
        }
        return content;
    }

    private void AddBuiltInSkills(List<SkillDefinition> skills)
    {
        // Note: Built-in skills are kept for backward compatibility but won't be passed to SDK
        // They only work when using the deprecated GenerateSkillPrompt method
        
        skills.Add(new SkillDefinition
        {
            Id = "builtin-coding-assistant",
            Name = "coding-assistant",
            DisplayName = "Coding Assistant",
            Description = "General coding assistance with best practices",
            Content = @"You are an expert software developer. Follow these guidelines:
- Write clean, maintainable code following SOLID principles
- Include proper error handling and logging
- Write unit tests when applicable
- Use appropriate design patterns
- Document complex logic with comments
- Prefer readability over cleverness",
            Source = SkillSource.BuiltIn,
            Format = SkillFormat.Markdown,
            LastModified = DateTime.UtcNow
        });

        skills.Add(new SkillDefinition
        {
            Id = "builtin-code-review",
            Name = "code-reviewer",
            DisplayName = "Code Reviewer",
            Description = "Thorough code review with improvement suggestions",
            Content = @"When reviewing code, analyze for:
- **Security**: Check for vulnerabilities, input validation, and secure practices
- **Performance**: Identify bottlenecks, unnecessary allocations, and optimization opportunities
- **Maintainability**: Assess code structure, naming, and documentation
- **Testing**: Evaluate test coverage and test quality
- **Best Practices**: Check adherence to language/framework conventions

Provide specific, actionable feedback with examples.",
            Source = SkillSource.BuiltIn,
            Format = SkillFormat.Markdown,
            LastModified = DateTime.UtcNow
        });

        skills.Add(new SkillDefinition
        {
            Id = "builtin-debugging",
            Name = "debugging-expert",
            DisplayName = "Debugging Expert",
            Description = "Systematic approach to debugging issues",
            Content = @"When debugging issues:
1. **Reproduce**: First confirm the issue can be reproduced
2. **Isolate**: Narrow down the problem area using binary search
3. **Hypothesize**: Form theories about the root cause
4. **Test**: Verify hypotheses with minimal changes
5. **Fix**: Implement the fix and verify it resolves the issue
6. **Prevent**: Consider how to prevent similar issues

Ask clarifying questions about error messages, stack traces, and recent changes.",
            Source = SkillSource.BuiltIn,
            Format = SkillFormat.Markdown,
            LastModified = DateTime.UtcNow
        });
    }

    private static string GenerateSkillId(string path)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToBase64String(hash).Substring(0, 16).Replace("+", "-").Replace("/", "_");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Replace(' ', '-').ToLower();
    }

    private static string SanitizeSkillName(string name)
    {
        // Create a valid skill name: lowercase, dashes instead of spaces, alphanumeric only
        var sanitized = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-"); // Collapse multiple dashes
        return sanitized.Trim('-');
    }

    private static string GenerateSkillNameFromPath(string filePath)
    {
        // Generate a skill name from file path
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        
        // Remove common suffixes
        fileName = Regex.Replace(fileName, @"\.skill$", "", RegexOptions.IgnoreCase);
        
        // If it's just "SKILL" or "skill", use parent directory name
        if (fileName.Equals("skill", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                fileName = new DirectoryInfo(dir).Name;
            }
        }
        
        return SanitizeSkillName(fileName);
    }
}