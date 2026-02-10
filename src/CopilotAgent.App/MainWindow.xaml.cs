using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CopilotAgent.App.ViewModels;
using CopilotAgent.App.Views;
using CopilotAgent.Core.Models;

namespace CopilotAgent.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Register Ctrl+N keyboard shortcut for New Session
        InputBindings.Add(new KeyBinding(
            new DelegateCommand(ExecuteNewSession),
            Key.N,
            ModifierKeys.Control));
    }

    /// <summary>
    /// Executes the CreateSessionCommand from the ViewModel via Ctrl+N shortcut
    /// </summary>
    private void ExecuteNewSession()
    {
        if (DataContext is MainWindowViewModel vm && vm.CreateSessionCommand.CanExecute(null))
        {
            vm.CreateSessionCommand.Execute(null);
        }
    }

    /// <summary>
    /// Minimal ICommand implementation for keyboard shortcut bindings.
    /// Delegates execution to an Action callback. Always executable.
    /// </summary>
    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }

    /// <summary>
    /// Handles close button click to prevent event bubbling to parent tab button
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Prevent the click from bubbling up to the parent tab button
        e.Handled = true;
    }

    /// <summary>
    /// Handles pen/edit icon click to rename the session
    /// </summary>
    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is FrameworkElement element && element.DataContext is Session session)
        {
            ShowRenameDialog(session);
        }
    }

    /// <summary>
    /// Handles double-click on tab name to rename the session
    /// </summary>
    private void TabName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            
            if (sender is FrameworkElement element && element.DataContext is Session session)
            {
                ShowRenameDialog(session);
            }
        }
    }

    /// <summary>
    /// Shows the rename dialog for a session and applies the new name if confirmed
    /// </summary>
    private void ShowRenameDialog(Session session)
    {
        var dialog = new RenameSessionDialog
        {
            Owner = this,
            SessionName = session.DisplayName
        };

        if (dialog.ShowDialog() == true)
        {
            session.DisplayName = dialog.SessionName;
        }
    }
}
