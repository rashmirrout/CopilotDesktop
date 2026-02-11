namespace CopilotAgent.Panel.Models;

/// <summary>
/// Records a tool invocation by an agent for audit and UI display.
/// </summary>
public sealed record ToolCallRecord(
    string ToolName,
    string Input,
    string? Output,
    bool Succeeded,
    TimeSpan Duration);