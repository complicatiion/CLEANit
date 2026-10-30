using System.Text.Json.Serialization;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;

namespace CLEANit.Core.Tweaks;

[JsonConverter(typeof(JsonStringEnumConverter<RegHive>))]
public enum RegHive
{
    CurrentUser,
    LocalMachine
}

/// <summary>What has to happen before a change is visible.</summary>
public enum TweakEffect
{
    Immediately,
    RestartExplorer,
    SignOut,
    Restart
}

/// <summary>One registry value a tweak writes (DWORD values only – every switch in the catalog is a DWORD).</summary>
public sealed record TweakValue(RegHive Hive, string Key, string Name, int Value)
{
    public bool IsPolicy => Key.StartsWith(@"SOFTWARE\Policies\", StringComparison.OrdinalIgnoreCase);

    public string Display => $@"{(Hive == RegHive.CurrentUser ? "HKCU" : "HKLM")}\{Key}\{Name} = {Value}";
}

/// <summary>
/// A documented Windows setting. "On" means the tweak is applied (e.g. transparency effects off).
/// Every switch is a value Microsoft documents (policy CSP / ADMX / Edge policy) or the value the Windows Settings page itself writes.
/// </summary>
/// <summary>Editions a switch is offered on (some policies only work on Enterprise/Education).</summary>
public enum TweakEdition
{
    All,
    EnterpriseOnly,
    NonEnterprise
}

public sealed record TweakDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string NameDe { get; init; }

    public required string Description { get; init; }

    public required string DescriptionDe { get; init; }

    public required string Category { get; init; }

    public required string CategoryDe { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    /// <summary>Part of the "Recommended" selection.</summary>
    public bool Recommended { get; init; }

    public TweakEffect Effect { get; init; } = TweakEffect.Immediately;

    /// <summary>Microsoft documentation of the setting.</summary>
    public required string DocUrl { get; init; }

    /// <summary>Source shown in the UI: "Policy" (documented policy) or "Settings" (value of the Windows Settings switch).</summary>
    public string Source { get; init; } = "Policy";

    public int MinBuild { get; init; }

    public TweakEdition Editions { get; init; } = TweakEdition.All;

    public int MaxBuild { get; init; } = int.MaxValue;

    /// <summary>Values for normal editions; <see cref="EnterpriseValues"/> replaces them on Enterprise/Education/Server.</summary>
    public required IReadOnlyList<TweakValue> Values { get; init; }

    public IReadOnlyList<TweakValue>? EnterpriseValues { get; init; }

    /// <summary>Optional note (edition limits, side effects), EN and DE.</summary>
    public string? Note { get; init; }

    public string? NoteDe { get; init; }

    public string DisplayName(string lang) => lang == "de" ? NameDe : Name;

    public string DisplayDescription(string lang) => lang == "de" ? DescriptionDe : Description;

    public string DisplayCategory(string lang) => lang == "de" ? CategoryDe : Category;

    public string? DisplayNote(string lang) => lang == "de" ? NoteDe ?? Note : Note;

    public bool AppliesTo(int build) => build == 0 || (build >= MinBuild && build <= MaxBuild);

    public bool AppliesTo(int build, bool enterprise) =>
        AppliesTo(build) && Editions switch
        {
            TweakEdition.EnterpriseOnly => enterprise,
            TweakEdition.NonEnterprise => !enterprise,
            _ => true
        };

    public IReadOnlyList<TweakValue> ValuesFor(bool enterprise) => enterprise && EnterpriseValues is not null ? EnterpriseValues : Values;
}

/// <summary>Registry access used by the tweak engine (Windows registry in the app, in-memory in tests).</summary>
public interface IRegistryStore
{
    /// <summary>Current DWORD value or null when missing / not a DWORD.</summary>
    int? Read(RegHive hive, string key, string name);

    bool Exists(RegHive hive, string key, string name);

    void Write(RegHive hive, string key, string name, int value);

    void Delete(RegHive hive, string key, string name);
}

/// <summary>State of a value before CLEANit changed it.</summary>
public sealed record TweakBackupValue(string TweakId, RegHive Hive, string Key, string Name, bool Existed, int? Value);

public sealed record TweakBackup(DateTime CreatedUtc, string Machine, string User, List<TweakBackupValue> Values);

/// <summary>
/// Applies and reverts tweaks. Before every change the previous values are backed up (backups\tweaks\*.json);
/// reverting restores the backed-up value (or removes a value CLEANit created).
/// </summary>
public sealed class TweakEngine(IRegistryStore registry, bool enterprise, string? backupDirectory = null)
{
    public string BackupDirectory { get; } = backupDirectory ?? Path.Combine(AppPaths.BackupsDirectory, "tweaks");

    public bool Enterprise { get; } = enterprise;

    public bool IsApplied(TweakDefinition tweak) =>
        tweak.ValuesFor(Enterprise).All(v => registry.Read(v.Hive, v.Key, v.Name) == v.Value);

    /// <summary>Some values are set, but not to the tweak's values (e.g. by a group policy or another tool).</summary>
    public bool IsChangedElsewhere(TweakDefinition tweak) =>
        !IsApplied(tweak) && tweak.ValuesFor(Enterprise).Any(v => registry.Exists(v.Hive, v.Key, v.Name));

    /// <summary>
    /// Backs up, then writes. Returns the backup file (null when nothing had to change) and the tweaks that were skipped
    /// because a value exists with another type (never overwritten).
    /// </summary>
    public (string? BackupFile, IReadOnlyList<TweakDefinition> Skipped) Apply(IReadOnlyList<TweakDefinition> tweaks, string user)
    {
        var skipped = tweaks.Where(t => t.ValuesFor(Enterprise).Any(v => registry.Exists(v.Hive, v.Key, v.Name) && registry.Read(v.Hive, v.Key, v.Name) is null))
            .ToList();
        tweaks = tweaks.Except(skipped).ToList();
        var backup = new TweakBackup(DateTime.UtcNow, Environment.MachineName, user, []);
        foreach (var tweak in tweaks)
        {
            foreach (var value in tweak.ValuesFor(Enterprise))
            {
                var current = registry.Read(value.Hive, value.Key, value.Name);
                if (current == value.Value)
                {
                    continue;
                }

                backup.Values.Add(new TweakBackupValue(tweak.Id, value.Hive, value.Key, value.Name, registry.Exists(value.Hive, value.Key, value.Name), current));
            }
        }

        if (backup.Values.Count == 0)
        {
            return (null, skipped);
        }

        // The backup is written first: no change without a way back.
        var file = Path.Combine(BackupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
        JsonFile.Write(file, backup);
        foreach (var tweak in tweaks)
        {
            foreach (var value in tweak.ValuesFor(Enterprise))
            {
                registry.Write(value.Hive, value.Key, value.Name, value.Value);
            }
        }

        return (file, skipped);
    }

    /// <summary>Reverts tweaks to the state before CLEANit (latest backup per value); values without a backup are removed.</summary>
    public void Revert(IReadOnlyList<TweakDefinition> tweaks)
    {
        var backups = ReadBackups();
        foreach (var tweak in tweaks)
        {
            foreach (var value in tweak.ValuesFor(Enterprise))
            {
                // Oldest backup of this value = state before CLEANit touched it.
                var original = backups.OrderBy(b => b.Backup.CreatedUtc)
                    .SelectMany(b => b.Backup.Values)
                    .FirstOrDefault(v => v.Hive == value.Hive && v.Key.Equals(value.Key, StringComparison.OrdinalIgnoreCase)
                                                             && v.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
                RestoreValue(original ?? new TweakBackupValue(tweak.Id, value.Hive, value.Key, value.Name, false, null));
            }
        }
    }

    /// <summary>Restores every value of one backup file.</summary>
    public void Restore(TweakBackup backup)
    {
        foreach (var value in backup.Values)
        {
            RestoreValue(value);
        }
    }

    public IReadOnlyList<(string File, TweakBackup Backup)> ReadBackups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        var list = new List<(string, TweakBackup)>();
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json").OrderDescending())
        {
            try
            {
                if (JsonFile.Read<TweakBackup>(file) is { } backup)
                {
                    list.Add((file, backup));
                }
            }
            catch (Exception)
            {
                // Broken backup.
            }
        }

        return list;
    }

    private void RestoreValue(TweakBackupValue value)
    {
        if (value.Existed && value.Value is { } previous)
        {
            registry.Write(value.Hive, value.Key, value.Name, previous);
        }
        else if (!value.Existed)
        {
            registry.Delete(value.Hive, value.Key, value.Name);
        }
    }
}
