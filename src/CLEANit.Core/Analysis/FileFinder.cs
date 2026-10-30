using System.IO.Enumeration;
using System.Security.Cryptography;
using CLEANit.Core.Paths;

namespace CLEANit.Core.Analysis;

public sealed record FoundFile(string Path, long Bytes, DateTime LastWriteUtc, DateTime LastAccessUtc);

public sealed record DuplicateGroup(string Hash, long Bytes, IReadOnlyList<FoundFile> Files)
{
    /// <summary>Space freed when all but one copy are removed.</summary>
    public long Wasted => Bytes * (Files.Count - 1);
}

/// <summary>Cache of file hashes (SQLite in the app). Key: path + size + last write time.</summary>
public interface IHashCache
{
    bool TryGet(string path, long size, DateTime lastWriteUtc, out string hash);

    void Set(string path, long size, DateTime lastWriteUtc, string hash);

    void Flush();
}

public readonly record struct FinderProgress(string Stage, long Done, long Total, string? Current);

/// <summary>Large/old files and duplicates below folders the user picked (read only). System folders and links are skipped.</summary>
public static class FileFinder
{
    private const int PartialBytes = 64 * 1024;

    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", "Windows", "WinSxS", "$WinREAgent", "Config.Msi", "Recovery",
        // Program internals: removing a "duplicate" there breaks applications.
        "AppData", "Program Files", "Program Files (x86)", "ProgramData", "node_modules", ".git", ".nuget", ".gradle"
    };

    /// <summary>Files of at least <paramref name="minBytes"/> not modified for <paramref name="olderThanDays"/> days, largest first.</summary>
    public static Task<IReadOnlyList<FoundFile>> FindLargeAsync(string root, long minBytes, int olderThanDays, int maxResults, PathGuard guard,
        IProgress<FinderProgress>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var cutoff = olderThanDays > 0 ? DateTime.UtcNow.AddDays(-olderThanDays) : DateTime.MaxValue;
            var heap = new PriorityQueue<FoundFile, long>();
            long seen = 0;
            foreach (var file in Enumerate(root, minBytes, guard, ct))
            {
                if (++seen % 2000 == 0)
                {
                    progress?.Report(new FinderProgress("scan", seen, 0, file.Path));
                }

                if (file.LastWriteUtc > cutoff)
                {
                    continue;
                }

                heap.Enqueue(file, file.Bytes);
                if (heap.Count > maxResults)
                {
                    heap.Dequeue();
                }
            }

            var list = new List<FoundFile>(heap.Count);
            while (heap.Count > 0)
            {
                list.Add(heap.Dequeue());
            }

            list.Reverse();
            return (IReadOnlyList<FoundFile>)list;
        }, CancellationToken.None);

    /// <summary>Duplicates: same size → same hash of the first and last 64 KB → same SHA-256 of the whole file.</summary>
    public static Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(IReadOnlyList<string> roots, long minBytes, PathGuard guard, IHashCache? cache,
        IProgress<FinderProgress>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var bySize = new Dictionary<long, List<FoundFile>>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long scanned = 0;
            foreach (var root in roots)
            {
                foreach (var file in Enumerate(root, minBytes, guard, ct))
                {
                    if (!seenPaths.Add(file.Path))
                    {
                        continue;
                    }

                    if (++scanned % 2000 == 0)
                    {
                        progress?.Report(new FinderProgress("scan", scanned, 0, file.Path));
                    }

                    if (!bySize.TryGetValue(file.Bytes, out var list))
                    {
                        bySize[file.Bytes] = list = [];
                    }

                    list.Add(file);
                }
            }

            var candidates = bySize.Values.Where(l => l.Count > 1).ToList();
            var total = candidates.Sum(c => (long)c.Count);
            long done = 0;
            var groups = new List<DuplicateGroup>();
            foreach (var sameSize in candidates)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                // Stage 1: first and last 64 KB (cheap). Stage 2: full hash, cached.
                foreach (var partial in sameSize.GroupBy(f => Hash(f, partial: true, null)).Where(g => g.Key is not null && g.Count() > 1))
                {
                    foreach (var full in partial.GroupBy(f => Hash(f, partial: false, cache)).Where(g => g.Key is not null && g.Count() > 1))
                    {
                        groups.Add(new DuplicateGroup(full.Key!, sameSize[0].Bytes, full.OrderBy(f => f.LastWriteUtc).ToList()));
                    }

                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                }

                done += sameSize.Count;
                progress?.Report(new FinderProgress("hash", done, total, sameSize[0].Path));
            }

            cache?.Flush();
            return (IReadOnlyList<DuplicateGroup>)groups.OrderByDescending(g => g.Wasted).ToList();
        }, CancellationToken.None);

    private static IEnumerable<FoundFile> Enumerate(string root, long minBytes, PathGuard guard, CancellationToken ct)
    {
        // The guard works on Windows paths; drive roots (D:\) may be searched, system folders below are skipped.
        if (!Directory.Exists(root) || (OperatingSystem.IsWindows() && !guard.CheckUserTarget(root).Allowed && !IsDriveRoot(root)))
        {
            yield break;
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline
        };

        var excluded = new[] { KnownFolders.Windows, KnownFolders.ProgramFiles, KnownFolders.ProgramFilesX86, KnownFolders.ProgramData }
            .Where(p => !string.IsNullOrEmpty(p)).Select(PathExpander.Normalize).ToList();
        var enumerable = new FileSystemEnumerable<FoundFile>(root,
            (ref FileSystemEntry e) => new FoundFile(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcDateTime, e.LastAccessTimeUtc.UtcDateTime), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory && e.Length >= minBytes,
            ShouldRecursePredicate = (ref FileSystemEntry e) => !SkipFolders.Contains(e.FileName.ToString())
        };

        using var enumerator = enumerable.GetEnumerator();
        while (!ct.IsCancellationRequested)
        {
            FoundFile file;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                file = enumerator.Current;
            }
            catch (Exception)
            {
                yield break;
            }

            if (excluded.Any(x => PathGuard.IsUnderOrEqual(file.Path, x)) || guard.IsProtectedFile(file.Path))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsDriveRoot(string path) => PathExpander.Normalize(path).Length <= 3;

    private static string? Hash(FoundFile file, bool partial, IHashCache? cache)
    {
        if (!partial && cache is not null && cache.TryGet(file.Path, file.Bytes, file.LastWriteUtc, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16,
                FileOptions.SequentialScan);
            string hash;
            if (partial && stream.Length > PartialBytes * 2)
            {
                var buffer = new byte[PartialBytes * 2];
                stream.ReadExactly(buffer, 0, PartialBytes);
                stream.Seek(-PartialBytes, SeekOrigin.End);
                stream.ReadExactly(buffer, PartialBytes, PartialBytes);
                hash = Convert.ToHexString(SHA256.HashData(buffer));
            }
            else
            {
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }

            if (!partial)
            {
                cache?.Set(file.Path, file.Bytes, file.LastWriteUtc, hash);
            }

            return hash;
        }
        catch (Exception)
        {
            // Locked or vanished: not a duplicate candidate.
            return null;
        }
    }
}
