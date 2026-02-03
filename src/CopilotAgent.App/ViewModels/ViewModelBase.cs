using CommunityToolkit.Mvvm.ComponentModel;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// Base class for all ViewModels providing MVVM infrastructure
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _errorMessage;

    /// <summary>
    /// Indicates if the ViewModel is performing an operation
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Current error message, if any
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Clears the current error message
    /// </summary>
    public void ClearError()
    {
        ErrorMessage = null;
    }

    /// <summary>
    /// Sets an error message
    /// </summary>
    protected void SetError(string message)
    {
        ErrorMessage = message;
    }

    /// <summary>
    /// Called when the ViewModel is initialized
    /// </summary>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the ViewModel is being disposed
    /// </summary>
    public virtual void Cleanup()
    {
    }
}