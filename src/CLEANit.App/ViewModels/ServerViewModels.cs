using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>One local user profile on the profiles page.</summary>
public sealed partial class ProfileRowViewModel(LocalProfileInfo info, Action changed) : ObservableObject
{
    public LocalProfileInfo Info { get; } = info;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Size))]
    private long? _bytes;

    public string UserName => Info.UserName;

    public string Path => Info.Path;

    public string Size => Bytes is { } b ? ByteSize.Format(b) : "…";

    public long SizeSort => Bytes ?? -1;

    public string LastUse => Info.LastUseUtc is { } t ? t.ToLocalTime().ToString("g") : "–";

    public DateTime LastUseSort => Info.LastUseUtc ?? DateTime.MinValue;

    public string Age => Info.AgeDays is { } d ? Loc.F("DaysCount", d) : "–";

    public int AgeSort => Info.AgeDays ?? -1;

    public string KindText => Loc.T("ProfileType" + Info.Kind);

    public bool IsSignedIn => Info.IsLoaded;

    public bool IsCurrent => Info.IsCurrent;

    public bool IsOrphaned => Info.Kind == ProfileKind.Orphaned;

    public bool IsSpecialKind => Info.Kind is ProfileKind.Temporary or ProfileKind.Orphaned;

    public bool CanDelete => Info.CanDelete;

    /// <summary>Why the profile cannot be selected (shown instead of the check box).</summary>
    public string LockReason => Info.IsCurrent ? Loc.T("ProfileLockCurrent")
        : Info.IsLoaded ? Loc.T("ProfileLockLoaded")
        : Info.Kind == ProfileKind.Orphaned ? Loc.T("ProfileLockOrphaned")
        : Loc.T("ProfileLockOther");

    public string StateText => Info.IsCurrent ? Loc.T("ProfileStateCurrent") : Info.IsLoaded ? Loc.T("ProfileStateSignedIn") : Loc.T("ProfileStateSignedOut");

    public string AccountTip => Info.AccountKnown ? Info.Sid : Loc.F("ProfileAccountUnknown", Info.Sid);

    public void RefreshText() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Server › Profiles (expert): local user profiles with size, last use and state. Unused profiles can be deleted through
/// the Windows API (folder and ProfileList entry, like the "delete profiles older than …" policy). Profiles in use are locked.
/// </summary>
public sealed partial class ServerProfilesViewModel(MainViewModel main) : PageViewModel(main)
{
    private CancellationTokenSource? _measure;
    private bool _loaded;

    public override string TitleKey => "NavProfiles";

    public ObservableCollection<ProfileRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool HasDeletable => Rows.Any(r => r.CanDelete);

    /// <summary>Header check box: true = all deletable profiles checked, false = none, null = some.</summary>
    public bool? AllChecked
    {
        get
        {
            var deletable = Rows.Where(r => r.CanDelete).ToList();
            var checkedCount = deletable.Count(r => r.IsChecked);
            return checkedCount == 0 ? false : checkedCount == deletable.Count ? true : null;
        }
        // Clicking the header: none or some checked → all; all checked → none (the incoming value is ignored).
        set => SetAll(AllChecked != true);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private decimal _ageDays = 90;

    partial void OnAgeDaysChanged(decimal value)
    {
        Ui.ProfileAgeDays = (int)Math.Clamp(value, 1, 3650);
        Ui.Save();
    }

    public string SummaryText => Rows.Count == 0
        ? Loc.T("ProfilesHint")
        : Loc.F("ProfilesSummary", Rows.Count(r => !r.IsOrphaned), Rows.Count(r => r.IsSignedIn), Rows.Count(r => r.IsChecked),
            ByteSize.Format(Rows.Where(r => r.IsChecked).Sum(r => r.Bytes ?? 0)));

    public override void OnActivated()
    {
        if (!_loaded)
        {
            _loaded = true;
            AgeDays = Math.Clamp(Ui.ProfileAgeDays, 1, 3650);
            _ = RefreshAsync();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in Rows)
        {
            row.RefreshText();
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _measure?.Cancel();
        IsBusy = true;
        Status = Loc.T("StatusReadingProfiles");
        try
        {
            var profiles = await Task.Run(ServerProfiles.List);
            Rows.Clear();
            foreach (var profile in profiles)
            {
                Rows.Add(new ProfileRowViewModel(profile, Changed));
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            Status = "";
            Changed();
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasDeletable));
        }

        _ = MeasureAsync();
    }

    /// <summary>Sizes are measured in the background, one profile after the other (links are not followed).</summary>
    private async Task MeasureAsync()
    {
        _measure = new CancellationTokenSource();
        var token = _measure.Token;
        foreach (var row in Rows.ToList())
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var path = row.Path;
            var measured = await Task.Run(() => SafeDelete.Measure(path, token), CancellationToken.None);
            row.Bytes = measured.Bytes;
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>Checks the deletable profiles not used for at least the given number of days (and only those).</summary>
    [RelayCommand]
    private void SelectOld()
    {
        var days = (int)AgeDays;
        var count = 0;
        _bulk = true;
        foreach (var row in Rows)
        {
            row.IsChecked = row.CanDelete && row.Info.AgeDays is { } age && age >= days;
            count += row.IsChecked ? 1 : 0;
        }

        _bulk = false;
        Changed();
        if (count == 0)
        {
            Main.ShowToast(HasDeletable ? Loc.F("ToastNoUnusedProfiles", days) : Loc.T("ToastNoDeletableProfiles"));
        }
    }

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    private bool _bulk;

    private void SetAll(bool value)
    {
        _bulk = true;
        foreach (var row in Rows)
        {
            row.IsChecked = value && row.CanDelete;
        }

        _bulk = false;
        Changed();
        if (value && !HasDeletable)
        {
            Main.ShowToast(Loc.T("ToastNoDeletableProfiles"));
        }
    }

    [RelayCommand]
    private void OpenFolder(ProfileRowViewModel? row) => AppEnvironment.OpenFolder(row?.Path);

    private bool CanDelete() => !IsBusy && Rows.Any(r => r.IsChecked && r.CanDelete);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var rows = Rows.Where(r => r.IsChecked && r.CanDelete).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var list = string.Join("\n", rows.Take(12).Select(r => $"• {r.UserName} ({r.Size}, {r.Age})")) + (rows.Count > 12 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(Loc.F("ProfilesDeleteTitle", rows.Count),
            Loc.F("ProfilesDeleteText", ByteSize.Format(rows.Sum(r => r.Bytes ?? 0)), list), Loc.T("BtnDelete"), danger: true,
            confirmIcon: "IconAccountRemove");
        if (!answer.Confirmed)
        {
            return;
        }

        _measure?.Cancel();
        IsBusy = true;
        var report = new CleanReport { Area = "Profiles" };
        var result = new RuleCleanResult { RuleId = "profiles", RuleName = Loc.T("AreaProfiles") };
        report.Rules.Add(result);
        try
        {
            foreach (var row in rows)
            {
                Status = Loc.F("StatusDeletingProfile", row.UserName);
                var (ok, error) = await Task.Run(() => ServerProfiles.Delete(row.Info));
                if (ok)
                {
                    result.DeletedFiles++;
                    result.DeletedBytes += row.Bytes ?? 0;
                }
                else
                {
                    result.AddError($"{row.UserName}: {error}");
                }
            }

            report.FinishedUtc = DateTime.UtcNow;
            await Task.Run(() => ReportStore.Save(report));
            Main.Reports.MarkDirty();
            Main.Overview.Refresh();
            Main.ShowToast(Loc.F("ToastProfilesDeleted", result.DeletedFiles, ByteSize.Format(result.DeletedBytes)),
                isError: result.DeletedFiles == 0 && result.Errors.Count > 0);
            foreach (var error in result.Errors.Take(3))
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
            Status = "";
        }

        await RefreshAsync();
    }

    private void Changed()
    {
        if (_bulk)
        {
            return;
        }

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(AllChecked));
        DeleteCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Group of environment lines (System, Remote Desktop, FSLogix, Citrix, Profiles).</summary>
public sealed class EnvGroupViewModel(string key, IReadOnlyList<EnvLineViewModel> items)
{
    public string Key { get; } = key;

    public string Title => Loc.T("EnvGroup" + char.ToUpperInvariant(Key[0]) + Key[1..]);

    public IReadOnlyList<EnvLineViewModel> Items { get; } = items;
}

public sealed class EnvLineViewModel(EnvItem item)
{
    public EnvItem Item { get; } = item;

    public string Label => Item.LabelKey.Length > 0 ? Loc.T(Item.LabelKey) : "";

    public bool HasLabel => Item.LabelKey.Length > 0;

    public string Value => Item.Value ?? (Item.ValueKey is { } key ? Loc.T(key) : "");

    public bool IsOk => Item.State == EnvState.Ok;

    public bool IsWarning => Item.State == EnvState.Warning;

    public bool IsOff => Item.State == EnvState.Off;

    /// <summary>Raw registry lines (policy values) are shown in the mono font.</summary>
    public bool IsRaw => !HasLabel;
}

/// <summary>Server › Environment (expert): read-only overview of RDS, sessions, FSLogix, Citrix and profiles.</summary>
public sealed partial class ServerEnvironmentViewModel(MainViewModel main) : PageViewModel(main)
{
    private IReadOnlyList<EnvItem> _items = [];
    private bool _loaded;

    public override string TitleKey => "NavEnvironment";

    public ObservableCollection<EnvGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    public override void OnActivated()
    {
        if (!_loaded)
        {
            _loaded = true;
            _ = RefreshAsync();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Build();
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IsBusy = true;
        try
        {
            _items = await ServerEnvironment.ReadAsync(CancellationToken.None);
            Build();
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenProfiles() => Main.NavigateTo(Main.ServerProfiles);

    [RelayCommand]
    private void OpenServerClean() => Main.NavigateTo(Main.ServerClean);

    private void Build()
    {
        Groups.Clear();
        foreach (var group in _items.GroupBy(i => i.Group))
        {
            Groups.Add(new EnvGroupViewModel(group.Key, group.Select(i => new EnvLineViewModel(i)).ToList()));
        }
    }
}
