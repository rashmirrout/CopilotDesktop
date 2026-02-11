using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Entities;

/// <summary>
/// Immutable record representing a single message in the panel discussion.
/// Uses record semantics for value equality and immutability.
/// </summary>
public sealed record PanelMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required PanelSessionId SessionId { get; init; }
    public required Guid AuthorAgentId { get; init; }
    public required string AuthorName { get; init; }
    public required PanelAgentRole AuthorRole { get; init; }
    public required string Content { get; init; }
    public required PanelMessageType Type { get; init; }
    public Guid? InReplyTo { get; init; }
    public IReadOnlyList<ToolCallRecord>? ToolCalls { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Factory method for creating messages with required fields.
    /// </summary>
    public static PanelMessage Create(
        PanelSessionId sessionId,
        Guid authorId,
        string authorName,
        PanelAgentRole role,
        string content,
        PanelMessageType type,
        Guid? inReplyTo = null,
        IReadOnlyList<ToolCallRecord>? toolCalls = null)
    {
        return new PanelMessage
        {
            SessionId = sessionId,
            AuthorAgentId = authorId,
            AuthorName = authorName,
            AuthorRole = role,
            Content = content,
            Type = type,
            InReplyTo = inReplyTo,
            ToolCalls = toolCalls
        };
    }
}