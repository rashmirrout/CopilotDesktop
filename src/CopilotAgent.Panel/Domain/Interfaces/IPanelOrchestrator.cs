using CopilotAgent.Panel.Domain.Enums;
using CopilotAgent.Panel.Domain.Events;
using CopilotAgent.Panel.Domain.ValueObjects;
using CopilotAgent.Panel.Models;

namespace CopilotAgent.Panel.Domain.Interfaces;

/// <summary>
/// Primary interface for Panel Discussion functionality.
/// The ViewModel interacts exclusively through this interface.
/// </summary>
public interface IPanelOrchestrator
{
    PanelSessionId? ActiveSessionId { get; }
    PanelPhase CurrentPhase { get; }
    IObservable<PanelEvent> Events { get; }

    Task<PanelSessionId> StartAsync(string userPrompt, PanelSettings settings, CancellationToken ct = default);
    Task SendUserMessageAsync(string message, CancellationToken ct = default);
    Task ApproveAndStartPanelAsync(CancellationToken ct = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task ResetAsync();
}