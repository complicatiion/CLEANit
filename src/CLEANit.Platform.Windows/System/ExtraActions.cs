using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Diagnostics;
using CLEANit.Core.Storage;

namespace CLEANit.Platform.Windows;

/// <summary>Firefox history through SQLite: visits, typed addresses and unbookmarked pages. Bookmarks and keywords stay.</summary>
public static class FirefoxHistory
{
    public static IEnumerable<string> Databases(IEnumerable<UserProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            var root = Path.Combine(profile.AppData, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var db = Path.Combine(dir, "places.sqlite");
                if (File.Exists(db))
                {
                    yield return db;
                }
            }
        }
    }

    /// <summary>Number of history visits (read only).</summary>
    public static long CountVisits(IEnumerable<UserProfile> profiles)
    {
        long count = 0;
        foreach (var file in Databases(profiles))
        {
            try
            {
                using var db = SqliteDb.Open(file, readOnly: true);
                count += db.Scalar("SELECT COUNT(*) FROM moz_historyvisits");
            }
            catch (Exception)
            {
                // Locked or other schema.
            }
        }

        return count;
    }

    /// <summary>Backs up each places.sqlite, then clears the history. Returns the bytes saved in the databases.</summary>
    public static long Clear(IEnumerable<UserProfile> profiles)
    {
        long freed = 0;
        var errors = new List<string>();
        var backupRoot = Path.Combine(AppPaths.BackupsDirectory, "firefox", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (var file in Databases(profiles))
        {
            try
            {
                var before = Size(file);
                var target = Path.Combine(backupRoot, Path.GetFileName(Path.GetDirectoryName(file)!));
                Directory.CreateDirectory(target);
                foreach (var part in new[] { file, file + "-wal", file + "-shm" }.Where(File.Exists))
                {
                    File.Copy(part, Path.Combine(target, Path.GetFileName(part)), overwrite: true);
                }

                ClearDatabase(file);
                freed += Math.Max(0, before - Size(file));
            }
            catch (Exception ex)
            {
                errors.Add($"{file}: {ex.Message}");
            }
        }

        return errors.Count == 0 ? freed : throw new InvalidOperationException(string.Join(" | ", errors));
    }

    internal static void ClearDatabase(string file)
    {
        using var db = SqliteDb.Open(file);
        const string unbookmarked =
            "id NOT IN (SELECT fk FROM moz_bookmarks WHERE fk IS NOT NULL)" +
            " AND id NOT IN (SELECT place_id FROM moz_keywords WHERE place_id IS NOT NULL)";

        db.Execute("BEGIN IMMEDIATE");
        try
        {
            db.Execute("DELETE FROM moz_historyvisits");
            foreach (var table in new[] { "moz_inputhistory", "moz_places_metadata", "moz_places_metadata_search_queries" })
            {
                if (db.TableExists(table))
                {
                    db.Execute($"DELETE FROM {table}");
                }
            }

            if (db.TableExists("moz_annos"))
            {
                db.Execute($"DELETE FROM moz_annos WHERE place_id IN (SELECT id FROM moz_places WHERE {unbookmarked})");
            }

            db.Execute($"DELETE FROM moz_places WHERE {unbookmarked}");
            db.Execute("UPDATE moz_places SET visit_count = 0, last_visit_date = NULL");
            if (db.TableExists("moz_origins"))
            {
                db.Execute("DELETE FROM moz_origins WHERE id NOT IN (SELECT origin_id FROM moz_places WHERE origin_id IS NOT NULL)");
            }

            db.Execute("COMMIT");
        }
        catch
        {
            db.Execute("ROLLBACK");
            throw;
        }

        db.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
        db.Execute("VACUUM");
    }

    private static long Size(string file) =>
        new[] { file, file + "-wal" }.Where(File.Exists).Sum(f => new FileInfo(f).Length);
}

/// <summary>Classic event logs: backup as .evtx, then clear (wevtutil).</summary>
public static partial class EventLogs
{
    public static readonly IReadOnlyList<string> Logs = ["Application", "System", "Setup", "Windows PowerShell"];

    [GeneratedRegex(@"^\s*fileSize:\s*(\d+)", RegexOptions.Multiline)]
    private static partial Regex FileSizeRegex();

    [GeneratedRegex(@"^\s*numberOfLogRecords:\s*(\d+)", RegexOptions.Multiline)]
    private static partial Regex RecordsRegex();

    public static (long Bytes, long Records) Estimate()
    {
        long bytes = 0, records = 0;
        foreach (var log in Logs)
        {
            var run = ProcessRunner.Run("wevtutil.exe", ["gli", log], TimeSpan.FromSeconds(15));
            if (FileSizeRegex().Match(run.Output) is { Success: true } size)
            {
                bytes += long.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            if (RecordsRegex().Match(run.Output) is { Success: true } count)
            {
                records += long.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return (bytes, records);
    }

    /// <summary>A log is only cleared when its backup was written.</summary>
    public static long BackupAndClear()
    {
        var (bytes, _) = Estimate();
        var folder = Path.Combine(AppPaths.BackupsDirectory, "eventlogs", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var errors = new List<string>();
        foreach (var log in Logs)
        {
            var backup = Path.Combine(folder, log.Replace(' ', '_') + ".evtx");
            var export = ProcessRunner.Run("wevtutil.exe", ["epl", log, backup], TimeSpan.FromMinutes(5));
            if (!export.Success || !File.Exists(backup))
            {
                errors.Add($"{log}: backup failed – not cleared");
                continue;
            }

            if (!ProcessRunner.Run("wevtutil.exe", ["cl", log], TimeSpan.FromMinutes(2)).Success)
            {
                errors.Add($"{log}: could not be cleared");
            }
        }

        return errors.Count == 0 ? bytes : throw new InvalidOperationException(string.Join(" | ", errors));
    }
}

/// <summary>Windows clipboard history (WinRT API through Windows PowerShell).</summary>
public static class ClipboardHistory
{
    private const string Type =
        "[Windows.ApplicationModel.DataTransfer.Clipboard,Windows.ApplicationModel.DataTransfer,ContentType=WindowsRuntime] | Out-Null; ";

    public static bool IsEnabled()
    {
        var run = ProcessRunner.Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            Type + "[Windows.ApplicationModel.DataTransfer.Clipboard]::IsHistoryEnabled()"], TimeSpan.FromSeconds(20));
        return run.Output.Contains("True", StringComparison.OrdinalIgnoreCase);
    }

    public static void Clear()
    {
        var run = ProcessRunner.Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-STA", "-Command",
            Type + "[Windows.ApplicationModel.DataTransfer.Clipboard]::ClearHistory()"], TimeSpan.FromSeconds(30));
        if (!run.Output.Contains("True", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The clipboard history could not be cleared.");
        }
    }
}

public sealed record ComponentStoreInfo(long Reclaimable, long ActualSize, string Recommended, string Output);

/// <summary>DISM /AnalyzeComponentStore: what a component cleanup would free (takes up to a few minutes).</summary>
public static partial class DismAnalyzer
{
    [GeneratedRegex(@"\s:\s*(.+)$")]
    private static partial Regex ValueRegex();

    [GeneratedRegex(@"^([\d.,]+)\s*(B|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    public static async Task<ComponentStoreInfo?> AnalyzeAsync(CancellationToken ct)
    {
        var dism = Path.Combine(Environment.SystemDirectory, "Dism.exe");
        var run = await ProcessRunner.RunAsync(dism, ["/Online", "/Cleanup-Image", "/AnalyzeComponentStore", "/English"],
            TimeSpan.FromMinutes(15), Encoding.UTF8, ct);
        return run.Success ? Parse(run.Output) : null;
    }

    /// <summary>Order of the result lines is fixed: reported size, actual size, shared, backups, cache, date, packages, recommended.</summary>
    internal static ComponentStoreInfo? Parse(string output)
    {
        var values = output.Split('\n').Select(l => ValueRegex().Match(l.TrimEnd('\r'))).Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).ToList();
        var sizes = values.Select(ParseSize).Where(s => s is not null).Select(s => s!.Value).ToList();
        if (sizes.Count < 5)
        {
            return null;
        }

        return new ComponentStoreInfo(sizes[3] + sizes[4], sizes[1], values.LastOrDefault() ?? "", output);
    }

    private static long? ParseSize(string value)
    {
        var match = SizeRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups[1].Value;
        // "7.83" / "7,83": the last separator is the decimal point.
        var last = Math.Max(number.LastIndexOf('.'), number.LastIndexOf(','));
        var normalized = last < 0 ? number : number[..last].Replace(".", "").Replace(",", "") + "." + number[(last + 1)..];
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return null;
        }

        var factor = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return (long)(amount * factor);
    }
}
