using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// Handles double-click on tab name to rename the session
    /// </summary>
    private void TabName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            
            if (sender is TextBlock textBlock && textBlock.DataContext is Session session)
            {
                // Show rename dialog
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
    }
}
