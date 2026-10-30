using System.Text;
using CLEANit.Core.Models;

namespace CLEANit.Core.Storage;

/// <summary>Cleaning reports (logs\clean-*.json, last 500) with an index for fast listing.</summary>
public static class ReportStore
{
    private const int KeepReports = 500;

    /// <summary>Index set by the app (SQLite); without it the folder is scanned.</summary>
    public static IReportIndex? Index { get; set; }

    public static string? Save(CleanReport report)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            var file = Path.Combine(AppPaths.LogsDirectory, $"clean-{report.StartedUtc:yyyyMMdd-HHmmss}-{report.Area.ToLowerInvariant()}.json");
            JsonFile.Write(file, report);
            var entry = Summarize(report, file);
            Index?.Add(entry);
            Rotate();
            CleaningStats.Record(entry);
            return file;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static IReadOnlyList<HistoryEntry> LoadHistory(int max = 500)
    {
        try
        {
            if (Index is not null)
            {
                return Index.List(max);
            }

            return ScanFolder().OrderByDescending(e => e.TimeUtc).Take(max).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Brings the index in line with the report files (first start, copied or deleted reports).</summary>
    public static void SyncIndex()
    {
        try
        {
            Index?.Sync(ScanFolder());
        }
        catch (Exception)
        {
            // Index stays as it is.
        }
    }

    public static CleanReport? Load(string file)
    {
        try
        {
            return JsonFile.Read<CleanReport>(file);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Deletes all cleaning reports; the totals (<see cref="CleaningStats"/>) stay. Returns the number deleted.</summary>
    public static int DeleteAll()
    {
        var deleted = 0;
        try
        {
            if (!Directory.Exists(AppPaths.LogsDirectory))
            {
                return 0;
            }

            foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDirectory, "clean-*.json"))
            {
                if (Delete(file))
                {
                    deleted++;
                }
            }

            Index?.Sync([]);
        }
        catch (Exception)
        {
            // Partly deleted: the index is synced on the next start.
        }

        return deleted;
    }

    public static bool Delete(string file)
    {
        try
        {
            File.Delete(file);
            Index?.Remove(file);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string ToCsv(CleanReport report)
    {
        var sb = new StringBuilder("Rule;Id;DeletedFiles;DeletedBytes;SkippedInUse;SkippedDenied;SkippedChanged;SkippedRunning;Errors\r\n");
        foreach (var r in report.Rules)
        {
            sb.Append(Csv(r.RuleName)).Append(';').Append(Csv(r.RuleId)).Append(';').Append(r.DeletedFiles).Append(';').Append(r.DeletedBytes).Append(';')
                .Append(r.SkippedInUse).Append(';').Append(r.SkippedDenied).Append(';').Append(r.SkippedChanged).Append(';')
                .Append(r.SkippedRunning).Append(';').Append(Csv(string.Join(" | ", r.Errors))).Append("\r\n");
        }

        return sb.ToString();
    }

    public static string ToHtml(CleanReport report, string title)
    {
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append("</title><style>")
            .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#18181B}h1{font-size:20px}table{border-collapse:collapse;width:100%}")
            .Append("th,td{border-bottom:1px solid #D9D9E0;padding:6px 8px;text-align:left;font-size:13px}th{background:#F3F3F6;color:#C2185B}")
            .Append("td.n{text-align:right}</style></head><body>")
            .Append("<h1>").Append(E(title)).Append("</h1><p>")
            .Append(E($"{report.StartedUtc.ToLocalTime():g} · {report.Area} · {ByteSize.Format(report.DeletedBytes)} · {report.DeletedFiles:N0} files · skipped {report.Skipped:N0}"))
            .Append("</p><table><tr><th>Rule</th><th>Files</th><th>Size</th><th>Skipped</th><th>Errors</th></tr>");
        foreach (var r in report.Rules)
        {
            sb.Append("<tr><td>").Append(E(r.RuleName)).Append("</td><td class=\"n\">").Append(r.DeletedFiles.ToString("N0"))
                .Append("</td><td class=\"n\">").Append(E(ByteSize.Format(r.DeletedBytes))).Append("</td><td class=\"n\">")
                .Append((r.SkippedInUse + r.SkippedDenied + r.SkippedChanged).ToString("N0")).Append(r.SkippedRunning ? " (running)" : "")
                .Append("</td><td>").Append(E(string.Join(" | ", r.Errors))).Append("</td></tr>");
        }

        return sb.Append("</table><p style=\"color:#71717A;font-size:12px\">CLEANit report</p></body></html>").ToString();
    }

    internal static HistoryEntry Summarize(CleanReport report, string file) =>
        new(report.StartedUtc, report.Area, report.DeletedBytes, report.DeletedFiles, file, report.Skipped, report.Cancelled);

    private static IEnumerable<HistoryEntry> ScanFolder()
    {
        if (!Directory.Exists(AppPaths.LogsDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDirectory, "clean-*.json"))
        {
            var report = Load(file);
            if (report is not null)
            {
                yield return Summarize(report, file);
            }
        }
    }

    private static void Rotate()
    {
        foreach (var old in new DirectoryInfo(AppPaths.LogsDirectory).GetFiles("clean-*.json")
                     .OrderByDescending(f => f.Name).Skip(KeepReports))
        {
            try
            {
                old.Delete();
                Index?.Remove(old.FullName);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Csv(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
