using System.Windows.Controls;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for McpConfigView.xaml
/// </summary>
public partial class McpConfigView : UserControl
{
    public McpConfigView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is McpConfigViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}