using CopilotAgent.Core.Models;

namespace CopilotAgent.Core.Services;

/// <summary>
/// Service for managing agent skills.
/// Supports both SDK format (skill.json) and markdown format (SKILL.md).
/// </summary>
public interface ISkillsService
{
    /// <summary>Event raised when skills are reloaded</summary>
    event EventHandler? SkillsReloaded;

    /// <summary>Get all discovered skills from all configured directories</summary>
    IReadOnlyList<SkillDefinition> GetSkills();

    /// <summary>Get skills for a specific folder path</summary>
    IReadOnlyList<SkillDefinition> GetSkillsForPath(string? folderPath);

    /// <summary>Get a skill by its unique ID</summary>
    SkillDefinition? GetSkill(string skillId);

    /// <summary>Get a skill by its name (used for SDK DisabledSkills matching)</summary>
    SkillDefinition? GetSkillByName(string skillName);

    /// <summary>
    /// Get enabled skills for a session.
    /// A skill is enabled if it's NOT in session.DisabledSkills.
    /// </summary>
    IReadOnlyList<SkillDefinition> GetEnabledSkills(Session session);

    /// <summary>
    /// Get disabled skills for a session.
    /// Returns skills that ARE in session.DisabledSkills.
    /// </summary>
    IReadOnlyList<SkillDefinition> GetDisabledSkills(Session session);

    /// <summary>
    /// Check if a specific skill is enabled for a session.
    /// </summary>
    bool IsSkillEnabled(Session session, string skillName);

    /// <summary>
    /// Enable or disable a skill for a session by updating DisabledSkills list.
    /// Returns true if the DisabledSkills list was modified.
    /// </summary>
    bool SetSkillEnabled(Session session, string skillName, bool enabled);

    /// <summary>
    /// Initialize session's DisabledSkills list with all discovered skill names.
    /// Call this for new sessions to ensure all skills start disabled.
    /// </summary>
    void InitializeSessionDisabledSkills(Session session);

    /// <summary>
    /// Get the list of skill names that should be passed to SDK's DisabledSkills config.
    /// </summary>
    List<string> GetDisabledSkillNames(Session session);

    /// <summary>
    /// Get the default skill directories that should be passed to SDK's SkillDirectories config.
    /// </summary>
    List<string> GetSkillDirectories();

    /// <summary>Generate system prompt content from enabled skills (deprecated - SDK handles this)</summary>
    [Obsolete("SDK handles skill prompt injection. This is kept for backward compatibility.")]
    string GenerateSkillPrompt(Session session);

    /// <summary>Scan and reload all skills from configured directories</summary>
    Task ScanSkillsAsync();

    /// <summary>Get personal skills folder path (~/CopilotAgent/Skills)</summary>
    string GetPersonalSkillsFolder();

    /// <summary>Get SDK skills folder path (~/.copilot/skills)</summary>
    string GetSdkSkillsFolder();

    /// <summary>Create a new skill in SDK format (skill.json + prompts/)</summary>
    Task<SkillDefinition?> CreateSkillAsync(string name, string description, string content, string? targetFolder = null);
}
