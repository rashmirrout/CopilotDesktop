using System.Windows;
using System.Windows.Controls;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for SessionInfoView.xaml
/// </summary>
public partial class SessionInfoView : UserControl
{
    public SessionInfoView()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-refresh session info when tab is shown
        if (DataContext is SessionInfoViewModel viewModel)
        {
            await viewModel.RefreshOnLoadAsync();
        }
    }
}