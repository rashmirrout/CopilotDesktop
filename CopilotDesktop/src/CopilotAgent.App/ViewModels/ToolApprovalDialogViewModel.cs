using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the tool approval modal dialog.
/// Displays tool details and allows user to approve/deny with scope selection.
/// </summary>
public partial class ToolApprovalDialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<ToolApprovalResponse> _responseSource;
    
    [ObservableProperty]
    private ToolApprovalRequest _request = null!;
    
    [ObservableProperty]
    private string _toolName = string.Empty;
    
    [ObservableProperty]
    private string _toolArgsDisplay = string.Empty;
    
    [ObservableProperty]
    private string _workingDirectory = string.Empty;
    
    [ObservableProperty]
    private ToolRiskLevel _riskLevel = ToolRiskLevel.Medium;
    
    [ObservableProperty]
    private string _riskLevelDisplay = string.Empty;
    
    [ObservableProperty]
    private string _riskColor = "#FFA726";
    
    [ObservableProperty]
    private string _description = string.Empty;
    
    [ObservableProperty]
    private bool _rememberDecision;
    
    [ObservableProperty]
    private string _timestamp = string.Empty;
    
    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler<bool>? RequestClose;
    
    public ToolApprovalDialogViewModel(ToolApprovalRequest request, TaskCompletionSource<ToolApprovalResponse> responseSource)
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
        Description = request.Description ?? GetDefaultDescription(request.ToolName);
        Timestamp = request.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        
        // Format tool arguments for display
        if (request.ToolArgs != null)
        {
            try
            {
                if (request.ToolArgs is string strArgs)
                {
                    ToolArgsDisplay = strArgs;
                }
                else
                {
                    ToolArgsDisplay = JsonSerializer.Serialize(request.ToolArgs, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
            }
            catch
            {
                ToolArgsDisplay = request.ToolArgs.ToString() ?? "Unable to display";
            }
        }
        else
        {
            ToolArgsDisplay = "No arguments";
        }
        
        // Set risk level display and color
        (RiskLevelDisplay, RiskColor) = RiskLevel switch
        {
            ToolRiskLevel.Low => ("Low Risk", "#4CAF50"),      // Green
            ToolRiskLevel.Medium => ("Medium Risk", "#FFA726"), // Orange
            ToolRiskLevel.High => ("High Risk", "#EF5350"),     // Red
            ToolRiskLevel.Critical => ("⚠️ Critical Risk", "#D32F2F"), // Dark Red
            _ => ("Unknown Risk", "#9E9E9E")
        };
    }
    
    private static string GetDefaultDescription(string toolName)
    {
        var lowerName = toolName.ToLowerInvariant();
        
        if (lowerName.Contains("read") || lowerName.Contains("view") || lowerName.Contains("list"))
            return "This tool reads or views data without making changes.";
        
        if (lowerName.Contains("write") || lowerName.Contains("edit") || lowerName.Contains("create"))
            return "This tool will modify or create files.";
        
        if (lowerName.Contains("delete") || lowerName.Contains("remove"))
            return "This tool will delete files or data.";
        
        if (lowerName.Contains("shell") || lowerName.Contains("exec") || lowerName.Contains("run"))
            return "This tool will execute shell commands.";
        
        if (lowerName.Contains("http") || lowerName.Contains("fetch") || lowerName.Contains("curl"))
            return "This tool will make network requests.";
        
        return "This tool will perform an operation on your system.";
    }
    
    [RelayCommand]
    private void ApproveOnce()
    {
        var response = new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Once,
            RememberDecision = false
        };
        _responseSource.TrySetResult(response);
        RequestClose?.Invoke(this, true);
    }
    
    [RelayCommand]
    private void ApproveSession()
    {
        var response = new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Session,
            RememberDecision = true
        };
        _responseSource.TrySetResult(response);
        RequestClose?.Invoke(this, true);
    }
    
    [RelayCommand]
    private void ApproveAlways()
    {
        var response = new ToolApprovalResponse
        {
            Approved = true,
            Scope = ApprovalScope.Global,
            RememberDecision = true
        };
        _responseSource.TrySetResult(response);
        RequestClose?.Invoke(this, true);
    }
    
    [RelayCommand]
    private void Deny()
    {
        var response = new ToolApprovalResponse
        {
            Approved = false,
            Scope = RememberDecision ? ApprovalScope.Global : ApprovalScope.Once,
            Reason = "User denied",
            RememberDecision = RememberDecision
        };
        _responseSource.TrySetResult(response);
        RequestClose?.Invoke(this, false);
    }
    
    [RelayCommand]
    private void Cancel()
    {
        var response = new ToolApprovalResponse
        {
            Approved = false,
            Scope = ApprovalScope.Once,
            Reason = "User cancelled"
        };
        _responseSource.TrySetResult(response);
        RequestClose?.Invoke(this, false);
    }
}