using System.Windows;

namespace CopilotAgent.App.Views;

/// <summary>
/// Dialog for renaming a session
/// </summary>
public partial class RenameSessionDialog : Window
{
    /// <summary>
    /// Gets the new session name entered by the user
    /// </summary>
    public string SessionName
    {
        get => SessionNameTextBox.Text;
        set => SessionNameTextBox.Text = value;
    }

    public RenameSessionDialog()
    {
        InitializeComponent();
        Loaded += (s, e) =>
        {
            SessionNameTextBox.Focus();
            SessionNameTextBox.SelectAll();
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SessionName))
        {
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}