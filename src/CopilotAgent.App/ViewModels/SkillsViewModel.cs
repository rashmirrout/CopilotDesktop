using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// Represents the pending state of a skill toggle
/// </summary>
public enum SkillPendingState
{
    /// <summary>No pending change</summary>
    None,
    /// <summary>Skill will be enabled when applied</summary>
    PendingEnable,
    /// <summary>Skill will be disabled when applied</summary>
    PendingDisable
}

/// <summary>
/// ViewModel for skills management with batch apply pattern
/// </summary>
public partial class SkillsViewModel : ObservableObject
{
    private readonly ISkillsService _skillsService;
    private readonly ISessionManager _sessionManager;
    private readonly ICopilotService _copilotService;
    private readonly ILogger<SkillsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<SkillItemViewModel> _skills = new();

    [ObservableProperty]
    private SkillItemViewModel? _selectedSkill;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showBuiltIn = true;

    [ObservableProperty]
    private bool _showPersonal = true;

    [ObservableProperty]
    private bool _showRepository = true;

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private int _pendingChangesCount;

    [ObservableProperty]
    private string _skillsFolderPath = string.Empty;

    [ObservableProperty]
    private bool _isApplying;

    public SkillsViewModel(
        ISkillsService skillsService,
        ISessionManager sessionManager,
        ICopilotService copilotService,
        ILogger<SkillsViewModel> logger)
    {
        _skillsService = skillsService;
        _sessionManager = sessionManager;
        _copilotService = copilotService;
        _logger = logger;

        _skillsService.SkillsReloaded += OnSkillsReloaded;
        SkillsFolderPath = _skillsService.GetPersonalSkillsFolder();
    }

    public async Task InitializeAsync()
    {
        await _skillsService.ScanSkillsAsync();
        RefreshSkillsList();
    }

    [RelayCommand]
    private async Task RefreshSkillsAsync()
    {
        try
        {
            await _skillsService.ScanSkillsAsync();
            RefreshSkillsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh skills");
        }
    }

    [RelayCommand]
    private void ToggleSkill(SkillItemViewModel? skill)
    {
        if (skill == null) return;

        // Toggle the pending state (not the actual enabled state)
        skill.TogglePendingState();

        // Update pending changes tracking
        UpdatePendingChangesState();
        
        _logger.LogDebug("Skill {SkillId} pending state: {State}", skill.Id, skill.PendingState);
    }

    [RelayCommand(CanExecute = nameof(CanApplyChanges))]
    private async Task ApplyChangesAsync()
    {
        var session = _sessionManager.ActiveSession;
        if (session == null) return;

        // Show confirmation dialog
        var result = MessageBox.Show(
            "Applying skill changes requires recreating the session.\n\n" +
            "• Message history will be preserved\n" +
            "• Current operation (if any) will be interrupted\n\n" +
            "Continue?",
            "Restart Session Required",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsApplying = true;

        try
        {
            // Apply all pending changes to the session
            foreach (var skill in Skills)
            {
                if (skill.PendingState == SkillPendingState.PendingEnable)
                {
                    _skillsService.SetSkillEnabled(session, skill.Id, true);
                    skill.CommitPendingState();
                }
                else if (skill.PendingState == SkillPendingState.PendingDisable)
                {
                    _skillsService.SetSkillEnabled(session, skill.Id, false);
                    skill.CommitPendingState();
                }
            }

            // Recreate the session with new skill configuration
            await _copilotService.RecreateSessionAsync(session, new SessionRecreateOptions
            {
                // Keep same model and working directory, just update skills
            });

            UpdatePendingChangesState();
            _logger.LogInformation("Applied skill changes and recreated session");

            MessageBox.Show(
                "Skills updated successfully. Session has been restarted.",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply skill changes");
            MessageBox.Show(
                $"Failed to apply changes: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsApplying = false;
        }
    }

    private bool CanApplyChanges() => HasPendingChanges && !IsApplying;

    [RelayCommand]
    private void DiscardChanges()
    {
        // Reset all pending states
        foreach (var skill in Skills)
        {
            skill.ResetPendingState();
        }
        UpdatePendingChangesState();
    }

    [RelayCommand]
    private void OpenSkillsFolder()
    {
        try
        {
            var folder = _skillsService.GetPersonalSkillsFolder();
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open skills folder");
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ViewSkillContent()
    {
        if (SelectedSkill == null) return;

        var skill = _skillsService.GetSkill(SelectedSkill.Id);
        if (skill == null) return;

        // Show skill content in a message box for now
        // Could be enhanced with a dedicated dialog
        MessageBox.Show(
            skill.Content,
            $"Skill: {skill.Name}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task UploadSkillAsync()
    {
        try
        {
            // Open file dialog
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                Title = "Select Skill File"
            };

            if (dialog.ShowDialog() == true)
            {
                var filePath = dialog.FileName;
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                var content = await System.IO.File.ReadAllTextAsync(filePath);

                // Parse basic metadata from file
                var (name, description) = ParseSkillMetadataFromContent(content, fileName);

                // Create the skill
                var skill = await _skillsService.CreateSkillAsync(name, description, content);
                if (skill != null)
                {
                    await RefreshSkillsAsync();
                    MessageBox.Show(
                        $"Skill '{name}' added successfully.",
                        "Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload skill");
            MessageBox.Show($"Failed to upload skill: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (string name, string description) ParseSkillMetadataFromContent(string content, string fallbackName)
    {
        string name = fallbackName;
        string description = string.Empty;

        // Try to extract from YAML front matter
        if (content.StartsWith("---"))
        {
            var endIndex = content.IndexOf("---", 3);
            if (endIndex > 0)
            {
                var frontMatter = content.Substring(3, endIndex - 3);
                foreach (var line in frontMatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = line.Substring(0, colonIndex).Trim().ToLower();
                        var value = line.Substring(colonIndex + 1).Trim();

                        if (key == "name" || key == "title")
                            name = value;
                        else if (key == "description")
                            description = value;
                    }
                }
            }
        }
        else
        {
            // Try first heading as name
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("# "))
                {
                    name = line.Substring(2).Trim();
                    break;
                }
            }
        }

        return (name, description);
    }

    private void RefreshSkillsList()
    {
        var session = _sessionManager.ActiveSession;
        var workingDir = session?.WorkingDirectory;
        var enabledSkills = session?.EnabledSkills ?? new List<string>();

        var allSkills = _skillsService.GetSkillsForPath(workingDir);

        Skills.Clear();
        foreach (var skill in allSkills)
        {
            // Apply source filters
            if (!ShowBuiltIn && skill.Source == SkillSource.BuiltIn) continue;
            if (!ShowPersonal && skill.Source == SkillSource.Personal) continue;
            if (!ShowRepository && skill.Source == SkillSource.Repository) continue;

            // Apply text filter
            if (!string.IsNullOrEmpty(FilterText))
            {
                var filter = FilterText.ToLower();
                if (!skill.Name.ToLower().Contains(filter) &&
                    !skill.Description.ToLower().Contains(filter))
                {
                    continue;
                }
            }

            var isEnabled = enabledSkills.Contains(skill.Id);
            Skills.Add(new SkillItemViewModel(skill, isEnabled));
        }

        UpdatePendingChangesState();
    }

    private void UpdatePendingChangesState()
    {
        var pendingCount = Skills.Count(s => s.PendingState != SkillPendingState.None);
        PendingChangesCount = pendingCount;
        HasPendingChanges = pendingCount > 0;
        ApplyChangesCommand.NotifyCanExecuteChanged();
    }

    private void OnSkillsReloaded(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(RefreshSkillsList);
    }

    partial void OnFilterTextChanged(string value) => RefreshSkillsList();
    partial void OnShowBuiltInChanged(bool value) => RefreshSkillsList();
    partial void OnShowPersonalChanged(bool value) => RefreshSkillsList();
    partial void OnShowRepositoryChanged(bool value) => RefreshSkillsList();
}

/// <summary>
/// ViewModel for individual skill items with pending state tracking
/// </summary>
public partial class SkillItemViewModel : ObservableObject
{
    public SkillDefinition Skill { get; }

    public string Id => Skill.Id;
    public string Name => Skill.Name;
    public string Description => Skill.Description;
    public SkillSource Source => Skill.Source;
    public string SourceDisplay => Source switch
    {
        SkillSource.BuiltIn => "Built-in",
        SkillSource.Personal => "Personal",
        SkillSource.Repository => "Repository",
        SkillSource.Remote => "Remote",
        _ => "Unknown"
    };

    public string SourceIcon => Source switch
    {
        SkillSource.BuiltIn => "⚙",
        SkillSource.Personal => "👤",
        SkillSource.Repository => "📁",
        SkillSource.Remote => "☁",
        _ => "?"
    };

    /// <summary>
    /// Current actual enabled state (as saved in session)
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Pending state for batch apply
    /// </summary>
    [ObservableProperty]
    private SkillPendingState _pendingState = SkillPendingState.None;

    /// <summary>
    /// Display color based on current and pending state
    /// </summary>
    public string StateColor => PendingState switch
    {
        SkillPendingState.PendingEnable => "#FF9800",   // Orange - pending enable
        SkillPendingState.PendingDisable => "#FF9800", // Orange - pending disable
        _ => IsEnabled ? "#4CAF50" : "#9E9E9E"         // Green if enabled, Gray if disabled
    };

    /// <summary>
    /// Checkbox state: shows target state (enabled or pending-enable)
    /// </summary>
    public bool DisplayCheckState => PendingState == SkillPendingState.PendingEnable || 
                                      (PendingState == SkillPendingState.None && IsEnabled);

    /// <summary>
    /// Status text for display
    /// </summary>
    public string StatusText => PendingState switch
    {
        SkillPendingState.PendingEnable => "Will be enabled",
        SkillPendingState.PendingDisable => "Will be disabled",
        _ => IsEnabled ? "Enabled" : "Disabled"
    };

    public SkillItemViewModel(SkillDefinition skill, bool isEnabled)
    {
        Skill = skill;
        _isEnabled = isEnabled;
    }

    /// <summary>
    /// Toggle the pending state when user clicks
    /// </summary>
    public void TogglePendingState()
    {
        if (PendingState == SkillPendingState.None)
        {
            // Start a pending change
            PendingState = IsEnabled ? SkillPendingState.PendingDisable : SkillPendingState.PendingEnable;
        }
        else
        {
            // Cancel the pending change
            PendingState = SkillPendingState.None;
        }

        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Commit the pending state to actual state
    /// </summary>
    public void CommitPendingState()
    {
        if (PendingState == SkillPendingState.PendingEnable)
        {
            IsEnabled = true;
        }
        else if (PendingState == SkillPendingState.PendingDisable)
        {
            IsEnabled = false;
        }
        PendingState = SkillPendingState.None;

        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Reset pending state without committing
    /// </summary>
    public void ResetPendingState()
    {
        PendingState = SkillPendingState.None;
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnPendingStateChanged(SkillPendingState value)
    {
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
    }
}