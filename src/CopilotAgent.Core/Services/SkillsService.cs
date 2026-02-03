using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Implementation of skills service for SKILL.md management
/// </summary>
public class SkillsService : ISkillsService
{
    private readonly ILogger<SkillsService> _logger;
    private readonly List<SkillDefinition> _skills = new();
    private readonly object _skillsLock = new();
    private readonly string _personalSkillsFolder;

    public event EventHandler? SkillsReloaded;

    public SkillsService(ILogger<SkillsService> logger)
    {
        _logger = logger;
        
        // Set up personal skills folder
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _personalSkillsFolder = Path.Combine(userProfile, "CopilotAgent", "Skills");
        
        // Ensure folder exists
        Directory.CreateDirectory(_personalSkillsFolder);
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
                // Return only personal and built-in skills
                return _skills.Where(s => s.Source == SkillSource.Personal || s.Source == SkillSource.BuiltIn)
                    .ToList().AsReadOnly();
            }

            // Return personal, built-in, and repository skills that match the path
            return _skills.Where(s => 
                s.Source == SkillSource.Personal || 
                s.Source == SkillSource.BuiltIn ||
                (s.Source == SkillSource.Repository && s.FilePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase)))
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

    public IReadOnlyList<SkillDefinition> GetEnabledSkills(Session session)
    {
        if (session.EnabledSkills == null || session.EnabledSkills.Count == 0)
        {
            return Array.Empty<SkillDefinition>();
        }

        lock (_skillsLock)
        {
            return _skills.Where(s => session.EnabledSkills.Contains(s.Id)).ToList().AsReadOnly();
        }
    }

    public void SetSkillEnabled(Session session, string skillId, bool enabled)
    {
        session.EnabledSkills ??= new List<string>();

        if (enabled)
        {
            if (!session.EnabledSkills.Contains(skillId))
            {
                session.EnabledSkills.Add(skillId);
            }
        }
        else
        {
            session.EnabledSkills.Remove(skillId);
        }
    }

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
            sb.AppendLine($"## {skill.Name}");
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
            // Scan personal skills folder
            await ScanFolderAsync(_personalSkillsFolder, SkillSource.Personal, newSkills);

            // Add built-in skills
            AddBuiltInSkills(newSkills);

            lock (_skillsLock)
            {
                _skills.Clear();
                _skills.AddRange(newSkills);
            }

            _logger.LogInformation("Scanned {Count} skills", newSkills.Count);
            SkillsReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan skills");
        }
    }

    public string GetPersonalSkillsFolder() => _personalSkillsFolder;

    public async Task<SkillDefinition?> CreateSkillAsync(string name, string description, string content, string? targetFolder = null)
    {
        try
        {
            var folder = targetFolder ?? _personalSkillsFolder;
            Directory.CreateDirectory(folder);

            // Sanitize name for file
            var fileName = SanitizeFileName(name) + ".md";
            var filePath = Path.Combine(folder, fileName);

            // Build SKILL.md content
            var fullContent = BuildSkillFileContent(name, description, content);

            await File.WriteAllTextAsync(filePath, fullContent);

            var skill = new SkillDefinition
            {
                Id = GenerateSkillId(filePath),
                Name = name,
                Description = description,
                FilePath = filePath,
                Content = content,
                Source = folder == _personalSkillsFolder ? SkillSource.Personal : SkillSource.Repository,
                LastModified = DateTime.UtcNow
            };

            lock (_skillsLock)
            {
                _skills.Add(skill);
            }

            _logger.LogInformation("Created skill: {Name} at {Path}", name, filePath);
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
            return;
        }

        try
        {
            // Find all SKILL.md files and *.skill.md files
            var patterns = new[] { "SKILL.md", "*.skill.md", "skill.md" };
            
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
                {
                    try
                    {
                        var skill = await ParseSkillFileAsync(file, source);
                        if (skill != null)
                        {
                            skills.Add(skill);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse skill file: {File}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan folder: {Folder}", folder);
        }
    }

    private async Task<SkillDefinition?> ParseSkillFileAsync(string filePath, SkillSource source)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var fileInfo = new FileInfo(filePath);

        // Parse metadata from YAML front matter or first heading
        var (name, description, tags, version, author) = ParseSkillMetadata(content, filePath);

        // Extract the main content (without front matter)
        var mainContent = ExtractMainContent(content);

        return new SkillDefinition
        {
            Id = GenerateSkillId(filePath),
            Name = name,
            Description = description,
            FilePath = filePath,
            Content = mainContent,
            Source = source,
            Tags = tags,
            Version = version,
            Author = author,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    private (string name, string description, List<string>? tags, string? version, string? author) ParseSkillMetadata(string content, string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
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
                            case "name" or "title":
                                name = value;
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
                name = headingMatch.Groups[1].Value.Trim();
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

        return (name, description, tags, version, author);
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
        // Add a default coding assistant skill
        skills.Add(new SkillDefinition
        {
            Id = "builtin-coding-assistant",
            Name = "Coding Assistant",
            Description = "General coding assistance with best practices",
            Content = @"You are an expert software developer. Follow these guidelines:
- Write clean, maintainable code following SOLID principles
- Include proper error handling and logging
- Write unit tests when applicable
- Use appropriate design patterns
- Document complex logic with comments
- Prefer readability over cleverness",
            Source = SkillSource.BuiltIn,
            LastModified = DateTime.UtcNow
        });

        skills.Add(new SkillDefinition
        {
            Id = "builtin-code-review",
            Name = "Code Reviewer",
            Description = "Thorough code review with improvement suggestions",
            Content = @"When reviewing code, analyze for:
- **Security**: Check for vulnerabilities, input validation, and secure practices
- **Performance**: Identify bottlenecks, unnecessary allocations, and optimization opportunities
- **Maintainability**: Assess code structure, naming, and documentation
- **Testing**: Evaluate test coverage and test quality
- **Best Practices**: Check adherence to language/framework conventions

Provide specific, actionable feedback with examples.",
            Source = SkillSource.BuiltIn,
            LastModified = DateTime.UtcNow
        });

        skills.Add(new SkillDefinition
        {
            Id = "builtin-debugging",
            Name = "Debugging Expert",
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
            LastModified = DateTime.UtcNow
        });
    }

    private static string GenerateSkillId(string filePath)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
        return Convert.ToBase64String(hash).Substring(0, 16).Replace("+", "-").Replace("/", "_");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Replace(' ', '-').ToLower();
    }

    private static string BuildSkillFileContent(string name, string description, string content)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {name}");
        if (!string.IsNullOrEmpty(description))
        {
            sb.AppendLine($"description: {description}");
        }
        sb.AppendLine($"version: 1.0.0");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {name}");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(description))
        {
            sb.AppendLine(description);
            sb.AppendLine();
        }
        sb.AppendLine(content);
        return sb.ToString();
    }
}