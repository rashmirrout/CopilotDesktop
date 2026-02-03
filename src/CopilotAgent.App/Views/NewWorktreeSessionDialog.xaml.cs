using System.Windows;
using Microsoft.Win32;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Dialog for creating a new worktree session from a GitHub issue
/// </summary>
public partial class NewWorktreeSessionDialog : Window
{
    public NewWorktreeSessionDialogViewModel ViewModel { get; }

    public NewWorktreeSessionDialog(NewWorktreeSessionDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Repository Directory",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.WorkingDirectory = dialog.FolderName;
        }
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var success = await ViewModel.CreateWorktreeSessionAsync();
        if (success)
        {
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}