using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CLEANit.Core.Diagnostics;
using Microsoft.Win32;

namespace CLEANit.Platform.Windows;

public enum EnvState
{
    Info,
    Ok,
    Warning,
    Off
}

/// <summary>
/// One line of the environment overview. <see cref="LabelKey"/> and <see cref="ValueKey"/> are UI text keys;
/// <see cref="Value"/> is shown as it is (versions, paths, registry values).
/// </summary>
public sealed record EnvItem(string Group, string LabelKey, string? Value, string? ValueKey = null, EnvState State = EnvState.Info);

/// <summary>A user session on this machine (WTSEnumerateSessions).</summary>
public sealed record UserSession(int Id, string UserName, bool Active);

/// <summary>
/// Read-only overview of a server / VDI machine: Windows, Remote Desktop Session Host, sessions, FSLogix, Citrix
/// (VDA, Profile Management, provisioning). Detection only reads registry, files and services – nothing is changed.
/// </summary>
public static class ServerEnvironment
{
    public static async Task<IReadOnlyList<EnvItem>> ReadAsync(CancellationToken ct)
    {
        var items = new List<EnvItem>();
        var server = SystemInfo.IsServer();
        items.Add(new EnvItem("system", "EnvWindows", SystemInfo.ProductName()));
        items.Add(new EnvItem("system", "EnvMultiUser", null, SystemInfo.IsMultiUser() ? "EnvYes" : "EnvNo",
            SystemInfo.IsMultiUser() ? EnvState.Ok : EnvState.Off));
        items.Add(new EnvItem("system", "EnvDomain", null, SystemInfo.IsDomainJoined() ? "EnvYes" : "EnvNo"));

        // Remote Desktop Session Host
        if (server)
        {
            var installed = await IsRdsHostAsync(ct);
            items.Add(new EnvItem("rds", "EnvRdsHost", null, installed switch { true => "EnvInstalled", false => "EnvNotInstalled", _ => "EnvUnknown" },
                installed == true ? EnvState.Ok : EnvState.Off));
        }
        else
        {
            items.Add(new EnvItem("rds", "EnvRdsHost", null, SystemInfo.IsMultiUser() ? "EnvMultiSession" : "EnvClientOs",
                SystemInfo.IsMultiUser() ? EnvState.Ok : EnvState.Off));
        }

        var sessions = Sessions();
        items.Add(new EnvItem("rds", "EnvSessionsActive", sessions.Count(s => s.Active).ToString()));
        items.Add(new EnvItem("rds", "EnvSessionsDisconnected", sessions.Count(s => !s.Active).ToString(),
            State: sessions.Any(s => !s.Active) ? EnvState.Warning : EnvState.Info));
        if (sessions.Count > 0)
        {
            items.Add(new EnvItem("rds", "EnvSessionUsers", string.Join(", ", sessions.Select(s => s.Active ? s.UserName : s.UserName + " (–)").Distinct())));
        }

        // FSLogix
        var frx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FSLogix", "Apps", "frx.exe");
        if (File.Exists(frx) || ServiceExists("frxsvc"))
        {
            items.Add(new EnvItem("fslogix", "EnvFslogixVersion", FileVersion(frx) ?? "?", State: EnvState.Ok));
            var values = MergedValues(@"SOFTWARE\FSLogix\Profiles", @"SOFTWARE\Policies\FSLogix\Profiles");
            var enabled = values.TryGetValue("Enabled", out var e) && e == "1";
            items.Add(new EnvItem("fslogix", "EnvFslogixProfiles", null, enabled ? "EnvOn" : "EnvOff", enabled ? EnvState.Ok : EnvState.Off));
            foreach (var (name, value) in values.Where(v => v.Key != "Enabled").OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(new EnvItem("fslogix", "", name + " = " + value));
            }
        }
        else
        {
            items.Add(new EnvItem("fslogix", "EnvFslogixVersion", null, "EnvNotInstalled", EnvState.Off));
        }

        // Citrix
        var vda = UninstallVersion("Citrix Virtual Delivery Agent") ?? UninstallVersion("Citrix Virtual Apps and Desktops");
        if (vda is not null || KeyExists(@"SOFTWARE\Citrix\VirtualDesktopAgent"))
        {
            items.Add(new EnvItem("citrix", "EnvCitrixVda", vda ?? "?", State: EnvState.Ok));
        }
        else
        {
            items.Add(new EnvItem("citrix", "EnvCitrixVda", null, "EnvNotInstalled", EnvState.Off));
        }

        var upm = ServiceExists("ctxProfile");
        items.Add(new EnvItem("citrix", "EnvCitrixUpm", null, upm ? "EnvInstalled" : "EnvNotInstalled", upm ? EnvState.Ok : EnvState.Off));
        if (upm)
        {
            foreach (var (name, value) in MergedValues(@"SOFTWARE\Policies\Citrix\UserProfileManager").OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Take(40))
            {
                items.Add(new EnvItem("citrix", "", name + " = " + value));
            }
        }

        var personality = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Personality.ini");
        if (File.Exists(personality))
        {
            items.Add(new EnvItem("citrix", "EnvProvisioned", null, ServiceExists("BNIStack") ? "EnvPvs" : "EnvMcs", EnvState.Ok));
        }

        if (server || SystemInfo.IsMultiUser())
        {
            var profiles = ServerProfiles.List();
            items.Add(new EnvItem("profiles", "EnvProfilesTotal", profiles.Count(p => p.Kind != ProfileKind.Orphaned).ToString()));
            var orphans = profiles.Count(p => p.Kind == ProfileKind.Orphaned);
            items.Add(new EnvItem("profiles", "EnvProfilesOrphaned", orphans.ToString(), State: orphans > 0 ? EnvState.Warning : EnvState.Info));
            var temp = profiles.Count(p => p.Kind == ProfileKind.Temporary);
            if (temp > 0)
            {
                items.Add(new EnvItem("profiles", "EnvProfilesTemporary", temp.ToString(), State: EnvState.Warning));
            }
        }

        return items;
    }

    /// <summary>User sessions (console and remote); services' session 0 and listeners are left out.</summary>
    public static IReadOnlyList<UserSession> Sessions()
    {
        var list = new List<UserSession>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var buffer, out var count))
        {
            return list;
        }

        try
        {
            var size = Marshal.SizeOf<WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(buffer + i * size);
                if (info.SessionId == 0 || info.State is not (0 or 4)) // WTSActive, WTSDisconnected
                {
                    continue;
                }

                var user = QueryString(info.SessionId, 5); // WTSUserName
                if (!string.IsNullOrEmpty(user))
                {
                    list.Add(new UserSession(info.SessionId, user, info.State == 0));
                }
            }
        }
        finally
        {
            WTSFreeMemory(buffer);
        }

        return list;
    }

    private static async Task<bool?> IsRdsHostAsync(CancellationToken ct)
    {
        try
        {
            var run = await ProcessRunner.RunAsync("powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", "(Get-WindowsFeature -Name RDS-RD-Server).Installed"],
                TimeSpan.FromSeconds(60), Encoding.UTF8, ct);
            var text = run.Output.Trim();
            return text.Equals("True", StringComparison.OrdinalIgnoreCase) ? true : text.Equals("False", StringComparison.OrdinalIgnoreCase) ? false : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string? QueryString(int session, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out var buffer, out _))
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <summary>Values of the first key, overridden by the following ones (policies win).</summary>
    private static Dictionary<string, string> MergedValues(params string[] keys)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in keys)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                foreach (var name in key?.GetValueNames() ?? [])
                {
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    values[name] = key!.GetValue(name) switch
                    {
                        string[] multi => string.Join("; ", multi),
                        byte[] bytes => $"({bytes.Length} bytes)",
                        { } v => v.ToString() ?? "",
                        _ => ""
                    };
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Skip unreadable key.
            }
        }

        return values;
    }

    private static bool ServiceExists(string name) => KeyExists($@"SYSTEM\CurrentControlSet\Services\{name}");

    private static bool KeyExists(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static string? FileVersion(string path)
    {
        try
        {
            return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).ProductVersion : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? UninstallVersion(string displayNamePrefix)
    {
        foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                foreach (var name in key?.GetSubKeyNames() ?? [])
                {
                    using var entry = key!.OpenSubKey(name);
                    if (entry?.GetValue("DisplayName") is string display && display.StartsWith(displayNamePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.GetValue("DisplayVersion") as string ?? display;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // Next view.
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSEnumerateSessionsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessionInfo, out int count);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
