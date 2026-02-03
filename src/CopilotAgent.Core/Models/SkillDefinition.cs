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

    /// <summary>Unique name for this skill</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of what this skill does</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>File path to the skill definition (e.g., SKILL.md)</summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Full content of the skill definition</summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>Source type of the skill</summary>
    [JsonPropertyName("source")]
    public SkillSource Source { get; set; } = SkillSource.Personal;

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