using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing agent skills (SKILL.md files)
/// </summary>
public interface ISkillsService
{
    /// <summary>Event raised when skills are reloaded</summary>
    event EventHandler? SkillsReloaded;

    /// <summary>Get all discovered skills</summary>
    IReadOnlyList<SkillDefinition> GetSkills();

    /// <summary>Get skills for a specific folder path</summary>
    IReadOnlyList<SkillDefinition> GetSkillsForPath(string? folderPath);

    /// <summary>Get a skill by its unique ID</summary>
    SkillDefinition? GetSkill(string skillId);

    /// <summary>Get enabled skills for a session</summary>
    IReadOnlyList<SkillDefinition> GetEnabledSkills(Session session);

    /// <summary>Enable or disable a skill for a session</summary>
    void SetSkillEnabled(Session session, string skillId, bool enabled);

    /// <summary>Generate system prompt content from enabled skills</summary>
    string GenerateSkillPrompt(Session session);

    /// <summary>Scan and reload all skills from configured folders</summary>
    Task ScanSkillsAsync();

    /// <summary>Get personal skills folder path</summary>
    string GetPersonalSkillsFolder();

    /// <summary>Create a new skill file</summary>
    Task<SkillDefinition?> CreateSkillAsync(string name, string description, string content, string? targetFolder = null);
}