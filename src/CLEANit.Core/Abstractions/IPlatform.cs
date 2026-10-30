using CLEANit.Core.Models;

namespace CLEANit.Core.Abstractions;

/// <summary>Read-only registry check (rule detection only – CLEANit never cleans the registry).</summary>
public interface IRegistryProbe
{
    /// <summary>Key like HKLM\Software\Vendor (HKLM, HKCU, HKCR, HKU; both registry views).</summary>
    bool KeyExists(string key);
}

/// <summary>Stops services for a rule and restores their original state.</summary>
public interface IServiceControl
{
    /// <summary>Stops the service if running; returns true when it was running before.</summary>
    bool StopIfRunning(string name, TimeSpan timeout);

    void Start(string name, TimeSpan timeout);
}

public interface IRecycleBin
{
    /// <summary>Moves files or folders to the recycle bin; returns the number of paths that failed.</summary>
    int Recycle(IReadOnlyList<string> paths);

    (long Bytes, long Items) Query();

    bool Empty();
}

public sealed record ActionEstimate(long Bytes, long Items, bool Unknown, bool Available);

/// <summary>Cleanup through official Windows tools (recycle bin API, DISM, Windows.old).</summary>
public interface IActionRunner
{
    ActionEstimate Estimate(CleanAction action);

    /// <summary>Returns freed bytes (0 when unknown) or throws.</summary>
    long Run(CleanAction action, CancellationToken ct);
}

/// <summary>User profile folders for multi-user cleaning (terminal servers).</summary>
public sealed record UserProfile(string UserName, string ProfilePath)
{
    public string AppData { get; init; } = Path.Combine(ProfilePath, "AppData", "Roaming");

    public string LocalAppData { get; init; } = Path.Combine(ProfilePath, "AppData", "Local");

    public string LocalLowAppData { get; init; } = Path.Combine(ProfilePath, "AppData", "LocalLow");

    public string Temp { get; init; } = Path.Combine(ProfilePath, "AppData", "Local", "Temp");
}

public interface IProfileProvider
{
    UserProfile Current { get; }

    /// <summary>All local user profiles (no system or default profiles).</summary>
    IReadOnlyList<UserProfile> All();
}

/// <summary>Platform services bundled for the engine.</summary>
public sealed class PlatformServices
{
    public required IProfileProvider Profiles { get; init; }

    public IRegistryProbe? Registry { get; init; }

    public IServiceControl? Services { get; init; }

    public IRecycleBin? RecycleBin { get; init; }

    public IActionRunner? Actions { get; init; }
}
