using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Code-behind for AgentTeamView. Handles keyboard shortcuts for input fields.
/// </summary>
public partial class AgentTeamView : UserControl
{
    public AgentTeamView()
    {
        InitializeComponent();
    }

    private void TaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is AgentTeamViewModel vm && vm.SubmitTaskCommand.CanExecute(null))
            {
                vm.SubmitTaskCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void InjectionInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is AgentTeamViewModel vm && vm.InjectInstructionCommand.CanExecute(null))
            {
                vm.InjectInstructionCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}