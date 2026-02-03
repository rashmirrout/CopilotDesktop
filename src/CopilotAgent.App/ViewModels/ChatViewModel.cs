using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for the chat interface
/// </summary>
public partial class ChatViewModel : ViewModelBase
{
    private readonly ICopilotService _copilotService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<ChatViewModel> _logger;
    private CancellationTokenSource? _currentCts;

    [ObservableProperty]
    private Session _session = null!;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _canSendMessage = true;

    [ObservableProperty]
    private TerminalViewModel _terminalViewModel = null!;

    public ChatViewModel(
        ICopilotService copilotService,
        ISessionManager sessionManager,
        ILogger<ChatViewModel> logger,
        TerminalViewModel terminalViewModel)
    {
        _copilotService = copilotService;
        _sessionManager = sessionManager;
        _logger = logger;
        _terminalViewModel = terminalViewModel;
    }

    partial void OnMessageInputChanged(string value)
    {
        CanSendMessage = !string.IsNullOrWhiteSpace(value) && !IsBusy;
    }

    private void UpdateBusyState(bool busy)
    {
        IsBusy = busy;
        CanSendMessage = !string.IsNullOrWhiteSpace(MessageInput) && !busy;
        StatusMessage = busy ? "Thinking..." : string.Empty;
    }

    [RelayCommand]
    private void Stop()
    {
        if (_currentCts != null && !_currentCts.IsCancellationRequested)
        {
            _logger.LogInformation("Stop requested - cancelling current operation");
            _currentCts.Cancel();
            StatusMessage = "Stopping...";
        }
    }

    public async Task InitializeAsync(Session session)
    {
        Session = session;
        
        // Load existing messages
        Messages.Clear();
        foreach (var message in session.MessageHistory)
        {
            Messages.Add(message);
        }
        
        // Set terminal working directory
        if (!string.IsNullOrEmpty(session.WorkingDirectory))
        {
            TerminalViewModel.SetWorkingDirectory(session.WorkingDirectory);
        }
        else if (session.GitWorktreeInfo != null)
        {
            TerminalViewModel.SetWorkingDirectory(session.GitWorktreeInfo.WorktreePath);
        }
        
        _logger.LogInformation("Chat initialized for session {SessionId} with {Count} messages", 
            session.SessionId, session.MessageHistory.Count);
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageInput) || IsBusy)
            return;

        var userMessage = MessageInput;
        MessageInput = string.Empty;
        UpdateBusyState(true);

        // Create new CancellationTokenSource for this operation
        _currentCts?.Dispose();
        _currentCts = new CancellationTokenSource();
        var cancellationToken = _currentCts.Token;

        try
        {
            _logger.LogInformation("Sending message to Copilot");
            
            // Add user message to UI immediately
            var userChatMessage = new ChatMessage
            {
                Role = MessageRole.User,
                Content = userMessage,
                Timestamp = DateTime.UtcNow
            };
            Messages.Add(userChatMessage);
            Session.MessageHistory.Add(userChatMessage);

            // Create placeholder for assistant response immediately
            var assistantMessage = new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = string.Empty,
                Timestamp = DateTime.UtcNow
            };
            Messages.Add(assistantMessage);

            // Run the Copilot call on a background thread to keep UI responsive
            await Task.Run(async () =>
            {
                // Stream response from Copilot
                await foreach (var chunk in _copilotService.SendMessageStreamingAsync(
                    Session, userMessage, cancellationToken))
                {
                    // Check for cancellation
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Update UI on dispatcher thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        assistantMessage.Content = chunk.Content;
                        var index = Messages.IndexOf(assistantMessage);
                        if (index >= 0)
                        {
                            Messages[index] = new ChatMessage
                            {
                                Role = assistantMessage.Role,
                                Content = assistantMessage.Content,
                                Timestamp = assistantMessage.Timestamp,
                                IsStreaming = chunk.IsStreaming
                            };
                            assistantMessage = Messages[index];
                        }
                    });
                }
            }, cancellationToken);

            // Add final message to session
            Session.MessageHistory.Add(assistantMessage);
            
            // Save session
            await _sessionManager.SaveActiveSessionAsync();
            
            _logger.LogInformation("Message sent and response received");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message sending was cancelled by user");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = "Operation stopped by user",
                    Timestamp = DateTime.UtcNow
                });
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = $"Error: {ex.Message}",
                    Timestamp = DateTime.UtcNow
                });
            });
        }
        finally
        {
            UpdateBusyState(false);
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var result = MessageBox.Show(
            "Clear all chat history for this session?",
            "Confirm Clear",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _logger.LogInformation("Clearing chat history for session {SessionId}", Session.SessionId);
            Session.MessageHistory.Clear();
            Messages.Clear();
            await _sessionManager.SaveActiveSessionAsync();
        }
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        try
        {
            await _sessionManager.SaveActiveSessionAsync();
            StatusMessage = "Session saved";
            _logger.LogInformation("Session {SessionId} saved", Session.SessionId);
            
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save session");
            MessageBox.Show(
                $"Failed to save session: {ex.Message}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}