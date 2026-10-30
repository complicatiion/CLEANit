namespace CLEANit.Core.Storage;

/// <summary>Data folders: %ProgramData%\CLEANit (setup) or .\data next to the app (portable).</summary>
public static class AppPaths
{
    public const string PortableMarker = "CLEANit.portable";

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static bool IsPortable => File.Exists(Path.Combine(BaseDirectory, PortableMarker));

    public static string DataDirectory => IsPortable
        ? Path.Combine(BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CLEANit");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");

    /// <summary>Package bundles of the software catalog (winget export format).</summary>
    public static string BundlesDirectory => Path.Combine(DataDirectory, "bundles");

    /// <summary>Own rule files (admin-only folder).</summary>
    public static string CustomRulesDirectory => Path.Combine(DataDirectory, "rules");

    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");

    /// <summary>Bundled Winapp2.ini (shipped unmodified next to the app).</summary>
    public static string BundledWinapp2 => Path.Combine(BaseDirectory, "rules", "winapp2", "Winapp2.ini");

    /// <summary>Per-user interface settings.</summary>
    public static string SettingsFile => IsPortable
        ? Path.Combine(DataDirectory, "settings.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CLEANit", "settings.json");
}
