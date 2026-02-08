using System.Collections.Specialized;
using System.ComponentModel;
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
    private bool _isAnimatingOut;

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
    /// Bug #5 fix: Listen for IsSidePanelOpen changes and trigger slide animations.
    /// </summary>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfficeViewModel.IsSidePanelOpen))
            return;

        if (sender is not OfficeViewModel vm)
            return;

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
}