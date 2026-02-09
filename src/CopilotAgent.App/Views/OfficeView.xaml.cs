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
            ChatScrollViewer.ScrollToEnd();
        }
    }

    private void ObjectiveInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is OfficeViewModel vm && vm.StartCommand.CanExecute(null))
            {
                vm.StartCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is OfficeViewModel vm && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
                e.Handled = true;
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
    /// Issue #2 fix: Re-dispatch mouse wheel events from child TextBox/MarkdownScrollViewer
    /// controls to the parent ChatScrollViewer so scrolling isn't blocked.
    /// </summary>
    private void ChatScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // If the event source is a read-only TextBox (SelectableText style) or a
        // MarkdownScrollViewer (which has its own internal ScrollViewer), we want
        // the parent ChatScrollViewer to handle scrolling instead.
        if (e.OriginalSource is System.Windows.Controls.Primitives.ScrollBar)
            return; // Let scrollbar thumb drags work normally

        // Check if the event originated from inside a child that would capture scroll
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source != ChatScrollViewer)
        {
            if (source is TextBox tb && tb.IsReadOnly)
            {
                // Read-only TextBox — redirect scroll to parent
                ScrollParent(e);
                return;
            }

            if (source is ScrollViewer sv && sv != ChatScrollViewer)
            {
                // Nested ScrollViewer (e.g., MdXaml MarkdownScrollViewer internal)
                // Only redirect if the nested viewer can't scroll further
                if (e.Delta > 0 && sv.VerticalOffset <= 0)
                {
                    ScrollParent(e);
                    return;
                }
                if (e.Delta < 0 && sv.VerticalOffset >= sv.ScrollableHeight)
                {
                    ScrollParent(e);
                    return;
                }
                // Nested viewer can scroll — let it handle
                return;
            }

            // Walk up the tree safely: VisualTreeHelper.GetParent only works on
            // Visual / Visual3D. ContentElements like Run, Inline, Paragraph etc.
            // are NOT visuals — use their Parent property (logical tree) instead.
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
    }

    /// <summary>
    /// Scrolls the ChatScrollViewer by the mouse wheel delta and marks the event handled.
    /// </summary>
    private void ScrollParent(MouseWheelEventArgs e)
    {
        ChatScrollViewer.ScrollToVerticalOffset(ChatScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
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
