namespace CopilotAgent.Panel.Domain.Enums;

/// <summary>
/// Classifies messages in the discussion transcript.
/// </summary>
public enum PanelMessageType
{
    UserMessage,
    Clarification,
    TopicOfDiscussion,
    PanelistArgument,
    ModerationNote,
    ToolCallResult,
    Commentary,
    Synthesis,
    SystemNotification,
    Error
}