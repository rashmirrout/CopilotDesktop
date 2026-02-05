using System.Text.Json.Serialization;

namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents a skill/plugin that can be used by the agent
/// </summary>
public class SkillDefinition
{
    /// <summary>Unique identifier for this skill (hash of file path)</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Unique name for this skill.
    /// IMPORTANT: This must match the "name" field in skill.json for SDK integration.
    /// The SDK uses this name in DisabledSkills list to exclude skills.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Display name for UI (may differ from technical name)</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Description of what this skill does</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Path to the skill definition.
    /// - For SDK format: Directory containing skill.json
    /// - For markdown format: Path to SKILL.md file
    /// </summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Full content of the skill definition (combined prompts)</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Source type of the skill</summary>
    [JsonPropertyName("source")]
    public SkillSource Source { get; set; } = SkillSource.Personal;

    /// <summary>Format of the skill definition</summary>
    [JsonPropertyName("format")]
    public SkillFormat Format { get; set; } = SkillFormat.Markdown;

    /// <summary>Tags for categorization</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>When the skill was last modified</summary>
    [JsonPropertyName("lastModified")]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>Version of the skill</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Author of the skill</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>List of prompt file paths relative to skill directory (SDK format)</summary>
    [JsonPropertyName("prompts")]
    public List<string>? Prompts { get; set; }

    /// <summary>List of tool file paths relative to skill directory (SDK format)</summary>
    [JsonPropertyName("tools")]
    public List<string>? Tools { get; set; }

    /// <summary>Gets the effective display name (DisplayName or Name)</summary>
    [JsonIgnore]
    public string EffectiveDisplayName => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Name;
}

/// <summary>
/// Represents the skill.json manifest file from SDK format skills
/// </summary>
public class SkillJsonManifest
{
    /// <summary>Technical name of the skill (used by SDK in DisabledSkills)</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable display name</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Description of the skill</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Version of the skill</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Author of the skill</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>List of prompt file paths relative to skill directory</summary>
    [JsonPropertyName("prompts")]
    public List<string>? Prompts { get; set; }

    /// <summary>List of tool file paths relative to skill directory</summary>
    [JsonPropertyName("tools")]
    public List<string>? Tools { get; set; }

    /// <summary>Optional tags for categorization</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

/// <summary>
/// Format of the skill definition
/// </summary>
public enum SkillFormat
{
    /// <summary>Markdown format (SKILL.md with optional YAML front matter)</summary>
    Markdown,

    /// <summary>SDK format (skill.json manifest with prompts/ and tools/ directories)</summary>
    SdkJson
}

/// <summary>
/// Source of a skill definition
/// </summary>
public enum SkillSource
{
    /// <summary>Personal skills folder (%USERPROFILE%\CopilotAgent\Skills)</summary>
    Personal,
    
    /// <summary>Repository-specific skill (e.g., SKILL.md in working directory)</summary>
    Repository,
    
    /// <summary>Built-in skill</summary>
    BuiltIn,
    
    /// <summary>Downloaded from a remote source</summary>
    Remote
}