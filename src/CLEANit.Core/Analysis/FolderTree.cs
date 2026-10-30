namespace CLEANit.Core.Analysis;

/// <summary>Largest file of a folder (kept per folder, not every file – memory stays bounded).</summary>
public readonly record struct FileLeaf(string Name, long Bytes, DateTime LastWriteUtc);

/// <summary>Folder with its total size (sub folders included). The path is built from the parent chain to save memory.</summary>
public sealed class FolderNode
{
    internal FolderNode(string name, FolderNode? parent)
    {
        Name = name;
        Parent = parent;
    }

    public string Name { get; }

    public FolderNode? Parent { get; }

    /// <summary>Size of all files below this folder.</summary>
    public long Bytes { get; internal set; }

    /// <summary>Files directly in this folder.</summary>
    public long OwnBytes { get; internal set; }

    public long Files { get; internal set; }

    public long Folders { get; internal set; }

    public DateTime NewestUtc { get; internal set; }

    /// <summary>Folder could not be read (access denied).</summary>
    public bool Denied { get; internal set; }

    public List<FolderNode> Children { get; } = [];

    /// <summary>Largest files directly in this folder, largest first.</summary>
    public List<FileLeaf> LargestFiles { get; } = [];

    /// <summary>Files directly in this folder that are not listed in <see cref="LargestFiles"/>.</summary>
    public long OtherFiles { get; internal set; }

    public string Path => Parent is null ? Name : System.IO.Path.Combine(Parent.Path, Name);
}

public readonly record struct TreeProgress(long Folders, long Files, string Current);

/// <summary>
/// Folder tree of a drive or folder (read only): sizes per folder and the largest files of each folder.
/// Links are never followed; cancelling returns the part scanned so far.
/// </summary>
public static class FolderTree
{
    public const int FilesPerFolder = 25;

    /// <summary>A deleted sub folder leaves the tree; the totals of all parents are corrected (no new scan needed).</summary>
    public static void RemoveFolder(FolderNode folder)
    {
        if (folder.Parent is not { } parent || !parent.Children.Remove(folder))
        {
            return;
        }

        for (var p = parent; p is not null; p = p.Parent)
        {
            p.Bytes = Math.Max(0, p.Bytes - folder.Bytes);
            p.Files = Math.Max(0, p.Files - folder.Files);
            p.Folders = Math.Max(0, p.Folders - folder.Folders - 1);
        }
    }

    /// <summary>A deleted file leaves its folder's list of largest files; the totals of all parents are corrected.</summary>
    public static void RemoveFile(FolderNode parent, FileLeaf file)
    {
        if (!parent.LargestFiles.Remove(file))
        {
            return;
        }

        parent.OwnBytes = Math.Max(0, parent.OwnBytes - file.Bytes);
        for (var p = parent; p is not null; p = p.Parent)
        {
            p.Bytes = Math.Max(0, p.Bytes - file.Bytes);
            p.Files = Math.Max(0, p.Files - 1);
        }
    }

    public static Task<FolderNode> ScanAsync(string root, IProgress<TreeProgress>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            var top = new FolderNode(root.TrimEnd('\\', '/') is { Length: > 0 } trimmed && !trimmed.EndsWith(':') ? trimmed : root, null);
            long folders = 0, files = 0;
            var lastReport = Environment.TickCount64;

            void Report(string path)
            {
                var now = Environment.TickCount64;
                if (now - lastReport > 150)
                {
                    lastReport = now;
                    progress?.Report(new TreeProgress(Interlocked.Read(ref folders), Interlocked.Read(ref files), path));
                }
            }

            // Root level: own files, then sub folders in parallel (each sub tree is built by one thread).
            var subDirs = ReadLevel(top, root, ref files);
            var children = new FolderNode?[subDirs.Count];
            Parallel.For(0, subDirs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6) }, i =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var node = new FolderNode(subDirs[i].Name, top);
                long localFiles = 0;
                Build(node, subDirs[i].Path, ref localFiles, ref folders, Report, ct);
                Interlocked.Add(ref files, localFiles);
                children[i] = node;
            });

            foreach (var child in children)
            {
                if (child is not null)
                {
                    top.Children.Add(child);
                }
            }

            Aggregate(top);
            progress?.Report(new TreeProgress(folders, files, root));
            return top;
        }, CancellationToken.None);

    private static void Build(FolderNode node, string path, ref long files, ref long folders, Action<string> report, CancellationToken ct)
    {
        // Explicit stack: deep trees do not overflow the call stack.
        var stack = new Stack<(FolderNode Node, string Path)>();
        stack.Push((node, path));
        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var (current, currentPath) = stack.Pop();
            Interlocked.Increment(ref folders);
            report(currentPath);
            foreach (var (name, subPath) in ReadLevel(current, currentPath, ref files))
            {
                var child = new FolderNode(name, current);
                current.Children.Add(child);
                stack.Push((child, subPath));
            }
        }
    }

    /// <summary>Reads one folder: own files into the node, returns the sub folders (no links).</summary>
    private static List<(string Name, string Path)> ReadLevel(FolderNode node, string path, ref long files)
    {
        var dirs = new List<(string, string)>();
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions
                     {
                         IgnoreInaccessible = true,
                         RecurseSubdirectories = false,
                         AttributesToSkip = FileAttributes.ReparsePoint,
                         ReturnSpecialDirectories = false
                     }))
            {
                if (entry is FileInfo file)
                {
                    long length;
                    try
                    {
                        length = file.Length;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    node.OwnBytes += length;
                    node.Files++;
                    files++;
                    if (file.LastWriteTimeUtc > node.NewestUtc)
                    {
                        node.NewestUtc = file.LastWriteTimeUtc;
                    }

                    AddLargest(node, new FileLeaf(file.Name, length, file.LastWriteTimeUtc));
                }
                else
                {
                    dirs.Add((entry.Name, entry.FullName));
                }
            }
        }
        catch (Exception)
        {
            node.Denied = true;
        }

        return dirs;
    }

    private static void AddLargest(FolderNode node, FileLeaf leaf)
    {
        var list = node.LargestFiles;
        if (list.Count < FilesPerFolder)
        {
            list.Add(leaf);
            return;
        }

        var smallest = 0;
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].Bytes < list[smallest].Bytes)
            {
                smallest = i;
            }
        }

        if (leaf.Bytes > list[smallest].Bytes)
        {
            list[smallest] = leaf;
        }
    }

    /// <summary>Totals bottom-up (iterative), sorts children and files by size.</summary>
    private static void Aggregate(FolderNode root)
    {
        var order = new List<FolderNode>();
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            order.Add(node);
            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        for (var i = order.Count - 1; i >= 0; i--)
        {
            var node = order[i];
            long bytes = node.OwnBytes, fileCount = node.Files, folderCount = node.Children.Count;
            var newest = node.NewestUtc;
            foreach (var child in node.Children)
            {
                bytes += child.Bytes;
                fileCount += child.Files;
                folderCount += child.Folders;
                if (child.NewestUtc > newest)
                {
                    newest = child.NewestUtc;
                }
            }

            node.OtherFiles = Math.Max(0, node.Files - node.LargestFiles.Count);
            node.Bytes = bytes;
            node.Files = fileCount;
            node.Folders = folderCount;
            node.NewestUtc = newest;
            node.Children.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            node.LargestFiles.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            node.Children.TrimExcess();
            node.LargestFiles.TrimExcess();
        }
    }
}
