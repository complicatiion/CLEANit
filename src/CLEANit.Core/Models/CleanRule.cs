using System.Text.Json.Serialization;

namespace CLEANit.Core.Models;

/// <summary>One folder (with optional wildcards) and the files to remove in it.</summary>
public sealed class CleanTarget
{
    /// <summary>Folder path; supports %Tokens% and * / ? in folder names.</summary>
    public string Path { get; init; } = "";

    /// <summary>File masks separated by ';'.</summary>
    public string Pattern { get; init; } = "*";

    public bool Recursive { get; init; } = true;

    /// <summary>Remove empty sub folders after cleaning (never the target folder itself).</summary>
    public bool RemoveEmptyDirs { get; init; } = true;

    /// <summary>Remove the target folder itself as well (Winapp2 REMOVESELF).</summary>
    public bool RemoveSelf { get; init; }

    /// <summary>Only files not modified for at least this many hours.</summary>
    public int? MinAgeHours { get; init; }

    /// <summary>File masks or relative paths (with *) that are never touched.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> Patterns =>
        Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>"*" or "*.*": everything in the folder.</summary>
    [JsonIgnore]
    public bool IsBroad => RemoveSelf || Patterns.Any(p => p is "*" or "*.*");
}

/// <summary>Cleaning rule (built-in JSON, Winapp2 entry or custom JSON).</summary>
public sealed class CleanRule
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string? NameDe { get; init; }

    public string Description { get; init; } = "";

    public string? DescriptionDe { get; init; }

    public CleanArea Area { get; set; }

    public string Category { get; init; } = "";

    public string? CategoryDe { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    /// <summary>Selected by default (only Safe rules should be).</summary>
    public bool Selected { get; init; }

    public IReadOnlyList<CleanTarget> Targets { get; init; } = [];

    /// <summary>Rule applies only if one of these paths exists (empty = check targets).</summary>
    public IReadOnlyList<string> Detect { get; init; } = [];

    /// <summary>Registry keys for detection (read only, Winapp2 Detect).</summary>
    public IReadOnlyList<string> DetectRegistry { get; init; } = [];

    /// <summary>Excluded files: "path|mask" with tokens (mask empty = whole folder).</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>Process names (without .exe) that must not run while cleaning.</summary>
    public IReadOnlyList<string> Processes { get; init; } = [];

    /// <summary>Services stopped during cleaning and restored afterwards.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    public CleanAction Action { get; init; } = CleanAction.None;

    public DeleteMode DeleteMode { get; init; } = DeleteMode.Permanent;

    public string? Warning { get; init; }

    public string? WarningDe { get; init; }

    public RuleSource Source { get; set; } = RuleSource.Builtin;

    /// <summary>Only shown in expert mode (admin and imaging tasks).</summary>
    public bool Expert { get; init; }

    public string DisplayName(string language) => language == "de" && !string.IsNullOrEmpty(NameDe) ? NameDe : Name;

    public string DisplayDescription(string language) =>
        language == "de" && !string.IsNullOrEmpty(DescriptionDe) ? DescriptionDe : Description;

    public string DisplayCategory(string language) =>
        language == "de" && !string.IsNullOrEmpty(CategoryDe) ? CategoryDe : Category;

    public string? DisplayWarning(string language) =>
        language == "de" && !string.IsNullOrEmpty(WarningDe) ? WarningDe : Warning;

    public override string ToString() => $"{Id} ({Risk})";
}

/// <summary>Rule file layout (rules/*.json).</summary>
public sealed class RuleFile
{
    public CleanArea Area { get; init; }

    public IReadOnlyList<CleanRule> Rules { get; init; } = [];
}
