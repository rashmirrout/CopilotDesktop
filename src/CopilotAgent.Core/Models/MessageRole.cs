namespace CopilotAgent.Core.Models;

/// <summary>
/// Represents the role of a message in a conversation
/// </summary>
public enum MessageRole
{
    /// <summary>User message</summary>
    User,
    
    /// <summary>Assistant/AI response</summary>
    Assistant,
    
    /// <summary>System instruction or context</summary>
    System,
    
    /// <summary>Tool invocation</summary>
    Tool,
    
    /// <summary>Tool result</summary>
    ToolResult,
    
    /// <summary>Summarized context from previous messages</summary>
    Summary,
    
    /// <summary>
    /// Agent reasoning/thinking commentary.
    /// Displays LLM's thought process during execution.
    /// Shown with faded styling and collapsed after completion.
    /// </summary>
    Reasoning,
    
    /// <summary>
    /// Collapsed group of agent work (reasoning + tool events).
    /// Expandable summary of what the agent did during a turn.
    /// </summary>
    AgentWorkSummary
}
