using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Code-behind for PanelView. Handles keyboard shortcuts for input fields,
/// scroll propagation, side panel slide animations, auto-scroll for chat/discussion,
/// and agent selection via mouse clicks on agent pills/cards.
/// </summary>
public partial class PanelView : UserControl
{
    private Storyboard? _slideInStoryboard;
    private Storyboard? _slideOutStoryboard;
    private bool _isAnimatingOut;

    public PanelView()
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

        if (_slideOutStoryboard is not null)
        {
            _slideOutStoryboard.Completed += SlideOutStoryboard_Completed;
        }

        if (DataContext is PanelViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;

            // Auto-scroll when new messages arrive
            vm.HeadChatMessages.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(() => ScrollToBottom(HeadChatScroller));

            vm.DiscussionMessages.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(() => ScrollToBottom(DiscussionScroller));
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_slideOutStoryboard is not null)
        {
            _slideOutStoryboard.Completed -= SlideOutStoryboard_Completed;
        }

        if (DataContext is PanelViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    /// <summary>
    /// Listen for IsSidePanelOpen changes and trigger slide animations.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PanelViewModel vm)
            return;

        if (e.PropertyName == nameof(PanelViewModel.IsSidePanelOpen))
        {
            HandleSidePanelAnimation(vm);
        }
    }

    private void HandleSidePanelAnimation(PanelViewModel vm)
    {
        if (vm.IsSidePanelOpen)
        {
            _isAnimatingOut = false;
            Backdrop.Visibility = Visibility.Visible;
            SidePanel.Visibility = Visibility.Visible;
            _slideInStoryboard?.Begin(this);
        }
        else
        {
            _isAnimatingOut = true;
            _slideOutStoryboard?.Begin(this);
        }
    }

    private void SlideOutStoryboard_Completed(object? sender, EventArgs e)
    {
        if (_isAnimatingOut)
        {
            Backdrop.Visibility = Visibility.Collapsed;
            SidePanel.Visibility = Visibility.Collapsed;
            _isAnimatingOut = false;
        }
    }

    /// <summary>
    /// Clicking the backdrop closes the side panel.
    /// </summary>
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PanelViewModel vm)
        {
            vm.IsSidePanelOpen = false;
        }
    }

    /// <summary>
    /// Enter → submit/send; Shift+Enter → new line.
    /// </summary>
    private void MainInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is not PanelViewModel vm) return;

            // Use the unified command — it routes based on orchestrator phase
            if (vm.SendInputCommand.CanExecute(null))
            {
                vm.SendInputCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Click on an agent pill in the discussion header to select it in the inspector.
    /// </summary>
    private void AgentPill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is PanelAgentInspectorItem agent
            && DataContext is PanelViewModel vm)
        {
            vm.SelectAgentCommand.Execute(agent);
        }
    }

    /// <summary>
    /// Click on an agent card in the inspector list to select it.
    /// </summary>
    private void AgentCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is PanelAgentInspectorItem agent
            && DataContext is PanelViewModel vm)
        {
            vm.SelectAgentCommand.Execute(agent);
        }
    }

    /// <summary>
    /// Clicking the synthesis backdrop dismisses it.
    /// </summary>
    private void SynthesisBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PanelViewModel vm)
        {
            vm.DismissSynthesisCommand.Execute(null);
        }
    }

    /// <summary>
    /// Prevent clicks inside the synthesis panel from bubbling to the backdrop.
    /// </summary>
    private void SynthesisPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// Ensures mouse wheel events propagate to the discussion ScrollViewer even when
    /// child elements capture the event. Safely handles ContentElement types.
    /// </summary>
    private void DiscussionScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var source = e.OriginalSource as DependencyObject;
        while (source != null && source != scrollViewer)
        {
            if (source is ScrollViewer nested && nested != scrollViewer)
            {
                e.Handled = true;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                return;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        if (!e.Handled)
        {
            e.Handled = true;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        }
    }

    /// <summary>
    /// Scrolls a ScrollViewer to the bottom.
    /// </summary>
    private static void ScrollToBottom(ScrollViewer? scrollViewer)
    {
        scrollViewer?.ScrollToEnd();
    }
}