using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CopilotAgent.App.ViewModels;
using CopilotAgent.Core.Models;

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

    /// <summary>
    /// Handles click on agent work summary bar to expand/collapse the details.
    /// </summary>
    private void AgentWorkSummary_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ChatMessage message)
        {
            // Toggle the IsExpanded state
            message.IsExpanded = !message.IsExpanded;
            
            // Find the message in the ViewModel and update it to trigger UI refresh
            if (DataContext is ChatViewModel viewModel)
            {
                var index = -1;
                for (int i = 0; i < viewModel.Messages.Count; i++)
                {
                    if (viewModel.Messages[i].Id == message.Id)
                    {
                        index = i;
                        break;
                    }
                }
                
                if (index >= 0)
                {
                    // Replace message to trigger UI update (WPF requires this for property changes)
                    viewModel.Messages[index] = new ChatMessage
                    {
                        Id = message.Id,
                        Role = message.Role,
                        Content = message.Content,
                        Timestamp = message.Timestamp,
                        IsStreaming = message.IsStreaming,
                        IsError = message.IsError,
                        ToolCall = message.ToolCall,
                        ToolResult = message.ToolResult,
                        Metadata = message.Metadata,
                        ReasoningId = message.ReasoningId,
                        TurnId = message.TurnId,
                        IsAgentWork = message.IsAgentWork,
                        CollapsedMessages = message.CollapsedMessages,
                        SummaryText = message.SummaryText,
                        IsExpanded = message.IsExpanded,
                        ToolCount = message.ToolCount,
                        ReasoningCount = message.ReasoningCount
                    };
                }
            }
            
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles copy button click - copies content to clipboard and shows checkmark feedback
    /// </summary>
    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var content = button.Tag as string;
        if (string.IsNullOrEmpty(content))
            return;

        // Find the copy and check icons within the button
        var buttonContent = button.Content as Grid;
        if (buttonContent == null)
            return;

        Canvas? copyIcon = null;
        Canvas? checkIcon = null;

        foreach (var child in buttonContent.Children)
        {
            if (child is Canvas canvas)
            {
                if (canvas.Name == "CopyIcon" || (copyIcon == null && canvas.Visibility == Visibility.Visible))
                    copyIcon = canvas;
                else if (canvas.Name == "CheckIcon" || (checkIcon == null && canvas.Visibility == Visibility.Collapsed))
                    checkIcon = canvas;
            }
        }

        // Copy to clipboard
        try
        {
            Clipboard.SetText(content);

            // Show checkmark feedback
            if (copyIcon != null && checkIcon != null)
            {
                copyIcon.Visibility = Visibility.Collapsed;
                checkIcon.Visibility = Visibility.Visible;
                button.ToolTip = "Copied!";

                // Revert after 1.5 seconds
                await Task.Delay(1500);

                copyIcon.Visibility = Visibility.Visible;
                checkIcon.Visibility = Visibility.Collapsed;
                button.ToolTip = "Copy to clipboard";
            }
        }
        catch
        {
            // Silently fail if clipboard access fails
        }
    }
}
