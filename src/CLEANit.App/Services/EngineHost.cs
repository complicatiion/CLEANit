using CLEANit.Core.Abstractions;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using CLEANit.Core.Rules;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.Services;

/// <summary>Engine objects for the current settings: platform services, safety guard, rule catalog, report index.</summary>
public sealed class EngineHost
{
    private readonly UiSettings _ui;
    private readonly bool? _allProfilesOverride;

    public EngineHost(UiSettings ui, bool? allProfilesOverride = null)
    {
        _ui = ui;
        _allProfilesOverride = allProfilesOverride;
        Guard = CreateGuard();
        Platform = WindowsPlatform.Create(Guard, () => Profiles);
    }

    public PlatformServices Platform { get; private set; }

    public PathGuard Guard { get; private set; }

    public RuleCatalog? Catalog { get; private set; }

    public event Action? CatalogChanged;

    /// <summary>All user profiles only in expert mode (or when the command line asks for it).</summary>
    public bool AllProfiles => _allProfilesOverride ?? (_ui.ExpertMode && _ui.AllProfiles);

    public IReadOnlyList<UserProfile> Profiles => AllProfiles ? Platform.Profiles.All() : [Platform.Profiles.Current];

    /// <summary>Report index (SQLite); folder scan when the database cannot be opened.</summary>
    public static void InitializeReports()
    {
        try
        {
            ReportStore.Index = new SqliteReportIndex();
            ReportStore.SyncIndex();
        }
        catch (Exception)
        {
            ReportStore.Index = null;
        }
    }

    public async Task ReloadAsync(bool includeExpert = false)
    {
        Guard = CreateGuard();
        Platform = WindowsPlatform.Create(Guard, () => Profiles);
        var options = new RuleCatalogOptions
        {
            IncludeWinapp2 = _ui.UseWinapp2,
            Winapp2Path = _ui.Winapp2Path,
            // Detection looks at every profile on multi-user machines (servers, multi-session) in expert mode,
            // so the server cleanup lists programs other users have; what is cleaned stays per page.
            AllProfiles = AllProfiles || ((_ui.ExpertMode || _ui.ServerMode) && OperatingSystem.IsWindows() && SystemInfo.IsMultiUser()),
            IncludeExpert = includeExpert || _ui.ExpertMode
        };
        var platform = Platform;
        Catalog = await Task.Run(() => RuleCatalog.Load(platform, options));
        CatalogChanged?.Invoke();
    }

    public IReadOnlyList<CleanRule> RulesFor(CleanArea? area) =>
        Catalog is null ? [] : area is null ? Catalog.Rules.ToList() : Catalog.For(area.Value).ToList();

    public Scanner CreateScanner() => new(Platform, new TargetResolver(Guard, Profiles));

    public Cleaner CreateCleaner() => new(Platform, new TargetResolver(Guard, Profiles));

    /// <summary>Scanner for an explicit set of profiles (server cleanup: signed-out users).</summary>
    public Scanner CreateScanner(IReadOnlyList<UserProfile> profiles) => new(Platform, new TargetResolver(Guard, profiles));

    public Cleaner CreateCleaner(IReadOnlyList<UserProfile> profiles) => new(Platform, new TargetResolver(Guard, profiles));

    // The guard always knows every profile, so no rule can reach into another user's libraries.
    private PathGuard CreateGuard() => new(new WindowsProfiles().All(), _ui.Exclusions);
}
