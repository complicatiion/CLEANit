using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

public sealed partial class UpdateRowViewModel(PackageUpdate update) : ObservableObject
{
    public PackageUpdate Update { get; } = update;

    [ObservableProperty]
    private bool _isChecked = !update.ExplicitOnly;

    [ObservableProperty]
    private string _state = "";

    /// <summary>On the ignore list: not counted, not part of "Update all".</summary>
    [ObservableProperty]
    private bool _isIgnored;

    /// <summary>Start menu shortcut found for the program (launch button).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLaunch))]
    private string? _shortcut;

    public bool CanLaunch => Shortcut is not null;

    public string Name => Update.Name;

    public string Id => Update.Id;

    public string Versions => $"{Update.Installed}  →  {Update.Available}";

    public string Source => Update.Source;

    public bool ExplicitOnly => Update.ExplicitOnly;
}

/// <summary>Software updates via winget: list, then update selected packages after confirmation. Nothing is installed automatically.</summary>
public sealed partial class UpdatesViewModel(MainViewModel main) : PageViewModel(main)
{
    private readonly List<UpdateRowViewModel> _all = [];
    private bool _probed;

    public override string TitleKey => "NavUpdates";

    public ObservableCollection<UpdateRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public int IgnoredCount => _all.Count(r => r.IsIgnored);

    public string IgnoredText => Loc.F("UpdatesIgnoredCount", IgnoredCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIgnoredTip))]
    private bool _showIgnored;

    partial void OnShowIgnoredChanged(bool value) => ApplyFilter();

    /// <summary>Tooltip of the eye button: what a click does, with the number of ignored packages.</summary>
    public string ShowIgnoredTip => Loc.F(ShowIgnored ? "TipHideIgnoredUpdates" : "TipShowIgnoredUpdates", IgnoredCount);

    [RelayCommand]
    private void ToggleShowIgnored() => ShowIgnored = !ShowIgnored;

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => ShowIgnored || !r.IsIgnored))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IgnoredCount));
        OnPropertyChanged(nameof(IgnoredText));
        OnPropertyChanged(nameof(ShowIgnoredTip));
        UpdateSelectedCommand.NotifyCanExecuteChanged();
        UpdateAllCommand.NotifyCanExecuteChanged();
    }

    private int CountAvailable() => _all.Count(r => !r.ExplicitOnly && !r.IsIgnored);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable), nameof(IsMissing), nameof(VersionText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(ImportCommand))]
    private string? _wingetVersion;

    public bool IsAvailable => WingetVersion is not null;

    public bool IsMissing => _probed && WingetVersion is null;

    public string VersionText => WingetVersion is null ? "" : Loc.F("WingetVersion", WingetVersion);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(UpdateSelectedCommand), nameof(UpdateAllCommand), nameof(ExportCommand), nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _output = "";

    [ObservableProperty]
    private DateTime? _lastCheck;

    public string LastCheckText => LastCheck is null ? Loc.T("UpdatesNeverChecked") : Loc.F("UpdatesLastCheck", LastCheck.Value.ToString("g"));

    partial void OnLastCheckChanged(DateTime? value) => OnPropertyChanged(nameof(LastCheckText));

    public override void OnActivated()
    {
        _ = ProbeAsync();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(LastCheckText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(IgnoredText));
        OnPropertyChanged(nameof(ShowIgnoredTip));
    }

    private async Task ProbeAsync()
    {
        if (_probed)
        {
            return;
        }

        try
        {
            WingetVersion = await WinGetClient.VersionAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            WingetVersion = null;
        }

        _probed = true;
        OnPropertyChanged(nameof(IsMissing));
    }

    /// <summary>First use: winget contacts its sources; the user accepts their agreements once.</summary>
    private async Task<bool> EnsureAgreementsAsync()
    {
        if (Ui.WingetAgreementsAccepted)
        {
            return true;
        }

        var answer = await Host.ConfirmAsync(Loc.T("WingetConsentTitle"), Loc.T("WingetConsentText"), Loc.T("BtnAcceptContinue"), danger: false,
            confirmIcon: "IconShieldCheck");
        if (!answer.Confirmed)
        {
            return false;
        }

        Ui.WingetAgreementsAccepted = true;
        Ui.Save();
        Main.RestartUpdateTimer();
        return true;
    }

    private bool CanCheck() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        try
        {
            await ProbeAsync();
            if (!IsAvailable || !await EnsureAgreementsAsync())
            {
                return;
            }

            await RunCheckAsync(quiet: false);
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
    }

    /// <summary>Background check (opt-in): only lists, never installs.</summary>
    public async Task CheckQuietlyAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ProbeAsync();
        if (IsAvailable && Ui.WingetAgreementsAccepted)
        {
            await RunCheckAsync(quiet: true);
        }
    }

    private async Task RunCheckAsync(bool quiet)
    {
        IsBusy = true;
        Status = Loc.T("StatusCheckingUpdates");
        try
        {
            var (updates, output) = await WinGetClient.ListUpgradesAsync(Ui.WingetAgreementsAccepted, CancellationToken.None);
            Output = output.Trim();
            _all.Clear();
            foreach (var update in updates)
            {
                var ignored = Ui.IgnoredPackages.Contains(update.Id, StringComparer.OrdinalIgnoreCase);
                var row = new UpdateRowViewModel(update) { IsIgnored = ignored };
                if (ignored)
                {
                    row.IsChecked = false;
                }

                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(UpdateRowViewModel.IsChecked))
                    {
                        UpdateSelectedCommand.NotifyCanExecuteChanged();
                    }
                };
                _all.Add(row);
            }

            ApplyFilter();
            _ = FindShortcutsAsync(_all.ToList());
            LastCheck = DateTime.Now;
            var available = CountAvailable();
            Main.OnUpdatesCounted(available);
            if (!quiet || available > 0)
            {
                Main.ShowToast(available > 0 ? Loc.F("ToastUpdatesFound", available) : Loc.T("ToastNoUpdates"));
            }
        }
        catch (Exception ex)
        {
            if (!quiet)
            {
                Main.ShowError(ex);
            }
        }
        finally
        {
            IsBusy = false;
            Status = "";
            OnPropertyChanged(nameof(HasRows));
            UpdateSelectedCommand.NotifyCanExecuteChanged();
            UpdateAllCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanUpdateSelected() => !IsBusy && Rows.Any(r => r.IsChecked);

    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private Task UpdateSelectedAsync() => UpdateAsync(Rows.Where(r => r.IsChecked).ToList());

    private bool CanUpdateAll() => !IsBusy && _all.Any(r => !r.IsIgnored && !r.ExplicitOnly);

    /// <summary>All listed packages except ignored and explicit-only ones (still confirmed first).</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private Task UpdateAllAsync() => UpdateAsync(_all.Where(r => !r.IsIgnored && !r.ExplicitOnly).ToList());

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows.Where(r => !r.IsIgnored))
        {
            row.IsChecked = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in Rows)
        {
            row.IsChecked = false;
        }
    }

    // ------------------------------------------------------------------ Launch

    private static async Task FindShortcutsAsync(IReadOnlyList<UpdateRowViewModel> rows)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AppLauncher.Refresh();
        var found = await Task.Run(() => rows.Select(r => AppLauncher.FindShortcut(r.Name)).ToList());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Shortcut = found[i];
        }
    }

    /// <summary>Starts the program through Explorer (normal user rights, not CLEANit's administrator rights).</summary>
    [RelayCommand]
    private void Launch(UpdateRowViewModel? row)
    {
        if (row?.Shortcut is { } shortcut && OperatingSystem.IsWindows() && !AppLauncher.Launch(shortcut))
        {
            Main.ShowToast(Loc.T("ToastLaunchFailed"), isError: true);
        }
    }

    // ------------------------------------------------------------------ Ignore list

    [RelayCommand]
    private void ToggleIgnore(UpdateRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsIgnored = !row.IsIgnored;
        Ui.IgnoredPackages.RemoveAll(id => id.Equals(row.Id, StringComparison.OrdinalIgnoreCase));
        if (row.IsIgnored)
        {
            Ui.IgnoredPackages.Add(row.Id);
            row.IsChecked = false;
        }

        Ui.Save();
        ApplyFilter();
        Main.OnUpdatesCounted(CountAvailable());
        Main.ShowToast(Loc.F(row.IsIgnored ? "ToastPackageIgnored" : "ToastPackageUnignored", row.Name));
    }

    // ------------------------------------------------------------------ Inventory (expert)

    private bool CanInventory() => !IsBusy && IsAvailable;

    /// <summary>winget export: list of installed packages as JSON (e.g. to set up a new machine).</summary>
    [RelayCommand(CanExecute = nameof(CanInventory))]
    private async Task ExportAsync()
    {
        if (!await EnsureAgreementsAsync())
        {
            return;
        }

        var path = await Host.PickSavePathAsync(Loc.T("BtnExportInventory"), $"cleanit-winget-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}.json", "json", "JSON");
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusExporting");
        try
        {
            var (success, output) = await WinGetClient.ExportAsync(path, Ui.WingetAgreementsAccepted, CancellationToken.None);
            Output = output.Trim();
            Main.ShowToast(success ? Loc.F("ToastExported", path) : Loc.T("ToastWingetFailed"), isError: !success);
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
    }

    /// <summary>winget import: installs the packages of an exported list – only after confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanInventory))]
    private async Task ImportAsync()
    {
        if (!await EnsureAgreementsAsync())
        {
            return;
        }

        var path = await Host.PickFileAsync(Loc.T("BtnImportInventory"), "JSON", ["*.json"]);
        if (path is null)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("ImportConfirmTitle"), Loc.F("ImportConfirmText", Path.GetFileName(path)), Loc.T("BtnInstall"),
            danger: false, confirmIcon: "IconImport");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusImporting");
        try
        {
            var (success, output) = await WinGetClient.ImportAsync(path, Ui.WingetAgreementsAccepted, CancellationToken.None);
            Output = output.Trim();
            Main.ShowToast(success ? Loc.T("ToastImported") : Loc.T("ToastWingetFailed"), isError: !success);
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
    }

    [RelayCommand]
    private Task UpdateOneAsync(UpdateRowViewModel? row) => row is null ? Task.CompletedTask : UpdateAsync([row]);

    private async Task UpdateAsync(IReadOnlyList<UpdateRowViewModel> rows)
    {
        if (rows.Count == 0 || IsBusy)
        {
            return;
        }

        var names = string.Join("\n", rows.Take(12).Select(r => $"• {r.Name} ({r.Versions})")) + (rows.Count > 12 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(Loc.F("UpdateConfirmTitle", rows.Count), Loc.F("UpdateConfirmText", names), Loc.T("BtnUpdateNow"),
            danger: false, confirmIcon: "IconPackageUp");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        var ok = 0;
        var log = new System.Text.StringBuilder();
        try
        {
            foreach (var row in rows)
            {
                row.State = Loc.T("StateUpdating");
                Status = Loc.F("StatusUpdating", row.Name);
                var (success, output) = await WinGetClient.UpgradeAsync(row.Id, Ui.WingetAgreementsAccepted, CancellationToken.None);
                row.State = Loc.T(success ? "StateUpdated" : "StateUpdateFailed");
                log.AppendLine($"=== {row.Id} ===").AppendLine(output.Trim()).AppendLine();
                if (success)
                {
                    ok++;
                    row.IsChecked = false;
                }
            }

            Output = log.ToString().Trim();
            Main.ShowToast(Loc.F("ToastUpdatesDone", ok, rows.Count), isError: ok < rows.Count);
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

        await RunCheckAsync(quiet: true);
    }
}
