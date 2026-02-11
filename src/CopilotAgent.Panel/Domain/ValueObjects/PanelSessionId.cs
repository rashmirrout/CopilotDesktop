namespace CopilotAgent.Panel.Domain.ValueObjects;

/// <summary>
/// Strongly-typed session identifier. Prevents accidental mixing with other GUIDs.
/// </summary>
public readonly record struct PanelSessionId(Guid Value)
{
    public static PanelSessionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N")[..8];
}