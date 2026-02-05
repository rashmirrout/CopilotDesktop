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
/// ViewModel for skills management with batch apply pattern.
/// 
/// Skills are disabled by default. User must explicitly enable them.
/// Enabling a skill removes it from the session's DisabledSkills list.
/// Changes require session recreation to take effect.
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
    private string _sdkSkillsFolderPath = string.Empty;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private int _totalSkillsCount;

    [ObservableProperty]
    private int _enabledSkillsCount;

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
        SdkSkillsFolderPath = _skillsService.GetSdkSkillsFolder();
    }

    public async Task InitializeAsync()
    {
        await _skillsService.ScanSkillsAsync();
        RefreshSkillsList();
        
        // Initialize session's disabled skills if not already done
        var session = _sessionManager.ActiveSession;
        if (session != null && session.DisabledSkills == null)
        {
            _skillsService.InitializeSessionDisabledSkills(session);
            RefreshSkillsList();
        }
    }

    [RelayCommand]
    private async Task RefreshSkillsAsync()
    {
        try
        {
            await _skillsService.ScanSkillsAsync();
            RefreshSkillsList();
            _logger.LogInformation("Refreshed skills list. Found {Count} skills", _skillsService.GetSkills().Count);
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
        
        _logger.LogDebug("Skill '{SkillName}' pending state: {State}", skill.Name, skill.PendingState);
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
            // Apply all pending changes to the session using the new DisabledSkills model
            foreach (var skill in Skills)
            {
                if (skill.PendingState == SkillPendingState.PendingEnable)
                {
                    // Enable = remove from DisabledSkills
                    _skillsService.SetSkillEnabled(session, skill.Name, true);
                    skill.CommitPendingState();
                    _logger.LogInformation("Enabled skill '{SkillName}' for session {SessionId}", 
                        skill.Name, session.SessionId);
                }
                else if (skill.PendingState == SkillPendingState.PendingDisable)
                {
                    // Disable = add to DisabledSkills
                    _skillsService.SetSkillEnabled(session, skill.Name, false);
                    skill.CommitPendingState();
                    _logger.LogInformation("Disabled skill '{SkillName}' for session {SessionId}", 
                        skill.Name, session.SessionId);
                }
            }

            // Recreate the session with new skill configuration
            await _copilotService.RecreateSessionAsync(session, new SessionRecreateOptions
            {
                // Keep same model and working directory, just update skills
            });

            UpdatePendingChangesState();
            _logger.LogInformation("Applied skill changes and recreated session. {EnabledCount} skills enabled, {DisabledCount} skills disabled",
                EnabledSkillsCount, TotalSkillsCount - EnabledSkillsCount);

            MessageBox.Show(
                $"Skills updated successfully.\n\n" +
                $"• {EnabledSkillsCount} skills enabled\n" +
                $"• {TotalSkillsCount - EnabledSkillsCount} skills disabled\n\n" +
                $"Session has been restarted.",
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
    private void OpenSdkSkillsFolder()
    {
        try
        {
            var folder = _skillsService.GetSdkSkillsFolder();
            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SDK skills folder");
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ViewSkillContent()
    {
        if (SelectedSkill == null) return;

        var skill = _skillsService.GetSkillByName(SelectedSkill.Name);
        if (skill == null) return;

        // Show skill content in a message box for now
        // Could be enhanced with a dedicated dialog
        var content = skill.Content;
        if (content.Length > 2000)
        {
            content = content[..2000] + "\n\n... (truncated)";
        }
        
        MessageBox.Show(
            $"Name: {skill.Name}\n" +
            $"Display Name: {skill.EffectiveDisplayName}\n" +
            $"Format: {skill.Format}\n" +
            $"Source: {skill.Source}\n" +
            $"Path: {skill.FilePath}\n\n" +
            $"--- Content ---\n{content}",
            $"Skill: {skill.EffectiveDisplayName}",
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

                // Create the skill in SDK format
                var skill = await _skillsService.CreateSkillAsync(name, description, content);
                if (skill != null)
                {
                    await RefreshSkillsAsync();
                    MessageBox.Show(
                        $"Skill '{skill.EffectiveDisplayName}' added successfully.\n\n" +
                        $"Created in SDK format at:\n{skill.FilePath}\n\n" +
                        $"Note: The skill is disabled by default. Enable it to use.",
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

    [RelayCommand]
    private void EnableAllSkills()
    {
        foreach (var skill in Skills)
        {
            if (!skill.IsEnabled && skill.PendingState != SkillPendingState.PendingEnable)
            {
                skill.TogglePendingState(); // Will set to PendingEnable
            }
            else if (skill.PendingState == SkillPendingState.PendingDisable)
            {
                skill.ResetPendingState();
            }
        }
        UpdatePendingChangesState();
    }

    [RelayCommand]
    private void DisableAllSkills()
    {
        foreach (var skill in Skills)
        {
            if (skill.IsEnabled && skill.PendingState != SkillPendingState.PendingDisable)
            {
                skill.TogglePendingState(); // Will set to PendingDisable
            }
            else if (skill.PendingState == SkillPendingState.PendingEnable)
            {
                skill.ResetPendingState();
            }
        }
        UpdatePendingChangesState();
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
                    !skill.EffectiveDisplayName.ToLower().Contains(filter) &&
                    !skill.Description.ToLower().Contains(filter))
                {
                    continue;
                }
            }

            // Use the new DisabledSkills model to determine enabled state
            // A skill is enabled if it's NOT in the DisabledSkills list
            var isEnabled = session != null && _skillsService.IsSkillEnabled(session, skill.Name);
            Skills.Add(new SkillItemViewModel(skill, isEnabled));
        }

        // Update counts
        TotalSkillsCount = Skills.Count;
        EnabledSkillsCount = Skills.Count(s => s.IsEnabled);
        
        UpdatePendingChangesState();
    }

    private void UpdatePendingChangesState()
    {
        var pendingCount = Skills.Count(s => s.PendingState != SkillPendingState.None);
        PendingChangesCount = pendingCount;
        HasPendingChanges = pendingCount > 0;
        
        // Recalculate enabled count considering pending states
        EnabledSkillsCount = Skills.Count(s => 
            (s.PendingState == SkillPendingState.PendingEnable) ||
            (s.PendingState == SkillPendingState.None && s.IsEnabled));
        
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
    
    /// <summary>
    /// Technical name used by SDK (matches skill.json "name" field)
    /// </summary>
    public string Name => Skill.Name;
    
    /// <summary>
    /// Human-friendly display name
    /// </summary>
    public string DisplayName => Skill.EffectiveDisplayName;
    
    public string Description => Skill.Description;
    public SkillSource Source => Skill.Source;
    public SkillFormat Format => Skill.Format;
    
    public string SourceDisplay => Source switch
    {
        SkillSource.BuiltIn => "Built-in",
        SkillSource.Personal => "Personal",
        SkillSource.Repository => "Repository",
        SkillSource.Remote => "Remote",
        _ => "Unknown"
    };

    public string FormatDisplay => Format switch
    {
        SkillFormat.SdkJson => "SDK Format",
        SkillFormat.Markdown => "Markdown",
        _ => "Unknown"
    };

    public string SourceIcon => Source switch
    {
        SkillSource.BuiltIn => "⚙️",
        SkillSource.Personal => "👤",
        SkillSource.Repository => "📁",
        SkillSource.Remote => "☁️",
        _ => "?"
    };

    public string FormatIcon => Format switch
    {
        SkillFormat.SdkJson => "📦",
        SkillFormat.Markdown => "📝",
        _ => "?"
    };

    /// <summary>
    /// Current actual enabled state (based on session's DisabledSkills list)
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
        SkillPendingState.PendingEnable => "⏳ Will be enabled",
        SkillPendingState.PendingDisable => "⏳ Will be disabled",
        _ => IsEnabled ? "✅ Enabled" : "⬜ Disabled"
    };

    /// <summary>
    /// Tooltip with full skill info
    /// </summary>
    public string Tooltip => $"Name: {Name}\n" +
                            $"Display: {DisplayName}\n" +
                            $"Format: {FormatDisplay}\n" +
                            $"Source: {SourceDisplay}\n" +
                            $"Status: {StatusText.Replace("⏳ ", "").Replace("✅ ", "").Replace("⬜ ", "")}";

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
        OnPropertyChanged(nameof(Tooltip));
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
        OnPropertyChanged(nameof(Tooltip));
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
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnPendingStateChanged(SkillPendingState value)
    {
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(DisplayCheckState));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Tooltip));
    }
}