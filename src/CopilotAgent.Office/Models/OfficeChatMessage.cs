namespace CopilotAgent.Office.Models;

/// <summary>
/// Role of a message in the Office chat plane.
/// </summary>
public enum OfficeChatRole
{
    /// <summary>Message from the user.</summary>
    User,

    /// <summary>Message from the Manager agent.</summary>
    Manager,

    /// <summary>Message from an Assistant agent.</summary>
    Assistant,

    /// <summary>System-level informational message.</summary>
    System,

    /// <summary>Iteration container header (visual grouping, not a real message).</summary>
    IterationHeader,

    /// <summary>Rest countdown display.</summary>
    RestCountdown
}

/// <summary>
/// A single message displayed in the Office chat plane.
/// </summary>
public sealed class OfficeChatMessage
{
    /// <summary>Unique message ID.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Role of the message sender.</summary>
    public OfficeChatRole Role { get; init; }

    /// <summary>Display name of the sender.</summary>
    public string SenderName { get; init; } = string.Empty;

    /// <summary>The message content (Markdown supported).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Timestamp when the message was created.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Iteration number this message belongs to (0 for pre-iteration messages).</summary>
    public int IterationNumber { get; init; }

    /// <summary>Assistant index (for Assistant role messages).</summary>
    public int? AssistantIndex { get; init; }

    /// <summary>Color accent for this message (hex string).</summary>
    public string AccentColor { get; init; } = "#888888";

    /// <summary>Whether this message's content can be collapsed.</summary>
    public bool IsCollapsible { get; init; }

    /// <summary>Whether the content is currently collapsed in the UI.</summary>
    public bool IsCollapsed { get; set; }

    /// <summary>Whether this is an iteration container header.</summary>
    public bool IsIterationContainer => Role == OfficeChatRole.IterationHeader;

    /// <summary>Whether the iteration container is expanded (for IterationHeader role).</summary>
    public bool ContainerExpanded { get; set; } = true;

    /// <summary>Task ID if this message is associated with a specific task.</summary>
    public string? TaskId { get; init; }
}