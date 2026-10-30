using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>Main window: navigation, toasts, language, theme, engine reload, opt-in background jobs (winget, reminder, release check).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _reminderTimer;
    private readonly DispatcherTimer _releaseTimer;

    public MainViewModel(UiHost host, UiSettings ui)
    {
        Host = host;
        Ui = ui;
        Engine = new EngineHost(ui);

        Overview = new OverviewViewModel(this);
        FullClean = new CleanerViewModel(this, null);
        SystemClean = new CleanerViewModel(this, CleanArea.System);
        BrowserClean = new CleanerViewModel(this, CleanArea.Browser);
        DeveloperClean = new CleanerViewModel(this, CleanArea.Developer);
        Programs = new ProgramsViewModel(this);
        Startup = new StartupViewModel(this);
        Updates = new UpdatesViewModel(this);
        Catalog = new SoftwareCatalogViewModel(this);
        Tweaks = new WindowsSettingsViewModel(this);
        DeviceTree = new DeviceTreeViewModel(this);
        LargeFiles = new LargeFilesViewModel(this);
        Duplicates = new DuplicatesViewModel(this);
        DiskUsage = new DiskUsageViewModel(this);
        Reports = new ReportsViewModel(this);
        Services = new ServicesViewModel(this);
        DriveHealth = new DriveHealthViewModel(this);
        RuleEditor = new RuleEditorViewModel(this);
        CommandLine = new CommandLineViewModel(this);
        ServerClean = new CleanerViewModel(this, null, server: true);
        ServerProfiles = new ServerProfilesViewModel(this);
        ServerEnvironment = new ServerEnvironmentViewModel(this);
        Settings = new SettingsViewModel(this);
        About = new AboutViewModel(this);

        UpdatesNav = new NavItem("NavUpdates", "IconUpdates", Updates);
        ExpertGroup = new NavGroup("NavGroupExpert",
        [
            new NavItem("NavServices", "IconServices", Services),
            new NavItem("NavRuleEditor", "IconRuleEditor", RuleEditor),
            new NavItem("NavLargeFiles", "IconFiles", LargeFiles),
            new NavItem("NavReports", "IconReport", Reports),
            new NavItem("NavCommandLine", "IconConsole", CommandLine)
        ]) { IsVisible = ui.ExpertMode };
        ServerGroup = new NavGroup("NavGroupServer",
        [
            new NavItem("NavEnvironment", "IconMonitorDashboard", ServerEnvironment),
            new NavItem("NavProfiles", "IconAccountMultiple", ServerProfiles),
            new NavItem("NavServerClean", "IconServer", ServerClean)
        ]) { IsVisible = ui.ServerMode };
        NavGroups =
        [
            new NavGroup(null, [new NavItem("NavOverview", "IconOverview", Overview)]),
            new NavGroup("NavGroupCleanup",
            [
                new NavItem("NavFull", "IconDeleteSweep", FullClean),
                new NavItem("NavSystem", "IconSystem", SystemClean),
                new NavItem("NavBrowser", "IconBrowser", BrowserClean),
                new NavItem("NavDeveloper", "IconDeveloper", DeveloperClean)
            ]),
            new NavGroup("NavGroupTools",
            [
                new NavItem("NavPrograms", "IconPrograms", Programs),
                new NavItem("NavCatalog", "IconPackagePlus", Catalog),
                UpdatesNav,
                new NavItem("NavStartup", "IconStartup", Startup),
                new NavItem("NavTweaks", "IconToggle", Tweaks),
                new NavItem("NavDisk", "IconDisk", DiskUsage),
                new NavItem("NavDriveHealth", "IconHealth", DriveHealth),
                new NavItem("NavDeviceTree", "IconFolderTree", DeviceTree),
                new NavItem("NavDuplicates", "IconCompare", Duplicates)
            ]),
            ExpertGroup,
            ServerGroup
        ];
        BottomGroup = new NavGroup(null,
        [
            new NavItem("NavSettings", "IconCog", Settings),
            new NavItem("NavAbout", "IconInfo", About)
        ]);

        foreach (var group in AllGroups)
        {
            group.Activated += OnNavActivated;
        }

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = null;
        };

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _updateTimer.Tick += async (_, _) => await BackgroundUpdateCheckAsync();

        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _reminderTimer.Tick += async (_, _) => await ReminderTickAsync();

        _releaseTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _releaseTimer.Tick += async (_, _) => await CheckReleaseAsync(manual: false);

        Loc.LanguageChanged += OnLanguageChanged;
        Engine.CatalogChanged += () => Dispatcher.UIThread.Post(OnCatalogChanged);

        _isSidebarCollapsed = ui.SidebarCollapsed;
        NavGroups[0].Selected = NavGroups[0].Items[0];
    }

    public UiHost Host { get; }

    public UiSettings Ui { get; }

    public EngineHost Engine { get; }

    public OverviewViewModel Overview { get; }

    /// <summary>System, browser and development rules on one page.</summary>
    public CleanerViewModel FullClean { get; }

    public CleanerViewModel SystemClean { get; }

    public CleanerViewModel BrowserClean { get; }

    public CleanerViewModel DeveloperClean { get; }

    public ProgramsViewModel Programs { get; }

    public StartupViewModel Startup { get; }

    public UpdatesViewModel Updates { get; }

    public SoftwareCatalogViewModel Catalog { get; }

    public WindowsSettingsViewModel Tweaks { get; }

    public DeviceTreeViewModel DeviceTree { get; }

    public LargeFilesViewModel LargeFiles { get; }

    public DuplicatesViewModel Duplicates { get; }

    public DiskUsageViewModel DiskUsage { get; }

    public ReportsViewModel Reports { get; }

    public ServicesViewModel Services { get; }

    public DriveHealthViewModel DriveHealth { get; }

    public RuleEditorViewModel RuleEditor { get; }

    public CommandLineViewModel CommandLine { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    public ObservableCollection<NavGroup> NavGroups { get; }

    public NavGroup ExpertGroup { get; }

    /// <summary>Server admin mode: server and terminal server tools (environment, profiles, cleanup of signed-out users).</summary>
    public NavGroup ServerGroup { get; }

    public CleanerViewModel ServerClean { get; }

    public ServerProfilesViewModel ServerProfiles { get; }

    public ServerEnvironmentViewModel ServerEnvironment { get; }

    public NavGroup BottomGroup { get; }

    public NavItem UpdatesNav { get; }

    private IEnumerable<NavGroup> AllGroups => NavGroups.Append(BottomGroup);

    public IEnumerable<PageViewModel> Pages => AllGroups.SelectMany(g => g.Items).Select(i => i.Page);

    /// <summary>Raised when tray-relevant state changes (language, updates, reminder, release).</summary>
    public event Action? StateChanged;

    public string WindowTitle { get; } = $"CLEANit  v{AppEnvironment.Version}";

    // ------------------------------------------------------------------ Startup

    public async Task InitializeAsync()
    {
        ApplyTheme();
        await AskFirstRunAsync();
        if (Program.ResetFailures is { } failed)
        {
            ShowToast(failed.Count == 0 ? Loc.T("ToastResetDone") : Loc.F("ToastResetPartial", string.Join(", ", failed.Select(f => Path.GetFileName(f)))),
                isError: failed.Count > 0);
        }

        await Task.Run(EngineHost.InitializeReports);
        if (Ui.BackupCleanupEnabled && OperatingSystem.IsWindows())
        {
            // Opt-in (confirmed when switched on): CLEANit's own backups older than n days.
            var days = Ui.BackupKeepDays;
            _ = Task.Run(() => BackupManager.DeleteOlderThan(days));
        }

        await ReloadEngineAsync();
        Overview.Refresh();
        RestartUpdateTimer();
        RestartReminderTimer();
        RestartReleaseTimer();
    }

    public async Task ReloadEngineAsync()
    {
        IsLoadingRules = true;
        try
        {
            await Engine.ReloadAsync();
            foreach (var error in Engine.Catalog?.Errors ?? [])
            {
                ShowToast(error, isError: true);
            }

            ReleaseMemory();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsLoadingRules = false;
        }
    }

    [ObservableProperty]
    private bool _isLoadingRules;

    /// <summary>
    /// After loading the rules (Winapp2 parsing creates many short-lived objects): one compacting collection that
    /// returns the freed memory to Windows, so the idle working set stays small.
    /// </summary>
    public static void ReleaseMemory() => _ = Task.Run(() =>
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    });

    private void OnCatalogChanged()
    {
        foreach (var page in Pages)
        {
            page.OnCatalogChanged();
        }
    }

    // ------------------------------------------------------------------ First start

    /// <summary>First start: show expert features or keep it simple (can be changed in the settings).</summary>
    private async Task AskFirstRunAsync()
    {
        if (Ui.FirstRunDone)
        {
            return;
        }

        // null = no window yet: ask again at the next start.
        var answer = await Host.FirstRunAsync();
        if (answer is null)
        {
            return;
        }

        Ui.FirstRunDone = true;
        Ui.Save();

        if (answer.Expert)
        {
            Ui.ExpertMode = true;
            ExpertGroup.IsVisible = true;
            OnPropertyChanged(nameof(IsExpert));
            Settings.ExpertMode = true;
        }

        if (answer.TrayAtSignIn)
        {
            // Same scheduled task as Settings › System tray (off unless switched on here).
            Settings.Autostart = true;
        }
    }

    // ------------------------------------------------------------------ Expert mode

    public bool IsExpert => Ui.ExpertMode;

    /// <summary>Shows or hides the admin features and reloads the rules (expert rules, all profiles).</summary>
    public async Task SetExpertModeAsync(bool value)
    {
        if (Ui.ExpertMode == value)
        {
            return;
        }

        Ui.ExpertMode = value;
        Ui.Save();
        ExpertGroup.IsVisible = value;
        if (!value && ExpertGroup.Items.Any(i => ReferenceEquals(i.Page, CurrentPage)))
        {
            NavigateTo(Overview);
        }

        OnPropertyChanged(nameof(IsExpert));
        foreach (var page in Pages)
        {
            page.OnExpertModeChanged();
        }

        await ReloadEngineAsync();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ Server admin mode

    public bool IsServerMode => Ui.ServerMode;

    /// <summary>Shows or hides the Server pages (independent of expert mode).</summary>
    public void SetServerMode(bool value)
    {
        if (Ui.ServerMode == value)
        {
            return;
        }

        Ui.ServerMode = value;
        Ui.Save();
        ServerGroup.IsVisible = value;
        if (!value && ServerGroup.Items.Any(i => ReferenceEquals(i.Page, CurrentPage)))
        {
            NavigateTo(Overview);
        }

        OnPropertyChanged(nameof(IsServerMode));
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ Navigation

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    public double SidebarWidth => IsSidebarCollapsed ? 68 : 240;

    public Geometry? CurrentPageIcon =>
        AllGroups.SelectMany(g => g.Items).FirstOrDefault(n => ReferenceEquals(n.Page, CurrentPage))?.Icon;

    private void OnNavActivated(NavGroup group, NavItem item)
    {
        foreach (var other in AllGroups)
        {
            if (!ReferenceEquals(other, group))
            {
                other.Selected = null;
            }
        }

        if (!ReferenceEquals(CurrentPage, item.Page))
        {
            CurrentPage = item.Page;
        }
    }

    private PageViewModel? _activePage;

    partial void OnCurrentPageChanged(PageViewModel? value)
    {
        OnPropertyChanged(nameof(CurrentPageIcon));
        _activePage?.OnDeactivated();
        _activePage = value;
        value?.OnActivated();
    }

    /// <summary>The main window was hidden to the tray or shown again.</summary>
    public void OnWindowVisibilityChanged(bool visible)
    {
        if (visible)
        {
            CurrentPage?.OnActivated();
        }
        else
        {
            CurrentPage?.OnDeactivated();
        }
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
        Ui.SidebarCollapsed = value;
        Ui.Save();
    }

    public void NavigateTo(PageViewModel page)
    {
        foreach (var group in AllGroups)
        {
            var item = group.Items.FirstOrDefault(n => ReferenceEquals(n.Page, page));
            if (item is not null && group.IsVisible)
            {
                group.Selected = item;
                return;
            }
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    // ------------------------------------------------------------------ Status pill

    public bool IsElevated { get; } = OperatingSystem.IsWindows() && SystemInfo.IsElevated();

    public string StatusPillText => IsElevated ? Loc.T("PillAdmin") : Loc.T("PillNoAdmin");

    // ------------------------------------------------------------------ Language & theme

    public void SetLanguage(string code)
    {
        Ui.Language = code;
        Ui.Save();
        Loc.Apply(code);
    }

    private void OnLanguageChanged()
    {
        foreach (var group in AllGroups)
        {
            group.RefreshText();
        }

        foreach (var page in Pages)
        {
            page.OnLanguageChanged();
        }

        OnPropertyChanged(nameof(StatusPillText));
        OnPropertyChanged(nameof(ReminderText));
        StateChanged?.Invoke();
    }

    public void ApplyTheme() => Host.ApplyTheme(Ui.Theme);

    // ------------------------------------------------------------------ Updates in the background (opt-in)

    public void RestartUpdateTimer()
    {
        _updateTimer.Stop();
        if (Ui.WingetBackgroundCheck && Ui.WingetAgreementsAccepted)
        {
            _updateTimer.Interval = TimeSpan.FromMinutes(1);
            _updateTimer.Start();
        }
    }

    private async Task BackgroundUpdateCheckAsync()
    {
        _updateTimer.Interval = TimeSpan.FromHours(Math.Clamp(Ui.WingetIntervalHours, 1, 168));
        if (!Ui.WingetBackgroundCheck || !Ui.WingetAgreementsAccepted)
        {
            _updateTimer.Stop();
            return;
        }

        await Updates.CheckQuietlyAsync();
    }

    public void OnUpdatesCounted(int count)
    {
        UpdatesNav.Badge = count > 0 ? count.ToString() : null;
        Overview.Refresh();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ Analysis reminder (opt-in, never cleans)

    /// <summary>Last background analysis for the tray ("1.2 GB can be cleaned").</summary>
    public string? ReminderText =>
        Ui.ReminderEnabled && Ui.LastReminderUtc is not null && Ui.LastReminderBytes > 0
            ? Loc.F("TrayReminder", ByteSize.Format(Ui.LastReminderBytes))
            : null;

    public void RestartReminderTimer()
    {
        _reminderTimer.Stop();
        if (Ui.ReminderEnabled)
        {
            _reminderTimer.Interval = TimeSpan.FromMinutes(2);
            _reminderTimer.Start();
        }

        OnPropertyChanged(nameof(ReminderText));
        StateChanged?.Invoke();
    }

    private async Task ReminderTickAsync()
    {
        _reminderTimer.Interval = TimeSpan.FromMinutes(30);
        if (!Ui.ReminderEnabled)
        {
            _reminderTimer.Stop();
            return;
        }

        var due = Ui.LastReminderUtc is null || DateTime.UtcNow - Ui.LastReminderUtc.Value >= TimeSpan.FromDays(Math.Clamp(Ui.ReminderDays, 1, 90));
        if (!due || FullClean.IsBusy || Engine.Catalog is null)
        {
            return;
        }

        var bytes = await FullClean.AnalyzeForReminderAsync();
        if (bytes is null)
        {
            return;
        }

        Ui.LastReminderUtc = DateTime.UtcNow;
        Ui.LastReminderBytes = bytes.Value;
        Ui.Save();
        OnPropertyChanged(nameof(ReminderText));
        if (bytes.Value > 0)
        {
            ShowToast(Loc.F("ToastReminder", ByteSize.Format(bytes.Value)));
        }

        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------ CLEANit release check (opt-in, opens the release page only)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewRelease), nameof(NewReleaseText))]
    private ReleaseInfo? _newRelease;

    public bool HasNewRelease => NewRelease is not null;

    public string NewReleaseText => NewRelease is null ? "" : Loc.F("ReleaseAvailable", NewRelease.Version);

    public void RestartReleaseTimer()
    {
        _releaseTimer.Stop();
        if (!Ui.UpdateCheckEnabled)
        {
            NewRelease = null;
            StateChanged?.Invoke();
            return;
        }

        _releaseTimer.Start();
        if (Ui.LastUpdateCheckUtc is null || DateTime.UtcNow - Ui.LastUpdateCheckUtc.Value > TimeSpan.FromDays(1))
        {
            _ = CheckReleaseAsync(manual: false);
        }
    }

    /// <summary>Asks GitHub for the latest release; only the request is sent.</summary>
    public async Task CheckReleaseAsync(bool manual)
    {
        if (!Ui.UpdateCheckEnabled && !manual)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            NewRelease = await ReleaseChecker.GetNewerAsync(AppEnvironment.Version, cts.Token);
            Ui.LastUpdateCheckUtc = DateTime.UtcNow;
            Ui.Save();
            if (manual)
            {
                ShowToast(NewRelease is null ? Loc.T("ToastNoNewRelease") : NewReleaseText);
            }
        }
        catch (Exception ex)
        {
            if (manual)
            {
                ShowError(ex);
            }
        }

        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void OpenRelease()
    {
        if (NewRelease is not null)
        {
            AppEnvironment.OpenUrl(NewRelease.Url);
        }
    }

    // ------------------------------------------------------------------ Toasts

    [ObservableProperty]
    private string? _toast;

    [ObservableProperty]
    private bool _toastIsError;

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    partial void OnToastChanged(string? value) => OnPropertyChanged(nameof(HasToast));

    public void ShowToast(string message, bool isError = false)
    {
        if (!Ui.NotificationsEnabled && !isError)
        {
            return;
        }

        _toastTimer.Stop();
        ToastIsError = isError;
        Toast = message;
        _toastTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(isError ? Ui.ErrorToastSeconds : Ui.ToastSeconds, 1, 120));
        _toastTimer.Start();
    }

    public void ShowError(Exception ex) => ShowToast(Loc.F("ToastError", ex.Message), isError: true);

    public void ShowCancelled() => ShowToast(Loc.T("ToastCancelled"));

    [RelayCommand]
    private void CloseToast()
    {
        _toastTimer.Stop();
        Toast = null;
    }
}
