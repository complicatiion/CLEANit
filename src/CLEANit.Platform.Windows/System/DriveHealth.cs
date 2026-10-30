using System.Text;
using System.Text.Json;
using CLEANit.Core.Diagnostics;

namespace CLEANit.Platform.Windows;

/// <summary>
/// Physical drive state (read only). Native storage IOCTLs deliver model, serial, firmware, temperature, NVMe health and
/// SATA SMART data; Windows' own view (Get-PhysicalDisk, Get-StorageReliabilityCounter) adds the health status and
/// counters for drives that block the native queries (USB bridges, RAID controllers).
/// </summary>
public static class DriveHealth
{
    /// <summary>Full read: native queries plus the Windows storage cmdlets (slower, used on page open and Refresh).</summary>
    public static async Task<IReadOnlyList<DriveHealthInfo>> ReadAsync(CancellationToken ct)
    {
        var native = await Task.Run(() => NativeStorage.ReadAll(ct), ct);
        var windows = await ReadWindowsAsync(ct);
        return Merge(native, windows).Select(DriveAssessor.Assess).ToList();
    }

    /// <summary>
    /// Quick read for auto-refresh: native queries only (no PowerShell); values only Windows knows are taken over from
    /// <paramref name="previous"/>.
    /// </summary>
    public static async Task<IReadOnlyList<DriveHealthInfo>> RefreshAsync(IReadOnlyList<DriveHealthInfo> previous, CancellationToken ct)
    {
        var native = await Task.Run(() => NativeStorage.ReadAll(ct), ct);
        var windows = previous.ToDictionary(p => p.Number, p => new WindowsDisk(p.Number, p.Name, p.MediaType, p.BusType, p.WindowsHealth,
            p.OperationalStatus, p.Size, null, p.TemperatureMax, p.Wear, p.PowerOnHours, p.ReadErrors, p.WriteErrors,
            p.ReadLatencyMaxMs, p.WriteLatencyMaxMs, p.StartStopCycles, p.LoadUnloadCycles, p.Rpm));
        return Merge(native, windows).Select(DriveAssessor.Assess).ToList();
    }

    private static IEnumerable<DriveHealthInfo> Merge(IReadOnlyList<DriveHealthInfo> native, IReadOnlyDictionary<int, WindowsDisk> windows)
    {
        var numbers = native.Select(n => n.Number).Union(windows.Keys).Order();
        foreach (var number in numbers)
        {
            var info = native.FirstOrDefault(n => n.Number == number) ?? new DriveHealthInfo { Number = number };
            if (!windows.TryGetValue(number, out var w))
            {
                yield return info;
                continue;
            }

            yield return info with
            {
                Name = info.Name.Length == 0 || info.Name.StartsWith("PhysicalDrive", StringComparison.Ordinal) ? w.Name : info.Name,
                MediaType = info.MediaType.Length > 0 ? info.MediaType : w.MediaType is "SSD" or "HDD" ? w.MediaType : "",
                BusType = info.BusType.Length > 0 ? info.BusType : w.BusType,
                Size = info.Size > 0 ? info.Size : w.Size,
                WindowsHealth = w.Health,
                OperationalStatus = w.Status,
                Temperature = info.Temperature ?? w.Temperature,
                TemperatureMax = w.TemperatureMax,
                Wear = info.Wear ?? w.Wear,
                PowerOnHours = info.PowerOnHours ?? w.PowerOnHours,
                ReadErrors = w.ReadErrors,
                WriteErrors = w.WriteErrors,
                ReadLatencyMaxMs = w.ReadLatencyMaxMs,
                WriteLatencyMaxMs = w.WriteLatencyMaxMs,
                StartStopCycles = info.StartStopCycles ?? w.StartStopCycles,
                LoadUnloadCycles = info.LoadUnloadCycles ?? w.LoadUnloadCycles,
                Rpm = w.Rpm is > 0 and < 30000 ? w.Rpm : null,
                Source = info.Source.Length > 0 ? info.Source : "windows"
            };
        }
    }

    internal sealed record WindowsDisk(
        int Number,
        string Name,
        string MediaType,
        string BusType,
        string Health,
        string Status,
        long Size,
        int? Temperature,
        int? TemperatureMax,
        int? Wear,
        long? PowerOnHours,
        long? ReadErrors,
        long? WriteErrors,
        long? ReadLatencyMaxMs,
        long? WriteLatencyMaxMs,
        long? StartStopCycles,
        long? LoadUnloadCycles,
        int? Rpm);

    private static async Task<IReadOnlyDictionary<int, WindowsDisk>> ReadWindowsAsync(CancellationToken ct)
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "Get-PhysicalDisk | ForEach-Object { $r = $_ | Get-StorageReliabilityCounter; [pscustomobject]@{ " +
            "Number=[int]$_.DeviceId; Name=$_.FriendlyName; MediaType=[string]$_.MediaType; BusType=[string]$_.BusType; " +
            "Health=[string]$_.HealthStatus; Status=[string]($_.OperationalStatus -join ', '); Size=[int64]$_.Size; " +
            "Rpm=$_.SpindleSpeed; Temperature=$r.Temperature; TemperatureMax=$r.TemperatureMax; Wear=$r.Wear; " +
            "PowerOnHours=$r.PowerOnHours; ReadErrors=$r.ReadErrorsTotal; WriteErrors=$r.WriteErrorsTotal; " +
            "ReadLatencyMax=$r.ReadLatencyMax; WriteLatencyMax=$r.WriteLatencyMax; StartStop=$r.StartStopCycleCount; " +
            "LoadUnload=$r.LoadUnloadCycleCount } } | ConvertTo-Json -Compress";

        try
        {
            var run = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
                TimeSpan.FromMinutes(1), Encoding.UTF8, ct);
            return Parse(run.Output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Dictionary<int, WindowsDisk>();
        }
    }

    internal static IReadOnlyDictionary<int, WindowsDisk> Parse(string output)
    {
        var json = output.Trim();
        var result = new Dictionary<int, WindowsDisk>();
        if (json.Length == 0 || (json[0] != '[' && json[0] != '{'))
        {
            return result;
        }

        using var doc = JsonDocument.Parse(json[0] == '[' ? json : "[" + json + "]");
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var number = (int)(Long(e, "Number") ?? -1);
            if (number < 0)
            {
                continue;
            }

            result[number] = new WindowsDisk(number, Str(e, "Name") ?? "Disk", Str(e, "MediaType") ?? "", Str(e, "BusType") ?? "",
                Str(e, "Health") ?? "", Str(e, "Status") ?? "", Long(e, "Size") ?? 0, (int?)Long(e, "Temperature"),
                (int?)Long(e, "TemperatureMax"), (int?)Long(e, "Wear"), Long(e, "PowerOnHours"), Long(e, "ReadErrors"),
                Long(e, "WriteErrors"), Long(e, "ReadLatencyMax"), Long(e, "WriteLatencyMax"), Long(e, "StartStop"),
                Long(e, "LoadUnload"), (int?)Long(e, "Rpm"));
        }

        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
}
