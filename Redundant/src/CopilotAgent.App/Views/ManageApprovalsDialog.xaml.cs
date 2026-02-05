using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for ManageApprovalsDialog.xaml
/// </summary>
public partial class ManageApprovalsDialog : Window
{
    public ManageApprovalsDialog(IToolApprovalService toolApprovalService)
    {
        InitializeComponent();
        DataContext = new ManageApprovalsViewModel(toolApprovalService);
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// ViewModel for managing approval rules
/// </summary>
public partial class ManageApprovalsViewModel : ObservableObject
{
    private readonly IToolApprovalService _toolApprovalService;
    private List<ToolApprovalRule> _allRules = new();
    
    [ObservableProperty]
    private ObservableCollection<RuleDisplayItem> _filteredRules = new();
    
    [ObservableProperty]
    private RuleDisplayItem? _selectedRule;
    
    [ObservableProperty]
    private int _filterIndex;
    
    [ObservableProperty]
    private string _ruleCountText = "Loading...";
    
    [ObservableProperty]
    private string _emptyStateHint = "Rules will appear here when you approve or deny tools.";
    
    public ManageApprovalsViewModel(IToolApprovalService toolApprovalService)
    {
        _toolApprovalService = toolApprovalService;
        LoadRules();
    }
    
    private void LoadRules()
    {
        _allRules = _toolApprovalService.GetSavedRules().ToList();
        UpdateRuleCountText();
        ApplyFilter();
    }
    
    private void UpdateRuleCountText()
    {
        var globalCount = _allRules.Count(r => r.SessionId == null);
        var sessionCount = _allRules.Count(r => r.SessionId != null);
        
        if (_allRules.Count == 0)
        {
            RuleCountText = "No rules saved";
        }
        else
        {
            var parts = new List<string>();
            parts.Add($"{_allRules.Count} total");
            if (globalCount > 0) parts.Add($"{globalCount} global");
            if (sessionCount > 0) parts.Add($"{sessionCount} session");
            RuleCountText = string.Join(" • ", parts);
        }
    }
    
    partial void OnFilterIndexChanged(int value)
    {
        ApplyFilter();
    }
    
    private void ApplyFilter()
    {
        IEnumerable<ToolApprovalRule> filtered = _allRules;
        
        switch (FilterIndex)
        {
            case 1: // Global Only
                filtered = _allRules.Where(r => r.SessionId == null);
                EmptyStateHint = "No global rules. Use 'Always Allow' to create global rules.";
                break;
            case 2: // Session Only
                filtered = _allRules.Where(r => r.SessionId != null);
                EmptyStateHint = "No session rules. Use 'Allow Session' to create session rules.";
                break;
            case 3: // Approved Only
                filtered = _allRules.Where(r => r.Approved);
                EmptyStateHint = "No approved rules.";
                break;
            case 4: // Denied Only
                filtered = _allRules.Where(r => !r.Approved);
                EmptyStateHint = "No denied rules.";
                break;
            default: // All Rules
                EmptyStateHint = "Rules will appear here when you approve or deny tools.";
                break;
        }
        
        FilteredRules = new ObservableCollection<RuleDisplayItem>(
            filtered.OrderByDescending(r => r.CreatedAt)
                    .Select(r => new RuleDisplayItem(r)));
    }
    
    [RelayCommand]
    private void RemoveRule(RuleDisplayItem? item)
    {
        if (item == null) return;
        
        var result = MessageBox.Show(
            $"Remove rule for '{item.ToolName}'?",
            "Remove Rule",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            _toolApprovalService.RemoveRule(item.Rule);
            LoadRules();
        }
    }
    
    [RelayCommand]
    private void ClearAll()
    {
        if (_allRules.Count == 0)
        {
            MessageBox.Show(
                "No rules to clear.",
                "Clear All Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        
        var result = MessageBox.Show(
            $"Clear all {_allRules.Count} approval rules?\n\nThis cannot be undone.",
            "Clear All Rules",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            foreach (var rule in _allRules.ToList())
            {
                _toolApprovalService.RemoveRule(rule);
            }
            LoadRules();
        }
    }
}

/// <summary>
/// Display wrapper for a ToolApprovalRule
/// </summary>
public class RuleDisplayItem
{
    public ToolApprovalRule Rule { get; }
    
    public string ToolName => Rule.ToolName;
    
    public string ApprovedText => Rule.Approved ? "Allowed" : "Denied";
    
    public Brush ApprovedBackground => Rule.Approved
        ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9))  // Light green
        : new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)); // Light red
    
    public Brush ApprovedForeground => Rule.Approved
        ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))  // Dark green
        : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)); // Dark red
    
    public string ScopeText => Rule.Scope switch
    {
        ApprovalScope.Once => "Once",
        ApprovalScope.Session => "Session",
        ApprovalScope.Global => "Global",
        _ => "Unknown"
    };
    
    public Brush ScopeBackground => Rule.Scope switch
    {
        ApprovalScope.Global => new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD)),  // Light blue
        ApprovalScope.Session => new SolidColorBrush(Color.FromRgb(0xF3, 0xE5, 0xF5)), // Light purple
        _ => new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5))  // Light grey
    };
    
    public Brush ScopeForeground => Rule.Scope switch
    {
        ApprovalScope.Global => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),  // Dark blue
        ApprovalScope.Session => new SolidColorBrush(Color.FromRgb(0x7B, 0x1F, 0xA2)), // Dark purple
        _ => new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75))  // Grey
    };
    
    public string CreatedAtText
    {
        get
        {
            var created = Rule.CreatedAt;
            var now = DateTimeOffset.Now;
            var diff = now - created;
            
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            
            return created.ToString("MMM d, yyyy");
        }
    }
    
    public RuleDisplayItem(ToolApprovalRule rule)
    {
        Rule = rule;
    }
}