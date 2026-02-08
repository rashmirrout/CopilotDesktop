using System.Collections.Specialized;
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
    public OfficeView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Wire auto-scroll when Messages collection changes
        if (DataContext is OfficeViewModel vm)
        {
            vm.Messages.CollectionChanged += Messages_CollectionChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is OfficeViewModel vm)
        {
            vm.Messages.CollectionChanged -= Messages_CollectionChanged;
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