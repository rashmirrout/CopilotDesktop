using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the Settings dialog
/// </summary>
public partial class SettingsDialogViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IToolApprovalService _toolApprovalService;
    private readonly IPersistenceService _persistenceService;
    private readonly IBrowserAutomationService _browserService;
    private readonly Action _closeAction;
    
    // Backing fields for settings (edited copies)
    [ObservableProperty]
    private bool _approvalModeModal;
    
    [ObservableProperty]
    private bool _approvalModeInline;
    
    [ObservableProperty]
    private bool _approvalModeBoth;
    
    [ObservableProperty]
    private bool _autoApproveLowRisk;
    
    [ObservableProperty]
    private bool _defaultAllowAll;
    
    [ObservableProperty]
    private bool _defaultAllowAllTools;
    
    [ObservableProperty]
    private bool _defaultAllowAllPaths;
    
    [ObservableProperty]
    private bool _defaultAllowAllUrls;
    
    [ObservableProperty]
    private string _approvalRulesSummary = "No saved rules";
    
    // Browser automation settings
    [ObservableProperty]
    private bool _browserHeadless;
    
    [ObservableProperty]
    private string _browserStorageInfo = "No browser data";
    
    public bool DefaultAllowAllNotChecked => !DefaultAllowAll;
    
    /// <summary>
    /// Result indicating whether settings were saved
    /// </summary>
    public bool DialogResult { get; private set; }
    
    public SettingsDialogViewModel(
        AppSettings settings,
        IToolApprovalService toolApprovalService,
        IPersistenceService persistenceService,
        IBrowserAutomationService browserService,
        Action closeAction)
    {
        _settings = settings;
        _toolApprovalService = toolApprovalService;
        _persistenceService = persistenceService;
        _browserService = browserService;
        _closeAction = closeAction;
        
        // Load current settings into editable properties
        LoadFromSettings();
        UpdateRulesSummary();
        UpdateBrowserStorageInfo();
    }
    
    private void LoadFromSettings()
    {
        // Approval UI mode
        ApprovalModeModal = _settings.ApprovalUIMode == ApprovalUIMode.Modal;
        ApprovalModeInline = _settings.ApprovalUIMode == ApprovalUIMode.Inline;
        ApprovalModeBoth = _settings.ApprovalUIMode == ApprovalUIMode.Both;
        
        // Auto-approve
        AutoApproveLowRisk = _settings.AutoApproveLowRisk;
        
        // Default autonomous mode
        DefaultAllowAll = _settings.DefaultAutonomousMode.AllowAll;
        DefaultAllowAllTools = _settings.DefaultAutonomousMode.AllowAllTools;
        DefaultAllowAllPaths = _settings.DefaultAutonomousMode.AllowAllPaths;
        DefaultAllowAllUrls = _settings.DefaultAutonomousMode.AllowAllUrls;
        
        // Browser automation settings
        BrowserHeadless = _settings.BrowserAutomation.Headless;
    }
    
    private void UpdateBrowserStorageInfo()
    {
        var storagePath = _settings.BrowserAutomation.StorageStatePath;
        if (string.IsNullOrEmpty(storagePath))
        {
            storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CopilotAgent", "browser-data", "storage-state.json");
        }
        
        if (File.Exists(storagePath))
        {
            try
            {
                var fileInfo = new FileInfo(storagePath);
                var sizeKb = fileInfo.Length / 1024.0;
                BrowserStorageInfo = $"Storage: {sizeKb:F1} KB";
            }
            catch
            {
                BrowserStorageInfo = "Storage data exists";
            }
        }
        else
        {
            BrowserStorageInfo = "No browser data stored";
        }
    }
    
    private void UpdateRulesSummary()
    {
        var rules = _toolApprovalService.GetSavedRules();
        var globalRules = rules.Count(r => r.SessionId == null);
        var sessionRules = rules.Count(r => r.SessionId != null);
        
        if (rules.Count == 0)
        {
            ApprovalRulesSummary = "No saved rules";
        }
        else
        {
            var parts = new List<string>();
            if (globalRules > 0)
                parts.Add($"{globalRules} global rule{(globalRules != 1 ? "s" : "")}");
            if (sessionRules > 0)
                parts.Add($"{sessionRules} session rule{(sessionRules != 1 ? "s" : "")}");
            ApprovalRulesSummary = string.Join(", ", parts);
        }
    }
    
    partial void OnApprovalModeModalChanged(bool value)
    {
        if (value)
        {
            ApprovalModeInline = false;
            ApprovalModeBoth = false;
        }
    }
    
    partial void OnApprovalModeInlineChanged(bool value)
    {
        if (value)
        {
            ApprovalModeModal = false;
            ApprovalModeBoth = false;
        }
    }
    
    partial void OnApprovalModeBothChanged(bool value)
    {
        if (value)
        {
            ApprovalModeModal = false;
            ApprovalModeInline = false;
        }
    }
    
    partial void OnDefaultAllowAllChanged(bool value)
    {
        OnPropertyChanged(nameof(DefaultAllowAllNotChecked));
        
        // When AllowAll is checked, auto-check all sub-options
        if (value)
        {
            DefaultAllowAllTools = true;
            DefaultAllowAllPaths = true;
            DefaultAllowAllUrls = true;
        }
    }
    
    [RelayCommand]
    private void Save()
    {
        // Apply settings
        if (ApprovalModeModal)
            _settings.ApprovalUIMode = ApprovalUIMode.Modal;
        else if (ApprovalModeInline)
            _settings.ApprovalUIMode = ApprovalUIMode.Inline;
        else
            _settings.ApprovalUIMode = ApprovalUIMode.Both;
        
        _settings.AutoApproveLowRisk = AutoApproveLowRisk;
        
        _settings.DefaultAutonomousMode = new AutonomousModeSettings
        {
            AllowAll = DefaultAllowAll,
            AllowAllTools = DefaultAllowAllTools,
            AllowAllPaths = DefaultAllowAllPaths,
            AllowAllUrls = DefaultAllowAllUrls
        };
        
        // Browser automation settings
        _settings.BrowserAutomation.Headless = BrowserHeadless;
        
        // Save to persistence
        _ = _persistenceService.SaveSettingsAsync(_settings);
        
        DialogResult = true;
        _closeAction();
    }
    
    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        _closeAction();
    }
    
    [RelayCommand]
    private void ResetToDefaults()
    {
        var result = MessageBox.Show(
            "Reset all settings to their default values?",
            "Reset Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            ApprovalModeModal = false;
            ApprovalModeInline = false;
            ApprovalModeBoth = true;
            
            AutoApproveLowRisk = false;
            
            DefaultAllowAll = false;
            DefaultAllowAllTools = false;
            DefaultAllowAllPaths = false;
            DefaultAllowAllUrls = false;
            
            // Browser automation defaults
            BrowserHeadless = true;
        }
    }
    
    [RelayCommand]
    private async Task ClearBrowserDataAsync()
    {
        var result = MessageBox.Show(
            "Clear all browser data including cookies, session storage, and cached authentication?\n\nYou will need to re-authenticate with MCP servers after this.",
            "Clear Browser Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _browserService.ClearBrowserDataAsync();
                UpdateBrowserStorageInfo();
                
                MessageBox.Show(
                    "Browser data cleared successfully.",
                    "Clear Browser Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to clear browser data: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
    
    [RelayCommand]
    private void ManageApprovals()
    {
        var dialog = new Views.ManageApprovalsDialog(_toolApprovalService);
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();
        
        // Refresh summary after managing
        UpdateRulesSummary();
    }
    
    [RelayCommand]
    private void ClearSessionApprovals()
    {
        var rules = _toolApprovalService.GetSavedRules();
        var sessionRulesCount = rules.Count(r => r.SessionId != null);
        
        if (sessionRulesCount == 0)
        {
            MessageBox.Show(
                "No session approvals to clear.",
                "Clear Session Approvals",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        
        var result = MessageBox.Show(
            $"Clear {sessionRulesCount} session approval rule{(sessionRulesCount != 1 ? "s" : "")}?\n\nGlobal approvals will not be affected.",
            "Clear Session Approvals",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            // Get all session IDs and clear them
            var sessionIds = rules
                .Where(r => r.SessionId != null)
                .Select(r => r.SessionId!)
                .Distinct()
                .ToList();
            
            foreach (var sessionId in sessionIds)
            {
                _toolApprovalService.ClearSessionApprovals(sessionId);
            }
            
            UpdateRulesSummary();
            
            MessageBox.Show(
                $"Cleared {sessionRulesCount} session approval rule{(sessionRulesCount != 1 ? "s" : "")}.",
                "Clear Session Approvals",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}