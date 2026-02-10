using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Code-behind for OfficeView. Handles keyboard shortcuts, auto-scroll, and side panel animations.
/// </summary>
public partial class OfficeView : UserControl
{
    private Storyboard? _slideInStoryboard;
    private Storyboard? _slideOutStoryboard;
    private Storyboard? _pulseStoryboard;
    private bool _isAnimatingOut;
    private bool _isPulseRunning;

    public OfficeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Cache storyboard references
        _slideInStoryboard = (Storyboard)FindResource("SlideInStoryboard");
        _slideOutStoryboard = (Storyboard)FindResource("SlideOutStoryboard");
        _pulseStoryboard = (Storyboard)FindResource("PulseStoryboard");

        // Wire slide-out completion to collapse the panel after animation
        if (_slideOutStoryboard is not null)
        {
            _slideOutStoryboard.Completed += SlideOutStoryboard_Completed;
        }

        // Wire auto-scroll and side panel animation when ViewModel is set
        if (DataContext is OfficeViewModel vm)
        {
            vm.Messages.CollectionChanged += Messages_CollectionChanged;
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_slideOutStoryboard is not null)
        {
            _slideOutStoryboard.Completed -= SlideOutStoryboard_Completed;
        }

        if (DataContext is OfficeViewModel vm)
        {
            vm.Messages.CollectionChanged -= Messages_CollectionChanged;
            vm.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    /// <summary>
    /// Listen for property changes and trigger corresponding animations.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OfficeViewModel vm)
            return;

        switch (e.PropertyName)
        {
            case nameof(OfficeViewModel.IsSidePanelOpen):
                HandleSidePanelAnimation(vm);
                break;
            case nameof(OfficeViewModel.IsActivityPulsing):
                HandlePulseAnimation(vm);
                break;
        }
    }

    /// <summary>
    /// Bug #5 fix: Side panel slide animations.
    /// </summary>
    private void HandleSidePanelAnimation(OfficeViewModel vm)
    {
        if (vm.IsSidePanelOpen)
        {
            // Opening: make elements visible first, then animate in
            _isAnimatingOut = false;
            Backdrop.Visibility = Visibility.Visible;
            SidePanel.Visibility = Visibility.Visible;
            _slideInStoryboard?.Begin(this);
        }
        else
        {
            // Closing: animate out, then collapse in Completed handler
            _isAnimatingOut = true;
            _slideOutStoryboard?.Begin(this);
        }
    }

    /// <summary>
    /// Issue #7: Start/stop the pulse animation based on IsActivityPulsing.
    /// </summary>
    private void HandlePulseAnimation(OfficeViewModel vm)
    {
        if (vm.IsActivityPulsing && !_isPulseRunning)
        {
            _pulseStoryboard?.Begin(this, true);
            _isPulseRunning = true;
        }
        else if (!vm.IsActivityPulsing && _isPulseRunning)
        {
            _pulseStoryboard?.Stop(this);
            // Reset opacity to full after stopping
            ActivityStatusPanel.Opacity = 1.0;
            _isPulseRunning = false;
        }
    }

    /// <summary>
    /// After slide-out animation completes, collapse the panel and backdrop.
    /// </summary>
    private void SlideOutStoryboard_Completed(object? sender, EventArgs e)
    {
        if (_isAnimatingOut)
        {
            Backdrop.Visibility = Visibility.Collapsed;
            SidePanel.Visibility = Visibility.Collapsed;
            _isAnimatingOut = false;
        }
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ChatScrollViewer?.ScrollToEnd();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void ObjectiveInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is OfficeViewModel vm && vm.StartCommand.CanExecute(null))
            {
                vm.StartCommand.Execute(null);
            }
        }
    }

    private void MessageInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is OfficeViewModel vm && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
            }
        }
    }

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OfficeViewModel vm)
        {
            vm.IsSidePanelOpen = false;
        }
    }

    /// <summary>
    /// Ensures mouse wheel events propagate to the main ChatScrollViewer even when
    /// child elements (MarkdownScrollViewer, TextBox, etc.) capture the event.
    /// Also safely handles ContentElement types (Run, Inline, Paragraph) that are not Visual/Visual3D.
    /// </summary>
    private void ChatScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        // Walk up from the original source to see if a nested ScrollViewer is capturing the event
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source != scrollViewer)
        {
            if (source is ScrollViewer nested && nested != scrollViewer)
            {
                // Found a nested ScrollViewer — force the main one to scroll instead
                e.Handled = true;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                return;
            }

            // Safe tree traversal: ContentElement (Run, Inline, Paragraph) is not a Visual,
            // so use LogicalTreeHelper for those types to avoid InvalidOperationException.
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        // If no nested ScrollViewer was found but the event still isn't reaching us
        // (e.g., TextBox with no scrollbar eats it), handle it anyway
        if (!e.Handled)
        {
            e.Handled = true;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        }
    }

    /// <summary>
    /// Copies the content bound to the button's Tag property to the system clipboard.
    /// Used by copy buttons on Manager and Assistant chat messages.
    /// </summary>
    private void CopyContent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string content && !string.IsNullOrEmpty(content))
        {
            try
            {
                Clipboard.SetDataObject(content, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OfficeView] Failed to copy to clipboard: {ex.Message}");
            }
        }
    }
}
