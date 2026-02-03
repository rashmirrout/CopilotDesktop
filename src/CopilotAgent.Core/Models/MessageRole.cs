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
    Summary
}