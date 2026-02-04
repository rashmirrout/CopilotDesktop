using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for inline tool approval UI embedded in chat.
/// Provides a compact approval interface within the message stream.
/// </summary>
public partial class InlineApprovalViewModel : ObservableObject
{
    private readonly TaskCompletionSource<ToolApprovalResponse> _responseSource;
    
    [ObservableProperty]
    private ToolApprovalRequest _request = null!;
    
    [ObservableProperty]
    private string _toolName = string.Empty;
    
    [ObservableProperty]
    private string _toolArgsSummary = string.Empty;
    
    [ObservableProperty]
    private ToolRiskLevel _riskLevel = ToolRiskLevel.Medium;
    
    [ObservableProperty]
    private string _riskLevelDisplay = string.Empty;
    
    [ObservableProperty]
    private string _riskColor = "#FFA726";
    
    [ObservableProperty]
    private string _riskEmoji = "⚠️";
    
    [ObservableProperty]
    private bool _isExpanded;
    
    [ObservableProperty]
    private string _fullToolArgs = string.Empty;
    
    [ObservableProperty]
    private string _workingDirectory = string.Empty;
    
    [ObservableProperty]
    private bool _isResolved;
    
    [ObservableProperty]
    private bool _wasApproved;
    
    [ObservableProperty]
    private string _resolutionMessage = string.Empty;
    
    /// <summary>
    /// Event raised when the approval has been resolved (approved or denied).
    /// </summary>
    public event EventHandler<ToolApprovalResponse>? ApprovalResolved;
    
    public InlineApprovalViewModel(ToolApprovalRequest request, TaskCompletionSource<ToolApprovalResponse> responseSource)
    {
        _request = request;
        _responseSource = responseSource;
        
        InitializeFromRequest(request);
    }
    
    private void InitializeFromRequest(ToolApprovalRequest request)
    {
        ToolName = request.ToolName;
        WorkingDirectory = request.WorkingDirectory ?? "Not specified";
        RiskLevel = request.RiskLevel;
        
        // Format tool arguments
        if (request.ToolArgs != null)
        {
            try
            {
                string fullArgs;
                if (request.ToolArgs is string strArgs)
                {
                    fullArgs = strArgs;
                }
                else
                {
                    fullArgs = JsonSerializer.Serialize(request.ToolArgs, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
                
                FullToolArgs = fullArgs;
                
                // Create summary (first line or truncated)
                var firstLine = fullArgs.Split('\n').FirstOrDefault() ?? fullArgs;
                ToolArgsSummary = firstLine.Length > 80 
                    ? firstLine.Substring(0, 77) + "..." 
                    : firstLine;
            }
            catch
            {
                FullToolArgs = request.ToolArgs.ToString() ?? "Unable to display";
                ToolArgsSummary = FullToolArgs.Length > 80 
                    ? FullToolArgs.Substring(0, 77) + "..." 
                    : FullToolArgs;
            }
        }
        else
        {
            FullToolArgs = "No arguments";
            ToolArgsSummary = "No arguments";
        }
        
        // Set risk level display
        (RiskLevelDisplay, RiskColor, RiskEmoji) = RiskLevel switch
        {
            ToolRiskLevel.Low => ("Low", "#4CAF50", "✅"),
            ToolRiskLevel.Medium => ("Medium", "#FFA726", "⚠️"),
            ToolRiskLevel.High => ("High", "#EF5350", "🔴"),
            ToolRiskLevel.Critical => ("Critical", "#D32F2F", "⛔"),
            _ => ("Unknown", "#9E9E9E", "❓")
        };
    }
    
    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
    
    [RelayCommand]
    private void ApproveOnce()
    {
        if (IsResolved) return;
        
        var response = new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Once,
            RememberDecision = false
        };
        ResolveApproval(response, "Allowed once");
    }
    
    [RelayCommand]
    private void ApproveSession()
    {
        if (IsResolved) return;
        
        var response = new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Session,
            RememberDecision = true
        };
        ResolveApproval(response, "Allowed for session");
    }
    
    [RelayCommand]
    private void ApproveAlways()
    {
        if (IsResolved) return;
        
        var response = new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Global,
            RememberDecision = true
        };
        ResolveApproval(response, "Always allowed");
    }
    
    [RelayCommand]
    private void Deny()
    {
        if (IsResolved) return;
        
        var response = new ToolApprovalResponse
        {
            Approved = false,
            Scope = ApprovalScope.Once,
            Reason = "User denied"
        };
        ResolveApproval(response, "Denied");
    }
    
    [RelayCommand]
    private void ShowDetails()
    {
        // Open modal dialog for detailed view
        _ = ShowModalDialogAsync();
    }
    
    private async Task ShowModalDialogAsync()
    {
        if (IsResolved) return;
        
        try
        {
            var response = await Views.ToolApprovalDialog.ShowDialogAsync(Request);
            ResolveApproval(response, response.Approved ? "Approved via dialog" : "Denied via dialog");
        }
        catch
        {
            // Dialog was cancelled or errored
        }
    }
    
    private void ResolveApproval(ToolApprovalResponse response, string message)
    {
        IsResolved = true;
        WasApproved = response.Approved;
        ResolutionMessage = message;
        
        _responseSource.TrySetResult(response);
        ApprovalResolved?.Invoke(this, response);
    }
    
    /// <summary>
    /// Creates an inline approval view model that auto-approves (for autonomous mode display).
    /// </summary>
    public static InlineApprovalViewModel CreateAutoApproved(ToolApprovalRequest request)
    {
        var tcs = new TaskCompletionSource<ToolApprovalResponse>();
        var vm = new InlineApprovalViewModel(request, tcs)
        {
            IsResolved = true,
            WasApproved = true,
            ResolutionMessage = "Auto-approved (autonomous mode)"
        };
        
        tcs.TrySetResult(new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Once
        });
        
        return vm;
    }
}