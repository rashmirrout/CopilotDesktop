using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for TerminalView.xaml
/// Handles command history navigation with Up/Down arrow keys.
/// </summary>
public partial class TerminalView : UserControl
{
    private ViewModels.TerminalViewModel? _viewModel;

    public TerminalView()
    {
        InitializeComponent();
        Loaded += TerminalView_Loaded;
    }

    private void TerminalView_Loaded(object sender, RoutedEventArgs e)
    {
        // Focus the command input when the view loads
        CommandInput.Focus();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        
        if (e.Property.Name == nameof(DataContext) && DataContext is ViewModels.TerminalViewModel viewModel)
        {
            _viewModel = viewModel;
            
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ViewModels.TerminalViewModel.TerminalOutput))
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        TerminalScrollViewer.ScrollToEnd();
                    });
                }
            };
        }
    }

    /// <summary>
    /// Handle special key presses in the command input
    /// </summary>
    private void CommandInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel == null)
            return;

        switch (e.Key)
        {
            case Key.Up:
                // Navigate to previous command in history
                _viewModel.HistoryUp();
                e.Handled = true;
                // Move cursor to end of text
                CommandInput.CaretIndex = CommandInput.Text.Length;
                break;

            case Key.Down:
                // Navigate to next command in history
                _viewModel.HistoryDown();
                e.Handled = true;
                // Move cursor to end of text
                CommandInput.CaretIndex = CommandInput.Text.Length;
                break;

            case Key.Escape:
                // Clear the current input
                _viewModel.CommandInput = string.Empty;
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                // Ctrl+C - send interrupt
                _viewModel.SendInterruptCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                // Ctrl+L - clear terminal
                _viewModel.ClearTerminalCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Focus the command input when clicking on the terminal output area
    /// </summary>
    private void TerminalScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Focus the command input when user clicks anywhere in the terminal
        CommandInput.Focus();
    }
}
