using CLEANit.Core.Engine;

namespace CLEANit.Core.Workspace;

/// <summary>Build output of one project (bin, obj, node_modules, Unity Library …).</summary>
public sealed record WorkspaceArtifact(
    string ProjectPath,
    string ProjectKind,
    string ArtifactPath,
    long Bytes,
    long Files,
    DateTime LastActivityUtc);

/// <summary>
/// Finds project folders below a root by their anchor files and lists their build output.
/// Everything listed is rebuilt by the next build/restore. Nothing is deleted here.
/// </summary>
public static class WorkspaceScanner
{
    private sealed record Anchor(string Kind, Func<string, bool> Matches, string[] Artifacts);

    private static readonly Anchor[] Anchors =
    [
        new(".NET", dir => HasAny(dir, "*.csproj", "*.vbproj", "*.fsproj"), ["bin", "obj"]),
        new("Visual Studio", dir => HasAny(dir, "*.sln", "*.slnx"), [".vs", "TestResults"]),
        new("Unity", dir => File.Exists(Path.Combine(dir, "ProjectSettings", "ProjectVersion.txt")), ["Library", "Temp", "Obj", "Logs"]),
        new("Gradle / Android", dir => HasAny(dir, "build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"),
            ["build", ".gradle", ".cxx"]),
        new("Node.js", dir => File.Exists(Path.Combine(dir, "package.json")),
            ["node_modules", ".next", ".nuxt", ".parcel-cache", ".turbo", ".svelte-kit", Path.Combine(".angular", "cache")]),
        new("Rust", dir => File.Exists(Path.Combine(dir, "Cargo.toml")), ["target"]),
        new("Maven", dir => File.Exists(Path.Combine(dir, "pom.xml")), ["target"]),
        new("Python", dir => HasAny(dir, "pyproject.toml", "requirements.txt", "setup.py"), [".pytest_cache", ".mypy_cache", ".ruff_cache", ".tox"])
    ];

    /// <summary>Folders never searched for projects.</summary>
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", "Library", "build", "target", ".gradle", ".vs", ".idea", "packages",
        "$RECYCLE.BIN", "System Volume Information"
    };

    public static async Task<IReadOnlyList<WorkspaceArtifact>> ScanAsync(string root, int maxDepth, IProgress<string>? progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var found = new List<(string Project, string Kind, string Artifact)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(root, 0, maxDepth, found, seen, progress, ct);

            var results = new List<WorkspaceArtifact>();
            foreach (var (project, kind, artifact) in found)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                progress?.Report(artifact);
                var (bytes, files, _) = SafeDelete.Measure(artifact, ct);
                if (bytes > 0 || files > 0)
                {
                    results.Add(new WorkspaceArtifact(project, kind, artifact, bytes, files, LastActivity(project)));
                }
            }

            return (IReadOnlyList<WorkspaceArtifact>)results.OrderByDescending(r => r.Bytes).ToList();
        }, CancellationToken.None);
    }

    private static void Walk(string dir, int depth, int maxDepth, List<(string, string, string)> found, HashSet<string> seen,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || depth > maxDepth || IsLink(dir))
        {
            return;
        }

        if (depth <= 2)
        {
            progress?.Report(dir);
        }

        var artifactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in Anchors)
        {
            bool matches;
            try
            {
                matches = anchor.Matches(dir);
            }
            catch (Exception)
            {
                matches = false;
            }

            if (!matches)
            {
                continue;
            }

            foreach (var name in anchor.Artifacts)
            {
                var artifact = Path.Combine(dir, name);
                artifactNames.Add(name.Split(Path.DirectorySeparatorChar)[0]);
                if (Directory.Exists(artifact) && !IsLink(artifact) && seen.Add(artifact))
                {
                    found.Add((dir, anchor.Kind, artifact));
                }
            }
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            }).ToList();
        }
        catch (Exception)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (!SkipFolders.Contains(name) && !artifactNames.Contains(name))
            {
                Walk(child, depth + 1, maxDepth, found, seen, progress, ct);
            }
        }
    }

    /// <summary>Newest source change: files in the project folder (two levels, build output excluded).</summary>
    private static DateTime LastActivity(string project)
    {
        var newest = DateTime.MinValue;
        try
        {
            foreach (var file in Directory.EnumerateFiles(project, "*", new EnumerationOptions { IgnoreInaccessible = true }))
            {
                newest = Max(newest, File.GetLastWriteTimeUtc(file));
            }

            foreach (var sub in Directory.EnumerateDirectories(project, "*", new EnumerationOptions
                     {
                         IgnoreInaccessible = true,
                         AttributesToSkip = FileAttributes.ReparsePoint
                     }))
            {
                if (SkipFolders.Contains(Path.GetFileName(sub)) || Anchors.Any(a => a.Artifacts.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(sub, "*", new EnumerationOptions { IgnoreInaccessible = true }).Take(500))
                {
                    newest = Max(newest, File.GetLastWriteTimeUtc(file));
                }
            }
        }
        catch (Exception)
        {
            // Partial result.
        }

        return newest == DateTime.MinValue ? Directory.GetLastWriteTimeUtc(project) : newest;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static bool HasAny(string dir, params string[] patterns) =>
        patterns.Any(p => p.Contains('*') ? Directory.EnumerateFiles(dir, p).Any() : File.Exists(Path.Combine(dir, p)));

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
