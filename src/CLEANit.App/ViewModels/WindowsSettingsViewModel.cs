using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Models;
using CLEANit.Core.Tweaks;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>One documented Windows setting: switch = wanted state, applied with the Apply button.</summary>
public sealed partial class TweakRowViewModel : ObservableObject
{
    private readonly Action _changed;

    public TweakRowViewModel(TweakDefinition definition, bool applied, bool changedElsewhere, bool enterprise, Action changed)
    {
        Definition = definition;
        _changed = changed;
        _isApplied = applied;
        _wanted = applied;
        ChangedElsewhere = changedElsewhere;
        Values = string.Join("\n", definition.ValuesFor(enterprise).Select(v => v.Display));
    }

    public TweakDefinition Definition { get; }

    /// <summary>Current state in Windows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private bool _isApplied;

    /// <summary>State the user wants (switch).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    private bool _wanted;

    partial void OnWantedChanged(bool value) => _changed();

    public bool IsPending => Wanted != IsApplied;

    public bool ChangedElsewhere { get; private set; }

    public string Values { get; }

    public string Name => Definition.DisplayName(Loc.Language);

    public string Description => Definition.DisplayDescription(Loc.Language);

    public string Category => Definition.DisplayCategory(Loc.Language);

    public string? Note => Definition.DisplayNote(Loc.Language);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool IsSafe => Definition.Risk == RiskLevel.Safe;

    public bool IsModerate => Definition.Risk == RiskLevel.Moderate;

    public bool IsDeep => Definition.Risk == RiskLevel.Deep;

    public string RiskText => Loc.T("Risk" + Definition.Risk);

    public bool IsRecommended => Definition.Recommended;

    public string SourceText => Loc.T(Definition.Source == "Settings" ? "TweakSourceSettings" : "TweakSourcePolicy");

    public string EffectText => Loc.T("TweakEffect" + Definition.Effect);

    public bool HasEffect => Definition.Effect != TweakEffect.Immediately;

    public void Refresh(bool applied, bool changedElsewhere)
    {
        IsApplied = applied;
        Wanted = applied;
        ChangedElsewhere = changedElsewhere;
        OnPropertyChanged(nameof(ChangedElsewhere));
    }

    public void RefreshText() => OnPropertyChanged(string.Empty);
}

public sealed class TweakGroupViewModel(string name) : ObservableObject
{
    public string Name { get; set; } = name;

    /// <summary>"Recommended" / "Optional" above the first category of each part.</summary>
    public string? Section { get; set; }

    public bool HasSection => !string.IsNullOrEmpty(Section);

    public ObservableCollection<TweakRowViewModel> Items { get; } = [];
}

/// <summary>
/// Windows settings: documented switches only (Microsoft policies or the value of the Windows Settings switch).
/// Every change is backed up first and can be reverted here or in Settings › Backups.
/// </summary>
public sealed partial class WindowsSettingsViewModel(MainViewModel main) : PageViewModel(main)
{
    private readonly List<TweakRowViewModel> _all = [];
    private TweakEngine? _engine;

    public override string TitleKey => "NavTweaks";

    public ObservableCollection<TweakGroupViewModel> Groups { get; } = [];

    public bool IsDomainJoined { get; } = OperatingSystem.IsWindows() && SystemInfo.IsDomainJoined();

    public string SystemText { get; } = OperatingSystem.IsWindows() ? SystemInfo.ProductName() : Environment.OSVersion.ToString();

    public int PendingCount => _all.Count(r => r.IsPending);

    public string PendingText => Loc.F("TweaksPending", PendingCount, _all.Count(r => r.IsApplied), _all.Count);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    // ------------------------------------------------------------------ Filter pills (OR within a group, AND between groups)

    [ObservableProperty]
    private bool _filterSafe;

    [ObservableProperty]
    private bool _filterModerate;

    [ObservableProperty]
    private bool _filterRecommended;

    [ObservableProperty]
    private bool _filterPolicy;

    [ObservableProperty]
    private bool _filterSettings;

    [ObservableProperty]
    private bool _filterApplied;

    [ObservableProperty]
    private bool _filterPending;

    partial void OnFilterSafeChanged(bool value) => BuildGroups();

    partial void OnFilterModerateChanged(bool value) => BuildGroups();

    partial void OnFilterRecommendedChanged(bool value) => BuildGroups();

    partial void OnFilterPolicyChanged(bool value) => BuildGroups();

    partial void OnFilterSettingsChanged(bool value) => BuildGroups();

    partial void OnFilterAppliedChanged(bool value) => BuildGroups();

    partial void OnFilterPendingChanged(bool value) => BuildGroups();

    public bool HasModerate => _all.Any(r => r.IsModerate);

    public bool IsFiltered => FilterSafe || FilterModerate || FilterRecommended || FilterPolicy || FilterSettings || FilterApplied || FilterPending;

    public bool HasVisible => Groups.Count > 0;

    private bool Matches(TweakRowViewModel row)
    {
        var risk = (!FilterSafe && !FilterModerate) || (FilterSafe && row.IsSafe) || (FilterModerate && row.IsModerate);
        var recommended = !FilterRecommended || row.IsRecommended;
        var source = (!FilterPolicy && !FilterSettings) || (FilterPolicy && row.Definition.Source != "Settings") ||
                     (FilterSettings && row.Definition.Source == "Settings");
        var state = (!FilterApplied && !FilterPending) || (FilterApplied && row.IsApplied) || (FilterPending && row.IsPending);
        return risk && recommended && source && state;
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterSafe = FilterModerate = FilterRecommended = FilterPolicy = FilterSettings = FilterApplied = FilterPending = false;
    }

    public override void OnActivated()
    {
        if (_all.Count == 0)
        {
            Load();
        }
    }

    public override void OnExpertModeChanged()
    {
        base.OnExpertModeChanged();
        Load();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in _all)
        {
            row.RefreshText();
        }

        BuildGroups();
        OnPropertyChanged(nameof(PendingText));
    }

    private void Load()
    {
        _all.Clear();
        if (!OperatingSystem.IsWindows())
        {
            BuildGroups();
            return;
        }

        _engine = TweakEngineFactory.Create();
        foreach (var tweak in TweakCatalog.For(SystemInfo.Build(), Ui.ExpertMode, _engine.Enterprise))
        {
            _all.Add(new TweakRowViewModel(tweak, _engine.IsApplied(tweak), _engine.IsChangedElsewhere(tweak), _engine.Enterprise, Changed));
        }

        BuildGroups();
        Changed();
    }

    /// <summary>Two parts: the recommended switches first, then the optional ones – each grouped by category.</summary>
    private void BuildGroups()
    {
        Groups.Clear();
        foreach (var part in new[] { true, false })
        {
            var first = true;
            foreach (var group in _all.Where(r => r.IsRecommended == part && Matches(r)).GroupBy(r => r.Category))
            {
                var vm = new TweakGroupViewModel(group.Key)
                {
                    Section = first ? Loc.T(part ? "TweakGroupRecommended" : "TweakGroupOptional") : null
                };
                first = false;
                foreach (var row in group)
                {
                    vm.Items.Add(row);
                }

                Groups.Add(vm);
            }
        }

        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(HasVisible));
        OnPropertyChanged(nameof(HasModerate));
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(PendingText));
        ApplyCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_engine is null)
        {
            Load();
            return;
        }

        foreach (var row in _all)
        {
            row.Refresh(_engine.IsApplied(row.Definition), _engine.IsChangedElsewhere(row.Definition));
        }

        Changed();
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var row in _all)
        {
            row.Wanted = row.IsRecommended || row.IsApplied;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in _all)
        {
            row.Wanted = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in _all)
        {
            row.Wanted = false;
        }
    }

    private bool HasPending() => _all.Any(r => r.IsPending);

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void Discard()
    {
        foreach (var row in _all)
        {
            row.Wanted = row.IsApplied;
        }
    }

    [RelayCommand]
    private void OpenDocs(TweakRowViewModel? row) => AppEnvironment.OpenUrl(row?.Definition.DocUrl);

    private bool CanApply() => !IsBusy && HasPending();

    /// <summary>Applies switched-on settings and reverts switched-off ones – after a confirmation, with a backup first.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_engine is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var apply = _all.Where(r => r.Wanted && !r.IsApplied).ToList();
        var revert = _all.Where(r => !r.Wanted && r.IsApplied).ToList();
        var lines = apply.Select(r => "✓ " + r.Name).Concat(revert.Select(r => "↺ " + r.Name)).ToList();
        var effects = apply.Concat(revert).Where(r => r.HasEffect).Select(r => r.EffectText).Distinct().ToList();
        var message = Loc.F("TweaksConfirmText", string.Join("\n", lines.Take(16)) + (lines.Count > 16 ? "\n…" : ""))
                      + (effects.Count > 0 ? "\n\n" + string.Join("\n", effects) : "")
                      + (IsDomainJoined ? "\n\n" + Loc.T("TweaksDomainNote") : "");
        var answer = await Host.ConfirmAsync(Loc.F("TweaksConfirmTitle", lines.Count), message, Loc.T("BtnApply"), danger: false, confirmIcon: "IconCheck");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var engine = _engine;
            var user = SystemInfo.CurrentUser();
            var skipped = await Task.Run(() =>
            {
                var (_, skippedTweaks) = engine.Apply(apply.Select(r => r.Definition).ToList(), user);
                engine.Revert(revert.Select(r => r.Definition).ToList());
                return skippedTweaks;
            });

            Refresh();
            Main.ShowToast(skipped.Count == 0
                ? Loc.F("ToastTweaksApplied", apply.Count, revert.Count)
                : Loc.F("ToastTweaksSkipped", string.Join(", ", skipped.Select(s => s.DisplayName(Loc.Language)))), isError: skipped.Count > 0);
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
            Refresh();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
