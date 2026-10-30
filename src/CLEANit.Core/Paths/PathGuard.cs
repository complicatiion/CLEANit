using System.IO.Enumeration;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Models;

namespace CLEANit.Core.Paths;

public readonly record struct GuardVerdict(bool Allowed, string? Reason)
{
    public static readonly GuardVerdict Ok = new(true, null);

    public static GuardVerdict Deny(string reason) => new(false, reason);
}

/// <summary>
/// Safety net that is independent of the rules: refuses system folders, user libraries, credentials,
/// browser passwords/bookmarks and user exclusions – whatever a rule says.
/// </summary>
public sealed class PathGuard
{
    /// <summary>Files never deleted (passwords, bookmarks, profile keys, system files).</summary>
    public static readonly IReadOnlySet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Chromium
        "Login Data", "Login Data-journal", "Login Data For Account", "Login Data For Account-journal",
        "Web Data", "Web Data-journal", "Bookmarks", "Bookmarks.bak", "Local State", "Preferences", "Secure Preferences",
        "Affiliation Database", "Account Web Data",
        // Firefox
        "logins.json", "logins-backup.json", "key3.db", "key4.db", "cert8.db", "cert9.db", "signons.sqlite",
        "places.sqlite", "places.sqlite-wal", "places.sqlite-shm", "favicons.sqlite", "formhistory.sqlite",
        "prefs.js", "user.js", "profiles.ini", "installs.ini", "pkcs11.txt",
        // Windows
        "NTUSER.DAT", "ntuser.dat.LOG1", "ntuser.dat.LOG2", "UsrClass.dat", "pagefile.sys", "hiberfil.sys", "swapfile.sys",
        "desktop.ini", ".git-credentials", "id_rsa", "id_ed25519"
    };

    public static readonly IReadOnlySet<string> ProtectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pst", ".ost", ".kdbx", ".kdb", ".pfx", ".p12", ".wallet", ".vhd", ".vhdx"
    };

    private static readonly string[] ProfileDenyZones =
    [
        "Documents", "Desktop", "Pictures", "Music", "Videos", "Downloads", "Favorites", "Contacts", "Links",
        "Saved Games", "Searches", "source", "OneDrive*",
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker",
        @"AppData\Roaming\Microsoft\Windows\Start Menu",
        @"AppData\Roaming\Microsoft\Protect",
        @"AppData\Roaming\Microsoft\Credentials",
        @"AppData\Local\Microsoft\Credentials",
        @"AppData\Roaming\Microsoft\SystemCertificates",
        @"AppData\Roaming\Microsoft\Crypto",
        @"AppData\Local\Microsoft\Vault",
        @"AppData\Roaming\Microsoft\Vault",
        @"AppData\Roaming\Microsoft\Signatures",
        @"AppData\Roaming\Microsoft\Templates",
        @"AppData\Local\Microsoft\Outlook"
    ];

    /// <summary>Folders below %WinDir% that rules may clean.</summary>
    private static readonly string[] WindowsAllowList =
    [
        "Temp", "SystemTemp", "Logs", "Minidump", "LiveKernelReports", "Panther", "Debug",
        @"SoftwareDistribution\Download",
        @"SoftwareDistribution\DataStore\Logs",
        @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache",
        @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Logs",
        @"ServiceProfiles\LocalService\AppData\Local\Temp",
        @"ServiceProfiles\NetworkService\AppData\Local\Temp",
        @"System32\config\systemprofile\AppData\Local\Temp",
        @"Microsoft.NET\Framework*\*\Temporary ASP.NET Files"
    ];

    /// <summary>Folders directly on a drive that rules may clean as a whole.</summary>
    private static readonly string[] DriveRootAllowList = ["Windows.old", "$Windows.~BT", "$Windows.~WS", "found.###", "$WinREAgent"];

    /// <summary>Extensions allowed for non-recursive cleaning directly in a protected root (e.g. %WinDir%\*.log).</summary>
    private static readonly HashSet<string> RootFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".dmp", ".tmp", ".bak", ".old", ".etl", ".chk"
    };

    private readonly string _windows;
    private readonly List<string> _criticalRoots = [];
    private readonly List<string> _appDataRoots = [];
    private readonly List<string> _profileRoots = [];
    private readonly List<string> _containerRoots = [];
    private readonly List<string> _denyZones = [];
    private readonly List<string> _credentialZones = [];
    private readonly List<string> _exclusions = [];

    public PathGuard(IEnumerable<UserProfile> profiles, IEnumerable<string>? exclusions = null)
    {
        _windows = Norm(KnownFolders.Windows);
        AddCritical(_containerRoots, KnownFolders.ProgramFiles, KnownFolders.ProgramFilesX86, KnownFolders.CommonProgramFiles,
            KnownFolders.CommonProgramFilesX86, KnownFolders.ProgramData, KnownFolders.UsersRoot);
        AddCritical(_criticalRoots, KnownFolders.Windows);

        foreach (var profile in profiles)
        {
            AddCritical(_profileRoots, profile.ProfilePath);
            AddCritical(_appDataRoots, Path.Combine(profile.ProfilePath, "AppData"), profile.AppData, profile.LocalAppData,
                profile.LocalLowAppData);
            foreach (var zone in ProfileDenyZones)
            {
                var full = Norm(Path.Combine(profile.ProfilePath, zone));
                _denyZones.Add(full);
                if (zone.StartsWith('.') || zone.Contains(@"\Microsoft\", StringComparison.Ordinal))
                {
                    _credentialZones.Add(full);
                }
            }
        }

        foreach (var zone in new[] { @"Microsoft\Crypto", @"Microsoft\Windows\Start Menu", @"Microsoft\Windows Defender" })
        {
            var full = Norm(Path.Combine(KnownFolders.ProgramData, zone));
            _denyZones.Add(full);
            _credentialZones.Add(full);
        }

        _criticalRoots.AddRange(_containerRoots);
        _criticalRoots.AddRange(_profileRoots);
        _criticalRoots.AddRange(_appDataRoots);

        foreach (var exclusion in exclusions ?? [])
        {
            if (!string.IsNullOrWhiteSpace(exclusion))
            {
                _exclusions.Add(Norm(Environment.ExpandEnvironmentVariables(exclusion.Trim())));
            }
        }
    }

    /// <summary>Strict check for rule targets (built-in, Winapp2, custom).</summary>
    public GuardVerdict CheckRuleTarget(string directory, CleanTarget target)
    {
        var full = Norm(directory);
        var basic = CheckBasics(full);
        if (!basic.Allowed)
        {
            return basic;
        }

        if (InZone(full, _denyZones))
        {
            return GuardVerdict.Deny("protected user folder");
        }

        var recursive = target.Recursive || target.RemoveSelf;
        if (IsUnderOrEqual(full, _windows))
        {
            if (full.Equals(_windows, StringComparison.OrdinalIgnoreCase))
            {
                return !recursive && OnlyRootFilePatterns(target) ? GuardVerdict.Ok : GuardVerdict.Deny("Windows folder");
            }

            var relative = full[(_windows.Length + 1)..];
            return WindowsAllowList.Any(a => MatchesPrefix(relative, a)) ? GuardVerdict.Ok : GuardVerdict.Deny("Windows folder");
        }

        // Directly on a drive (C:\Something).
        var root = Path.GetPathRoot(full) ?? "";
        var belowDrive = full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (belowDrive.Length == 1 && target.IsBroad && !IsCriticalRoot(full))
        {
            return DriveRootAllowList.Any(a => FileSystemName.MatchesSimpleExpression(a.Replace("###", "???"), belowDrive[0]))
                ? GuardVerdict.Ok
                : GuardVerdict.Deny("folder on drive root");
        }

        var (anchor, depth) = DeepestCritical(full);
        if (anchor is null)
        {
            return GuardVerdict.Ok;
        }

        if (depth == 0)
        {
            return !recursive && !target.IsBroad ? GuardVerdict.Ok : GuardVerdict.Deny("protected root folder");
        }

        // One level below Program Files, ProgramData, Users or a profile: never everything (would remove apps or data).
        if (depth == 1 && target.IsBroad && (_containerRoots.Contains(anchor) || _profileRoots.Contains(anchor)))
        {
            return GuardVerdict.Deny("application or profile folder");
        }

        return GuardVerdict.Ok;
    }

    /// <summary>Check for folders the user picked (workspace artifacts, uninstall leftovers).</summary>
    public GuardVerdict CheckUserTarget(string path)
    {
        var full = Norm(path);
        var basic = CheckBasics(full);
        if (!basic.Allowed)
        {
            return basic;
        }

        if (IsUnderOrEqual(full, _windows))
        {
            return GuardVerdict.Deny("Windows folder");
        }

        if (_criticalRoots.Any(r => r.Equals(full, StringComparison.OrdinalIgnoreCase)))
        {
            return GuardVerdict.Deny("protected root folder");
        }

        return InZone(full, _credentialZones) ? GuardVerdict.Deny("credentials folder") : GuardVerdict.Ok;
    }

    /// <summary>File-level check used by scanner and cleaner.</summary>
    /// <param name="checkZones">False when no protected zone lies inside the scanned folder (see <see cref="HasZonesInside"/>).</param>
    public bool IsProtectedFile(string fullPath, bool checkZones = true)
    {
        var name = Path.GetFileName(fullPath);
        if (ProtectedNames.Contains(name) || ProtectedExtensions.Contains(Path.GetExtension(name)))
        {
            return true;
        }

        return checkZones && (InZone(fullPath, _exclusions) || InZone(fullPath, _credentialZones) || InZone(fullPath, _denyZones));
    }

    /// <summary>True when an excluded or protected folder lies inside <paramref name="root"/> (files then need the zone check).</summary>
    public bool HasZonesInside(string root)
    {
        var full = Norm(root);
        return _exclusions.Concat(_credentialZones).Concat(_denyZones).Any(z =>
        {
            if (!z.Contains('*'))
            {
                return IsStrictAncestor(z, full);
            }

            // parent\OneDrive*: its matches lie inside root when the parent does.
            var parent = Path.GetDirectoryName(z);
            return parent is not null && IsUnderOrEqual(parent, full);
        });
    }

    public bool IsExcluded(string fullPath) => InZone(fullPath, _exclusions);

    private GuardVerdict CheckBasics(string full)
    {
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.Length < 3 || full[1] != ':')
        {
            return GuardVerdict.Deny("not a local path");
        }

        if (full.Length <= 3)
        {
            return GuardVerdict.Deny("drive root");
        }

        if (_criticalRoots.Any(r => IsStrictAncestor(r, full)))
        {
            return GuardVerdict.Deny("contains system or profile folders");
        }

        return InZone(full, _exclusions) ? GuardVerdict.Deny("excluded in settings") : GuardVerdict.Ok;
    }

    private bool IsCriticalRoot(string full) => _criticalRoots.Any(r => r.Equals(full, StringComparison.OrdinalIgnoreCase));

    private (string? Anchor, int Depth) DeepestCritical(string full)
    {
        string? best = null;
        foreach (var root in _criticalRoots)
        {
            if (IsUnderOrEqual(full, root) && (best is null || root.Length > best.Length))
            {
                best = root;
            }
        }

        if (best is null)
        {
            return (null, 0);
        }

        var rest = full.Length == best.Length ? "" : full[(best.Length + 1)..];
        return (best, rest.Length == 0 ? 0 : rest.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static bool OnlyRootFilePatterns(CleanTarget target) =>
        !target.IsBroad && target.Patterns.All(p => RootFileExtensions.Contains(Path.GetExtension(p)));

    private static bool InZone(string full, List<string> zones)
    {
        foreach (var zone in zones)
        {
            if (zone.Contains('*'))
            {
                var parent = Path.GetDirectoryName(zone);
                if (parent is not null && IsStrictAncestor(full, parent))
                {
                    var first = full[(parent.Length + 1)..].Split('\\')[0];
                    if (FileSystemName.MatchesSimpleExpression(Path.GetFileName(zone), first))
                    {
                        return true;
                    }
                }
            }
            else if (IsUnderOrEqual(full, zone))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Relative path starts with the allow-list entry (segments may contain wildcards).</summary>
    private static bool MatchesPrefix(string relative, string allowed)
    {
        var path = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var rule = allowed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length < rule.Length)
        {
            return false;
        }

        for (var i = 0; i < rule.Length; i++)
        {
            if (!FileSystemName.MatchesSimpleExpression(rule[i], path[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsUnderOrEqual(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || IsStrictAncestor(path, root);

    /// <summary>True when <paramref name="root"/> is a parent folder of <paramref name="path"/>.</summary>
    public static bool IsStrictAncestor(string path, string root) =>
        path.Length > root.Length + 1
        && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
        && path[root.Length] == '\\';

    private void AddCritical(List<string> list, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                var full = Norm(path);
                if (!list.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(full);
                }
            }
        }
    }

    private static string Norm(string path) => PathExpander.Normalize(path);
}
