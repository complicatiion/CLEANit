using System.Text.Json.Serialization;

namespace CLEANit.Core.Models;

/// <summary>How much a rule can affect the system or the user.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskLevel>))]
public enum RiskLevel
{
    /// <summary>Pure temp/cache data, rebuilt silently.</summary>
    Safe,

    /// <summary>Rebuild or re-download needed, or the data may still be useful.</summary>
    Moderate,

    /// <summary>Not reversible or affects the user (sign-outs, Windows.old). Needs explicit confirmation.</summary>
    Deep
}

[JsonConverter(typeof(JsonStringEnumConverter<CleanArea>))]
public enum CleanArea
{
    System,
    Browser,
    Developer
}

[JsonConverter(typeof(JsonStringEnumConverter<RuleSource>))]
public enum RuleSource
{
    Builtin,
    Winapp2,
    Custom
}

/// <summary>Cleanup that is not plain file deletion; runs through official Windows tools only.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CleanAction>))]
public enum CleanAction
{
    None,
    EmptyRecycleBin,
    DismComponentCleanup,
    DismResetBase,
    RemoveWindowsOld,

    /// <summary>Firefox history via SQLite: visits and unbookmarked pages; bookmarks stay.</summary>
    FirefoxHistory,

    /// <summary>Back up (.evtx) and clear the classic event logs.</summary>
    ClearEventLogs,

    /// <summary>Windows clipboard history (Win+V).</summary>
    ClearClipboardHistory
}

[JsonConverter(typeof(JsonStringEnumConverter<DeleteMode>))]
public enum DeleteMode
{
    Permanent,
    RecycleBin
}
