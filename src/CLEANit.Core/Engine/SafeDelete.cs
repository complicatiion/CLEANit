using CLEANit.Core.Abstractions;
using CLEANit.Core.Paths;

namespace CLEANit.Core.Engine;

public sealed record FolderDeleteResult(string Path, bool Deleted, long Bytes, string? Error);

/// <summary>
/// Removes whole folders the user picked (workspace artifacts, uninstall leftovers).
/// Links (junctions, symlinks – e.g. pnpm node_modules) are removed as links, their targets are never entered.
/// </summary>
public static class SafeDelete
{
    public static FolderDeleteResult DeleteFolder(string path, long bytes, PathGuard guard, IRecycleBin? recycleBin, bool useRecycleBin)
    {
        var verdict = guard.CheckUserTarget(path);
        if (!verdict.Allowed)
        {
            return new FolderDeleteResult(path, false, 0, verdict.Reason);
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return new FolderDeleteResult(path, true, 0, null);
            }

            if (useRecycleBin && recycleBin is not null)
            {
                var failed = recycleBin.Recycle([path]);
                return new FolderDeleteResult(path, failed == 0 && !Directory.Exists(path), failed == 0 ? bytes : 0,
                    failed == 0 ? null : "recycle bin");
            }

            DeleteTree(new DirectoryInfo(path));
            return new FolderDeleteResult(path, !Directory.Exists(path), bytes, Directory.Exists(path) ? "partly in use" : null);
        }
        catch (Exception ex)
        {
            return new FolderDeleteResult(path, false, 0, ex.Message);
        }
    }

    private static void DeleteTree(DirectoryInfo dir)
    {
        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            // Junction or symlink: remove the link only.
            dir.Delete(recursive: false);
            return;
        }

        foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            try
            {
                if (entry is DirectoryInfo sub)
                {
                    DeleteTree(sub);
                }
                else
                {
                    if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    entry.Delete();
                }
            }
            catch (IOException)
            {
                // In use: continue with the rest.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            dir.Delete(recursive: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Single files the user picked (large files, duplicates): guard per file, recycle bin or permanent.</summary>
    public static (long DeletedFiles, long DeletedBytes, List<string> Errors) DeleteFiles(IReadOnlyList<(string Path, long Bytes)> files,
        PathGuard guard, IRecycleBin? recycleBin, bool useRecycleBin)
    {
        long count = 0, bytes = 0;
        var errors = new List<string>();
        var allowed = new List<(string Path, long Bytes)>();
        foreach (var file in files)
        {
            var dir = Path.GetDirectoryName(file.Path);
            if (dir is null || !guard.CheckUserTarget(dir).Allowed || guard.IsProtectedFile(file.Path) || PathExpander.IsReparsePoint(file.Path))
            {
                errors.Add($"{file.Path}: protected");
                continue;
            }

            allowed.Add(file);
        }

        if (useRecycleBin && recycleBin is not null)
        {
            foreach (var chunk in allowed.Chunk(200))
            {
                recycleBin.Recycle(chunk.Select(c => c.Path).ToList());
                foreach (var file in chunk)
                {
                    if (File.Exists(file.Path))
                    {
                        errors.Add($"{file.Path}: in use");
                    }
                    else
                    {
                        count++;
                        bytes += file.Bytes;
                    }
                }
            }

            return (count, bytes, errors);
        }

        foreach (var file in allowed)
        {
            try
            {
                var info = new FileInfo(file.Path);
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }

                info.Delete();
                count++;
                bytes += file.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{file.Path}: {ex.Message}");
            }
        }

        return (count, bytes, errors);
    }

    /// <summary>Folder size without following links.</summary>
    public static (long Bytes, long Files, DateTime NewestUtc) Measure(string path, CancellationToken ct = default)
    {
        long bytes = 0, files = 0;
        var newest = DateTime.MinValue;
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                // Cancelled: partial result, no exception (callers check the token).
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                bytes += file.Length;
                files++;
                if (file.LastWriteTimeUtc > newest)
                {
                    newest = file.LastWriteTimeUtc;
                }
            }
        }
        catch (Exception)
        {
            // Partial result.
        }

        return (bytes, files, newest);
    }
}
