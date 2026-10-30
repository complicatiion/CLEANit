using System.IO.Enumeration;
using CLEANit.Core.Abstractions;

namespace CLEANit.Core.Paths;

/// <summary>
/// Expands rule paths: %Tokens% (per user profile), %ProgramFiles% (both views) and * / ? in folder names.
/// Never follows junctions or symbolic links.
/// </summary>
public static class PathExpander
{
    private const int MaxResults = 4000;

    private static readonly string[] UserTokens =
    [
        "%UserProfile%", "%AppData%", "%LocalAppData%", "%LocalLowAppData%", "%Temp%", "%Tmp%", "%UserName%",
        "%Documents%", "%Desktop%", "%Pictures%", "%Music%", "%Video%", "%Videos%", "%Downloads%"
    ];

    public static bool IsPerUser(string path) =>
        UserTokens.Any(t => path.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>Existing folders for a rule path, for the given profiles.</summary>
    public static IReadOnlyList<string> Resolve(string path, IEnumerable<UserProfile> profiles)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contexts = IsPerUser(path) ? profiles.Select(p => (UserProfile?)p) : [null];
        foreach (var profile in contexts)
        {
            foreach (var expanded in ExpandTokens(path, profile))
            {
                foreach (var dir in ResolveWildcards(expanded))
                {
                    if (seen.Add(dir))
                    {
                        results.Add(dir);
                        if (results.Count >= MaxResults)
                        {
                            return results;
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>Replaces tokens; %ProgramFiles% and %CommonProgramFiles% yield both the 64-bit and the 32-bit folder.</summary>
    public static IEnumerable<string> ExpandTokens(string path, UserProfile? profile)
    {
        var value = path.Replace('/', '\\');
        if (profile is not null)
        {
            value = ReplaceToken(value, "%UserProfile%", profile.ProfilePath);
            value = ReplaceToken(value, "%AppData%", profile.AppData);
            value = ReplaceToken(value, "%LocalAppData%", profile.LocalAppData);
            value = ReplaceToken(value, "%LocalLowAppData%", profile.LocalLowAppData);
            value = ReplaceToken(value, "%Temp%", profile.Temp);
            value = ReplaceToken(value, "%Tmp%", profile.Temp);
            value = ReplaceToken(value, "%UserName%", profile.UserName);
            value = ReplaceToken(value, "%Documents%", Path.Combine(profile.ProfilePath, "Documents"));
            value = ReplaceToken(value, "%Desktop%", Path.Combine(profile.ProfilePath, "Desktop"));
            value = ReplaceToken(value, "%Pictures%", Path.Combine(profile.ProfilePath, "Pictures"));
            value = ReplaceToken(value, "%Music%", Path.Combine(profile.ProfilePath, "Music"));
            value = ReplaceToken(value, "%Videos%", Path.Combine(profile.ProfilePath, "Videos"));
            value = ReplaceToken(value, "%Video%", Path.Combine(profile.ProfilePath, "Videos"));
            value = ReplaceToken(value, "%Downloads%", Path.Combine(profile.ProfilePath, "Downloads"));
        }

        value = ReplaceToken(value, "%CommonAppData%", KnownFolders.ProgramData);
        value = ReplaceToken(value, "%AllUsersProfile%", KnownFolders.ProgramData);
        value = ReplaceToken(value, "%ProgramData%", KnownFolders.ProgramData);
        value = ReplaceToken(value, "%WinDir%", KnownFolders.Windows);
        value = ReplaceToken(value, "%SystemRoot%", KnownFolders.Windows);

        var variants = new List<string> { value };
        variants = Fork(variants, "%ProgramFiles%", KnownFolders.ProgramFiles, KnownFolders.ProgramFilesX86);
        variants = Fork(variants, "%CommonProgramFiles%", KnownFolders.CommonProgramFiles, KnownFolders.CommonProgramFilesX86);

        foreach (var variant in variants.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var expanded = Environment.ExpandEnvironmentVariables(variant);

            // Unknown token left: skip instead of guessing.
            if (!expanded.Contains('%'))
            {
                yield return expanded;
            }
        }
    }

    /// <summary>Resolves * and ? in folder names to existing folders (no reparse points).</summary>
    public static IEnumerable<string> ResolveWildcards(string path)
    {
        var trimmed = path.TrimEnd('\\');
        if (trimmed.IndexOfAny(['*', '?']) < 0)
        {
            if (Directory.Exists(trimmed) && !IsReparsePoint(trimmed))
            {
                yield return Normalize(trimmed);
            }

            yield break;
        }

        var segments = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments[0].IndexOfAny(['*', '?']) >= 0)
        {
            yield break;
        }

        var current = new List<string> { segments[0] + "\\" };
        for (var i = 1; i < segments.Length && current.Count > 0; i++)
        {
            var segment = segments[i];
            var next = new List<string>();
            foreach (var basePath in current)
            {
                if (segment.IndexOfAny(['*', '?']) >= 0)
                {
                    next.AddRange(MatchingChildren(basePath, segment));
                }
                else
                {
                    var candidate = Path.Combine(basePath, segment);
                    if (Directory.Exists(candidate) && !IsReparsePoint(candidate))
                    {
                        next.Add(candidate);
                    }
                }

                if (next.Count >= MaxResults)
                {
                    break;
                }
            }

            current = next;
        }

        foreach (var dir in current)
        {
            yield return Normalize(dir);
        }
    }

    public static string Normalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return full.Length > (root?.Length ?? 0) ? full.TrimEnd('\\', '/') : full;
        }
        catch (Exception)
        {
            return path.TrimEnd('\\', '/');
        }
    }

    public static bool IsReparsePoint(string path)
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

    private static IEnumerable<string> MatchingChildren(string basePath, string segment)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            return new FileSystemEnumerable<string>(basePath, (ref FileSystemEntry e) => e.ToFullPath(), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    e.IsDirectory && FileSystemName.MatchesSimpleExpression(segment, e.FileName)
            }.ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string ReplaceToken(string value, string token, string replacement) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase)
            ? value.Replace(token, replacement, StringComparison.OrdinalIgnoreCase)
            : value;

    private static List<string> Fork(List<string> values, string token, string first, string second)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(ReplaceToken(value, token, first));
                if (!string.IsNullOrEmpty(second))
                {
                    result.Add(ReplaceToken(value, token, second));
                }
            }
            else
            {
                result.Add(value);
            }
        }

        return result;
    }
}

/// <summary>Machine folders.</summary>
public static class KnownFolders
{
    public static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    public static string CommonProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);

    public static string CommonProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);

    public static string SystemDrive => Environment.GetEnvironmentVariable("SystemDrive") is { Length: > 0 } drive ? drive + "\\" : "C:\\";

    public static string UsersRoot =>
        Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\')) ?? "C:\\Users";
}
