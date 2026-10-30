using System.IO.Enumeration;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;

namespace CLEANit.Core.Engine;

/// <summary>A target folder that passed the guard, with its exclusions.</summary>
public sealed record ResolvedTarget(string Root, CleanTarget Target, IReadOnlyList<(string Dir, string Mask)> Excludes, bool CheckZones);

public readonly record struct FileMatch(string Path, long Bytes, DateTime LastWriteUtc);

/// <summary>Resolves rule targets and enumerates matching files – shared by scanner and cleaner, so both see the same set.</summary>
public sealed class TargetResolver(PathGuard guard, IReadOnlyList<UserProfile> profiles)
{
    public PathGuard Guard { get; } = guard;

    public IReadOnlyList<UserProfile> Profiles { get; } = profiles;

    public (List<ResolvedTarget> Allowed, List<string> Blocked) Resolve(CleanRule rule)
    {
        var allowed = new List<ResolvedTarget>();
        var blocked = new List<string>();
        var excludes = ResolveExcludes(rule);
        foreach (var target in rule.Targets)
        {
            foreach (var dir in PathExpander.Resolve(target.Path, Profiles))
            {
                var verdict = Guard.CheckRuleTarget(dir, target);
                if (verdict.Allowed)
                {
                    allowed.Add(new ResolvedTarget(dir, target, excludes, Guard.HasZonesInside(dir)));
                }
                else
                {
                    blocked.Add($"{dir} – {verdict.Reason}");
                }
            }
        }

        return (allowed, blocked);
    }

    /// <summary>Files matching the target. Files changed after <paramref name="cutoffUtc"/> are left out.</summary>
    public IEnumerable<FileMatch> Enumerate(ResolvedTarget resolved, DateTime? cutoffUtc, CancellationToken ct)
    {
        var target = resolved.Target;
        IReadOnlyList<string> patterns = target.RemoveSelf ? ["*"] : target.Patterns;
        var minAge = target.MinAgeHours is > 0 ? DateTime.UtcNow.AddHours(-target.MinAgeHours.Value) : (DateTime?)null;
        var root = resolved.Root;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = target.Recursive || target.RemoveSelf,
            // Hidden and system files are cache files too; only links are skipped (never leave the folder).
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        IEnumerable<FileMatch> source;
        try
        {
            source = new FileSystemEnumerable<FileMatch>(root,
                (ref FileSystemEntry e) => new FileMatch(e.ToFullPath(), e.Length, e.LastWriteTimeUtc.UtcDateTime), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                {
                    if (e.IsDirectory)
                    {
                        return false;
                    }

                    var name = e.FileName;
                    var matched = false;
                    foreach (var pattern in patterns)
                    {
                        if (FileSystemName.MatchesSimpleExpression(pattern, name))
                        {
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        return false;
                    }

                    var written = e.LastWriteTimeUtc.UtcDateTime;
                    return (minAge is null || written <= minAge) && (cutoffUtc is null || written <= cutoffUtc);
                }
            };
        }
        catch (Exception)
        {
            yield break;
        }

        using var enumerator = SafeEnumerator(source);
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                yield break;
            }

            FileMatch match;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                match = enumerator.Current;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                yield break;
            }

            if (Guard.IsProtectedFile(match.Path, resolved.CheckZones) || IsExcluded(match.Path, root, resolved))
            {
                continue;
            }

            yield return match;
        }
    }

    private static IEnumerator<FileMatch> SafeEnumerator(IEnumerable<FileMatch> source) => source.GetEnumerator();

    private static bool IsExcluded(string file, string root, ResolvedTarget resolved)
    {
        var name = Path.GetFileName(file);
        foreach (var mask in resolved.Target.Exclude)
        {
            if (mask.Contains('\\'))
            {
                var relative = file.Length > root.Length + 1 ? file[(root.Length + 1)..] : name;
                if (FileSystemName.MatchesSimpleExpression(mask, relative))
                {
                    return true;
                }
            }
            else if (FileSystemName.MatchesSimpleExpression(mask, name))
            {
                return true;
            }
        }

        foreach (var (dir, mask) in resolved.Excludes)
        {
            if (PathGuard.IsStrictAncestor(file, dir) && (mask.Length == 0 || FileSystemName.MatchesSimpleExpression(mask, name)))
            {
                return true;
            }
        }

        return false;
    }

    private List<(string Dir, string Mask)> ResolveExcludes(CleanRule rule)
    {
        var list = new List<(string, string)>();
        foreach (var entry in rule.Excludes)
        {
            var sep = entry.LastIndexOf('|');
            var path = sep >= 0 ? entry[..sep] : entry;
            var mask = sep >= 0 ? entry[(sep + 1)..] : "";
            foreach (var dir in PathExpander.Resolve(path, Profiles))
            {
                list.Add((dir, mask));
            }
        }

        return list;
    }
}
