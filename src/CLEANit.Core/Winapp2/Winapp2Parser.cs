using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;

namespace CLEANit.Core.Winapp2;

/// <summary>Raw [Entry *] section of Winapp2.ini.</summary>
public sealed class Winapp2Entry
{
    public required string Name { get; init; }

    public List<KeyValuePair<string, string>> Keys { get; } = [];

    public IEnumerable<string> Values(string key) =>
        Keys.Where(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(k => k.Value);

    public string? Value(string key) => Values(key).FirstOrDefault();
}

/// <summary>
/// Reads Winapp2.ini (CC BY-SA 4.0, used unmodified) and converts entries into rules at runtime.
/// Registry keys (RegKey) are ignored – CLEANit never cleans the registry. Detect keys are only read.
/// Entries for passwords, autofill, bookmarks and sync data are dropped; cookies, history and sessions become Deep.
/// </summary>
public static class Winapp2Parser
{
    private static readonly string[] SensitiveNames =
        ["Password", "Autofill", "Autocomplete", "Bookmark", "Sync Data", "Synced", "Login", "Credential", "Wallet", "Keychain"];

    private static readonly string[] DeepNames =
        ["Cookie", "History", "Session", "Web Storage", "Pinned Tabs", "Storage Quota", "Backup", "Installation Packages", "Installer"];

    /// <summary>Browsers CLEANit cleans with its own rules; their Winapp2 entries would only duplicate them.</summary>
    private static readonly string[] NativeBrowserSections =
    [
        "Google Chrome Web Browser", "Microsoft Edge Web Browser", "Brave Web Browser", "Brave Origin Web Browser",
        "Vivaldi Web Browser", "Opera Web Browser", "Opera GX Web Browser", "Chromium Web Browser", "Mozilla Firefox Web Browser"
    ];

    private static readonly string[] DeveloperKeywords =
    [
        "Visual Studio", "VS Code", "VSCode", "JetBrains", "IntelliJ", "PyCharm", "Rider", "WebStorm", "CLion", "GoLand",
        "Android Studio", "Android SDK", "Gradle", "NuGet", "npm", "Node.js", "Yarn", "pnpm", "Python", "Git ", "GitHub",
        "Docker", "Unity", "Unreal Engine", "Eclipse", "NetBeans", "Xamarin", "MSBuild", "Postman", "Sublime Text"
    ];

    private static readonly Regex DeveloperPattern = new(
        @"(?<![\w.])(" + string.Join("|", DeveloperKeywords.Select(k => Regex.Escape(k.Trim()))) + @")(?![\w])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, (string En, string De)> LangSections = new()
    {
        ["3021"] = ("Applications", "Anwendungen"),
        ["3022"] = ("Internet", "Internet"),
        ["3023"] = ("Multimedia", "Multimedia"),
        ["3024"] = ("Utilities", "Dienstprogramme"),
        ["3025"] = ("Windows", "Windows")
    };

    public static IReadOnlyList<Winapp2Entry> ReadEntries(TextReader reader)
    {
        var entries = new List<Winapp2Entry>();
        Winapp2Entry? current = null;
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1].Trim();
                if (name.EndsWith(" *", StringComparison.Ordinal))
                {
                    name = name[..^2].TrimEnd();
                }

                current = new Winapp2Entry { Name = name };
                entries.Add(current);
                continue;
            }

            var eq = line.IndexOf('=');
            if (current is null || eq <= 0)
            {
                continue;
            }

            // FileKey12 → FileKey
            var key = line[..eq].Trim().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            current.Keys.Add(new(key, line[(eq + 1)..].Trim()));
        }

        return entries;
    }

    public static IReadOnlyList<CleanRule> Parse(TextReader reader) =>
        ReadEntries(reader).Select(ToRule).OfType<CleanRule>().ToList();

    public static IReadOnlyList<CleanRule> ParseFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    /// <summary>Converts one entry; null when it has no file keys or touches sensitive data.</summary>
    public static CleanRule? ToRule(Winapp2Entry entry)
    {
        if (SensitiveNames.Any(s => entry.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var section = entry.Value("Section");
        if (section is not null && NativeBrowserSections.Any(b => b.Equals(section, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var targets = new List<CleanTarget>();
        foreach (var fileKey in entry.Values("FileKey"))
        {
            var target = ParseFileKey(fileKey);
            if (target is not null)
            {
                targets.Add(target);
            }
        }

        if (targets.Count == 0)
        {
            return null;
        }

        var warning = entry.Value("Warning");
        // REMOVESELF is how Winapp2 removes whole cache folders – Moderate like every community rule.
        var deep = warning is not null || DeepNames.Any(d => entry.Name.Contains(d, StringComparison.OrdinalIgnoreCase));

        var isBrowser = section?.Contains("Browser", StringComparison.OrdinalIgnoreCase) == true;
        var isDeveloper = !isBrowser && (DeveloperPattern.IsMatch(entry.Name) || (section is not null && DeveloperPattern.IsMatch(section)));

        var lang = entry.Value("LangSecRef");
        var (categoryEn, categoryDe) = section is not null
            ? (CleanCategory(section), CleanCategory(section))
            : lang is not null && LangSections.TryGetValue(lang, out var names) ? names : ("Applications", "Anwendungen");

        return new CleanRule
        {
            Id = "winapp2." + Slug(entry.Name),
            Name = entry.Name,
            Description = "Community rule from Winapp2.ini (CC BY-SA 4.0).",
            DescriptionDe = "Community-Regel aus Winapp2.ini (CC BY-SA 4.0).",
            Area = isBrowser ? CleanArea.Browser : isDeveloper ? CleanArea.Developer : CleanArea.System,
            Category = categoryEn,
            CategoryDe = categoryDe,
            Risk = deep ? RiskLevel.Deep : RiskLevel.Moderate,
            Selected = false,
            Targets = targets,
            Detect = entry.Values("DetectFile").ToList(),
            DetectRegistry = entry.Values("Detect").ToList(),
            Excludes = entry.Values("ExcludeKey").Select(ParseExclude).OfType<string>().ToList(),
            Warning = warning,
            Source = RuleSource.Winapp2
        };
    }

    /// <summary>"path|masks|RECURSE" or "path|masks|REMOVESELF".</summary>
    internal static CleanTarget? ParseFileKey(string value)
    {
        var parts = value.Split('|');
        var path = parts[0].Trim().TrimEnd('\\');
        if (path.Length == 0)
        {
            return null;
        }

        var masks = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : "*";
        var flag = parts.Length > 2 ? parts[2].Trim() : "";
        var removeSelf = flag.Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase);
        var recurse = removeSelf || flag.Equals("RECURSE", StringComparison.OrdinalIgnoreCase);

        // Drop masks that aim at protected files (passwords, bookmarks, profile keys).
        var kept = masks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => m is "*" or "*.*" || !PathGuard.ProtectedNames.Any(n => FileSystemName.MatchesSimpleExpression(m, n)))
            .ToList();
        if (kept.Count == 0)
        {
            return null;
        }

        return new CleanTarget
        {
            Path = path,
            Pattern = string.Join(';', kept),
            Recursive = recurse,
            RemoveSelf = removeSelf,
            RemoveEmptyDirs = recurse
        };
    }

    /// <summary>"FILE|path\|mask" or "PATH|path\|mask" → "path|mask"; registry excludes are ignored.</summary>
    internal static string? ParseExclude(string value)
    {
        var parts = value.Split('|');
        if (parts.Length < 2 || parts[0].Trim().Equals("REG", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = parts[1].Trim().TrimEnd('\\');
        var mask = parts.Length > 2 ? parts[2].Trim() : "";
        return path.Length == 0 ? null : $"{path}|{mask}";
    }

    private static string CleanCategory(string section) =>
        section.Replace(" Web Browser", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Web browser", "", StringComparison.OrdinalIgnoreCase).Trim();

    internal static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        return sb.ToString().Trim('-');
    }
}
