using System.Windows;
using CopilotAgent.App.Views;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.Services;

/// <summary>
/// UI service that bridges IToolApprovalService with WPF dialogs.
/// Subscribes to ApprovalRequested events and shows modal or inline approval UI.
/// </summary>
public class ToolApprovalUIService : IDisposable
{
    private readonly IToolApprovalService _toolApprovalService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<ToolApprovalUIService> _logger;
    private bool _isSubscribed;

    public ToolApprovalUIService(
        IToolApprovalService toolApprovalService,
        AppSettings appSettings,
        ILogger<ToolApprovalUIService> logger)
    {
        _toolApprovalService = toolApprovalService;
        _appSettings = appSettings;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the service and subscribe to approval events.
    /// Call this after the main window is loaded.
    /// </summary>
    public void Initialize()
    {
        if (_isSubscribed)
            return;

        _toolApprovalService.ApprovalRequested += OnApprovalRequested;
        _isSubscribed = true;
        
        _logger.LogInformation("ToolApprovalUIService initialized, listening for approval requests");
    }

    private async void OnApprovalRequested(object? sender, ToolApprovalRequestEventArgs e)
    {
        _logger.LogDebug("Approval UI requested for tool {Tool}", e.Request.ToolName);
        
        try
        {
            // Determine which UI mode to use
            var uiMode = _appSettings.ApprovalUIMode;
            
            ToolApprovalResponse response;
            
            switch (uiMode)
            {
                case ApprovalUIMode.Modal:
                    response = await ShowModalApprovalAsync(e.Request);
                    break;
                    
                case ApprovalUIMode.Inline:
                    // For inline mode, we still need to show something
                    // The inline UI is shown in the chat, but we need a fallback for now
                    response = await ShowModalApprovalAsync(e.Request);
                    break;
                    
                case ApprovalUIMode.Both:
                default:
                    // For "Both" mode, show modal dialog (inline is shown separately in chat)
                    response = await ShowModalApprovalAsync(e.Request);
                    break;
            }
            
            // Record the decision if user wants to remember it
            if (response.RememberDecision || response.Scope != ApprovalScope.Once)
            {
                _toolApprovalService.RecordDecision(e.Request, response);
            }
            
            // Complete the approval request
            e.ResponseSource.TrySetResult(response);
            
            _logger.LogInformation("Tool {Tool} {Decision} by user (scope: {Scope})",
                e.Request.ToolName,
                response.Approved ? "approved" : "denied",
                response.Scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing approval UI for tool {Tool}", e.Request.ToolName);
            
            // Default to denial on error
            e.ResponseSource.TrySetResult(new ToolApprovalResponse
            {
                Approved = false,
                Reason = $"Error showing approval UI: {ex.Message}"
            });
        }
    }

    private async Task<ToolApprovalResponse> ShowModalApprovalAsync(ToolApprovalRequest request)
    {
        // Ensure we're on the UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return await Application.Current.Dispatcher.InvokeAsync(async () =>
                await ShowModalApprovalAsync(request)).Result;
        }
        
        return await ToolApprovalDialog.ShowDialogAsync(request);
    }

    public void Dispose()
    {
        if (_isSubscribed)
        {
            _toolApprovalService.ApprovalRequested -= OnApprovalRequested;
            _isSubscribed = false;
        }
    }
}