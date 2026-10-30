namespace CLEANit.Core.Models;

public sealed record FileSample(string Path, long Bytes, DateTime LastWriteUtc);

/// <summary>Analysis result of one rule (preview before cleaning).</summary>
public sealed class RuleScanResult
{
    public required CleanRule Rule { get; init; }

    public long Bytes { get; init; }

    public long Files { get; init; }

    /// <summary>Files changed after this point are never deleted (TOCTOU protection).</summary>
    public DateTime ScannedUtc { get; init; }

    /// <summary>Largest files (capped) for the preview.</summary>
    public IReadOnlyList<FileSample> Samples { get; init; } = [];

    /// <summary>Existing, allowed target folders.</summary>
    public IReadOnlyList<string> Roots { get; init; } = [];

    /// <summary>Folders refused by the safety guard.</summary>
    public IReadOnlyList<string> Blocked { get; init; } = [];

    /// <summary>Running processes that block this rule.</summary>
    public IReadOnlyList<string> RunningProcesses { get; init; } = [];

    /// <summary>Size cannot be determined in advance (DISM).</summary>
    public bool SizeUnknown { get; init; }

    public bool HasContent => Files > 0 || Bytes > 0 || SizeUnknown;
}

public sealed class RuleCleanResult
{
    public required string RuleId { get; init; }

    public required string RuleName { get; init; }

    public long DeletedFiles { get; set; }

    public long DeletedBytes { get; set; }

    public long SkippedInUse { get; set; }

    public long SkippedDenied { get; set; }

    public long SkippedChanged { get; set; }

    public bool SkippedRunning { get; set; }

    public List<string> Errors { get; init; } = [];

    public void AddError(string message)
    {
        if (Errors.Count < 20)
        {
            Errors.Add(message);
        }
    }
}

/// <summary>Result of one cleaning run (saved as JSON in the logs folder).</summary>
public sealed class CleanReport
{
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;

    public DateTime FinishedUtc { get; set; }

    public string Area { get; init; } = "";

    public bool Cancelled { get; set; }

    public List<RuleCleanResult> Rules { get; init; } = [];

    /// <summary>Selection profile or mode (e.g. "imaging", "cli").</summary>
    public string? Profile { get; set; }

    public long DeletedFiles => Rules.Sum(r => r.DeletedFiles);

    public long DeletedBytes => Rules.Sum(r => r.DeletedBytes);

    public long Skipped => Rules.Sum(r => r.SkippedInUse + r.SkippedDenied + r.SkippedChanged);
}

/// <summary>Short entry for the overview and the report list.</summary>
public sealed record HistoryEntry(DateTime TimeUtc, string Area, long Bytes, long Files, string? ReportFile, long Skipped = 0, bool Cancelled = false);

/// <summary>Index of the cleaning reports (SQLite in the app; folder scan as fallback).</summary>
public interface IReportIndex
{
    void Add(HistoryEntry entry);

    IReadOnlyList<HistoryEntry> List(int max);

    void Remove(string reportFile);

    /// <summary>Adds reports found in the folder that are not indexed yet; drops entries whose file is gone.</summary>
    void Sync(IEnumerable<HistoryEntry> onDisk);
}
