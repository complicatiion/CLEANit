using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using CLEANit.Core.Rules;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>One rule in the list: selection, analysis result, details.</summary>
public sealed partial class RuleItemViewModel : ObservableObject, IAnimatedRow
{
    public long EnterTicks { get; set; }

    public int EnterDelay { get; set; }

    /// <summary>Fading out before the row is removed (collapsing its category).</summary>
    [ObservableProperty]
    private bool _isExiting;

    private readonly Action<RuleItemViewModel> _selectionChanged;

    public RuleItemViewModel(CleanRule rule, bool isChecked, Action<RuleItemViewModel> selectionChanged)
    {
        Rule = rule;
        _isChecked = isChecked;
        _selectionChanged = selectionChanged;
    }

    public CleanRule Rule { get; }

    /// <summary>Set while the selection is synced from the settings (no save, no events).</summary>
    internal bool Silent { get; set; }

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(FilesText), nameof(HasResult), nameof(IsEmpty), nameof(IsRunning), nameof(RunningText),
        nameof(HasBlocked), nameof(Roots), nameof(Blocked), nameof(Samples), nameof(HasSamples), nameof(SizeBytes))]
    private RuleScanResult? _result;

    /// <summary>Shown in the details panel.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Result of the DISM component store analysis.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDismInfo))]
    private string? _dismInfo;

    [ObservableProperty]
    private bool _isDismBusy;

    partial void OnIsCheckedChanged(bool value)
    {
        if (!Silent)
        {
            _selectionChanged(this);
        }
    }

    public string Name => Rule.DisplayName(Loc.Language);

    public string Description => Rule.DisplayDescription(Loc.Language);

    public string Category => Rule.DisplayCategory(Loc.Language);

    public string AreaText => Loc.T("Area" + Rule.Area);

    public string? Warning => Rule.DisplayWarning(Loc.Language);

    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    public bool IsSafe => Rule.Risk == RiskLevel.Safe;

    public bool IsModerate => Rule.Risk == RiskLevel.Moderate;

    public bool IsDeep => Rule.Risk == RiskLevel.Deep;

    public string RiskText => Loc.T("Risk" + Rule.Risk);

    public bool IsCommunity => Rule.Source == RuleSource.Winapp2;

    public bool IsCustom => Rule.Source == RuleSource.Custom;

    public bool IsExpertRule => Rule.Expert;

    public bool IsAction => Rule.Action != CleanAction.None;

    public bool IsDism => Rule.Action is CleanAction.DismComponentCleanup or CleanAction.DismResetBase;

    public bool HasDismInfo => !string.IsNullOrEmpty(DismInfo);

    public bool UsesRecycleBin => Rule.DeleteMode == DeleteMode.RecycleBin;

    public bool HasResult => Result is not null;

    public long SizeBytes => Result?.Bytes ?? 0;

    public bool IsEmpty => Result is { HasContent: false };

    public bool IsRunning => Result?.RunningProcesses.Count > 0;

    public string RunningText => IsRunning ? Loc.F("NoteAppRunning", string.Join(", ", Result!.RunningProcesses)) : "";

    public string SizeText => Result switch
    {
        null => "–",
        { SizeUnknown: true } => Loc.T("SizeUnknown"),
        { HasContent: false } => Loc.T("NothingFound"),
        _ => ByteSize.Format(Result.Bytes)
    };

    public string FilesText => Result switch
    {
        { SizeUnknown: true, Files: > 0 } => Loc.F("EntriesCount", Result.Files.ToString("N0")),
        { HasContent: true, SizeUnknown: false } => Loc.F("FilesCount", Result.Files.ToString("N0")),
        _ => ""
    };

    /// <summary>Folders to clean: resolved after an analysis, rule paths before.</summary>
    public IReadOnlyList<string> Roots => Result is { Roots.Count: > 0 } ? Result.Roots : Rule.Targets.Select(t => $"{t.Path}  [{t.Pattern}]").ToList();

    public IReadOnlyList<string> Blocked => Result?.Blocked ?? [];

    public bool HasBlocked => Blocked.Count > 0;

    public IReadOnlyList<string> Samples =>
        Result?.Samples.Take(60).Select(s => $"{ByteSize.Format(s.Bytes),10}   {s.Path}").ToList() ?? [];

    public bool HasSamples => Samples.Count > 0;

    public void RefreshText()
    {
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>Category with its rules (expander).</summary>
public sealed partial class RuleGroupViewModel(string name, bool isCommunity) : ObservableObject
{
    public string Name { get; set; } = name;

    public bool IsCommunity { get; } = isCommunity;

    public ObservableCollection<RuleItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = !isCommunity;

    public string SummaryText
    {
        get
        {
            var selected = Items.Count(i => i.IsChecked);
            var bytes = Items.Where(i => i.IsChecked).Sum(i => i.SizeBytes);
            return bytes > 0 ? Loc.F("GroupSummarySize", selected, Items.Count, ByteSize.Format(bytes)) : Loc.F("GroupSummary", selected, Items.Count);
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(Name));
    }
}

/// <summary>Selection profile in the combo box.</summary>
public sealed class ProfileOption(SelectionProfile profile)
{
    public SelectionProfile Profile { get; } = profile;

    public string Name => Profile.BuiltIn ? Profile.DisplayName(Loc.Language) : Profile.Name;

    public bool IsCustom => !Profile.BuiltIn;

    public override string ToString() => Name;
}

/// <summary>
/// Cleanup page for one area (System, Browser, Developer) or all of them (Full cleanup):
/// analyze → preview → confirm → clean.
/// </summary>
public sealed partial class CleanerViewModel : PageViewModel
{
    private readonly List<RuleItemViewModel> _all = [];
    private CancellationTokenSource? _cts;
    private string? _appliedProfile;

    public CleanerViewModel(MainViewModel main, CleanArea? area, bool server = false) : base(main)
    {
        Area = area;
        IsServer = server;
        if (area == CleanArea.Developer)
        {
            Workspace = new WorkspaceViewModel(main);
        }
    }

    /// <summary>null = Full cleanup (all areas).</summary>
    public CleanArea? Area { get; }

    public bool IsFull => Area is null;

    /// <summary>
    /// Server cleanup (expert): all areas for the profiles of signed-out users (optionally also signed-in ones),
    /// with its own rule selection that starts from the "RDS session host" profile.
    /// </summary>
    public bool IsServer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerScopeText))]
    private bool _includeSignedIn;

    partial void OnIncludeSignedInChanged(bool value)
    {
        IsAnalyzed = false;
        LastResult = null;
    }

    /// <summary>Profiles the server cleanup works on (signed-in users only when switched on).</summary>
    public IReadOnlyList<UserProfile> ScanProfiles()
    {
        if (!IsServer)
        {
            return Engine.Profiles;
        }

        var all = Engine.Platform.Profiles.All();
        if (IncludeSignedIn || !OperatingSystem.IsWindows())
        {
            return all;
        }

        var loaded = ServerProfiles.LoadedProfilePaths();
        return all.Where(p => !loaded.Contains(p.ProfilePath)).ToList();
    }

    public string ServerScopeText
    {
        get
        {
            if (!IsServer || !OperatingSystem.IsWindows())
            {
                return "";
            }

            var all = Engine.Platform.Profiles.All();
            var loaded = ServerProfiles.LoadedProfilePaths();
            var signedIn = all.Count(p => loaded.Contains(p.ProfilePath));
            return Loc.F(IncludeSignedIn ? "ServerScopeAll" : "ServerScopeSignedOut", all.Count - (IncludeSignedIn ? 0 : signedIn), all.Count, signedIn);
        }
    }

    private Dictionary<string, bool> Selection => IsServer ? Ui.ServerRuleSelection : Ui.RuleSelection;

    public override string TitleKey => IsServer ? "NavServerClean" : Area switch
    {
        null => "NavFull",
        CleanArea.System => "NavSystem",
        CleanArea.Browser => "NavBrowser",
        _ => "NavDeveloper"
    };

    public WorkspaceViewModel? Workspace { get; }

    public bool IsDeveloper => Workspace is not null;

    public ObservableCollection<RuleGroupViewModel> Groups { get; } = [];

    /// <summary>
    /// Flat list for the virtualized rule list: group headers followed by the rules of expanded groups.
    /// Only the visible rows become UI elements (keeps memory low with thousands of community rules).
    /// </summary>
    public ObservableCollection<object> Rows { get; } = [];

    /// <summary>At least one category is expanded: the toggle button collapses all, otherwise it expands all.</summary>
    public bool AnyExpanded => Groups.Any(g => g.IsExpanded);

    public bool HasRules => _all.Count > 0;

    /// <summary>Width of the details panel (persisted).</summary>
    public double DetailsWidth
    {
        get => Math.Clamp(Ui.DetailsWidth, 280, 900);
        set
        {
            Ui.DetailsWidth = Math.Clamp(value, 280, 900);
            Ui.Save();
        }
    }

    // ------------------------------------------------------------------ Filter

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private bool _showSafe = true;

    [ObservableProperty]
    private bool _showModerate = true;

    [ObservableProperty]
    private bool _showDeep = true;

    [ObservableProperty]
    private bool _showCommunity = true;

    partial void OnSearchChanged(string value) => BuildGroups();

    partial void OnShowSafeChanged(bool value) => BuildGroups();

    partial void OnShowModerateChanged(bool value) => BuildGroups();

    partial void OnShowDeepChanged(bool value) => BuildGroups();

    partial void OnShowCommunityChanged(bool value) => BuildGroups();

    public int CommunityCount => _all.Count(i => i.IsCommunity);

    public bool HasCommunity => CommunityCount > 0;

    // ------------------------------------------------------------------ Profiles

    public ObservableCollection<ProfileOption> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteProfile))]
    private ProfileOption? _selectedProfile;

    public bool CanDeleteProfile => SelectedProfile?.IsCustom == true;

    private void LoadProfiles()
    {
        var selectedId = SelectedProfile?.Profile.Id;
        Profiles.Clear();
        foreach (var profile in SelectionProfiles.All(Ui.CustomProfiles, Ui.ExpertMode || IsServer))
        {
            Profiles.Add(new ProfileOption(profile));
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.Id == selectedId) ?? Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void ApplyProfile()
    {
        if (SelectedProfile is not { } option)
        {
            return;
        }

        SetChecked(_all, i => option.Profile.Includes(i.Rule));
        _appliedProfile = option.Profile.Id;
        Main.ShowToast(Loc.F("ToastProfileApplied", option.Name, _all.Count(i => i.IsChecked)));
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        var name = await Host.PromptAsync(Loc.T("ProfileSaveTitle"), Loc.T("ProfileSaveText"), Loc.T("BtnSave"), Loc.T("WatermarkProfileName"),
            SelectedProfile?.IsCustom == true ? SelectedProfile.Name : null);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (SelectionProfiles.BuiltIn.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.NameDe.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Main.ShowToast(Loc.T("ToastProfileReserved"), isError: true);
            return;
        }

        Ui.CustomProfiles[name] = _all.Where(i => i.IsChecked).Select(i => i.Rule.Id).ToList();
        Ui.Save();
        RefreshProfilesEverywhere("custom:" + name);
        Main.ShowToast(Loc.F("ToastProfileSaved", name));
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { IsCustom: true } option)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("ProfileDeleteTitle"), Loc.F("ProfileDeleteText", option.Name), Loc.T("BtnDelete"), danger: true,
            confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        Ui.CustomProfiles.Remove(option.Profile.Name);
        Ui.Save();
        RefreshProfilesEverywhere(null);
    }

    private void RefreshProfilesEverywhere(string? selectId)
    {
        foreach (var page in Main.Pages.OfType<CleanerViewModel>())
        {
            page.LoadProfiles();
        }

        if (selectId is not null)
        {
            SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.Id.Equals(selectId, StringComparison.OrdinalIgnoreCase)) ?? SelectedProfile;
        }
    }

    public override void OnExpertModeChanged()
    {
        base.OnExpertModeChanged();
        LoadProfiles();
    }

    // ------------------------------------------------------------------ State

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand), nameof(CleanCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isAnalyzed;

    [ObservableProperty]
    private string? _lastResult;

    [ObservableProperty]
    private RuleItemViewModel? _selectedRule;

    public bool HasSelectedRule => SelectedRule is not null;

    partial void OnSelectedRuleChanged(RuleItemViewModel? oldValue, RuleItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }

        OnPropertyChanged(nameof(HasSelectedRule));
    }

    public string SummaryText
    {
        get
        {
            var selected = _all.Where(i => i.IsChecked).ToList();
            if (!IsAnalyzed)
            {
                return Loc.F("SummaryNotAnalyzed", selected.Count, _all.Count);
            }

            var bytes = selected.Sum(i => i.SizeBytes);
            var files = selected.Sum(i => i.Result?.Files ?? 0);
            return Loc.F("SummaryAnalyzed", selected.Count, ByteSize.Format(bytes), files.ToString("N0"));
        }
    }

    // ------------------------------------------------------------------ Load

    public override void OnCatalogChanged()
    {
        _all.Clear();
        foreach (var rule in Engine.RulesFor(Area))
        {
            _all.Add(new RuleItemViewModel(rule, IsSelected(rule), OnRuleSelectionChanged));
        }

        IsAnalyzed = false;
        LastResult = null;
        SelectedRule = null;
        LoadProfiles();
        BuildGroups();
        AnalyzeCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(CommunityCount));
        OnPropertyChanged(nameof(HasCommunity));
    }

    private bool IsSelected(CleanRule rule) => Selection.TryGetValue(rule.Id, out var stored)
        ? stored
        : IsServer ? SelectionProfiles.Find(SelectionProfiles.RdsId, null)?.Includes(rule) == true : rule.Selected;

    /// <summary>The selection is shared between the full cleanup and the area pages.</summary>
    public override void OnActivated()
    {
        OnPropertyChanged(nameof(DetailsWidth));
        if (IsServer)
        {
            OnPropertyChanged(nameof(ServerScopeText));
        }

        var changed = false;
        foreach (var item in _all)
        {
            var value = IsSelected(item.Rule);
            if (item.IsChecked != value)
            {
                item.Silent = true;
                item.IsChecked = value;
                item.Silent = false;
                changed = true;
            }
        }

        if (changed)
        {
            foreach (var group in Groups)
            {
                group.Refresh();
            }

            OnPropertyChanged(nameof(SummaryText));
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private void BuildGroups()
    {
        var term = Search.Trim();
        var visible = _all.Where(i =>
                (i.Rule.Risk switch { RiskLevel.Safe => ShowSafe, RiskLevel.Moderate => ShowModerate, _ => ShowDeep })
                && (ShowCommunity || !i.IsCommunity)
                && (term.Length == 0 || i.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                     || i.Category.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        var expanded = Groups.ToDictionary(g => (g.Name, g.IsCommunity), g => g.IsExpanded);
        Groups.Clear();

        // Built-in categories in rule order, then community categories alphabetically; full cleanup prefixes the area.
        string Key(RuleItemViewModel i) => IsFull ? $"{i.AreaText} · {i.Category}" : i.Category;
        foreach (var group in visible.Where(i => !i.IsCommunity).GroupBy(Key)
                     .Concat(visible.Where(i => i.IsCommunity).GroupBy(Key).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)))
        {
            var community = group.First().IsCommunity;
            var vm = new RuleGroupViewModel(group.Key, community);
            if (expanded.TryGetValue((group.Key, community), out var wasExpanded))
            {
                vm.IsExpanded = wasExpanded;
            }
            else if (term.Length > 0)
            {
                vm.IsExpanded = true;
            }
            else if (IsFull)
            {
                vm.IsExpanded = false;
            }

            foreach (var item in group)
            {
                vm.Items.Add(item);
            }

            Groups.Add(vm);
        }

        RebuildRows();
        OnPropertyChanged(nameof(SummaryText));
    }

    private void RebuildRows()
    {
        Rows.Clear();
        foreach (var group in Groups)
        {
            Rows.Add(group);
            if (group.IsExpanded)
            {
                foreach (var item in group.Items)
                {
                    Rows.Add(item);
                }
            }
        }

        OnPropertyChanged(nameof(AnyExpanded));
    }

    /// <summary>Fade-out time of collapsing rows before they leave the list.</summary>
    private const int ExitMilliseconds = 130;

    /// <summary>Expands or collapses one category in place (the scroll position stays), with a short fade/slide.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleGroupAsync(RuleGroupViewModel? group)
    {
        if (group is null)
        {
            return;
        }

        group.IsExpanded = !group.IsExpanded;
        OnPropertyChanged(nameof(AnyExpanded));
        if (Rows.IndexOf(group) < 0)
        {
            RebuildRows();
            return;
        }

        if (group.IsExpanded)
        {
            var at = Rows.IndexOf(group) + 1;
            AnimatedRows.MarkEntering(group.Items);
            foreach (var item in group.Items)
            {
                item.IsExiting = false;
                Rows.Insert(at++, item);
            }

            return;
        }

        foreach (var item in group.Items)
        {
            item.IsExiting = true;
        }

        await Task.Delay(ExitMilliseconds);
        if (group.IsExpanded)
        {
            // Opened again while fading out.
            foreach (var item in group.Items)
            {
                item.IsExiting = false;
            }

            return;
        }

        var index = Rows.IndexOf(group);
        while (index >= 0 && index + 1 < Rows.Count && Rows[index + 1] is RuleItemViewModel)
        {
            ((RuleItemViewModel)Rows[index + 1]).IsExiting = false;
            Rows.RemoveAt(index + 1);
        }
    }

    /// <summary>One button: collapses everything when something is open, otherwise expands everything.</summary>
    [RelayCommand]
    private async Task ToggleExpandAllAsync()
    {
        var expand = !AnyExpanded;
        if (!expand)
        {
            foreach (var item in Rows.OfType<RuleItemViewModel>())
            {
                item.IsExiting = true;
            }

            await Task.Delay(ExitMilliseconds);
        }

        foreach (var group in Groups)
        {
            group.IsExpanded = expand;
            foreach (var item in group.Items)
            {
                item.IsExiting = false;
            }
        }

        if (expand)
        {
            AnimatedRows.MarkEntering(Groups.SelectMany(g => g.Items));
        }

        RebuildRows();
    }

    private void OnRuleSelectionChanged(RuleItemViewModel item)
    {
        Selection[item.Rule.Id] = item.IsChecked;
        Ui.Save();
        _appliedProfile = null;
        foreach (var group in Groups.Where(g => g.Items.Contains(item)))
        {
            group.Refresh();
        }

        OnPropertyChanged(nameof(SummaryText));
        CleanCommand.NotifyCanExecuteChanged();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var item in _all)
        {
            item.RefreshText();
        }

        LoadProfiles();
        BuildGroups();
    }

    // ------------------------------------------------------------------ Selection

    [RelayCommand]
    private void SelectRule(RuleItemViewModel? item) => SelectedRule = item;

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    /// <summary>Applies to the visible rules (filter and search are respected).</summary>
    private void SetAll(bool value) => SetChecked(Groups.SelectMany(g => g.Items).ToList(), _ => value);

    /// <summary>Bulk selection: one settings save instead of one per rule.</summary>
    private void SetChecked(IReadOnlyList<RuleItemViewModel> items, Func<RuleItemViewModel, bool> value)
    {
        foreach (var item in items)
        {
            var v = value(item);
            item.Silent = true;
            item.IsChecked = v;
            item.Silent = false;
            Selection[item.Rule.Id] = v;
        }

        Ui.Save();
        _appliedProfile = null;
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        OnPropertyChanged(nameof(SummaryText));
        CleanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // "path  [pattern]" before an analysis: expand tokens and wildcards first; nothing is created.
        var clean = path.Split("  [", StringSplitOptions.None)[0];
        var folder = Directory.Exists(clean)
            ? clean
            : PathExpander.Resolve(clean, Engine.Profiles).FirstOrDefault();
        if (folder is not null)
        {
            AppEnvironment.OpenFolder(folder);
        }
        else
        {
            Main.ShowToast(Loc.T("ToastFolderMissing"));
        }
    }

    // ------------------------------------------------------------------ DISM pre-analysis

    /// <summary>Runs DISM /AnalyzeComponentStore (read only) for the size estimate.</summary>
    [RelayCommand]
    private async Task AnalyzeComponentStoreAsync(RuleItemViewModel? item)
    {
        if (item is null || item.IsDismBusy || !OperatingSystem.IsWindows())
        {
            return;
        }

        item.IsDismBusy = true;
        item.DismInfo = Loc.T("DismAnalyzing");
        try
        {
            var info = await DismAnalyzer.AnalyzeAsync(CancellationToken.None);
            item.DismInfo = info is null
                ? Loc.T("DismFailed")
                : Loc.F("DismResult", ByteSize.Format(info.Reclaimable), ByteSize.Format(info.ActualSize), info.Recommended);
        }
        catch (Exception ex)
        {
            item.DismInfo = Loc.T("DismFailed");
            Main.ShowError(ex);
        }
        finally
        {
            item.IsDismBusy = false;
        }
    }

    // ------------------------------------------------------------------ Analyze

    private bool CanAnalyze() => !IsBusy && _all.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    public async Task AnalyzeAsync()
    {
        await ScanAsync(_all, quiet: false);
    }

    /// <summary>Background analysis for the reminder: selected bytes, null when cancelled or failed. Never cleans.</summary>
    public async Task<long?> AnalyzeForReminderAsync()
    {
        if (IsBusy || _all.Count == 0)
        {
            return null;
        }

        var ok = await ScanAsync(_all, quiet: true);
        return ok ? _all.Where(i => i.IsChecked).Sum(i => i.SizeBytes) : null;
    }

    private async Task<bool> ScanAsync(IReadOnlyList<RuleItemViewModel> items, bool quiet)
    {
        if (items.Count == 0 || IsBusy)
        {
            return false;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        ProgressText = Loc.T("StatusAnalyzing");
        var progress = new Progress<ScanProgress>(p =>
        {
            Progress = p.Total == 0 ? 0 : p.Done * 100d / p.Total;
            ProgressText = Loc.F("StatusAnalyzingRule", p.Done, p.Total, p.Current);
        });

        try
        {
            // Cancelling returns the rules scanned so far (no exception).
            var results = await Engine.CreateScanner(ScanProfiles()).ScanAsync(items.Select(i => i.Rule).ToList(), progress, token);
            var byId = results.ToDictionary(r => r.Rule.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (byId.TryGetValue(item.Rule.Id, out var result) || !token.IsCancellationRequested)
                {
                    item.Result = result;
                }
            }

            IsAnalyzed = _all.Any(i => i.HasResult);
            foreach (var group in Groups)
            {
                group.Refresh();
            }

            if (token.IsCancellationRequested)
            {
                if (!quiet)
                {
                    Main.ShowCancelled();
                }

                return false;
            }

            if (!quiet)
            {
                var selected = _all.Where(i => i.IsChecked).ToList();
                Main.ShowToast(Loc.F("ToastAnalyzed", ByteSize.Format(selected.Sum(i => i.SizeBytes)), selected.Sum(i => i.Result?.Files ?? 0).ToString("N0")));
            }

            return true;
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                Main.ShowError(ex);
            }

            return false;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        Workspace?.CancelCommand.Execute(null);
    }

    // ------------------------------------------------------------------ Clean

    private bool CanClean() => !IsBusy && IsAnalyzed && _all.Any(i => i.IsChecked && i.Result is { HasContent: true });

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = _all.Where(i => i.IsChecked && i.Result is { HasContent: true }).ToList();
        if (selected.Count == 0)
        {
            Main.ShowToast(Loc.T("ToastNothingSelected"));
            return;
        }

        var hasDeep = selected.Any(i => i.IsDeep);
        var canRestore = hasDeep && OperatingSystem.IsWindows() && RestorePoint.IsSupported;
        var bytes = selected.Sum(i => i.SizeBytes);
        var files = selected.Sum(i => i.Result!.Files);
        var model = new CleanConfirmModel
        {
            Title = Loc.F("ConfirmCleanTitle", Title),
            Summary = Loc.F("ConfirmCleanSummary", selected.Count, ByteSize.Format(bytes), files.ToString("N0")),
            Rows = selected.Select(i => new CleanConfirmRow(i.Name, IsFull ? $"{i.AreaText} · {i.Category}" : i.Category, i.Rule.Risk, i.RiskText,
                    i.SizeText, i.FilesText, i.IsRunning ? i.RunningText : i.UsesRecycleBin ? Loc.T("NoteRecycleBin") : null))
                .ToList(),
            Warnings = selected.Where(i => i.IsDeep).Select(i => $"{i.Name}: {i.Warning ?? Loc.T("DeepDefaultWarning")}").ToList(),
            HasDeep = hasDeep,
            ShowRestorePoint = canRestore,
            CreateRestorePoint = canRestore && Ui.RestorePointBeforeDeep
        };

        var answer = await Host.ConfirmCleanAsync(model);
        if (!answer.Confirmed)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            if (answer.RestorePoint && (!await CreateRestorePointAsync(_cts.Token) || _cts.IsCancellationRequested))
            {
                return;
            }

            IsIndeterminate = false;
            Progress = 0;
            var progress = new Progress<CleanProgress>(p =>
            {
                Progress = p.RuleCount == 0 ? 0 : (p.Rule - 1) * 100d / p.RuleCount;
                ProgressText = Loc.F("StatusCleaningRule", p.Rule, p.RuleCount, p.RuleName);
            });

            var report = await Engine.CreateCleaner(ScanProfiles()).CleanAsync(selected.Select(i => i.Result!).ToList(),
                IsServer ? "Server" : Area?.ToString() ?? "Full", progress, _cts.Token);
            report.Profile = _appliedProfile;
            await Task.Run(() => ReportStore.Save(report));
            var running = report.Rules.Count(r => r.SkippedRunning);
            LastResult = Loc.F("ResultText", ByteSize.Format(report.DeletedBytes), report.DeletedFiles.ToString("N0"), report.Skipped.ToString("N0"))
                         + (running > 0 ? " " + Loc.F("ResultRunning", running) : "")
                         + (report.Cancelled ? " " + Loc.T("ResultCancelled") : "");
            Main.ShowToast(LastResult);
            foreach (var error in report.Rules.SelectMany(r => r.Errors).Take(1))
            {
                Main.ShowToast(error, isError: true);
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }

        // Fresh numbers for what was just cleaned.
        await ScanAsync(selected, quiet: true);
        Main.Overview.Refresh();
        Main.Reports.MarkDirty();
    }

    private async Task<bool> CreateRestorePointAsync(CancellationToken ct)
    {
        IsIndeterminate = true;
        ProgressText = Loc.T("StatusRestorePoint");
        var (result, output) = await RestorePoint.CreateAsync("CLEANit – before cleaning", uninstall: false, ct);
        IsIndeterminate = false;
        switch (result)
        {
            case RestorePointResult.Created:
                Main.ShowToast(Loc.T("ToastRestorePointCreated"));
                return true;
            case RestorePointResult.SkippedRecent:
                Main.ShowToast(Loc.T("ToastRestorePointRecent"));
                return true;
            default:
                var go = await Host.ConfirmAsync(Loc.T("RestoreFailedTitle"), Loc.F("RestoreFailedText", output.Trim()), Loc.T("BtnContinueAnyway"),
                    danger: true, confirmIcon: "IconBroom");
                return go.Confirmed;
        }
    }
}
