using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CLEANit.Core.Diagnostics;
using CLEANit.Core.Storage;

namespace CLEANit.Platform.Windows;

/// <summary>Automatically starting service (third-party by default).</summary>
public sealed record ServiceEntry(string Name, string DisplayName, string State, string StartMode, bool Delayed, string? Path, string? Publisher,
    bool Signed, bool IsMicrosoft, bool AcceptStop = true);

public sealed record ServiceBackup(string Name, string DisplayName, string StartMode, bool Delayed, DateTime ChangedUtc);

/// <summary>Automatic services: switch to "Manual" with backup, restore the original start type.</summary>
public static class ServicesManager
{
    public static string BackupDirectory => System.IO.Path.Combine(AppPaths.BackupsDirectory, "services");

    public static async Task<IReadOnlyList<ServiceEntry>> ReadAutomaticAsync(CancellationToken ct)
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "Get-CimInstance Win32_Service -Filter \"StartMode='Auto'\" | ForEach-Object { [pscustomobject]@{ Name=$_.Name; DisplayName=$_.DisplayName; " +
            "State=[string]$_.State; StartMode=[string]$_.StartMode; Delayed=[bool]$_.DelayedAutoStart; PathName=$_.PathName; AcceptStop=[bool]$_.AcceptStop } } | ConvertTo-Json -Compress";

        var run = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
            TimeSpan.FromMinutes(1), Encoding.UTF8, ct);
        var json = run.Output.Trim();
        if (json.Length == 0 || (json[0] != '[' && json[0] != '{'))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json[0] == '[' ? json : "[" + json + "]");
        var list = new List<ServiceEntry>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var name = Str(e, "Name");
            if (name is null)
            {
                continue;
            }

            var path = Str(e, "PathName") is { } command ? StartupManager.ExecutableFromCommand(command) : null;
            var (signed, signer) = path is not null && File.Exists(path) ? Authenticode.GetSignature(path) : (false, null);
            var publisher = signer;
            if (publisher is null && path is not null && File.Exists(path))
            {
                try
                {
                    publisher = FileVersionInfo.GetVersionInfo(path).CompanyName;
                }
                catch (Exception)
                {
                    // No version info.
                }
            }

            var isMicrosoft = publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true
                              || (path?.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase) == true && publisher is null);
            list.Add(new ServiceEntry(name, Str(e, "DisplayName") ?? name, Str(e, "State") ?? "", Str(e, "StartMode") ?? "Auto",
                e.TryGetProperty("Delayed", out var d) && d.ValueKind == JsonValueKind.True, path, publisher, signed, isMicrosoft,
                !e.TryGetProperty("AcceptStop", out var stop) || stop.ValueKind != JsonValueKind.False));
        }

        return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Services CLEANit never stops: Windows core (RPC, DCOM, event log, Plug and Play, security, networking basics) and
    /// Remote Desktop – stopping them can freeze Windows or cut off a remote session.
    /// </summary>
    public static readonly IReadOnlySet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "EventLog", "EventSystem", "PlugPlay", "LSM", "SamSs", "Power", "BrokerInfrastructure",
        "CoreMessagingRegistrar", "SystemEventsBroker", "gpsvc", "Schedule", "Winmgmt", "WinDefend", "mpssvc", "BFE", "ProfSvc",
        "UserManager", "TermService", "UmRdpService", "SessionEnv", "Dhcp", "Dnscache", "NlaSvc", "nsi", "LanmanWorkstation", "Netlogon",
        "CryptSvc", "KeyIso", "VaultSvc", "WdNisSvc", "SecurityHealthService", "Themes", "AudioEndpointBuilder", "Audiosrv", "TrustedInstaller"
    };

    public static bool IsProtected(string name) => Protected.Contains(name);

    /// <summary>Running services that depend on this one (they are stopped too).</summary>
    public static async Task<IReadOnlyList<string>> RunningDependentsAsync(string name, CancellationToken ct)
    {
        var run = await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command",
             $"(Get-Service -Name {Quote(name)}).DependentServices | Where-Object Status -eq 'Running' | ForEach-Object {{ $_.DisplayName }}"],
            TimeSpan.FromSeconds(30), Encoding.UTF8, ct);
        return run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Stops a running service (and its running dependents). Protected services are refused.</summary>
    public static async Task<(bool Ok, string? Error)> StopAsync(string name, CancellationToken ct)
    {
        if (IsProtected(name))
        {
            return (false, "protected Windows service");
        }

        var run = await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference='Stop'; Stop-Service -Name {Quote(name)} -Force"],
            TimeSpan.FromMinutes(2), Encoding.UTF8, ct);
        return run.Success ? (true, null) : (false, FirstLine(run.Output));
    }

    public static async Task<(bool Ok, string? Error)> StartAsync(string name, CancellationToken ct)
    {
        var run = await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference='Stop'; Start-Service -Name {Quote(name)}"],
            TimeSpan.FromMinutes(2), Encoding.UTF8, ct);
        return run.Success ? (true, null) : (false, FirstLine(run.Output));
    }

    /// <summary>Single-quoted PowerShell literal (service names never contain quotes, but stay safe).</summary>
    internal static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static string? FirstLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    /// <summary>Writes a backup, then sets the start type to "Manual" (demand). The running service is not stopped.</summary>
    public static bool SetManual(ServiceEntry service)
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            JsonFile.Write(System.IO.Path.Combine(BackupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{service.Name}.json"),
                new ServiceBackup(service.Name, service.DisplayName, service.StartMode, service.Delayed, DateTime.UtcNow));
            return ProcessRunner.Run("sc.exe", ["config", service.Name, "start=", "demand"], TimeSpan.FromSeconds(20)).Success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static IReadOnlyList<(string File, ServiceBackup Backup)> ReadBackups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        var list = new List<(string, ServiceBackup)>();
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json").OrderDescending())
        {
            try
            {
                if (JsonFile.Read<ServiceBackup>(file) is { } backup)
                {
                    list.Add((file, backup));
                }
            }
            catch (Exception)
            {
                // Broken backup.
            }
        }

        return list;
    }

    public static bool Restore(string file, ServiceBackup backup)
    {
        var start = backup.Delayed ? "delayed-auto" : "auto";
        if (!ProcessRunner.Run("sc.exe", ["config", backup.Name, "start=", start], TimeSpan.FromSeconds(20)).Success)
        {
            return false;
        }

        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }

        return true;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
