using CLEANit.Core.Analysis;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;

namespace CLEANit.Platform.Windows;

/// <summary>Hash cache of the duplicate finder (data\cache\hashes.db): path, size, last write, SHA-256.</summary>
public sealed class SqliteHashCache : IHashCache, IDisposable
{
    private readonly SqliteDb _db;
    private readonly SqliteDb.Statement _select;
    private readonly SqliteDb.Statement _upsert;
    private int _pending;

    public SqliteHashCache(string? file = null)
    {
        _db = SqliteDb.Open(file ?? Path.Combine(AppPaths.DataDirectory, "cache", "hashes.db"));
        _db.Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS hashes(path TEXT PRIMARY KEY, size INTEGER NOT NULL, mtime INTEGER NOT NULL, hash TEXT NOT NULL, seen INTEGER NOT NULL)");
        _select = _db.Prepare("SELECT hash FROM hashes WHERE path=? AND size=? AND mtime=?");
        // INSERT OR REPLACE works with the older SQLite of Windows Server 2016 (no UPSERT syntax needed).
        _upsert = _db.Prepare("INSERT OR REPLACE INTO hashes(path,size,mtime,hash,seen) VALUES(?,?,?,?,?)");
        _db.Execute("BEGIN");
    }

    public bool TryGet(string path, long size, DateTime lastWriteUtc, out string hash)
    {
        _select.Bind(path.ToUpperInvariant(), size, lastWriteUtc);
        if (_select.Step())
        {
            hash = new SqliteDb.SqliteRow(_select.Handle).Text(0) ?? "";
            return hash.Length > 0;
        }

        hash = "";
        return false;
    }

    public void Set(string path, long size, DateTime lastWriteUtc, string hash)
    {
        _upsert.Bind(path.ToUpperInvariant(), size, lastWriteUtc, hash, DateTime.UtcNow);
        _upsert.StepToEnd();
        if (++_pending >= 500)
        {
            Flush();
        }
    }

    public void Flush()
    {
        _db.Execute("COMMIT");
        _pending = 0;

        // Entries not seen for 180 days are dropped.
        _db.Execute("DELETE FROM hashes WHERE seen < ?", DateTime.UtcNow.AddDays(-180));
        _db.Execute("BEGIN");
    }

    public void Dispose()
    {
        try
        {
            _db.Execute("COMMIT");
        }
        catch (InvalidOperationException)
        {
            // No open transaction.
        }

        _select.Dispose();
        _upsert.Dispose();
        _db.Dispose();
    }
}

/// <summary>Index of the cleaning reports (data\reports.db). The JSON reports stay the source.</summary>
public sealed class SqliteReportIndex : IReportIndex
{
    private readonly string _file;
    private readonly object _lock = new();

    public SqliteReportIndex(string? file = null)
    {
        _file = file ?? Path.Combine(AppPaths.DataDirectory, "reports.db");
        using var db = SqliteDb.Open(_file);
        db.Execute("CREATE TABLE IF NOT EXISTS reports(file TEXT PRIMARY KEY, time INTEGER NOT NULL, area TEXT NOT NULL, bytes INTEGER NOT NULL, " +
                   "files INTEGER NOT NULL, skipped INTEGER NOT NULL, cancelled INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS ix_reports_time ON reports(time)");
    }

    public void Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            using var db = SqliteDb.Open(_file);
            Insert(db, entry);
        }
    }

    public IReadOnlyList<HistoryEntry> List(int max)
    {
        lock (_lock)
        {
            using var db = SqliteDb.Open(_file);
            return db.Query("SELECT file,time,area,bytes,files,skipped,cancelled FROM reports ORDER BY time DESC LIMIT ?", max)
                .Select(r => new HistoryEntry(new DateTime(r.Int(1), DateTimeKind.Utc), r.Text(2) ?? "", r.Int(3), r.Int(4), r.Text(0), r.Int(5), r.Int(6) != 0))
                .ToList();
        }
    }

    public void Remove(string reportFile)
    {
        lock (_lock)
        {
            using var db = SqliteDb.Open(_file);
            db.Execute("DELETE FROM reports WHERE file=?", reportFile);
        }
    }

    public void Sync(IEnumerable<HistoryEntry> onDisk)
    {
        lock (_lock)
        {
            using var db = SqliteDb.Open(_file);
            var known = db.Query("SELECT file FROM reports").Select(r => r.Text(0) ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            db.Execute("BEGIN");
            foreach (var entry in onDisk.Where(e => e.ReportFile is not null && !known.Contains(e.ReportFile)))
            {
                Insert(db, entry);
            }

            foreach (var file in known.Where(f => !File.Exists(f)))
            {
                db.Execute("DELETE FROM reports WHERE file=?", file);
            }

            db.Execute("COMMIT");
        }
    }

    private static void Insert(SqliteDb db, HistoryEntry e) =>
        db.Execute("INSERT OR REPLACE INTO reports(file,time,area,bytes,files,skipped,cancelled) VALUES(?,?,?,?,?,?,?)",
            e.ReportFile ?? "", e.TimeUtc, e.Area, e.Bytes, e.Files, e.Skipped, e.Cancelled);
}
