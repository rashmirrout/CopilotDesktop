using System.Windows;
using System.Windows.Controls;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for ChatView.xaml
/// </summary>
public partial class ChatView : UserControl
{
    private bool _isFirstLoad = true;

    public ChatView()
    {
        InitializeComponent();
        Loaded += ChatView_Loaded;
    }

    private void ChatView_Loaded(object sender, RoutedEventArgs e)
    {
        // Scroll to bottom on first load (where input is), then focus input
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            Dispatcher.InvokeAsync(() =>
            {
                // Scroll to bottom where the input area is
                MessageScrollViewer?.ScrollToEnd();
                MessageInput?.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    protected override void OnPropertyChanged(System.Windows.DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        
        if (e.Property.Name == nameof(DataContext) && DataContext is ChatViewModel viewModel)
        {
            viewModel.Messages.CollectionChanged += (s, args) =>
            {
                // Only auto-scroll if not at the very beginning (user has sent messages)
                if (viewModel.Messages.Count > 0)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (MessageScrollViewer != null)
                        {
                            MessageScrollViewer.ScrollToEnd();
                        }
                    });
                }
            };
        }
    }

    private void MessageInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Check for Ctrl+Enter to send message
        if (e.Key == System.Windows.Input.Key.Enter && 
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true;
            
            // Execute the Send command via ViewModel
            if (DataContext is ChatViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
            {
                _ = viewModel.SendMessageCommand.ExecuteAsync(null);
            }
        }
    }
}