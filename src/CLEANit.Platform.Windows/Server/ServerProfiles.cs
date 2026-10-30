using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace CLEANit.Platform.Windows;

/// <summary>Kind of a local user profile as far as Windows and the profile solutions show it.</summary>
public enum ProfileKind
{
    Local,
    Roaming,
    Temporary,
    Mandatory,
    CitrixUpm,
    Orphaned
}

/// <summary>One local user profile (ProfileList) or a profile folder without a ProfileList entry.</summary>
public sealed record LocalProfileInfo(
    string Sid,
    string UserName,
    string Path,
    DateTime? LastUseUtc,
    bool IsLoaded,
    bool IsCurrent,
    ProfileKind Kind,
    bool AccountKnown)
{
    /// <summary>Only profiles that are not in use, not the own one and listed by Windows can be deleted (DeleteProfile).</summary>
    public bool CanDelete => !IsLoaded && !IsCurrent && Kind != ProfileKind.Orphaned && Sid.Length > 0;

    public int? AgeDays => LastUseUtc is { } t ? (int)Math.Max(0, (DateTime.UtcNow - t).TotalDays) : null;
}

/// <summary>
/// Local user profiles on servers and multi-user PCs: list with last use and state, delete through the documented
/// Windows API (DeleteProfile removes folder and ProfileList entry like "Delete user profiles older than …").
/// </summary>
public static class ServerProfiles
{
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    /// <summary>Profile folders whose registry hive is loaded (user signed in, or a service/app still holds it).</summary>
    public static HashSet<string> LoadedProfilePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var list = Registry.LocalMachine.OpenSubKey(ProfileListKey);
            foreach (var sid in LoadedSids())
            {
                using var key = list?.OpenSubKey(sid);
                if (key?.GetValue("ProfileImagePath") is string path && path.Length > 0)
                {
                    paths.Add(Environment.ExpandEnvironmentVariables(path).TrimEnd('\\'));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Without access nothing counts as loaded: callers then exclude nothing – the cleaner skips locked files anyway.
        }

        return paths;
    }

    public static IReadOnlyList<LocalProfileInfo> List()
    {
        var result = new List<LocalProfileInfo>();
        var loaded = LoadedSids();
        var currentSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var list = Registry.LocalMachine.OpenSubKey(ProfileListKey);
            foreach (var sid in list?.GetSubKeyNames() ?? [])
            {
                if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var key = list!.OpenSubKey(sid);
                if (key?.GetValue("ProfileImagePath") is not string raw || raw.Length == 0)
                {
                    continue;
                }

                var path = Environment.ExpandEnvironmentVariables(raw).TrimEnd('\\');
                listed.Add(path);
                var (name, known) = Account(sid, path);
                result.Add(new LocalProfileInfo(sid, name, path, LastUse(key, path), loaded.Contains(sid), sid == currentSid,
                    Kind(key, path), known));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Partial list.
        }

        // Folders below C:\Users without a profile entry (e.g. FSLogix "local_<user>" copies, removed accounts).
        try
        {
            var root = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (root is not null && Directory.Exists(root))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var name = System.IO.Path.GetFileName(dir);
                    if (listed.Contains(dir) || IsSystemFolder(name) ||
                        (File.GetAttributes(dir) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0)
                    {
                        continue;
                    }

                    result.Add(new LocalProfileInfo("", name, dir, Directory.GetLastWriteTimeUtc(dir), false, false, ProfileKind.Orphaned, false));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Without the orphan check.
        }

        return result.OrderBy(p => p.UserName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Deletes a profile through userenv!DeleteProfile (folder + ProfileList entry). Refuses profiles in use.</summary>
    public static (bool Ok, string? Error) Delete(LocalProfileInfo profile)
    {
        if (!profile.CanDelete)
        {
            return (false, "profile in use or not deletable");
        }

        if (LoadedSids().Contains(profile.Sid))
        {
            return (false, "profile was loaded in the meantime");
        }

        if (DeleteProfile(profile.Sid, null, null))
        {
            return (true, null);
        }

        return (false, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
    }

    internal static HashSet<string> LoadedSids()
    {
        try
        {
            return Registry.Users.GetSubKeyNames()
                .Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal) && !s.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Folders below C:\Users that belong to Windows itself: Public/Default, setup accounts and the virtual accounts of the
    /// font driver host (UMFD-n) and the desktop window manager (DWM-n), including their temporary "TEMP.…" copies.
    /// </summary>
    internal static bool IsSystemFolder(string name) =>
        name.Equals("Public", StringComparison.OrdinalIgnoreCase) || name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Default User", StringComparison.OrdinalIgnoreCase) || name.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("defaultuser", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WsiAccount", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("UMFD-", StringComparison.OrdinalIgnoreCase) || name.Contains("DWM-", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Font Driver Host", StringComparison.OrdinalIgnoreCase) || name.Contains("Window Manager", StringComparison.OrdinalIgnoreCase);

    /// <summary>Last load/unload time recorded by Windows (FILETIME in two DWORDs), else NTUSER.DAT.</summary>
    private static DateTime? LastUse(RegistryKey key, string path)
    {
        DateTime? FromParts(string high, string low)
        {
            if (key.GetValue(high) is int h && key.GetValue(low) is int l && (h != 0 || l != 0))
            {
                var ticks = ((long)(uint)h << 32) | (uint)l;
                try
                {
                    return DateTime.FromFileTimeUtc(ticks);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            return null;
        }

        var candidates = new[]
        {
            FromParts("LocalProfileUnloadTimeHigh", "LocalProfileUnloadTimeLow"),
            FromParts("LocalProfileLoadTimeHigh", "LocalProfileLoadTimeLow")
        }.Where(t => t is not null).ToList();
        if (candidates.Count > 0)
        {
            return candidates.Max();
        }

        var hive = System.IO.Path.Combine(path, "NTUSER.DAT");
        return File.Exists(hive) ? File.GetLastWriteTimeUtc(hive) : null;
    }

    private static ProfileKind Kind(RegistryKey key, string path)
    {
        var folder = System.IO.Path.GetFileName(path);
        if (folder.StartsWith("TEMP", StringComparison.OrdinalIgnoreCase))
        {
            return ProfileKind.Temporary;
        }

        if (path.EndsWith(".man", StringComparison.OrdinalIgnoreCase) || File.Exists(System.IO.Path.Combine(path, "NTUSER.MAN")))
        {
            return ProfileKind.Mandatory;
        }

        // Citrix Profile Management keeps its settings file in the local profile.
        if (File.Exists(System.IO.Path.Combine(path, "UPMSettings.ini")))
        {
            return ProfileKind.CitrixUpm;
        }

        return key.GetValue("CentralProfile") is string central && central.Length > 0 ? ProfileKind.Roaming : ProfileKind.Local;
    }

    private static (string Name, bool Known) Account(string sid, string path)
    {
        try
        {
            return (new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value, true);
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return (System.IO.Path.GetFileName(path), false);
        }
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteProfile(string sidString, string? profilePath, string? computerName);
}
