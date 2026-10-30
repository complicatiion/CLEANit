using CLEANit.Core.Storage;

namespace CLEANit.App.Services;

/// <summary>Per-user settings (%AppData%\CLEANit\settings.json, portable: data\settings.json). Saved on every change.</summary>
public sealed class UiSettings
{
    public string? Language { get; set; }

    /// <summary>system, dark, shadow or light (first start: dark).</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>The first-start question (expert features yes/no) was answered.</summary>
    public bool FirstRunDone { get; set; }

    public bool SidebarCollapsed { get; set; }

    /// <summary>Window state when CLEANit was closed (first start: maximized).</summary>
    public bool WindowMaximized { get; set; } = true;

    public double WindowWidth { get; set; } = 1320;

    public double WindowHeight { get; set; } = 860;

    /// <summary>Closing the window keeps CLEANit in the tray.</summary>
    public bool CloseToTray { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Admin features (all profiles, event logs, imaging, services, large files, reports, rule editor, command line). Off by default.</summary>
    public bool ExpertMode { get; set; }

    /// <summary>Server admin mode: Server pages (environment, user profiles, server cleanup). Separate from expert mode, off by default.</summary>
    public bool ServerMode { get; set; }

    /// <summary>Width of the details panel on the cleanup pages.</summary>
    public double DetailsWidth { get; set; } = 480;

    /// <summary>Settings format version (one-time adjustments of old defaults, see <see cref="Load"/>); missing in files before v0.0.7.</summary>
    public int SettingsVersion { get; set; }

    private const int CurrentVersion = 7;

    public double ToastSeconds { get; set; } = 4;

    public double ErrorToastSeconds { get; set; } = 8;

    // ------------------------------------------------------------------ Cleaning

    /// <summary>Clean the profiles of all local users (terminal servers). Default: only the current user.</summary>
    public bool AllProfiles { get; set; }

    public bool UseWinapp2 { get; set; } = true;

    /// <summary>Own Winapp2.ini; empty = bundled file.</summary>
    public string? Winapp2Path { get; set; }

    /// <summary>Folders and files CLEANit never touches.</summary>
    public List<string> Exclusions { get; set; } = [];

    /// <summary>Workspace artifacts and uninstall leftovers go to the recycle bin.</summary>
    public bool RecycleBinForFolders { get; set; } = true;

    /// <summary>Rule selection of the user (rule id → selected); missing = rule default.</summary>
    public Dictionary<string, bool> RuleSelection { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? WorkspaceRoot { get; set; }

    public int WorkspaceIdleDays { get; set; } = 30;

    /// <summary>Rule selection of the server cleanup (separate from the normal pages); missing = "RDS" profile.</summary>
    public Dictionary<string, bool> ServerRuleSelection { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Profiles page: "older than" filter in days.</summary>
    public int ProfileAgeDays { get; set; } = 90;

    /// <summary>Own selection profiles: name → rule ids.</summary>
    public Dictionary<string, List<string>> CustomProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? LargeFilesRoot { get; set; }

    public int LargeFilesMinMb { get; set; } = 100;

    public int LargeFilesOlderDays { get; set; }

    public List<string> DuplicateRoots { get; set; } = [];

    public int DuplicateMinMb { get; set; } = 1;

    public string? CompareLeft { get; set; }

    public string? CompareRight { get; set; }

    /// <summary>Folder comparison by content (SHA-256) instead of size and change time.</summary>
    public bool CompareByContent { get; set; }

    /// <summary>Copied files go over existing ones only after this was confirmed per run; deleted files go to the recycle bin.</summary>
    public bool CompareUseRecycleBin { get; set; } = true;

    // ------------------------------------------------------------------ Drive health

    /// <summary>Auto refresh of the drive health page in seconds while it is visible; 0 = off (default).</summary>
    public int DriveHealthRefreshSeconds { get; set; }

    // ------------------------------------------------------------------ Reminder & update check (opt-in)

    /// <summary>Analyze in the background every n days and show the result in the tray. Never cleans.</summary>
    public bool ReminderEnabled { get; set; }

    public int ReminderDays { get; set; } = 7;

    public DateTime? LastReminderUtc { get; set; }

    public long LastReminderBytes { get; set; }

    /// <summary>Check GitHub for a newer CLEANit version (only the request is sent). Off by default.</summary>
    public bool UpdateCheckEnabled { get; set; }

    public DateTime? LastUpdateCheckUtc { get; set; }

    // ------------------------------------------------------------------ Safety

    public bool RestorePointBeforeUninstall { get; set; }

    public bool RestorePointBeforeDeep { get; set; }

    // ------------------------------------------------------------------ Updates (winget)

    /// <summary>Check for updates in the background while the tray runs. Off by default.</summary>
    public bool WingetBackgroundCheck { get; set; }

    public int WingetIntervalHours { get; set; } = 12;

    /// <summary>The user accepted the winget source agreements in CLEANit.</summary>
    public bool WingetAgreementsAccepted { get; set; }

    /// <summary>Package ids excluded from updates.</summary>
    public List<string> IgnoredPackages { get; set; } = [];

    /// <summary>Chocolatey as additional search/install source (only when choco.exe is installed). Off by default.</summary>
    public bool ChocolateyEnabled { get; set; }

    // ------------------------------------------------------------------ Backups

    /// <summary>Delete CLEANit's own backups older than <see cref="BackupKeepDays"/> at start. Off by default.</summary>
    public bool BackupCleanupEnabled { get; set; }

    public int BackupKeepDays { get; set; } = 90;

    public static UiSettings Load()
    {
        try
        {
            var settings = JsonFile.Read<UiSettings>(AppPaths.SettingsFile) ?? new UiSettings();
            if (settings.SettingsVersion < 7)
            {
                // v0.0.7: wider details panel – only if the old default (390) was never changed.
                if (Math.Abs(settings.DetailsWidth - 390) < 0.5)
                {
                    settings.DetailsWidth = 480;
                }

                settings.SettingsVersion = CurrentVersion;
            }

            return settings;
        }
        catch
        {
            return new UiSettings();
        }
    }

    /// <summary>Set before a reset restart: the closing instance must not write its settings back.</summary>
    public static bool Frozen { get; set; }

    public void Save()
    {
        if (Frozen)
        {
            return;
        }

        try
        {
            JsonFile.Write(AppPaths.SettingsFile, this);
        }
        catch
        {
            // Not critical.
        }
    }
}
