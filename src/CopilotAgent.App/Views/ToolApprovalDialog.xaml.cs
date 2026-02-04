using System.Windows;
using CopilotAgent.App.ViewModels;
using CopilotAgent.Core.Models;

namespace CopilotAgent.App.Views;

/// <summary>
/// Modal dialog for tool approval requests.
/// Shows tool details and allows user to approve/deny with scope selection.
/// </summary>
public partial class ToolApprovalDialog : Window
{
    private readonly ToolApprovalDialogViewModel _viewModel;
    
    public ToolApprovalDialog(ToolApprovalRequest request, TaskCompletionSource<ToolApprovalResponse> responseSource)
    {
        InitializeComponent();
        
        _viewModel = new ToolApprovalDialogViewModel(request, responseSource);
        _viewModel.RequestClose += OnRequestClose;
        DataContext = _viewModel;
    }
    
    private void OnRequestClose(object? sender, bool dialogResult)
    {
        DialogResult = dialogResult;
        Close();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        base.OnClosed(e);
    }
    
    /// <summary>
    /// Shows the dialog and returns the user's approval response.
    /// </summary>
    public static async Task<ToolApprovalResponse> ShowDialogAsync(
        ToolApprovalRequest request,
        Window? owner = null)
    {
        var responseSource = new TaskCompletionSource<ToolApprovalResponse>();
        
        // Ensure we're on the UI thread
        if (Application.Current.Dispatcher.CheckAccess())
        {
            ShowDialogInternal(request, responseSource, owner);
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                ShowDialogInternal(request, responseSource, owner));
        }
        
        return await responseSource.Task;
    }
    
    private static void ShowDialogInternal(
        ToolApprovalRequest request,
        TaskCompletionSource<ToolApprovalResponse> responseSource,
        Window? owner)
    {
        var dialog = new ToolApprovalDialog(request, responseSource);
        
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        else if (Application.Current.MainWindow != null && 
                 Application.Current.MainWindow.IsLoaded)
        {
            dialog.Owner = Application.Current.MainWindow;
        }
        
        dialog.ShowDialog();
        
        // If dialog was closed without a response (e.g., X button), set default denial
        if (!responseSource.Task.IsCompleted)
        {
            responseSource.TrySetResult(new ToolApprovalResponse
            {
                Approved = false,
                Scope = ApprovalScope.Once,
                Reason = "Dialog closed"
            });
        }
    }
}