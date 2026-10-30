using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

public sealed record ThemeOption(string Code, string Name);

/// <summary>Settings: saved immediately on every change.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    public SettingsViewModel(MainViewModel main) : base(main)
    {
        _loading = true;
        _closeToTray = Ui.CloseToTray;
        _notifications = Ui.NotificationsEnabled;
        _allProfiles = Ui.AllProfiles;
        _useWinapp2 = Ui.UseWinapp2;
        _recycleBin = Ui.RecycleBinForFolders;
        _restoreBeforeUninstall = Ui.RestorePointBeforeUninstall;
        _restoreBeforeDeep = Ui.RestorePointBeforeDeep;
        _wingetBackground = Ui.WingetBackgroundCheck;
        _wingetInterval = Intervals.OrderBy(i => Math.Abs(i - Ui.WingetIntervalHours)).First();
        _exclusions = string.Join(Environment.NewLine, Ui.Exclusions);
        _expertMode = Ui.ExpertMode;
        _serverMode = Ui.ServerMode;
        _reminderEnabled = Ui.ReminderEnabled;
        _reminderDays = Math.Clamp(Ui.ReminderDays, 1, 90);
        _updateCheck = Ui.UpdateCheckEnabled;
        _chocolatey = Ui.ChocolateyEnabled;
        _backupCleanup = Ui.BackupCleanupEnabled;
        _backupKeepDays = Math.Clamp(Ui.BackupKeepDays, 7, 3650);
        _loading = false;
    }

    public override string TitleKey => "NavSettings";

    // ------------------------------------------------------------------ General

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    public LanguageOption? SelectedLanguage
    {
        get => LanguageOption.Current;
        set
        {
            if (value is not null && value.Code != Loc.Language)
            {
                Main.SetLanguage(value.Code);
            }
        }
    }

    /// <summary>Dark (default), Shadow, Light, System.</summary>
    public IReadOnlyList<ThemeOption> Themes =>
    [
        new("dark", Loc.T("ThemeDark")),
        new("shadow", Loc.T("ThemeShadow")),
        new("light", Loc.T("ThemeLight")),
        new("system", Loc.T("ThemeSystem"))
    ];

    public ThemeOption? SelectedTheme
    {
        get => Themes.FirstOrDefault(t => t.Code == Ui.Theme) ?? Themes[0];
        set
        {
            if (value is not null && value.Code != Ui.Theme)
            {
                Ui.Theme = value.Code;
                Ui.Save();
                Main.ApplyTheme();
                OnPropertyChanged();
            }
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(AutostartStatus));
        OnPropertyChanged(nameof(Winapp2Text));
        OnPropertyChanged(nameof(CheckNowTip));
        OnPropertyChanged(nameof(ChocolateyTip));
        OnPropertyChanged(nameof(BackupSummary));
        OnPropertyChanged(nameof(DriveRefreshOptions));
        OnPropertyChanged(nameof(DriveRefreshIndex));
        foreach (var row in Backups)
        {
            row.RefreshText();
        }
    }

    // ------------------------------------------------------------------ Expert mode

    /// <summary>Shows admin features: all profiles, expert rules and profiles, services, drive health, rule editor, command line.</summary>
    [ObservableProperty]
    private bool _expertMode;

    partial void OnExpertModeChanged(bool value)
    {
        if (!_loading)
        {
            _ = Main.SetExpertModeAsync(value);
        }
    }

    // ------------------------------------------------------------------ Server admin mode (separate switch)

    /// <summary>Shows the Server pages; not switched on by expert mode.</summary>
    [ObservableProperty]
    private bool _serverMode;

    partial void OnServerModeChanged(bool value)
    {
        if (!_loading)
        {
            Main.SetServerMode(value);
        }
    }

    // ------------------------------------------------------------------ Analysis reminder (opt-in)

    [ObservableProperty]
    private bool _reminderEnabled;

    [ObservableProperty]
    private decimal _reminderDays;

    partial void OnReminderEnabledChanged(bool value)
    {
        Save(() => Ui.ReminderEnabled = value);
        Main.RestartReminderTimer();
    }

    partial void OnReminderDaysChanged(decimal value)
    {
        Save(() => Ui.ReminderDays = (int)Math.Clamp(value, 1, 90));
        Main.RestartReminderTimer();
    }

    // ------------------------------------------------------------------ CLEANit release check (opt-in)

    [ObservableProperty]
    private bool _updateCheck;

    partial void OnUpdateCheckChanged(bool value)
    {
        Save(() => Ui.UpdateCheckEnabled = value);
        Main.RestartReleaseTimer();
        OnPropertyChanged(nameof(CheckNowTip));
    }

    /// <summary>Tooltip of "Check now": the time of the last check.</summary>
    public string CheckNowTip => Loc.T("BtnCheckNow") + " – " + (Ui.LastUpdateCheckUtc is { } last
        ? Loc.F("UpdatesLastCheck", last.ToLocalTime().ToString("g"))
        : Loc.T("UpdatesNeverChecked"));

    [RelayCommand]
    private async Task CheckReleaseNowAsync()
    {
        await Main.CheckReleaseAsync(manual: true);
        OnPropertyChanged(nameof(CheckNowTip));
    }

    // ------------------------------------------------------------------ Drive health (expert)

    private static readonly int[] DriveRefreshSeconds = [0, 10, 30, 60, 300];

    public IReadOnlyList<string> DriveRefreshOptions =>
        DriveRefreshSeconds.Select(s => s == 0 ? Loc.T("IntervalOff") : DriveHealthViewModel.IntervalText(s)).ToList();

    public int DriveRefreshIndex
    {
        get => Math.Max(0, Array.IndexOf(DriveRefreshSeconds, Ui.DriveHealthRefreshSeconds));
        set
        {
            var seconds = DriveRefreshSeconds[Math.Clamp(value, 0, DriveRefreshSeconds.Length - 1)];
            if (seconds == Ui.DriveHealthRefreshSeconds)
            {
                return;
            }

            Ui.DriveHealthRefreshSeconds = seconds;
            Ui.Save();
            Main.DriveHealth.ApplyRefreshInterval();
            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ Tray & autostart

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _notifications;

    partial void OnCloseToTrayChanged(bool value) => Save(() => Ui.CloseToTray = value);

    partial void OnNotificationsChanged(bool value) => Save(() => Ui.NotificationsEnabled = value);

    private bool? _autostart;
    private string? _registeredCommand;
    private bool _autostartLoaded;

    /// <summary>schtasks is slow: read the task state once in the background when the page opens.</summary>
    public override async void OnActivated()
    {
        _ = RefreshBackupsAsync();
        if (_autostartLoaded || !OperatingSystem.IsWindows())
        {
            return;
        }

        _autostartLoaded = true;
        var user = SystemInfo.CurrentUser();
        _registeredCommand = await Task.Run(() => SelfAutostart.RegisteredCommand(user));
        _autostart = _registeredCommand is not null;
        OnPropertyChanged(nameof(Autostart));
        OnPropertyChanged(nameof(AutostartStatus));
        OnPropertyChanged(nameof(AutostartMoved));
    }

    public bool Autostart
    {
        get => _autostart ?? false;
        set
        {
            if (value == Autostart || !OperatingSystem.IsWindows())
            {
                return;
            }

            var user = SystemInfo.CurrentUser();
            var ok = value ? SelfAutostart.Register(user, AppEnvironment.ExecutablePath) : SelfAutostart.Unregister(user);
            _autostart = ok ? value : !value;
            _registeredCommand = _autostart == true ? AppEnvironment.ExecutablePath : null;
            if (!ok)
            {
                Main.ShowToast(Loc.T("ToastAutostartFailed"), isError: true);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(AutostartStatus));
            OnPropertyChanged(nameof(AutostartMoved));
        }
    }

    /// <summary>The task points to another CLEANit.exe (portable folder moved).</summary>
    public bool AutostartMoved
    {
        get
        {
            var command = _registeredCommand;
            return Autostart && !string.IsNullOrEmpty(command) && !string.Equals(command, AppEnvironment.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public string AutostartStatus => Loc.T(Autostart ? "AutostartOn" : "AutostartOff");

    [RelayCommand]
    private void RepairAutostart()
    {
        if (OperatingSystem.IsWindows() && SelfAutostart.Register(SystemInfo.CurrentUser(), AppEnvironment.ExecutablePath))
        {
            _registeredCommand = AppEnvironment.ExecutablePath;
            Main.ShowToast(Loc.T("ToastAutostartRepaired"));
        }

        OnPropertyChanged(nameof(AutostartMoved));
    }

    // ------------------------------------------------------------------ Cleaning

    [ObservableProperty]
    private bool _allProfiles;

    [ObservableProperty]
    private bool _useWinapp2;

    [ObservableProperty]
    private bool _recycleBin;

    [ObservableProperty]
    private string _exclusions;

    partial void OnAllProfilesChanged(bool value) => SaveAndReload(() => Ui.AllProfiles = value);

    partial void OnUseWinapp2Changed(bool value) => SaveAndReload(() => Ui.UseWinapp2 = value);

    partial void OnRecycleBinChanged(bool value) => Save(() => Ui.RecycleBinForFolders = value);

    public string Winapp2Text => string.IsNullOrEmpty(Ui.Winapp2Path) ? Loc.T("Winapp2Bundled") : Ui.Winapp2Path;

    public bool HasCustomWinapp2 => !string.IsNullOrEmpty(Ui.Winapp2Path);

    [RelayCommand]
    private async Task ImportWinapp2Async()
    {
        var file = await Host.PickFileAsync(Loc.T("Winapp2PickTitle"), "Winapp2.ini", ["*.ini"]);
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        SaveAndReload(() => Ui.Winapp2Path = file);
        OnPropertyChanged(nameof(Winapp2Text));
        OnPropertyChanged(nameof(HasCustomWinapp2));
    }

    [RelayCommand]
    private void ResetWinapp2()
    {
        SaveAndReload(() => Ui.Winapp2Path = null);
        OnPropertyChanged(nameof(Winapp2Text));
        OnPropertyChanged(nameof(HasCustomWinapp2));
    }

    /// <summary>Discards unsaved edits of the exclusion list.</summary>
    [RelayCommand]
    private void RevertExclusions() => Exclusions = string.Join(Environment.NewLine, Ui.Exclusions);

    [RelayCommand]
    private void SaveExclusions()
    {
        var list = (Exclusions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SaveAndReload(() => Ui.Exclusions = list);
        Main.ShowToast(Loc.F("ToastExclusionsSaved", list.Count));
    }

    [RelayCommand]
    private async Task AddExclusionAsync()
    {
        var folder = await Host.PickFolderAsync(Loc.T("ExclusionPickTitle"), null);
        if (!string.IsNullOrEmpty(folder))
        {
            Exclusions = string.IsNullOrWhiteSpace(Exclusions) ? folder : Exclusions.TrimEnd() + Environment.NewLine + folder;
            SaveExclusions();
        }
    }

    // ------------------------------------------------------------------ Safety

    public bool RestoreSupported { get; } = OperatingSystem.IsWindows() && RestorePoint.IsSupported;

    public bool RestoreUnsupported => !RestoreSupported;

    [ObservableProperty]
    private bool _restoreBeforeUninstall;

    [ObservableProperty]
    private bool _restoreBeforeDeep;

    partial void OnRestoreBeforeUninstallChanged(bool value) => Save(() => Ui.RestorePointBeforeUninstall = value);

    partial void OnRestoreBeforeDeepChanged(bool value) => Save(() => Ui.RestorePointBeforeDeep = value);

    // ------------------------------------------------------------------ Updates

    public IReadOnlyList<int> Intervals { get; } = [6, 12, 24, 48];

    [ObservableProperty]
    private bool _wingetBackground;

    [ObservableProperty]
    private int _wingetInterval;

    public bool WingetAccepted => Ui.WingetAgreementsAccepted;

    partial void OnWingetBackgroundChanged(bool value)
    {
        Save(() => Ui.WingetBackgroundCheck = value);
        Main.RestartUpdateTimer();
    }

    partial void OnWingetIntervalChanged(int value)
    {
        Save(() => Ui.WingetIntervalHours = value <= 0 ? 12 : value);
        Main.RestartUpdateTimer();
    }

    [RelayCommand]
    private void RevokeWinget()
    {
        Save(() => Ui.WingetAgreementsAccepted = false);
        Main.RestartUpdateTimer();
        OnPropertyChanged(nameof(WingetAccepted));
        Main.ShowToast(Loc.T("ToastWingetRevoked"));
    }

    // ------------------------------------------------------------------ Chocolatey (optional source)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChocolateyTip), nameof(ChocolateyMissing))]
    [NotifyCanExecuteChangedFor(nameof(InstallChocolateyCommand))]
    private bool _chocolateyAvailable = OperatingSystem.IsWindows() && ChocolateyClient.IsInstalled;

    public bool ChocolateyMissing => !ChocolateyAvailable;

    /// <summary>Only shown when Chocolatey is missing (the switch is disabled then).</summary>
    public string? ChocolateyTip => ChocolateyAvailable ? null : Loc.T("LblChocolateyMissing");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallChocolateyCommand))]
    private bool _isInstallingChocolatey;

    private bool CanInstallChocolatey() => !ChocolateyAvailable && !IsInstallingChocolatey;

    /// <summary>
    /// One-click install of Chocolatey through winget (official package Chocolatey.Chocolatey) – nothing is shipped with CLEANit.
    /// Confirmed first (source, publisher, license, terms of the community packages); the source is detected but not switched on.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInstallChocolatey))]
    private async Task InstallChocolateyAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IsInstallingChocolatey = true;
        try
        {
            if (await WinGetClient.VersionAsync(CancellationToken.None) is null)
            {
                Main.ShowToast(Loc.T("ToastChocolateyNoWinget"), isError: true);
                return;
            }

            var answer = await Host.ConfirmAsync(Loc.T("ChocolateyInstallTitle"),
                Loc.T("ChocolateyInstallText") + (Ui.WingetAgreementsAccepted ? "" : "\n\n" + Loc.T("WingetConsentText")),
                Loc.T("BtnInstall"), danger: false, confirmIcon: "IconDownload");
            if (!answer.Confirmed)
            {
                return;
            }

            if (!Ui.WingetAgreementsAccepted)
            {
                Ui.WingetAgreementsAccepted = true;
                Ui.Save();
                OnPropertyChanged(nameof(WingetAccepted));
            }

            var (success, output) = await WinGetClient.InstallAsync("Chocolatey.Chocolatey", new InstallOptions(), acceptSourceAgreements: true,
                CancellationToken.None);
            ChocolateyAvailable = ChocolateyClient.IsInstalled;
            if (ChocolateyAvailable)
            {
                Main.ShowToast(Loc.T("ToastChocolateyInstalled"));
            }
            else
            {
                Main.ShowToast(Loc.T("ToastChocolateyFailed") + (success ? "" : " – " + LastLine(output)), isError: true);
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsInstallingChocolatey = false;
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";

    [ObservableProperty]
    private bool _chocolatey;

    partial void OnChocolateyChanged(bool value) => Save(() => Ui.ChocolateyEnabled = value);

    // ------------------------------------------------------------------ Backups

    public System.Collections.ObjectModel.ObservableCollection<BackupRowViewModel> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    public string BackupSummary => Loc.F("BackupsSummary", Backups.Count, ByteSize.Format(Backups.Sum(b => b.Item.Bytes)));

    [ObservableProperty]
    private bool _backupCleanup;

    [ObservableProperty]
    private decimal _backupKeepDays;

    partial void OnBackupCleanupChanged(bool value)
    {
        if (!_loading)
        {
            _ = ConfirmBackupCleanupAsync(value);
        }
    }

    // Automatic deletion only with an explicit yes.
    private async Task ConfirmBackupCleanupAsync(bool value)
    {
        if (value)
        {
            var answer = await Host.ConfirmAsync(Loc.T("BackupCleanupConfirmTitle"), Loc.F("BackupCleanupConfirmText", (int)BackupKeepDays),
                Loc.T("BtnTurnOn"), danger: false, confirmIcon: "IconCheck");
            if (!answer.Confirmed)
            {
                _loading = true;
                BackupCleanup = false;
                _loading = false;
                return;
            }
        }

        Save(() => Ui.BackupCleanupEnabled = value);
    }

    partial void OnBackupKeepDaysChanged(decimal value) => Save(() => Ui.BackupKeepDays = (int)Math.Clamp(value, 7, 3650));

    [RelayCommand]
    private async Task RefreshBackupsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var items = await Task.Run(BackupManager.List);
        Backups.Clear();
        foreach (var item in items)
        {
            Backups.Add(new BackupRowViewModel(item));
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(BackupSummary));
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupRowViewModel? row)
    {
        if (row is null || !row.Item.CanRestore || !OperatingSystem.IsWindows())
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("BackupRestoreTitle"), Loc.F("BackupRestoreText", row.KindText, row.Title), Loc.T("BtnRestore"),
            danger: false, confirmIcon: "IconUndo");
        if (!answer.Confirmed)
        {
            return;
        }

        var ok = await Task.Run(() => BackupManager.Restore(row.Item));
        Main.ShowToast(ok ? Loc.F("ToastBackupRestored", row.Title) : Loc.T("ToastBackupRestoreFailed"), isError: !ok);
        await RefreshBackupsAsync();
    }

    [RelayCommand]
    private void OpenBackup(BackupRowViewModel? row) => AppEnvironment.OpenFolder(row?.Item.Path);

    [RelayCommand]
    private async Task DeleteBackupAsync(BackupRowViewModel? row)
    {
        if (row is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("BackupDeleteTitle"), Loc.F("BackupDeleteText", row.KindText, row.Title), Loc.T("BtnDelete"),
            danger: true, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        if (!BackupManager.Delete(row.Item))
        {
            Main.ShowToast(Loc.T("ToastDeleteFailed"), isError: true);
        }

        await RefreshBackupsAsync();
    }

    [RelayCommand]
    private async Task CleanupBackupsNowAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var days = (int)BackupKeepDays;
        var answer = await Host.ConfirmAsync(Loc.T("BackupDeleteTitle"), Loc.F("BackupCleanupNowText", days), Loc.T("BtnDelete"), danger: true,
            confirmIcon: "IconDeleteSweep");
        if (!answer.Confirmed)
        {
            return;
        }

        var removed = await Task.Run(() => BackupManager.DeleteOlderThan(days));
        Main.ShowToast(Loc.F("ToastBackupsCleaned", removed));
        await RefreshBackupsAsync();
    }

    // ------------------------------------------------------------------ Data

    public string DataFolder => AppPaths.DataDirectory;

    [RelayCommand]
    private void OpenFolder(string? which) => AppEnvironment.OpenFolder(which switch
    {
        "logs" => AppPaths.LogsDirectory,
        "rules" => AppPaths.CustomRulesDirectory,
        "backups" => AppPaths.BackupsDirectory,
        _ => AppPaths.DataDirectory
    }, create: true);

    // ------------------------------------------------------------------ Helpers

    private void Save(Action apply)
    {
        if (_loading)
        {
            return;
        }

        apply();
        Ui.Save();
    }

    // ------------------------------------------------------------------ Reset CLEANit

    /// <summary>Everything – like a new installation (overrides the single switches).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetPartsEnabled))]
    [NotifyCanExecuteChangedFor(nameof(ResetAppCommand))]
    private bool _resetAll;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAppCommand))]
    private bool _resetSettings = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAppCommand))]
    private bool _resetReports = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAppCommand))]
    private bool _resetCaches = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAppCommand))]
    private bool _resetBackups;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetAppCommand))]
    private bool _resetRules;

    public bool ResetPartsEnabled => !ResetAll;

    private ResetParts SelectedResetParts => ResetAll
        ? ResetParts.All
        : (ResetSettings ? ResetParts.Settings : 0) | (ResetReports ? ResetParts.Reports : 0) | (ResetCaches ? ResetParts.Caches : 0) |
          (ResetBackups ? ResetParts.Backups : 0) | (ResetRules ? ResetParts.RulesAndBundles : 0);

    private bool CanResetApp() => SelectedResetParts != ResetParts.None;

    /// <summary>Deletes the chosen parts of CLEANit's own data after a confirmation; CLEANit restarts to do it.</summary>
    [RelayCommand(CanExecute = nameof(CanResetApp))]
    private async Task ResetAppAsync()
    {
        var parts = SelectedResetParts;
        string[] lines = parts == ResetParts.All
            ? [Loc.T("ResetPartAll")]
            : new[]
            {
                (ResetParts.Settings, "ResetPartSettings"), (ResetParts.Reports, "ResetPartReports"), (ResetParts.Caches, "ResetPartCaches"),
                (ResetParts.Backups, "ResetPartBackups"), (ResetParts.RulesAndBundles, "ResetPartRules")
            }.Where(p => parts.HasFlag(p.Item1)).Select(p => Loc.T(p.Item2)).ToArray();
        var answer = await Host.ConfirmAsync(Loc.T("ResetConfirmTitle"),
            Loc.F("ResetConfirmText", string.Join("\n", lines.Select(l => "• " + l))) +
            (parts.HasFlag(ResetParts.Backups) ? "\n\n" + Loc.T("ResetBackupsWarning") : "") +
            (!AppPaths.IsPortable && parts != ResetParts.Settings ? "\n\n" + Loc.T("ResetSharedNote") : ""),
            Loc.T("BtnResetApp"), danger: true, confirmIcon: "IconBackupRestore");
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            if (!Host.RestartForReset(parts))
            {
                Main.ShowToast(Loc.T("ToastResetFailed"), isError: true);
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
    }

    private void SaveAndReload(Action apply)
    {
        if (_loading)
        {
            return;
        }

        apply();
        Ui.Save();
        _ = Main.ReloadEngineAsync();
    }
}

/// <summary>Backup in the settings list.</summary>
public sealed class BackupRowViewModel(BackupItem item) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public BackupItem Item { get; } = item;

    public string KindText => Loc.T("BackupKind" + char.ToUpperInvariant(Item.Kind[0]) + Item.Kind[1..]);

    public string Title => Item.Title;

    public string Time => Item.TimeUtc.ToLocalTime().ToString("g");

    public string Size => ByteSize.Format(Item.Bytes);

    public void RefreshText() => OnPropertyChanged(nameof(KindText));
}
