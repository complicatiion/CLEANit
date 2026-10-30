using System.Reflection;
using System.Text.Json;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using CLEANit.Core.Storage;
using CLEANit.Core.Winapp2;

namespace CLEANit.Core.Rules;

public sealed class RuleCatalogOptions
{
    public bool IncludeWinapp2 { get; init; } = true;

    /// <summary>Own Winapp2.ini (imported by the user); null = bundled file.</summary>
    public string? Winapp2Path { get; init; }

    public bool AllProfiles { get; init; }

    /// <summary>Expert rules (event logs, DISM reset base …) are hidden otherwise.</summary>
    public bool IncludeExpert { get; init; }
}

/// <summary>All rules that apply to this PC: built-in, custom JSON and detected Winapp2 entries.</summary>
public sealed class RuleCatalog
{
    private RuleCatalog(List<CleanRule> rules, int winapp2Parsed, List<string> errors)
    {
        Rules = rules;
        Winapp2Parsed = winapp2Parsed;
        Errors = errors;
    }

    public IReadOnlyList<CleanRule> Rules { get; }

    public int Winapp2Parsed { get; }

    public int Winapp2Detected => Rules.Count(r => r.Source == RuleSource.Winapp2);

    public IReadOnlyList<string> Errors { get; }

    public IEnumerable<CleanRule> For(CleanArea area) => Rules.Where(r => r.Area == area);

    public static RuleCatalog Load(PlatformServices platform, RuleCatalogOptions options)
    {
        var errors = new List<string>();
        var profiles = options.AllProfiles ? platform.Profiles.All() : [platform.Profiles.Current];
        var rules = new List<CleanRule>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in LoadBuiltin(errors).Concat(LoadCustom(errors)))
        {
            if (rule.Expert && !options.IncludeExpert)
            {
                continue;
            }

            if (ids.Add(rule.Id) && IsDetected(rule, profiles, platform.Registry, requireTargets: false))
            {
                rules.Add(rule);
            }
        }

        var parsed = 0;
        if (options.IncludeWinapp2)
        {
            var file = options.Winapp2Path is { Length: > 0 } custom && File.Exists(custom) ? custom : AppPaths.BundledWinapp2;
            if (File.Exists(file))
            {
                try
                {
                    var known = new HashSet<string>(rules.SelectMany(r => r.Targets).Select(t => NormalizeRulePath(t.Path)),
                        StringComparer.OrdinalIgnoreCase);
                    var community = Winapp2Parser.ParseFile(file);
                    parsed = community.Count;
                    foreach (var rule in community)
                    {
                        var unique = Deduplicate(rule, known);
                        if (unique is not null && ids.Add(unique.Id) && IsDetected(unique, profiles, platform.Registry, requireTargets: true))
                        {
                            rules.Add(unique);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Winapp2.ini: {ex.Message}");
                }
            }
        }

        return new RuleCatalog(rules, parsed, errors);
    }

    public static IReadOnlyList<CleanRule> LoadBuiltin(List<string>? errors = null)
    {
        var rules = new List<CleanRule>();
        var assembly = typeof(RuleCatalog).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("CLEANit.Rules.", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is not null)
            {
                rules.AddRange(ReadRuleFile(stream, name, RuleSource.Builtin, errors));
            }
        }

        return rules;
    }

    /// <summary>Own rule files in %ProgramData%\CLEANit\rules (never selected by default).</summary>
    public static IReadOnlyList<CleanRule> LoadCustom(List<string>? errors = null)
    {
        var rules = new List<CleanRule>();
        if (!Directory.Exists(AppPaths.CustomRulesDirectory))
        {
            return rules;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.CustomRulesDirectory, "*.json").Order())
        {
            try
            {
                using var stream = File.OpenRead(file);
                rules.AddRange(ReadRuleFile(stream, Path.GetFileName(file), RuleSource.Custom, errors));
            }
            catch (Exception ex)
            {
                errors?.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return rules;
    }

    private const string EditorPrefix = "editor-";

    /// <summary>Rules created in the rule editor (editor-system.json, editor-browser.json, editor-developer.json).</summary>
    public static IReadOnlyList<CleanRule> LoadEditorRules()
    {
        var rules = new List<CleanRule>();
        if (!Directory.Exists(AppPaths.CustomRulesDirectory))
        {
            return rules;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.CustomRulesDirectory, EditorPrefix + "*.json"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                rules.AddRange(ReadRuleFile(stream, Path.GetFileName(file), RuleSource.Custom, null));
            }
            catch (Exception)
            {
                // Broken file: ignored here, reported by the catalog.
            }
        }

        return rules;
    }

    /// <summary>Writes the editor rules, one file per area. Returns validation problems (nothing written when not empty).</summary>
    public static IReadOnlyList<string> SaveEditorRules(IReadOnlyList<CleanRule> rules)
    {
        var problems = rules.Select(r => (r.Id, Problem: Validate(r))).Where(p => p.Problem is not null).Select(p => $"{p.Id}: {p.Problem}").ToList();
        problems.AddRange(rules.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => $"{g.Key}: duplicate id"));
        if (problems.Count > 0)
        {
            return problems;
        }

        Directory.CreateDirectory(AppPaths.CustomRulesDirectory);
        foreach (var area in Enum.GetValues<CleanArea>())
        {
            var file = Path.Combine(AppPaths.CustomRulesDirectory, $"{EditorPrefix}{area.ToString().ToLowerInvariant()}.json");
            var list = rules.Where(r => r.Area == area).ToList();
            if (list.Count == 0)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }

                continue;
            }

            JsonFile.Write(file, new RuleFile { Area = area, Rules = list });
        }

        return [];
    }

    internal static IReadOnlyList<CleanRule> ReadRuleFile(Stream stream, string name, RuleSource source, List<string>? errors)
    {
        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(stream, CleanitJson.Options);
        }
        catch (JsonException ex)
        {
            errors?.Add($"{name}: {ex.Message}");
            return [];
        }

        var valid = new List<CleanRule>();
        foreach (var rule in file?.Rules ?? [])
        {
            var problem = source != RuleSource.Builtin && (rule.Action != CleanAction.None || rule.Services.Count > 0)
                ? "actions and services are reserved for built-in rules"
                : Validate(rule);
            if (problem is not null)
            {
                errors?.Add($"{name} / {rule.Id}: {problem}");
                continue;
            }

            rule.Area = file!.Area;
            rule.Source = source;
            valid.Add(source == RuleSource.Builtin || !rule.Selected ? rule : Unselected(rule));
        }

        return valid;
    }

    internal static string? Validate(CleanRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Name))
        {
            return "id and name are required";
        }

        if (rule.Action == CleanAction.None && rule.Targets.Count == 0)
        {
            return "no targets";
        }

        if (rule.Targets.Any(t => string.IsNullOrWhiteSpace(t.Path) || t.Patterns.Count == 0))
        {
            return "target without path or pattern";
        }

        // Only Safe rules may be preselected.
        return rule.Selected && rule.Risk != RiskLevel.Safe ? "only Safe rules may be selected by default" : null;
    }

    private static CleanRule Unselected(CleanRule rule) => new()
    {
        Id = rule.Id, Name = rule.Name, NameDe = rule.NameDe, Description = rule.Description, DescriptionDe = rule.DescriptionDe,
        Area = rule.Area, Category = rule.Category, CategoryDe = rule.CategoryDe, Risk = rule.Risk, Selected = false,
        Targets = rule.Targets, Detect = rule.Detect, DetectRegistry = rule.DetectRegistry, Excludes = rule.Excludes,
        Processes = rule.Processes, Services = rule.Services, Action = rule.Action, DeleteMode = rule.DeleteMode,
        Warning = rule.Warning, WarningDe = rule.WarningDe, Source = rule.Source
    };

    /// <summary>Removes Winapp2 targets that a built-in rule already covers.</summary>
    private static CleanRule? Deduplicate(CleanRule rule, HashSet<string> known)
    {
        var targets = rule.Targets.Where(t => !known.Contains(NormalizeRulePath(t.Path))).ToList();
        if (targets.Count == 0)
        {
            return null;
        }

        if (targets.Count == rule.Targets.Count)
        {
            return rule;
        }

        return new CleanRule
        {
            Id = rule.Id, Name = rule.Name, Description = rule.Description, DescriptionDe = rule.DescriptionDe, Area = rule.Area,
            Category = rule.Category, CategoryDe = rule.CategoryDe, Risk = rule.Risk, Targets = targets, Detect = rule.Detect,
            DetectRegistry = rule.DetectRegistry, Excludes = rule.Excludes, Warning = rule.Warning, Source = rule.Source
        };
    }

    private static string NormalizeRulePath(string path) => path.Replace('/', '\\').TrimEnd('\\');

    /// <summary>Detect keys/paths decide; without them the rule applies when a target folder exists.</summary>
    internal static bool IsDetected(CleanRule rule, IReadOnlyList<UserProfile> profiles, IRegistryProbe? registry, bool requireTargets)
    {
        if (rule.Action != CleanAction.None)
        {
            return true;
        }

        if (rule.Detect.Count > 0 || rule.DetectRegistry.Count > 0)
        {
            if (registry is not null && rule.DetectRegistry.Any(k => SafeKeyExists(registry, k)))
            {
                return true;
            }

            return rule.Detect.Any(d => PathExists(d, profiles));
        }

        return !requireTargets || rule.Targets.Any(t => PathExpander.Resolve(t.Path, profiles).Count > 0);
    }

    private static bool SafeKeyExists(IRegistryProbe registry, string key)
    {
        try
        {
            return registry.KeyExists(key);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>File or folder exists; folder segments and the last segment may contain wildcards.</summary>
    internal static bool PathExists(string path, IReadOnlyList<UserProfile> profiles)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        if (PathExpander.Resolve(normalized, profiles).Count > 0)
        {
            return true;
        }

        var parent = Path.GetDirectoryName(normalized);
        var leaf = Path.GetFileName(normalized);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
        {
            return false;
        }

        foreach (var dir in PathExpander.Resolve(parent, profiles))
        {
            try
            {
                if (leaf.IndexOfAny(['*', '?']) >= 0 ? Directory.EnumerateFileSystemEntries(dir, leaf).Any() : File.Exists(Path.Combine(dir, leaf)))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Access denied.
            }
        }

        return false;
    }
}
