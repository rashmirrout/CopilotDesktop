using System.Windows;
using System.Windows.Input;
using CopilotAgent.App.ViewModels;
using Microsoft.Win32;

namespace CopilotAgent.App.Views;

/// <summary>
/// Dialog for adding a new skill with proper metadata capture
/// </summary>
public partial class AddSkillDialog : Window
{
    public AddSkillDialogViewModel ViewModel { get; }

    public AddSkillDialog(AddSkillDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        
        // Focus the skill name textbox when loaded
        Loaded += (_, _) => SkillNameTextBox.Focus();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Validate())
        {
            return;
        }

        var success = await ViewModel.CreateSkillAsync();
        if (success)
        {
            DialogResult = true;
            Close();
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            Title = "Select Skill File"
        };

        if (dialog.ShowDialog() == true)
        {
            await ViewModel.LoadFileAsync(dialog.FileName);
        }
    }
}