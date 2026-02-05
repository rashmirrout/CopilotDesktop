using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CopilotAgent.App.Views;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.Services;

/// <summary>
/// Event args for inline approval notifications.
/// </summary>
public class InlineApprovalEventArgs : EventArgs
{
    public required ToolApprovalRequest Request { get; init; }
    public TaskCompletionSource<ToolApprovalResponse> ResponseSource { get; } = new();
}

/// <summary>
/// UI service that bridges IToolApprovalService with WPF dialogs.
/// Subscribes to ApprovalRequested events and shows modal or inline approval UI.
/// 
/// UI Modes:
/// - Modal: Shows a popup dialog that the user must interact with (centered, detailed options)
/// - Inline: Shows a toast notification (bottom-right corner) with quick approve/deny buttons
///           Auto-denies after 10 seconds if no action taken
/// - Both: Shows quick action toast first (3 seconds), then transitions to full modal dialog
///         if user doesn't respond quickly. Best of both worlds!
/// </summary>
public class ToolApprovalUIService : IDisposable
{
    private readonly IToolApprovalService _toolApprovalService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<ToolApprovalUIService> _logger;
    private bool _isSubscribed;

    /// <summary>
    /// Event raised when an inline approval request occurs.
    /// Subscribers (like ChatViewModel) can use this to display inline UI in the chat.
    /// </summary>
    public event EventHandler<InlineApprovalEventArgs>? InlineApprovalRequested;

    public ToolApprovalUIService(
        IToolApprovalService toolApprovalService,
        AppSettings appSettings,
        ILogger<ToolApprovalUIService> logger)
    {
        _toolApprovalService = toolApprovalService;
        _appSettings = appSettings;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the service and subscribe to approval events.
    /// Call this after the main window is loaded.
    /// </summary>
    public void Initialize()
    {
        if (_isSubscribed)
            return;

        _toolApprovalService.ApprovalRequested += OnApprovalRequested;
        _isSubscribed = true;
        
        _logger.LogInformation("ToolApprovalUIService initialized, listening for approval requests (mode: {Mode})", 
            _appSettings.ApprovalUIMode);
    }

    private async void OnApprovalRequested(object? sender, ToolApprovalRequestEventArgs e)
    {
        _logger.LogInformation("[OnApprovalRequested] START - Approval UI requested for tool {Tool}", e.Request.ToolName);
        
        try
        {
            // Determine which UI mode to use
            var uiMode = _appSettings.ApprovalUIMode;
            _logger.LogInformation("[OnApprovalRequested] UI mode from settings: {UIMode} ({ModeInt})", uiMode, (int)uiMode);
            
            ToolApprovalResponse response;
            
            switch (uiMode)
            {
                case ApprovalUIMode.Modal:
                    // Modal mode: Show a popup dialog that blocks until user responds
                    _logger.LogInformation("[OnApprovalRequested] Modal mode - showing popup dialog...");
                    response = await ShowModalApprovalAsync(e.Request);
                    break;
                    
                case ApprovalUIMode.Inline:
                    // Inline mode: For lightweight approval workflow
                    // Currently shows a non-modal notification toast and auto-denies after timeout
                    // This allows users to see what's happening without blocking workflow
                    _logger.LogInformation("[OnApprovalRequested] Inline mode - showing non-blocking notification...");
                    response = await ShowInlineApprovalAsync(e.Request);
                    break;
                    
                case ApprovalUIMode.Both:
                default:
                    // Both mode: Show inline notification first, then modal if not quickly responded
                    _logger.LogInformation("[OnApprovalRequested] Both mode - showing inline notification then modal...");
                    response = await ShowBothApprovalAsync(e.Request);
                    break;
            }
            
            _logger.LogInformation("[OnApprovalRequested] Got response: Approved={Approved}, Scope={Scope}", 
                response.Approved, response.Scope);
            
            // Record the decision if user wants to remember it
            if (response.RememberDecision || response.Scope != ApprovalScope.Once)
            {
                _logger.LogInformation("[OnApprovalRequested] Recording decision for future use...");
                _toolApprovalService.RecordDecision(e.Request, response);
            }
            
            // Complete the approval request
            _logger.LogInformation("[OnApprovalRequested] Setting ResponseSource result...");
            var setResultSuccess = e.ResponseSource.TrySetResult(response);
            _logger.LogInformation("[OnApprovalRequested] TrySetResult returned: {Success}", setResultSuccess);
            
            _logger.LogInformation("[OnApprovalRequested] COMPLETE - Tool {Tool} {Decision} by user (scope: {Scope})",
                e.Request.ToolName,
                response.Approved ? "approved" : "denied",
                response.Scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OnApprovalRequested] ERROR showing approval UI for tool {Tool}: {Message}", 
                e.Request.ToolName, ex.Message);
            
            // Default to denial on error
            var setErrorSuccess = e.ResponseSource.TrySetResult(new ToolApprovalResponse
            {
                Approved = false,
                Reason = $"Error showing approval UI: {ex.Message}"
            });
            _logger.LogInformation("[OnApprovalRequested] Error path TrySetResult returned: {Success}", setErrorSuccess);
        }
    }

    private async Task<ToolApprovalResponse> ShowModalApprovalAsync(ToolApprovalRequest request)
    {
        _logger.LogDebug("ShowModalApprovalAsync called for tool {Tool}", request.ToolName);
        
        // Ensure we're on the UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            _logger.LogDebug("Not on UI thread, dispatching to UI thread for tool {Tool}", request.ToolName);
            
            // Use TaskCompletionSource to avoid deadlock with .Result
            var tcs = new TaskCompletionSource<ToolApprovalResponse>();
            
            Application.Current.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    _logger.LogDebug("Now on UI thread, showing dialog for tool {Tool}", request.ToolName);
                    var result = await ToolApprovalDialog.ShowDialogAsync(request);
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error showing approval dialog for tool {Tool}", request.ToolName);
                    tcs.TrySetException(ex);
                }
            });
            
            return await tcs.Task;
        }
        
        _logger.LogDebug("Already on UI thread, showing dialog directly for tool {Tool}", request.ToolName);
        return await ToolApprovalDialog.ShowDialogAsync(request);
    }

    /// <summary>
    /// Shows a non-modal inline approval notification.
    /// This is a lightweight approval mode that shows a toast notification in the main window
    /// with quick approve/deny buttons, auto-denying after a timeout.
    /// 
    /// For "Inline" mode, this provides a non-intrusive way to handle approvals
    /// that doesn't block the workflow completely.
    /// </summary>
    private async Task<ToolApprovalResponse> ShowInlineApprovalAsync(ToolApprovalRequest request)
    {
        _logger.LogDebug("ShowInlineApprovalAsync called for tool {Tool}", request.ToolName);
        
        var tcs = new TaskCompletionSource<ToolApprovalResponse>();
        
        // Dispatch to UI thread
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Create a simple toast-style notification window
                var notificationWindow = new Window
                {
                    Title = "Tool Approval",
                    Width = 400,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    ResizeMode = ResizeMode.NoResize,
                };
                
                // Position in bottom-right corner
                var workArea = SystemParameters.WorkArea;
                notificationWindow.Left = workArea.Right - notificationWindow.Width - 20;
                notificationWindow.Top = workArea.Bottom - notificationWindow.Height - 20;
                
                // Create content
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(63, 81, 181)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                };
                
                var mainStack = new StackPanel();
                
                // Header
                var header = new TextBlock
                {
                    Text = $"🔧 Tool Request: {request.ToolName}",
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                mainStack.Children.Add(header);
                
                // Description
                var description = new TextBlock
                {
                    Text = request.Description ?? $"Allow '{request.ToolName}' to execute?",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12),
                };
                mainStack.Children.Add(description);
                
                // Countdown timer
                var countdownText = new TextBlock
                {
                    Text = "Auto-deny in 10 seconds...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                mainStack.Children.Add(countdownText);
                
                // Buttons
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                
                var denyButton = new Button
                {
                    Content = "Deny",
                    Padding = new Thickness(16, 6, 16, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(198, 40, 40)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                
                var approveButton = new Button
                {
                    Content = "Approve Once",
                    Padding = new Thickness(16, 6, 16, 6),
                    Background = new SolidColorBrush(Color.FromRgb(46, 125, 50)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                
                buttonPanel.Children.Add(denyButton);
                buttonPanel.Children.Add(approveButton);
                mainStack.Children.Add(buttonPanel);
                
                border.Child = mainStack;
                notificationWindow.Content = border;
                
                // Timer for auto-deny
                var countdown = 10;
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                
                timer.Tick += (s, e) =>
                {
                    countdown--;
                    countdownText.Text = $"Auto-deny in {countdown} seconds...";
                    
                    if (countdown <= 0)
                    {
                        timer.Stop();
                        notificationWindow.Close();
                        tcs.TrySetResult(new ToolApprovalResponse
                        {
                            Approved = false,
                            Scope = ApprovalScope.Once,
                            Reason = "Auto-denied after timeout (inline mode)"
                        });
                    }
                };
                
                // Button handlers
                approveButton.Click += (s, e) =>
                {
                    timer.Stop();
                    notificationWindow.Close();
                    tcs.TrySetResult(new ToolApprovalResponse
                    {
                        Approved = true,
                        Scope = ApprovalScope.Once,
                        Reason = "Approved via inline notification"
                    });
                };
                
                denyButton.Click += (s, e) =>
                {
                    timer.Stop();
                    notificationWindow.Close();
                    tcs.TrySetResult(new ToolApprovalResponse
                    {
                        Approved = false,
                        Scope = ApprovalScope.Once,
                        Reason = "Denied via inline notification"
                    });
                };
                
                // Show window and start timer
                notificationWindow.Show();
                timer.Start();
                
                _logger.LogDebug("Inline notification shown for tool {Tool}", request.ToolName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing inline notification for tool {Tool}", request.ToolName);
                tcs.TrySetResult(new ToolApprovalResponse
                {
                    Approved = false,
                    Reason = $"Error showing inline notification: {ex.Message}"
                });
            }
        });
        
        return await tcs.Task;
    }

    /// <summary>
    /// Shows both inline notification AND modal dialog.
    /// First shows a brief inline toast (3 seconds for quick approval),
    /// then shows the full modal dialog for detailed options.
    /// This gives users both a quick action option and full control.
    /// </summary>
    private async Task<ToolApprovalResponse> ShowBothApprovalAsync(ToolApprovalRequest request)
    {
        _logger.LogDebug("ShowBothApprovalAsync called for tool {Tool}", request.ToolName);
        
        var tcs = new TaskCompletionSource<ToolApprovalResponse>();
        var quickResponseReceived = false;
        
        // Dispatch to UI thread
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Create a quick action toast notification (shorter timeout)
                var notificationWindow = new Window
                {
                    Title = "Tool Approval - Quick Action",
                    Width = 420,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    ResizeMode = ResizeMode.NoResize,
                };
                
                // Position in bottom-right corner
                var workArea = SystemParameters.WorkArea;
                notificationWindow.Left = workArea.Right - notificationWindow.Width - 20;
                notificationWindow.Top = workArea.Bottom - notificationWindow.Height - 20;
                
                // Create content
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0)), // Orange for "Both" mode
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                };
                
                var mainStack = new StackPanel();
                
                // Header with mode indicator
                var header = new TextBlock
                {
                    Text = $"⚡ Quick Approval: {request.ToolName}",
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                mainStack.Children.Add(header);
                
                // Description
                var description = new TextBlock
                {
                    Text = request.Description ?? $"Allow '{request.ToolName}' to execute?",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                mainStack.Children.Add(description);
                
                // Mode indicator
                var modeText = new TextBlock
                {
                    Text = "📋 Full dialog will open in 3 seconds for more options...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Amber
                    FontStyle = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                mainStack.Children.Add(modeText);
                
                // Countdown timer
                var countdownText = new TextBlock
                {
                    Text = "Opening full dialog in 3...",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    Margin = new Thickness(0, 0, 0, 8),
                };
                mainStack.Children.Add(countdownText);
                
                // Buttons (quick actions)
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                
                var denyButton = new Button
                {
                    Content = "❌ Quick Deny",
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(198, 40, 40)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                
                var approveButton = new Button
                {
                    Content = "✅ Quick Approve",
                    Padding = new Thickness(12, 6, 12, 6),
                    Background = new SolidColorBrush(Color.FromRgb(46, 125, 50)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                };
                
                buttonPanel.Children.Add(denyButton);
                buttonPanel.Children.Add(approveButton);
                mainStack.Children.Add(buttonPanel);
                
                border.Child = mainStack;
                notificationWindow.Content = border;
                
                // Timer for transition to modal (shorter: 3 seconds)
                var countdown = 3;
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                
                timer.Tick += async (s, e) =>
                {
                    countdown--;
                    countdownText.Text = $"Opening full dialog in {countdown}...";
                    
                    if (countdown <= 0)
                    {
                        timer.Stop();
                        notificationWindow.Close();
                        
                        if (!quickResponseReceived)
                        {
                            // Transition to modal dialog for full options
                            _logger.LogDebug("Quick action timeout, showing full modal for tool {Tool}", request.ToolName);
                            try
                            {
                                var result = await ToolApprovalDialog.ShowDialogAsync(request);
                                tcs.TrySetResult(result);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error showing modal dialog after quick action for tool {Tool}", request.ToolName);
                                tcs.TrySetResult(new ToolApprovalResponse
                                {
                                    Approved = false,
                                    Reason = $"Error showing modal dialog: {ex.Message}"
                                });
                            }
                        }
                    }
                };
                
                // Quick action button handlers
                approveButton.Click += (s, e) =>
                {
                    quickResponseReceived = true;
                    timer.Stop();
                    notificationWindow.Close();
                    tcs.TrySetResult(new ToolApprovalResponse
                    {
                        Approved = true,
                        Scope = ApprovalScope.Once,
                        Reason = "Quick approved via inline notification (Both mode)"
                    });
                };
                
                denyButton.Click += (s, e) =>
                {
                    quickResponseReceived = true;
                    timer.Stop();
                    notificationWindow.Close();
                    tcs.TrySetResult(new ToolApprovalResponse
                    {
                        Approved = false,
                        Scope = ApprovalScope.Once,
                        Reason = "Quick denied via inline notification (Both mode)"
                    });
                };
                
                // Show window and start timer
                notificationWindow.Show();
                timer.Start();
                
                _logger.LogDebug("Both mode: inline notification shown for tool {Tool}, will transition to modal in 3s", request.ToolName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing both-mode notification for tool {Tool}", request.ToolName);
                // Fall back to modal dialog
                Application.Current.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var result = await ToolApprovalDialog.ShowDialogAsync(request);
                        tcs.TrySetResult(result);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Error showing fallback modal for tool {Tool}", request.ToolName);
                        tcs.TrySetResult(new ToolApprovalResponse
                        {
                            Approved = false,
                            Reason = $"Error showing approval UI: {innerEx.Message}"
                        });
                    }
                });
            }
        });
        
        return await tcs.Task;
    }

    public void Dispose()
    {
        if (_isSubscribed)
        {
            _toolApprovalService.ApprovalRequested -= OnApprovalRequested;
            _isSubscribed = false;
        }
    }
}
