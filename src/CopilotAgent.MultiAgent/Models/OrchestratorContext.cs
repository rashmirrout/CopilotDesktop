using CopilotAgent.Core.Models;

namespace CopilotAgent.MultiAgent.Models;

/// <summary>
/// Maintains conversational context across orchestration tasks,
/// enabling follow-up queries with full history awareness.
/// </summary>
public sealed class OrchestratorContext
{
    public string ContextId { get; set; } = Guid.NewGuid().ToString();
    public string OrchestratorSessionId { get; set; } = string.Empty;
    public List<OrchestrationPlan> ExecutedPlans { get; set; } = new();
    public List<ConsolidatedReport> Reports { get; set; } = new();
    public List<ChatMessage> ConversationHistory { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}