using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CopilotAgent.App.ViewModels;

namespace CopilotAgent.App.Views;

/// <summary>
/// Code-behind for AgentTeamView. Handles keyboard shortcuts for input fields,
/// scroll propagation for the main content area, and side panel slide animations.
/// </summary>
public partial class AgentTeamView : UserControl
{
    private Storyboard? _slideInStoryboard;
    private Storyboard? _slideOutStoryboard;
    private bool _isAnimatingOut;

    public AgentTeamView()
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

        // Wire slide-out completion to collapse the panel after animation
        if (_slideOutStoryboard is not null)
        {
            _slideOutStoryboard.Completed += SlideOutStoryboard_Completed;
        }

        // Wire side panel animation when ViewModel is set
        if (DataContext is AgentTeamViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_slideOutStoryboard is not null)
        {
            _slideOutStoryboard.Completed -= SlideOutStoryboard_Completed;
        }

        if (DataContext is AgentTeamViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    /// <summary>
    /// Listen for IsSidePanelOpen changes and trigger slide animations.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AgentTeamViewModel vm)
            return;

        if (e.PropertyName == nameof(AgentTeamViewModel.IsSidePanelOpen))
        {
            HandleSidePanelAnimation(vm);
        }
    }

    /// <summary>
    /// Side panel slide-in / slide-out animations.
    /// </summary>
    private void HandleSidePanelAnimation(AgentTeamViewModel vm)
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

    /// <summary>
    /// Clicking the backdrop closes the side panel.
    /// </summary>
    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AgentTeamViewModel vm)
        {
            vm.IsSidePanelOpen = false;
        }
    }

    /// <summary>
    /// Enter → submit task; Shift+Enter → new line.
    /// </summary>
    private void TaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is AgentTeamViewModel vm && vm.SubmitTaskCommand.CanExecute(null))
            {
                vm.SubmitTaskCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Enter → inject instruction; Shift+Enter → new line.
    /// </summary>
    private void InjectionInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is AgentTeamViewModel vm && vm.InjectInstructionCommand.CanExecute(null))
            {
                vm.InjectInstructionCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Enter → send clarification response; Shift+Enter → new line.
    /// </summary>
    private void ClarificationInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (DataContext is AgentTeamViewModel vm && vm.RespondToClarificationCommand.CanExecute(null))
            {
                vm.RespondToClarificationCommand.Execute(null);
            }
        }
    }

    /// <summary>
    /// Ensures mouse wheel events propagate to the main ScrollViewer even when
    /// child elements (MarkdownScrollViewer, TextBox, ListBox, etc.) capture the event.
    /// Safely handles ContentElement types (Run, Inline, Paragraph) that are not Visual/Visual3D.
    /// </summary>
    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
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
}