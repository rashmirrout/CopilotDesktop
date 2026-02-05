using System.Windows;
using CopilotAgent.App.ViewModels;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for SettingsDialog.xaml
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsDialogViewModel _viewModel;
    
    public SettingsDialog(
        AppSettings settings,
        IToolApprovalService toolApprovalService,
        IPersistenceService persistenceService)
    {
        InitializeComponent();
        
        _viewModel = new SettingsDialogViewModel(
            settings,
            toolApprovalService,
            persistenceService,
            () => Close());
        
        DataContext = _viewModel;
    }
    
    /// <summary>
    /// Gets whether the settings were saved
    /// </summary>
    public bool SettingsSaved => _viewModel.DialogResult;
    
    /// <summary>
    /// Shows the settings dialog and returns whether settings were saved
    /// </summary>
    public static bool ShowSettings(
        AppSettings settings,
        IToolApprovalService toolApprovalService,
        IPersistenceService persistenceService,
        Window? owner = null)
    {
        var dialog = new SettingsDialog(settings, toolApprovalService, persistenceService);
        
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        else if (Application.Current.MainWindow != null)
        {
            dialog.Owner = Application.Current.MainWindow;
        }
        
        dialog.ShowDialog();
        return dialog.SettingsSaved;
    }
}