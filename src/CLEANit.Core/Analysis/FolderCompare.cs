using System.Buffers;
using CLEANit.Core.Paths;

namespace CLEANit.Core.Analysis;

/// <summary>Result of comparing one path that exists in the left and/or right folder.</summary>
public enum CompareState
{
    Identical,
    Different,
    LeftOnly,
    RightOnly
}

/// <summary>Why two files count as different.</summary>
public enum CompareReason
{
    None,
    Size,
    Time,
    Content,
    Type
}

/// <summary>
/// One entry of a folder comparison. A folder that exists on one side only is one entry (with its size and file count),
/// not one entry per file inside.
/// </summary>
public sealed record CompareEntry(
    string RelativePath,
    bool IsFolder,
    CompareState State,
    CompareReason Reason,
    long LeftBytes,
    long RightBytes,
    DateTime? LeftTimeUtc,
    DateTime? RightTimeUtc,
    long Files)
{
    public string Name => Path.GetFileName(RelativePath);

    public string Folder => Path.GetDirectoryName(RelativePath) ?? "";

    /// <summary>Different files: which side changed last (null = same time or unknown).</summary>
    public bool? LeftIsNewer => LeftTimeUtc is { } l && RightTimeUtc is { } r && Math.Abs((l - r).TotalSeconds) > FolderCompare.TimeToleranceSeconds
        ? l > r
        : null;
}

public sealed record CompareProgress(long Compared, string Current);

public sealed record CompareResult(IReadOnlyList<CompareEntry> Entries, bool Cancelled, IReadOnlyList<string> Errors)
{
    public int Count(CompareState state) => Entries.Count(e => e.State == state);
}

/// <summary>
/// WinMerge-style folder comparison (read only): by size and change time (fast) or by content (exact, byte by byte).
/// Links (junctions, symlinks) are never followed; a cancelled run returns what was compared so far.
/// </summary>
public static class FolderCompare
{
    /// <summary>FAT/exFAT store times with 2 s resolution; network shares round as well.</summary>
    public const double TimeToleranceSeconds = 2;

    public static Task<CompareResult> CompareAsync(string left, string right, bool byContent, IProgress<CompareProgress>? progress,
        CancellationToken ct) =>
        Task.Run(() => Compare(left, right, byContent, progress, ct), CancellationToken.None);

    public static CompareResult Compare(string left, string right, bool byContent, IProgress<CompareProgress>? progress, CancellationToken ct)
    {
        var entries = new List<CompareEntry>();
        var errors = new List<string>();
        long compared = 0;
        var lastReport = Environment.TickCount64;

        void Walk(string relative)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var leftItems = List(Path.Combine(left, relative), errors);
            var rightItems = List(Path.Combine(right, relative), errors);
            var names = leftItems.Keys.Union(rightItems.Keys, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in names)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var path = relative.Length == 0 ? name : Path.Combine(relative, name);
                leftItems.TryGetValue(name, out var l);
                rightItems.TryGetValue(name, out var r);
                if (l is not null && r is null)
                {
                    entries.Add(OneSide(path, l, CompareState.LeftOnly, ct));
                    continue;
                }

                if (l is null && r is not null)
                {
                    entries.Add(OneSide(path, r, CompareState.RightOnly, ct));
                    continue;
                }

                if (l is DirectoryInfo && r is DirectoryInfo)
                {
                    Walk(path);
                    continue;
                }

                if (l is FileInfo lf && r is FileInfo rf)
                {
                    entries.Add(CompareFiles(path, lf, rf, byContent, errors, ct));
                    compared++;
                    if (progress is not null && Environment.TickCount64 - lastReport > 150)
                    {
                        lastReport = Environment.TickCount64;
                        progress.Report(new CompareProgress(compared, path));
                    }

                    continue;
                }

                // A file on one side, a folder on the other.
                entries.Add(new CompareEntry(path, l is DirectoryInfo, CompareState.Different, CompareReason.Type,
                    (l as FileInfo)?.Length ?? 0, (r as FileInfo)?.Length ?? 0, l!.LastWriteTimeUtc, r!.LastWriteTimeUtc, 0));
            }
        }

        Walk("");
        progress?.Report(new CompareProgress(compared, ""));
        return new CompareResult(entries, ct.IsCancellationRequested, errors.Distinct().Take(50).ToList());
    }

    private static CompareEntry OneSide(string path, FileSystemInfo item, CompareState state, CancellationToken ct)
    {
        long bytes;
        long files;
        if (item is FileInfo file)
        {
            bytes = file.Length;
            files = 1;
        }
        else
        {
            var measured = Measure(item.FullName, ct);
            bytes = measured.Bytes;
            files = measured.Files;
        }

        var left = state == CompareState.LeftOnly;
        return new CompareEntry(path, item is DirectoryInfo, state, CompareReason.None, left ? bytes : 0, left ? 0 : bytes,
            left ? item.LastWriteTimeUtc : null, left ? null : item.LastWriteTimeUtc, files);
    }

    private static CompareEntry CompareFiles(string path, FileInfo left, FileInfo right, bool byContent, List<string> errors, CancellationToken ct)
    {
        var reason = CompareReason.None;
        if (left.Length != right.Length)
        {
            reason = CompareReason.Size;
        }
        else if (byContent)
        {
            try
            {
                if (!SameContent(left.FullName, right.FullName, ct))
                {
                    reason = CompareReason.Content;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{path}: {ex.Message}");
                reason = CompareReason.Content;
            }
        }
        else if (Math.Abs((left.LastWriteTimeUtc - right.LastWriteTimeUtc).TotalSeconds) > TimeToleranceSeconds)
        {
            reason = CompareReason.Time;
        }

        return new CompareEntry(path, false, reason == CompareReason.None ? CompareState.Identical : CompareState.Different, reason,
            left.Length, right.Length, left.LastWriteTimeUtc, right.LastWriteTimeUtc, 1);
    }

    /// <summary>Byte-by-byte comparison in 1 MB blocks; stops at the first difference.</summary>
    public static bool SameContent(string leftPath, string rightPath, CancellationToken ct = default)
    {
        const int block = 1024 * 1024;
        using var a = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, block, FileOptions.SequentialScan);
        using var b = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, block, FileOptions.SequentialScan);
        if (a.Length != b.Length)
        {
            return false;
        }

        var bufferA = ArrayPool<byte>.Shared.Rent(block);
        var bufferB = ArrayPool<byte>.Shared.Rent(block);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var readA = a.ReadAtLeast(bufferA.AsSpan(0, block), block, throwOnEndOfStream: false);
                var readB = b.ReadAtLeast(bufferB.AsSpan(0, block), block, throwOnEndOfStream: false);
                if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                {
                    return false;
                }

                if (readA == 0)
                {
                    return true;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bufferA);
            ArrayPool<byte>.Shared.Return(bufferB);
        }
    }

    /// <summary>Direct children of a folder (links skipped); missing or denied folders give an empty list.</summary>
    private static Dictionary<string, FileSystemInfo> List(string directory, List<string> errors)
    {
        var items = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return items;
        }

        try
        {
            foreach (var item in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", new EnumerationOptions
                     {
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                         RecurseSubdirectories = false
                     }))
            {
                items[item.Name] = item;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"{directory}: {ex.Message}");
        }

        return items;
    }

    private static (long Bytes, long Files) Measure(string directory, CancellationToken ct)
    {
        long bytes = 0;
        long files = 0;
        try
        {
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions
                     {
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                         RecurseSubdirectories = true
                     }))
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                bytes += file.Length;
                files++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Partial size is fine for the overview.
        }

        return (bytes, files);
    }
}

public sealed record CopyProgress(long Files, long Bytes, string Current);

public sealed record CopyResult(long Files, long Bytes, long Skipped, bool Cancelled, IReadOnlyList<string> Errors);

/// <summary>
/// Copies compared entries from one folder to the other. Existing files are only replaced when <c>overwrite</c> is set
/// (confirmed by the user); times and attributes are kept; links are never followed. The target folder must pass the guard.
/// </summary>
public static class FolderCopy
{
    public static Task<CopyResult> CopyAsync(string sourceRoot, string targetRoot, IReadOnlyList<CompareEntry> entries, bool overwrite,
        PathGuard guard, IProgress<CopyProgress>? progress, CancellationToken ct) =>
        Task.Run(() => Copy(sourceRoot, targetRoot, entries, overwrite, guard, progress, ct), CancellationToken.None);

    public static CopyResult Copy(string sourceRoot, string targetRoot, IReadOnlyList<CompareEntry> entries, bool overwrite, PathGuard guard,
        IProgress<CopyProgress>? progress, CancellationToken ct)
    {
        var errors = new List<string>();
        long files = 0, bytes = 0, skipped = 0;
        var verdict = guard.CheckUserTarget(targetRoot);
        if (!verdict.Allowed)
        {
            return new CopyResult(0, 0, entries.Count, false, [$"{targetRoot}: {verdict.Reason}"]);
        }

        void CopyFile(string source, string target)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var targetFile = new FileInfo(target);
                if (targetFile.Exists && !overwrite)
                {
                    skipped++;
                    return;
                }

                var directory = targetFile.DirectoryName!;
                var check = guard.CheckUserTarget(directory);
                if (!check.Allowed)
                {
                    errors.Add($"{directory}: {check.Reason}");
                    skipped++;
                    return;
                }

                Directory.CreateDirectory(directory);
                if (targetFile.Exists && targetFile.IsReadOnly)
                {
                    targetFile.IsReadOnly = false;
                }

                File.Copy(source, target, overwrite: true);
                var length = new FileInfo(source).Length;
                files++;
                bytes += length;
                progress?.Report(new CopyProgress(files, bytes, source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{source}: {ex.Message}");
                skipped++;
            }
        }

        foreach (var entry in entries)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var source = Path.Combine(sourceRoot, entry.RelativePath);
            var target = Path.Combine(targetRoot, entry.RelativePath);
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(target);
                foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", new EnumerationOptions
                         {
                             IgnoreInaccessible = true,
                             AttributesToSkip = FileAttributes.ReparsePoint,
                             RecurseSubdirectories = true
                         }))
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    CopyFile(file.FullName, Path.Combine(target, Path.GetRelativePath(source, file.FullName)));
                }
            }
            else if (File.Exists(source))
            {
                CopyFile(source, target);
            }
            else
            {
                skipped++;
            }
        }

        return new CopyResult(files, bytes, skipped, ct.IsCancellationRequested, errors.Distinct().Take(50).ToList());
    }
}
