using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the Add Skill dialog with validation and file handling
/// </summary>
public partial class AddSkillDialogViewModel : ObservableObject
{
    private readonly ISkillsService _skillsService;
    private readonly ILogger<AddSkillDialogViewModel> _logger;

    [ObservableProperty]
    private string _skillName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private int _contentLength;

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    private bool _enableAfterCreate = true;

    [ObservableProperty]
    private bool _canCreate;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isCreating;

    /// <summary>
    /// The created skill definition (available after successful creation)
    /// </summary>
    public SkillDefinition? CreatedSkill { get; private set; }

    public AddSkillDialogViewModel(
        ISkillsService skillsService,
        ILogger<AddSkillDialogViewModel> logger)
    {
        _skillsService = skillsService;
        _logger = logger;

        // Set default target path
        UpdateTargetPath();
    }

    partial void OnSkillNameChanged(string value)
    {
        UpdateTargetPath();
        ValidateForm();
        
        // Auto-generate display name if empty
        if (string.IsNullOrWhiteSpace(DisplayName) && !string.IsNullOrWhiteSpace(value))
        {
            DisplayName = GenerateDisplayName(value);
        }
    }

    partial void OnDisplayNameChanged(string value)
    {
        ValidateForm();
    }

    partial void OnContentChanged(string value)
    {
        ContentLength = value?.Length ?? 0;
        ValidateForm();
    }

    partial void OnFilePathChanged(string value)
    {
        ValidateForm();
    }

    /// <summary>
    /// Load content from a file and auto-populate metadata
    /// </summary>
    public async Task LoadFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                SetValidationError("File does not exist");
                return;
            }

            FilePath = path;
            Content = await File.ReadAllTextAsync(path);

            // Auto-populate metadata from file content
            var (name, displayName, description) = ParseMetadataFromContent(Content, path);
            
            if (string.IsNullOrWhiteSpace(SkillName))
            {
                SkillName = name;
            }
            
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = displayName;
            }
            
            if (string.IsNullOrWhiteSpace(Description))
            {
                Description = description;
            }

            ClearValidationError();
            _logger.LogInformation("Loaded skill file: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load file: {Path}", path);
            SetValidationError($"Failed to load file: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate the form and return whether it's valid
    /// </summary>
    public bool Validate()
    {
        // Skill name is required
        if (string.IsNullOrWhiteSpace(SkillName))
        {
            SetValidationError("Skill name is required");
            return false;
        }

        // Validate skill name format (lowercase, hyphens, alphanumeric)
        var sanitized = SanitizeSkillName(SkillName);
        if (sanitized != SkillName)
        {
            // Auto-correct the name
            SkillName = sanitized;
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            SetValidationError("Skill name must contain at least one alphanumeric character");
            return false;
        }

        // Content is required
        if (string.IsNullOrWhiteSpace(Content))
        {
            SetValidationError("Skill content is required. Paste content or select a file.");
            return false;
        }

        // Content should have meaningful length
        if (Content.Length < 10)
        {
            SetValidationError("Skill content is too short. Please provide meaningful instructions.");
            return false;
        }

        // Check if skill name already exists
        var existing = _skillsService.GetSkillByName(SkillName);
        if (existing != null)
        {
            SetValidationError($"A skill with name '{SkillName}' already exists");
            return false;
        }

        ClearValidationError();
        return true;
    }

    /// <summary>
    /// Create the skill using the service
    /// </summary>
    public async Task<bool> CreateSkillAsync()
    {
        if (!Validate())
        {
            return false;
        }

        IsCreating = true;

        try
        {
            var effectiveDisplayName = string.IsNullOrWhiteSpace(DisplayName) 
                ? GenerateDisplayName(SkillName) 
                : DisplayName;

            CreatedSkill = await _skillsService.CreateSkillAsync(
                SkillName,
                Description,
                Content);

            if (CreatedSkill != null)
            {
                _logger.LogInformation("Created skill: {Name} at {Path}", 
                    CreatedSkill.Name, CreatedSkill.FilePath);
                return true;
            }
            else
            {
                SetValidationError("Failed to create skill. Check logs for details.");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create skill: {Name}", SkillName);
            SetValidationError($"Failed to create skill: {ex.Message}");
            return false;
        }
        finally
        {
            IsCreating = false;
        }
    }

    private void UpdateTargetPath()
    {
        var skillsFolder = _skillsService.GetPersonalSkillsFolder();
        var sanitizedName = SanitizeSkillName(SkillName);
        
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            TargetPath = Path.Combine(skillsFolder, "[skill-name]", "skill.json");
        }
        else
        {
            TargetPath = Path.Combine(skillsFolder, sanitizedName, "skill.json");
        }
    }

    private void ValidateForm()
    {
        // Basic validation for enabling the create button
        CanCreate = !string.IsNullOrWhiteSpace(SkillName) && 
                   !string.IsNullOrWhiteSpace(Content) &&
                   Content.Length >= 10;
    }

    private void SetValidationError(string message)
    {
        ValidationMessage = message;
        HasValidationErrors = true;
    }

    private void ClearValidationError()
    {
        ValidationMessage = string.Empty;
        HasValidationErrors = false;
    }

    private static string SanitizeSkillName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Create a valid skill name: lowercase, hyphens instead of spaces, alphanumeric only
        var sanitized = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        sanitized = Regex.Replace(sanitized, @"-+", "-"); // Collapse multiple hyphens
        return sanitized.Trim('-');
    }

    private static string GenerateDisplayName(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            return string.Empty;

        // Convert hyphen-separated name to Title Case
        var words = skillName.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => 
            char.ToUpper(w[0]) + (w.Length > 1 ? w[1..] : "")));
    }

    private (string name, string displayName, string description) ParseMetadataFromContent(string content, string filePath)
    {
        string name = GenerateSkillNameFromPath(filePath);
        string displayName = GenerateDisplayName(name);
        string description = string.Empty;

        // Check for YAML front matter
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3);
            if (endIndex > 0)
            {
                var frontMatter = content.Substring(3, endIndex - 3);
                
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
                if (description.Length > 200)
                {
                    description = description.Substring(0, 197) + "...";
                }
            }
        }

        return (name, displayName, description);
    }

    private static string GenerateSkillNameFromPath(string filePath)
    {
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