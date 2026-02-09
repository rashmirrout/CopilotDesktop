using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Code-behind for AgentTeamView. Handles keyboard shortcuts for input fields
/// and scroll propagation for the main content area.
/// </summary>
public partial class AgentTeamView : UserControl
{
    public AgentTeamView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Enter → submit task; Shift+Enter → new line.
    /// </summary>
    private void TaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is AgentTeamViewModel vm && vm.SubmitTaskCommand.CanExecute(null))
            {
                vm.SubmitTaskCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Enter → inject instruction; Shift+Enter → new line.
    /// </summary>
    private void InjectionInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is AgentTeamViewModel vm && vm.InjectInstructionCommand.CanExecute(null))
            {
                vm.InjectInstructionCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Enter → send clarification response; Shift+Enter → new line.
    /// </summary>
    private void ClarificationInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is AgentTeamViewModel vm && vm.RespondToClarificationCommand.CanExecute(null))
            {
                vm.RespondToClarificationCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Ensures mouse wheel events propagate to the main ScrollViewer even when
    /// child elements (MarkdownScrollViewer, TextBox, ListBox, etc.) capture the event.
    /// Safely handles ContentElement types (Run, Inline, Paragraph) that are not Visual/Visual3D.
    /// </summary>
    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var source = e.OriginalSource as DependencyObject;
        while (source != null && source != scrollViewer)
        {
            if (source is ScrollViewer nested && nested != scrollViewer)
            {
                e.Handled = true;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                return;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        if (!e.Handled)
        {
            e.Handled = true;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        }
    }
}