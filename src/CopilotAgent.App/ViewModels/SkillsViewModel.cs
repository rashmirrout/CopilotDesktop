using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopilotAgent.Core.Models;
using CopilotAgent.Core.Services;
using Microsoft.Extensions.Logging;

namespace CopilotAgent.App.ViewModels;

/// <summary>
/// ViewModel for skills management
/// </summary>
public partial class SkillsViewModel : ObservableObject
{
    private readonly ISkillsService _skillsService;
    private readonly ISessionManager _sessionManager;
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

    public SkillsViewModel(
        ISkillsService skillsService, 
        ISessionManager sessionManager,
        ILogger<SkillsViewModel> logger)
    {
        _skillsService = skillsService;
        _sessionManager = sessionManager;
        _logger = logger;

        _skillsService.SkillsReloaded += OnSkillsReloaded;
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

        var session = _sessionManager.ActiveSession;
        if (session == null) return;

        skill.IsEnabled = !skill.IsEnabled;
        _skillsService.SetSkillEnabled(session, skill.Id, skill.IsEnabled);
        _logger.LogDebug("Skill {SkillId} enabled: {Enabled}", skill.Id, skill.IsEnabled);
    }

    [RelayCommand]
    private void OpenSkillsFolder()
    {
        try
        {
            var folder = _skillsService.GetPersonalSkillsFolder();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
    private void ApplyFilter()
    {
        RefreshSkillsList();
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

            Skills.Add(new SkillItemViewModel(skill, enabledSkills.Contains(skill.Id)));
        }
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
/// ViewModel for individual skill items
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

    [ObservableProperty]
    private bool _isEnabled;

    public SkillItemViewModel(SkillDefinition skill, bool isEnabled)
    {
        Skill = skill;
        _isEnabled = isEnabled;
    }
}