using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Models;
using CLEANit.Core.Rules;
using CLEANit.Core.Storage;

namespace CLEANit.App.ViewModels;

/// <summary>Editable target (folder + pattern).</summary>
public sealed partial class EditorTargetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _pattern = "*";

    [ObservableProperty]
    private bool _recursive = true;

    [ObservableProperty]
    private bool _removeEmptyDirs = true;

    [ObservableProperty]
    private decimal _minAgeHours;

    /// <summary>Exclusions, separated by ";".</summary>
    [ObservableProperty]
    private string _exclude = "";

    public static EditorTargetViewModel From(CleanTarget t) => new()
    {
        Path = t.Path,
        Pattern = t.Pattern,
        Recursive = t.Recursive,
        RemoveEmptyDirs = t.RemoveEmptyDirs,
        MinAgeHours = t.MinAgeHours ?? 0,
        Exclude = string.Join("; ", t.Exclude)
    };

    public CleanTarget ToTarget() => new()
    {
        Path = Path.Trim(),
        Pattern = string.IsNullOrWhiteSpace(Pattern) ? "*" : Pattern.Trim(),
        Recursive = Recursive,
        RemoveEmptyDirs = RemoveEmptyDirs,
        MinAgeHours = MinAgeHours > 0 ? (int)MinAgeHours : null,
        Exclude = Split(Exclude)
    };

    internal static List<string> Split(string value) =>
        value.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

/// <summary>Editable own rule (stored in %ProgramData%\CLEANit\rules\editor-*.json).</summary>
public sealed partial class EditorRuleViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _id = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _name = "";

    [ObservableProperty]
    private string _nameDe = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private int _areaIndex;

    [ObservableProperty]
    private int _riskIndex;

    [ObservableProperty]
    private bool _useRecycleBin;

    /// <summary>Process names that block the rule, separated by ";".</summary>
    [ObservableProperty]
    private string _processes = "";

    [ObservableProperty]
    private string _warning = "";

    [ObservableProperty]
    private bool _isActive;

    public ObservableCollection<EditorTargetViewModel> Targets { get; } = [];

    /// <summary>Raised on any edit of the rule or its targets (unsaved changes).</summary>
    public event Action? Edited;

    public EditorRuleViewModel()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(IsActive) or nameof(Title)))
            {
                Edited?.Invoke();
            }
        };
        Targets.CollectionChanged += (_, e) =>
        {
            foreach (var target in e.NewItems?.OfType<EditorTargetViewModel>() ?? [])
            {
                target.PropertyChanged += (_, _) => Edited?.Invoke();
            }

            Edited?.Invoke();
        };
    }

    // A combo box that loses its items reports -1: keep the last valid value (never fall back to Safe silently).
    partial void OnAreaIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0)
        {
            AreaIndex = Math.Max(0, oldValue);
        }
    }

    partial void OnRiskIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0)
        {
            RiskIndex = Math.Max(0, oldValue);
        }
    }

    public string Title => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    public static EditorRuleViewModel From(CleanRule r)
    {
        var vm = new EditorRuleViewModel
        {
            Id = r.Id,
            Name = r.Name,
            NameDe = r.NameDe ?? "",
            Description = r.Description,
            Category = r.Category,
            AreaIndex = (int)r.Area,
            RiskIndex = (int)r.Risk,
            UseRecycleBin = r.DeleteMode == DeleteMode.RecycleBin,
            Processes = string.Join("; ", r.Processes),
            Warning = r.Warning ?? ""
        };
        foreach (var target in r.Targets)
        {
            vm.Targets.Add(EditorTargetViewModel.From(target));
        }

        return vm;
    }

    public CleanRule ToRule() => new()
    {
        Id = Id.Trim(),
        Name = Name.Trim(),
        NameDe = string.IsNullOrWhiteSpace(NameDe) ? null : NameDe.Trim(),
        Description = Description.Trim(),
        Category = string.IsNullOrWhiteSpace(Category) ? "Custom" : Category.Trim(),
        Area = (CleanArea)Math.Clamp(AreaIndex, 0, 2),
        Risk = (RiskLevel)Math.Clamp(RiskIndex, 0, 2),
        DeleteMode = UseRecycleBin ? DeleteMode.RecycleBin : DeleteMode.Permanent,
        Processes = EditorTargetViewModel.Split(Processes),
        Warning = string.IsNullOrWhiteSpace(Warning) ? null : Warning.Trim(),
        Targets = Targets.Select(t => t.ToTarget()).ToList(),
        Source = RuleSource.Custom
    };
}

/// <summary>Expert: create and test own file rules. Preview runs the real analysis including the safety guard; nothing is deleted here.</summary>
public sealed partial class RuleEditorViewModel(MainViewModel main) : PageViewModel(main)
{
    private bool _loaded;
    private bool _dirty;

    public override string TitleKey => "NavRuleEditor";

    public ObservableCollection<EditorRuleViewModel> Rules { get; } = [];

    public bool HasRules => Rules.Count > 0;

    // Built once: replacing the lists would make the combo boxes drop their selection.
    public IReadOnlyList<string> Areas { get; } = [Loc.T("AreaSystem"), Loc.T("AreaBrowser"), Loc.T("AreaDeveloper")];

    public IReadOnlyList<string> Risks { get; } = [Loc.T("RiskSafe"), Loc.T("RiskModerate"), Loc.T("RiskDeep")];

    public IReadOnlyList<string> Tokens { get; } =
    [
        "%LocalAppData%", "%AppData%", "%LocalLowAppData%", "%Temp%", "%UserProfile%", "%Downloads%", "%ProgramData%", "%WinDir%", "%ProgramFiles%"
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EditorRuleViewModel? _selected;

    public bool HasSelection => Selected is not null;

    partial void OnSelectedChanged(EditorRuleViewModel? oldValue, EditorRuleViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }

        PreviewText = "";
        PreviewRoots.Clear();
        PreviewBlocked.Clear();
        PreviewSamples.Clear();
    }

    // ------------------------------------------------------------------ Preview

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _previewText = "";

    public ObservableCollection<string> PreviewRoots { get; } = [];

    public ObservableCollection<string> PreviewBlocked { get; } = [];

    public ObservableCollection<string> PreviewSamples { get; } = [];

    public override void OnActivated()
    {
        if (!_loaded)
        {
            Load();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
    }

    private void Load()
    {
        Rules.Clear();
        foreach (var rule in RuleCatalog.LoadEditorRules())
        {
            Rules.Add(Track(EditorRuleViewModel.From(rule)));
        }

        _loaded = true;
        _dirty = false;
        Selected = Rules.FirstOrDefault();
        OnPropertyChanged(nameof(HasRules));
    }

    [RelayCommand]
    private void Select(EditorRuleViewModel? rule) => Selected = rule;

    private EditorRuleViewModel Track(EditorRuleViewModel rule)
    {
        rule.Edited += () => _dirty = true;
        return rule;
    }

    [RelayCommand]
    private void AddRule()
    {
        var n = 1;
        while (Rules.Any(r => r.Id.Equals($"custom.rule{n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        var rule = new EditorRuleViewModel { Id = $"custom.rule{n}", Name = Loc.F("NewRuleName", n), Category = "Custom" };
        rule.Targets.Add(new EditorTargetViewModel { Path = @"%LocalAppData%\Vendor\App\Cache" });
        Rules.Add(Track(rule));
        Selected = rule;
        _dirty = true;
        OnPropertyChanged(nameof(HasRules));
    }

    [RelayCommand]
    private async Task DeleteRuleAsync()
    {
        if (Selected is not { } rule)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("RuleDeleteTitle"), Loc.F("RuleDeleteText", rule.Title), Loc.T("BtnDelete"), danger: true,
            confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        var index = Rules.IndexOf(rule);
        Rules.Remove(rule);
        Selected = Rules.Count == 0 ? null : Rules[Math.Clamp(index, 0, Rules.Count - 1)];
        _dirty = true;
        OnPropertyChanged(nameof(HasRules));
    }

    [RelayCommand]
    private void AddTarget()
    {
        Selected?.Targets.Add(new EditorTargetViewModel());
        _dirty = true;
    }

    [RelayCommand]
    private void RemoveTarget(EditorTargetViewModel? target)
    {
        if (target is not null && Selected?.Targets.Remove(target) == true)
        {
            _dirty = true;
        }
    }

    /// <summary>Analysis of the selected rule with the real engine: folders found, folders the guard refuses, largest files.</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (Selected is null || IsBusy)
        {
            return;
        }

        var rule = Selected.ToRule();
        PreviewRoots.Clear();
        PreviewBlocked.Clear();
        PreviewSamples.Clear();
        if (rule.Targets.Count == 0 || rule.Targets.Any(t => string.IsNullOrWhiteSpace(t.Path)))
        {
            PreviewText = Loc.T("RulePreviewNoTarget");
            return;
        }

        IsBusy = true;
        PreviewText = Loc.T("StatusAnalyzing");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var scanner = Engine.CreateScanner();
            var result = await Task.Run(() => scanner.ScanRule(rule, cts.Token));
            PreviewText = Loc.F("RulePreviewResult", ByteSize.Format(result.Bytes), result.Files.ToString("N0"), result.Roots.Count, result.Blocked.Count);
            foreach (var root in result.Roots)
            {
                PreviewRoots.Add(root);
            }

            foreach (var blocked in result.Blocked)
            {
                PreviewBlocked.Add(blocked);
            }

            foreach (var sample in result.Samples.Take(40))
            {
                PreviewSamples.Add($"{ByteSize.Format(sample.Bytes),10}   {sample.Path}");
            }
        }
        catch (Exception ex)
        {
            PreviewText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var problems = RuleCatalog.SaveEditorRules(Rules.Select(r => r.ToRule()).ToList());
            if (problems.Count > 0)
            {
                await Host.ShowTextAsync(Loc.T("RuleSaveProblems"), string.Join(Environment.NewLine, problems));
                return;
            }

            _dirty = false;
            Main.ShowToast(Loc.F("ToastRulesSaved", Rules.Count));
            await Main.ReloadEngineAsync();
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task RevertAsync()
    {
        if (_dirty)
        {
            var answer = await Host.ConfirmAsync(Loc.T("RuleRevertTitle"), Loc.T("RuleRevertText"), Loc.T("BtnRevert"), danger: true, confirmIcon: "IconUndo");
            if (!answer.Confirmed)
            {
                return;
            }
        }

        Load();
    }

    [RelayCommand]
    private void OpenFolder() => AppEnvironment.OpenFolder(Directory.Exists(AppPaths.CustomRulesDirectory) ? AppPaths.CustomRulesDirectory : AppPaths.DataDirectory);

    [RelayCommand]
    private async Task CopyTokenAsync(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            await Host.CopyAsync(token);
            Main.ShowToast(Loc.F("ToastCopiedValue", token));
        }
    }
}
