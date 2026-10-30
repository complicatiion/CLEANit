using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Diagnostics;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using Microsoft.Win32;

namespace CLEANit.Platform.Windows;

/// <summary>Factory for the engine's platform services.</summary>
public static class WindowsPlatform
{
    /// <param name="targetProfiles">Profiles the per-user actions work on (current user or all profiles).</param>
    public static PlatformServices Create(PathGuard? guard = null, Func<IReadOnlyList<UserProfile>>? targetProfiles = null)
    {
        var profiles = new WindowsProfiles();
        var recycleBin = new RecycleBin();
        return new PlatformServices
        {
            Profiles = profiles,
            Registry = new RegistryProbe(),
            Services = new ServiceControl(),
            RecycleBin = recycleBin,
            Actions = new ActionRunner(recycleBin, guard ?? new PathGuard(profiles.All()), targetProfiles ?? (() => [profiles.Current]))
        };
    }
}

/// <summary>Elevation and edition of this Windows.</summary>
public static class SystemInfo
{
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string CurrentUser()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
        catch (Exception)
        {
            return Environment.UserDomainName + "\\" + Environment.UserName;
        }
    }

    public static bool IsServer() =>
        string.Equals(ReadCurrentVersion("InstallationType"), "Server", StringComparison.OrdinalIgnoreCase);

    public static int Build() => int.TryParse(ReadCurrentVersion("CurrentBuild"), out var build) ? build : 0;

    public static string EditionId() => ReadCurrentVersion("EditionID") ?? "";

    /// <summary>Windows Server or Windows Enterprise multi-session (EditionID "ServerRdsh"): several users at once.</summary>
    public static bool IsMultiUser() => IsServer() || EditionId().Equals("ServerRdsh", StringComparison.OrdinalIgnoreCase);

    /// <summary>Enterprise, Education, IoT Enterprise or Server: diagnostic data can go down to "Security/Off".</summary>
    public static bool IsEnterpriseEdition()
    {
        var edition = ReadCurrentVersion("EditionID") ?? "";
        return IsServer() || edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
                          || edition.Contains("Education", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Member of an Active Directory domain (group policies may override local settings).</summary>
    public static bool IsDomainJoined()
    {
        try
        {
            if (NetGetJoinInformation(null, out var buffer, out var status) == 0)
            {
                NetApiBufferFree(buffer);
                return status == 3; // NetSetupDomainName
            }
        }
        catch (Exception)
        {
            // netapi32 not available.
        }

        return false;
    }

    [System.Runtime.InteropServices.DllImport("netapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr domain, out int status);

    [System.Runtime.InteropServices.DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    public static string ProductName()
    {
        var name = ReadCurrentVersion("ProductName") ?? "Windows";
        var build = ReadCurrentVersion("CurrentBuild");

        // Windows 11 still reports "Windows 10" in ProductName.
        if (int.TryParse(build, out var number) && number >= 22000 && !IsServer())
        {
            name = name.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        var display = ReadCurrentVersion("DisplayVersion");
        return string.IsNullOrEmpty(display) ? $"{name} (Build {build})" : $"{name} {display} (Build {build})";
    }

    private static string? ReadCurrentVersion(string value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue(value)?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>Local user profiles from the ProfileList (no system accounts, no default profile).</summary>
public sealed class WindowsProfiles : IProfileProvider
{
    public UserProfile Current { get; } = new(Environment.UserName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
        AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Temp = Path.GetTempPath().TrimEnd('\\')
    };

    public IReadOnlyList<UserProfile> All()
    {
        var list = new List<UserProfile> { Current };
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            foreach (var sid in key?.GetSubKeyNames() ?? [])
            {
                // Only real accounts (S-1-5-21-…), not SYSTEM, LocalService, NetworkService.
                if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal))
                {
                    continue;
                }

                using var profileKey = key!.OpenSubKey(sid);
                var path = profileKey?.GetValue("ProfileImagePath") as string;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                path = Environment.ExpandEnvironmentVariables(path);
                if (Directory.Exists(path) && !list.Any(p => p.ProfilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(new UserProfile(Path.GetFileName(path.TrimEnd('\\')), path));
                }
            }
        }
        catch (Exception)
        {
            // Only the current profile.
        }

        return list;
    }
}

/// <summary>Read-only registry probe for rule detection.</summary>
public sealed class RegistryProbe : IRegistryProbe
{
    public bool KeyExists(string key)
    {
        var sep = key.IndexOf('\\');
        var hiveName = sep < 0 ? key : key[..sep];
        var path = sep < 0 ? "" : key[(sep + 1)..];
        var hive = hiveName.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            _ => (RegistryHive?)null
        };

        if (hive is null)
        {
            return false;
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive.Value, view);
                using var sub = root.OpenSubKey(path);
                if (sub is not null)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // No access.
            }
        }

        return false;
    }
}

/// <summary>Stops/starts services via sc.exe and waits for the state.</summary>
public sealed partial class ServiceControl : IServiceControl
{
    [GeneratedRegex(@"\b([1-7])\s+[A-Z_]+\b")]
    private static partial Regex StateRegex();

    public bool StopIfRunning(string name, TimeSpan timeout)
    {
        if (Query(name) != 4)
        {
            return false;
        }

        ProcessRunner.Run("sc.exe", ["stop", name], TimeSpan.FromSeconds(15));
        return WaitFor(name, 1, timeout);
    }

    public void Start(string name, TimeSpan timeout)
    {
        if (Query(name) == 4)
        {
            return;
        }

        ProcessRunner.Run("sc.exe", ["start", name], TimeSpan.FromSeconds(15));
        WaitFor(name, 4, timeout);
    }

    /// <summary>1 = stopped, 4 = running, 0 = unknown.</summary>
    private static int Query(string name)
    {
        var run = ProcessRunner.Run("sc.exe", ["query", name], TimeSpan.FromSeconds(8));
        var match = StateRegex().Match(run.Output);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static bool WaitFor(string name, int state, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (Query(name) == state)
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }
}

/// <summary>Recycle bin through the shell (SHFileOperation, SHQueryRecycleBin, SHEmptyRecycleBin).</summary>
public sealed class RecycleBin : IRecycleBin
{
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const uint SherbNoConfirmation = 0x1;
    private const uint SherbNoProgressUi = 0x2;
    private const uint SherbNoSound = 0x4;

    public int Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return 0;
        }

        return RunSta(() =>
        {
            var op = new ShFileOpStruct
            {
                wFunc = FoDelete,
                pFrom = string.Join('\0', paths) + "\0\0",
                fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi)
            };
            SHFileOperation(ref op);
            return paths.Count(p => File.Exists(p) || Directory.Exists(p));
        });
    }

    public (long Bytes, long Items) Query()
    {
        var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public bool Empty() =>
        RunSta(() => SHEmptyRecycleBin(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound) is 0 or unchecked((int)0x8000FFFF));

    private static T RunSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    // x64 only: natural alignment (the header packs to 1 byte only on 32-bit Windows).
    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}

/// <summary>Cleanup through official Windows tools only.</summary>
public sealed class ActionRunner(IRecycleBin recycleBin, PathGuard guard, Func<IReadOnlyList<UserProfile>> targetProfiles) : IActionRunner
{
    private static string WindowsOld => Path.Combine(KnownFolders.SystemDrive, "Windows.old");

    public ActionEstimate Estimate(CleanAction action)
    {
        switch (action)
        {
            case CleanAction.EmptyRecycleBin:
                var (bytes, items) = recycleBin.Query();
                return new ActionEstimate(bytes, items, false, items > 0);
            case CleanAction.DismComponentCleanup:
            case CleanAction.DismResetBase:
                return new ActionEstimate(0, 0, true, File.Exists(Dism));
            case CleanAction.RemoveWindowsOld:
                if (!Directory.Exists(WindowsOld))
                {
                    return new ActionEstimate(0, 0, false, false);
                }

                var (size, files, _) = SafeDelete.Measure(WindowsOld);
                return new ActionEstimate(size, files, size == 0, true);
            case CleanAction.FirefoxHistory:
                var visits = FirefoxHistory.CountVisits(targetProfiles());
                return new ActionEstimate(0, visits, true, visits > 0);
            case CleanAction.ClearEventLogs:
                var (logBytes, records) = EventLogs.Estimate();
                return new ActionEstimate(logBytes, records, false, records > 0);
            case CleanAction.ClearClipboardHistory:
                return new ActionEstimate(0, 0, true, ClipboardHistory.IsEnabled());
            default:
                return new ActionEstimate(0, 0, false, false);
        }
    }

    public long Run(CleanAction action, CancellationToken ct)
    {
        switch (action)
        {
            case CleanAction.EmptyRecycleBin:
                var (bytes, _) = recycleBin.Query();
                return recycleBin.Empty() ? bytes : throw new InvalidOperationException("The recycle bin could not be emptied.");
            case CleanAction.DismComponentCleanup:
                RunDism(["/Online", "/Cleanup-Image", "/StartComponentCleanup"]);
                return 0;
            case CleanAction.DismResetBase:
                RunDism(["/Online", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase"]);
                return 0;
            case CleanAction.RemoveWindowsOld:
                return RemoveWindowsOld();
            case CleanAction.FirefoxHistory:
                return FirefoxHistory.Clear(targetProfiles());
            case CleanAction.ClearEventLogs:
                return EventLogs.BackupAndClear();
            case CleanAction.ClearClipboardHistory:
                ClipboardHistory.Clear();
                return 0;
            default:
                return 0;
        }
    }

    private static string Dism => Path.Combine(Environment.SystemDirectory, "Dism.exe");

    private static void RunDism(string[] args)
    {
        var run = ProcessRunner.Run(Dism, args, TimeSpan.FromHours(2));
        if (!run.Success)
        {
            throw new InvalidOperationException($"DISM exit code {run.ExitCode}: {LastLines(run.Output)}");
        }
    }

    /// <summary>Administrators take ownership (icacls, no prompts), then the folder is removed without following links.</summary>
    private long RemoveWindowsOld()
    {
        if (!Directory.Exists(WindowsOld))
        {
            return 0;
        }

        var (size, _, _) = SafeDelete.Measure(WindowsOld);
        ProcessRunner.Run("icacls.exe", [WindowsOld, "/setowner", "*S-1-5-32-544", "/T", "/C", "/L", "/Q"], TimeSpan.FromMinutes(30));
        ProcessRunner.Run("icacls.exe", [WindowsOld, "/grant", "*S-1-5-32-544:F", "/T", "/C", "/L", "/Q"], TimeSpan.FromMinutes(30));
        var result = SafeDelete.DeleteFolder(WindowsOld, size, guard, null, useRecycleBin: false);
        var (left, _, _) = Directory.Exists(WindowsOld) ? SafeDelete.Measure(WindowsOld) : (0, 0, DateTime.MinValue);
        if (!result.Deleted && left == size)
        {
            throw new InvalidOperationException(result.Error ?? "Windows.old could not be removed.");
        }

        return Math.Max(0, size - left);
    }

    private static string LastLines(string output) =>
        string.Join(" ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3));
}
