using System.IO.Enumeration;
using CLEANit.Core.Models;

namespace CLEANit.Core.Rules;

/// <summary>Named rule selection. Rule ids may contain * (e.g. "browser.*.cache").</summary>
public sealed record SelectionProfile(string Id, string Name, string NameDe, IReadOnlyList<string> RuleIds)
{
    public bool BuiltIn { get; init; }

    public bool Expert { get; init; }

    /// <summary>"Recommended": the defaults of the rules (Safe rules marked as selected).</summary>
    public bool UseDefaults { get; init; }

    public string DisplayName(string language) => language == "de" ? NameDe : Name;

    public bool Includes(CleanRule rule) =>
        UseDefaults ? rule.Selected : RuleIds.Any(p => FileSystemName.MatchesSimpleExpression(p, rule.Id));
}

/// <summary>Built-in selection profiles (user profiles are stored in the settings).</summary>
public static class SelectionProfiles
{
    public const string RecommendedId = "recommended";
    public const string ImagingId = "imaging";
    public const string RdsId = "rds";

    public static IReadOnlyList<SelectionProfile> BuiltIn { get; } =
    [
        new(RecommendedId, "Recommended", "Empfohlen", []) { BuiltIn = true, UseDefaults = true },
        new("daily", "Daily", "Täglich",
        [
            "sys.temp.*", "sys.thumbcache", "sys.wer", "sys.crashdumps", "sys.inetcache", "media.d3dscache",
            "browser.*.cache", "dev.vscode", "dev.nuget.http", "dev.android.sdktemp", "dev.unity.logs"
        ]) { BuiltIn = true },
        new("developer", "Developer workstation", "Entwickler-Arbeitsplatz",
        [
            "sys.temp.*", "sys.wer", "sys.crashdumps", "browser.*.cache", "dev.*"
        ]) { BuiltIn = true },
        new(ImagingId, "Prepare master image (MCS/PVS)", "Master-Image vorbereiten (MCS/PVS)",
        [
            "sys.temp.*", "sys.thumbcache", "sys.wer", "sys.crashdumps", "sys.memorydumps", "sys.logs", "sys.deliveryoptimization",
            "sys.update.download", "sys.inetcache", "sys.recyclebin", "sys.dism.cleanup", "sys.eventlogs", "browser.*.cache"
        ]) { BuiltIn = true, Expert = true },
        new(RdsId, "Terminal server (RDS / Citrix VDA)", "Terminalserver (RDS / Citrix VDA)",
        [
            "sys.temp.*", "sys.thumbcache", "sys.wer", "sys.crashdumps", "sys.inetcache", "media.d3dscache",
            "browser.*.cache", "browser.*.serviceworker", "app.teams"
        ]) { BuiltIn = true, Expert = true }
    ];

    /// <summary>Built-in and user profiles (name → rule ids), expert profiles only in expert mode.</summary>
    public static IReadOnlyList<SelectionProfile> All(IReadOnlyDictionary<string, List<string>>? custom, bool expert) =>
        BuiltIn.Where(p => expert || !p.Expert)
            .Concat((custom ?? new Dictionary<string, List<string>>()).Select(c => new SelectionProfile("custom:" + c.Key, c.Key, c.Key, c.Value)))
            .ToList();

    /// <summary>Finds a profile by id or name (command line).</summary>
    public static SelectionProfile? Find(string idOrName, IReadOnlyDictionary<string, List<string>>? custom) =>
        All(custom, expert: true).FirstOrDefault(p =>
            p.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
            || p.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
            || p.NameDe.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
            || p.Id.Equals("custom:" + idOrName, StringComparison.OrdinalIgnoreCase));
}
