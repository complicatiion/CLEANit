using CLEANit.Core.Models;

namespace CLEANit.Core.Storage;

/// <summary>Running totals of all cleanings (data\stats.json). Kept separately so deleting reports does not reset them.</summary>
public sealed record CleaningTotals
{
    public long Runs { get; init; }

    public long Bytes { get; init; }

    public long Files { get; init; }

    public DateTime? SinceUtc { get; init; }
}

public static class CleaningStats
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "stats.json");

    /// <summary>
    /// Current totals. On the first call after an update the file does not exist yet: the totals start from the
    /// reports that are still there, so nothing counted so far gets lost.
    /// </summary>
    public static CleaningTotals Load(IReadOnlyList<HistoryEntry> existingReports)
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath) && JsonFile.Read<CleaningTotals>(FilePath) is { } totals)
                {
                    return totals;
                }
            }
            catch (Exception)
            {
                // Unreadable: rebuild from the reports below.
            }

            var fresh = FromReports(existingReports);
            Write(fresh);
            return fresh;
        }
    }

    /// <summary>Adds one cleaning; call after its report was saved.</summary>
    public static void Record(HistoryEntry entry)
    {
        lock (Gate)
        {
            CleaningTotals? current = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    current = JsonFile.Read<CleaningTotals>(FilePath);
                }
            }
            catch (Exception)
            {
                current = null;
            }

            if (current is null)
            {
                // No (readable) totals yet: the saved report is already among the reports, so start from all of them.
                Write(FromReports(ReportStore.LoadHistory()));
                return;
            }

            Write(current with
            {
                Runs = current.Runs + 1,
                Bytes = current.Bytes + entry.Bytes,
                Files = current.Files + entry.Files,
                SinceUtc = current.SinceUtc ?? entry.TimeUtc
            });
        }
    }

    internal static CleaningTotals FromReports(IReadOnlyList<HistoryEntry> reports) => new()
    {
        Runs = reports.Count,
        Bytes = reports.Sum(r => r.Bytes),
        Files = reports.Sum(r => r.Files),
        SinceUtc = reports.Count == 0 ? null : reports.Min(r => r.TimeUtc)
    };

    private static void Write(CleaningTotals totals)
    {
        try
        {
            JsonFile.Write(FilePath, totals);
        }
        catch (Exception)
        {
            // Not critical: the next start rebuilds the totals from the reports.
        }
    }
}
