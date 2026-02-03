using System.Windows.Controls;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for SkillsView.xaml
/// </summary>
public partial class SkillsView : UserControl
{
    public SkillsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SkillsViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}