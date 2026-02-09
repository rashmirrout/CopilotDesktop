using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CopilotAgent.App.Views;

/// <summary>
/// Interaction logic for IterativeTaskView.xaml
/// </summary>
public partial class IterativeTaskView : UserControl
{
    public IterativeTaskView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Ensures mouse wheel events propagate to the iteration history ScrollViewer even when
    /// child elements (Expander, TextBox, etc.) capture the event.
    /// Safely handles ContentElement types (Run, Inline, Paragraph) that are not Visual/Visual3D.
    /// </summary>
    private void IterationScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
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
