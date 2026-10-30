namespace CLEANit.Core.Storage;

/// <summary>Parts of CLEANit's own data that can be reset in Settings › Reset CLEANit.</summary>
[Flags]
public enum ResetParts
{
    None = 0,

    /// <summary>Interface settings: language, theme, selections, exclusions, own profiles, folders (and the sign-in task).</summary>
    Settings = 1,

    /// <summary>Cleaning reports, the report index and the statistics (freed space, runs).</summary>
    Reports = 2,

    /// <summary>Program list cache and duplicate hash cache.</summary>
    Caches = 4,

    /// <summary>Backups (startup, services, Windows settings, Firefox history, event logs).</summary>
    Backups = 8,

    /// <summary>Own rule files and software bundles.</summary>
    RulesAndBundles = 16,

    /// <summary>Everything CLEANit stored – like a new installation.</summary>
    All = Settings | Reports | Caches | Backups | RulesAndBundles
}

/// <summary>
/// Deletes CLEANit's own data (never anything else). Runs at the start of a fresh CLEANit process (<c>--reset</c>) before
/// any file is opened, so nothing is locked and the old process cannot write its settings back.
/// </summary>
public static class AppDataReset
{
    public const string Argument = "--reset";

    public static string ToArgument(ResetParts parts) => $"{Argument}={(int)parts}";

    public static ResetParts Parse(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(Argument + "=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg.AsSpan(Argument.Length + 1), out var value))
            {
                return (ResetParts)value & ResetParts.All;
            }
        }

        return ResetParts.None;
    }

    /// <summary>Files and folders that belong to the given parts.</summary>
    public static IReadOnlyList<string> Targets(ResetParts parts, string dataDirectory, string settingsFile)
    {
        var list = new List<string>();
        if (parts == ResetParts.All)
        {
            list.Add(dataDirectory);
            list.Add(settingsFile);
            var settingsDir = Path.GetDirectoryName(settingsFile);
            if (settingsDir is not null && !string.Equals(settingsDir.TrimEnd('\\', '/'), dataDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                list.Add(settingsDir);
            }

            return list;
        }

        if (parts.HasFlag(ResetParts.Settings))
        {
            list.Add(settingsFile);
        }

        if (parts.HasFlag(ResetParts.Reports))
        {
            list.Add(Path.Combine(dataDirectory, "logs"));
            list.Add(Path.Combine(dataDirectory, "reports.db"));
            list.Add(Path.Combine(dataDirectory, "reports.db-wal"));
            list.Add(Path.Combine(dataDirectory, "reports.db-shm"));
            list.Add(Path.Combine(dataDirectory, "stats.json"));
            list.Add(Path.Combine(dataDirectory, "history.json"));
        }

        if (parts.HasFlag(ResetParts.Caches))
        {
            list.Add(Path.Combine(dataDirectory, "cache"));
        }

        if (parts.HasFlag(ResetParts.Backups))
        {
            list.Add(Path.Combine(dataDirectory, "backups"));
        }

        if (parts.HasFlag(ResetParts.RulesAndBundles))
        {
            list.Add(Path.Combine(dataDirectory, "rules"));
            list.Add(Path.Combine(dataDirectory, "bundles"));
        }

        return list;
    }

    /// <summary>Deletes the targets (permanently – it is CLEANit's own data). Returns the entries that could not be removed.</summary>
    public static IReadOnlyList<string> Run(ResetParts parts) => Run(parts, AppPaths.DataDirectory, AppPaths.SettingsFile);

    public static IReadOnlyList<string> Run(ResetParts parts, string dataDirectory, string settingsFile)
    {
        var failed = new List<string>();
        foreach (var target in Targets(parts, dataDirectory, settingsFile))
        {
            // Retry briefly: the previous CLEANit process may still be closing.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, recursive: true);
                    }
                    else if (File.Exists(target))
                    {
                        File.Delete(target);
                    }

                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= 5)
                    {
                        failed.Add(target);
                        break;
                    }

                    Thread.Sleep(400);
                }
            }
        }

        return failed;
    }
}
