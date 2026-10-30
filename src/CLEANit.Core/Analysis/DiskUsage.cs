using CLEANit.Core.Engine;

namespace CLEANit.Core.Analysis;

public sealed record UsageItem(string Name, string Path, bool IsFolder, long Bytes, long Files, DateTime LastWriteUtc);

/// <summary>Space used by one file extension (".mp4"; "" = no extension).</summary>
public sealed record FileTypeUsage(string Extension, long Bytes, long Files);

public sealed record DriveUsage(string Name, string Label, string Format, long Total, long Free)
{
    public long Used => Total - Free;

    public double UsedPercent => Total > 0 ? Used * 100d / Total : 0;
}

/// <summary>Disk usage (read only): drives and the size of each item in a folder.</summary>
public static class DiskUsage
{
    public static IReadOnlyList<DriveUsage> Drives()
    {
        var list = new List<DriveUsage>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
                {
                    list.Add(new DriveUsage(drive.Name, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
                }
            }
            catch (Exception)
            {
                // Not ready.
            }
        }

        return list;
    }

    /// <summary>Direct children with their total size, largest first. Links are not followed.</summary>
    public static async Task<IReadOnlyList<UsageItem>> AnalyzeAsync(string folder, IProgress<string>? progress, CancellationToken ct)
    {
        var dir = new DirectoryInfo(folder);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var items = new List<UsageItem>();
        foreach (var file in dir.EnumerateFiles("*", options))
        {
            items.Add(new UsageItem(file.Name, file.FullName, false, file.Length, 1, file.LastWriteTimeUtc));
        }

        var folders = dir.EnumerateDirectories("*", options).ToList();
        var results = new UsageItem[folders.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, folders.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            (i, _) =>
            {
                var sub = folders[i];
                if (ct.IsCancellationRequested)
                {
                    results[i] = new UsageItem(sub.Name, sub.FullName, true, 0, 0, sub.LastWriteTimeUtc);
                    return ValueTask.CompletedTask;
                }

                progress?.Report(sub.FullName);
                var (bytes, files, newest) = SafeDelete.Measure(sub.FullName, ct);
                results[i] = new UsageItem(sub.Name, sub.FullName, true, bytes, files, newest == DateTime.MinValue ? sub.LastWriteTimeUtc : newest);
                return ValueTask.CompletedTask;
            });

        items.AddRange(results);
        return items.OrderByDescending(i => i.Bytes).ToList();
    }

    /// <summary>Size per file extension below a folder, largest first. Cancelling returns the partial result.</summary>
    public static Task<IReadOnlyList<FileTypeUsage>> FileTypesAsync(string folder, int maxTypes, IProgress<long>? progress, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileTypeUsage>>(() =>
        {
            var totals = new Dictionary<string, (long Bytes, long Files)>(StringComparer.OrdinalIgnoreCase);
            long count = 0;
            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    var ext = file.Extension.Length is > 0 and <= 12 ? file.Extension.ToLowerInvariant() : "";
                    var (bytes, files) = totals.GetValueOrDefault(ext);
                    totals[ext] = (bytes + file.Length, files + 1);
                    if (++count % 5000 == 0)
                    {
                        progress?.Report(count);
                    }
                }
            }
            catch (Exception)
            {
                // Partial result.
            }

            var ordered = totals.Select(t => new FileTypeUsage(t.Key, t.Value.Bytes, t.Value.Files)).OrderByDescending(t => t.Bytes).ToList();
            if (ordered.Count <= maxTypes)
            {
                return ordered;
            }

            var rest = ordered.Skip(maxTypes - 1).ToList();
            return [.. ordered.Take(maxTypes - 1), new FileTypeUsage("*", rest.Sum(r => r.Bytes), rest.Sum(r => r.Files))];
        }, CancellationToken.None);
}
