using System.Windows;
using System.Windows.Media;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.Views;

/// <summary>
/// Dialog for requesting user approval of commands
/// </summary>
public partial class CommandApprovalDialog : Window
{
    private readonly string _command;
    private readonly CommandEvaluationResult _evaluation;

    /// <summary>
    /// The user's decision
    /// </summary>
    public CommandDecision Decision { get; private set; } = CommandDecision.DeniedByUser;

    /// <summary>
    /// Whether to add the command to the allow list
    /// </summary>
    public bool AddToAllowList { get; private set; }

    public CommandApprovalDialog(string command, CommandEvaluationResult evaluation)
    {
        InitializeComponent();
        
        _command = command;
        _evaluation = evaluation;
        
        // Set up the UI
        CommandText.Text = command;
        ReasonText.Text = evaluation.Reason;
        
        // Set risk level styling
        SetRiskLevelStyle(evaluation.RiskLevel);
        
        // Show warning for high-risk commands
        if (evaluation.RiskLevel >= RiskLevel.High)
        {
            WarningPanel.Visibility = Visibility.Visible;
        }
    }

    private void SetRiskLevelStyle(RiskLevel riskLevel)
    {
        var (text, background, foreground) = riskLevel switch
        {
            RiskLevel.Low => ("LOW", "#2E7D32", "#FFFFFF"),
            RiskLevel.Medium => ("MEDIUM", "#F57C00", "#FFFFFF"),
            RiskLevel.High => ("HIGH", "#D32F2F", "#FFFFFF"),
            RiskLevel.Critical => ("CRITICAL", "#B71C1C", "#FFFFFF"),
            _ => ("UNKNOWN", "#666666", "#FFFFFF")
        };

        RiskText.Text = text;
        RiskBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
        RiskText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground));
    }

    private void AllowButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = CommandDecision.AllowedByUser;
        AddToAllowList = AddToAllowListCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void AllowOnceButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = CommandDecision.AllowedOnce;
        AddToAllowList = false;
        DialogResult = true;
        Close();
    }

    private void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = CommandDecision.DeniedByUser;
        AddToAllowList = false;
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows the approval dialog and returns the user's decision
    /// </summary>
    public static (CommandDecision decision, bool addToAllowList) ShowApproval(
        Window owner, 
        string command, 
        CommandEvaluationResult evaluation)
    {
        var dialog = new CommandApprovalDialog(command, evaluation)
        {
            Owner = owner
        };
        
        var result = dialog.ShowDialog();
        
        return (dialog.Decision, dialog.AddToAllowList);
    }
}