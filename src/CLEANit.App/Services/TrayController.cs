using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CLEANit.App.Localization;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Services;

/// <summary>System tray: left click opens CLEANit, right click shows the full menu. Every entry has an icon.</summary>
public sealed class TrayController : IDisposable
{
    // Native tray menus have no theme: neutral icons, brand pink for the main actions.
    private static readonly Color IconColor = Color.Parse("#8A8A93");
    private static readonly Color AccentColor = Color.Parse("#E91E63");

    // The real CLEANit logo (full colour) in front of the name and version.
    private static readonly Lazy<Bitmap> Logo = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://CLEANit/Assets/tray-logo.png"));
        return new Bitmap(stream);
    });

    private readonly MainViewModel _main;
    private readonly Action _showMainWindow;
    private readonly Action _exit;
    private readonly TrayIcon _trayIcon;

    public TrayController(Application app, MainViewModel main, Action showMainWindow, Action exit)
    {
        _main = main;
        _showMainWindow = showMainWindow;
        _exit = exit;

        using var stream = AssetLoader.Open(new Uri("avares://CLEANit/Assets/tray.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(stream),
            ToolTipText = "CLEANit",
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => _showMainWindow();
        TrayIcon.SetIcons(app, new TrayIcons { _trayIcon });

        _main.StateChanged += Update;
        Update();
    }

    private void Update()
    {
        var updates = _main.UpdatesNav.Badge;
        var reminder = _main.ReminderText;
        var tip = "CLEANit";
        if (!string.IsNullOrEmpty(reminder))
        {
            tip += " – " + reminder;
        }

        if (!string.IsNullOrEmpty(updates))
        {
            tip += " – " + Loc.F("TrayUpdates", updates);
        }

        _trayIcon.ToolTipText = tip;
        _trayIcon.Menu = BuildMenu(updates, reminder);
    }

    private NativeMenu BuildMenu(string? updates, string? reminder)
    {
        var menu = new NativeMenu();
        var header = new NativeMenuItem("CLEANit  v" + AppEnvironment.Version) { Icon = Logo.Value };
        header.Click += (_, _) => _showMainWindow();
        menu.Add(header);
        if (_main.HasNewRelease)
        {
            menu.Add(Item(_main.NewReleaseText, "IconDownloadCircle", () => _main.OpenReleaseCommand.Execute(null), AccentColor));
        }

        if (!string.IsNullOrEmpty(reminder))
        {
            menu.Add(Item(reminder, "IconBellAlert", () => Navigate(_main.FullClean), AccentColor));
        }

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(Loc.T("TrayOpen"), "IconWindow", _showMainWindow));
        menu.Add(Item(Loc.T("NavOverview"), "IconOverview", () => Navigate(_main.Overview)));
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Item(Loc.T("TrayAnalyzeFull"), "IconDeleteSweep", () => Analyze(_main.FullClean), AccentColor));
        menu.Add(Item(Loc.T("TrayAnalyzeSystem"), "IconAnalyze", () => Analyze(_main.SystemClean)));
        menu.Add(Item(Loc.T("TrayAnalyzeBrowser"), "IconBrowser", () => Analyze(_main.BrowserClean)));
        menu.Add(Item(Loc.T("TrayAnalyzeDeveloper"), "IconDeveloper", () => Analyze(_main.DeveloperClean)));
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Item(Loc.T("NavPrograms"), "IconPrograms", () => Navigate(_main.Programs)));
        menu.Add(Item(Loc.T("NavCatalog"), "IconPackagePlus", () => Navigate(_main.Catalog)));
        menu.Add(Item(string.IsNullOrEmpty(updates) ? Loc.T("NavUpdates") : Loc.F("TrayUpdates", updates), "IconUpdates",
            () => Navigate(_main.Updates), string.IsNullOrEmpty(updates) ? null : AccentColor));
        menu.Add(Item(Loc.T("NavStartup"), "IconStartup", () => Navigate(_main.Startup)));
        menu.Add(Item(Loc.T("NavTweaks"), "IconToggle", () => Navigate(_main.Tweaks)));
        menu.Add(Item(Loc.T("NavDisk"), "IconDisk", () => Navigate(_main.DiskUsage)));
        menu.Add(Item(Loc.T("NavDriveHealth"), "IconHealth", () => Navigate(_main.DriveHealth)));
        menu.Add(Item(Loc.T("NavDeviceTree"), "IconFolderTree", () => Navigate(_main.DeviceTree)));
        menu.Add(Item(Loc.T("NavDuplicates"), "IconCompare", () => Navigate(_main.Duplicates)));
        if (_main.IsExpert)
        {
            menu.Add(Item(Loc.T("NavLargeFiles"), "IconFiles", () => Navigate(_main.LargeFiles)));
            menu.Add(Item(Loc.T("NavReports"), "IconReport", () => Navigate(_main.Reports)));
        }

        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Item(Loc.T("NavSettings"), "IconCog", () => Navigate(_main.Settings)));
        menu.Add(Submenu(Loc.T("LblLanguage"), "IconLanguage", BuildLanguageMenu()));
        menu.Add(Item(Loc.T("NavAbout"), "IconInfo", () => Navigate(_main.About)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(Loc.T("TrayExit"), "IconExit", _exit));
        return menu;
    }

    private NativeMenu BuildLanguageMenu()
    {
        var menu = new NativeMenu();
        foreach (var language in LanguageOption.All)
        {
            var code = language.Code;
            var header = language.Code == Loc.Language ? $"{language.Name}  ✓" : language.Name;
            var item = new NativeMenuItem(header) { Icon = language.Flag ?? IconRenderer.Render("IconLanguage", IconColor) };
            item.Click += (_, _) => _main.SetLanguage(code);
            menu.Add(item);
        }

        return menu;
    }

    /// <summary>Opens the page and starts the analysis – cleaning still needs the confirmation in the window.</summary>
    private void Analyze(CleanerViewModel page)
    {
        Navigate(page);
        if (page.AnalyzeCommand.CanExecute(null))
        {
            _ = page.AnalyzeAsync();
        }
    }

    private void Navigate(PageViewModel page)
    {
        _showMainWindow();
        _main.NavigateTo(page);
    }

    private static NativeMenuItem Item(string header, string iconKey, Action action, Color? color = null)
    {
        var item = new NativeMenuItem(header) { Icon = IconRenderer.Render(iconKey, color ?? IconColor) };
        item.Click += (_, _) => action();
        return item;
    }

    private static NativeMenuItem Submenu(string header, string iconKey, NativeMenu menu) =>
        new(header) { Icon = IconRenderer.Render(iconKey, IconColor), Menu = menu };

    public void Dispose()
    {
        _main.StateChanged -= Update;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
