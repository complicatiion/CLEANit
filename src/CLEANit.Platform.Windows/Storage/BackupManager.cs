using CLEANit.Core.Storage;
using CLEANit.Core.Tweaks;

namespace CLEANit.Platform.Windows;

/// <summary>Backup made by CLEANit before a change (startup entry, service, Windows setting, Firefox history, event logs).</summary>
public sealed record BackupItem(string Kind, string Title, string Path, IReadOnlyList<string> Files, long Bytes, DateTime TimeUtc, bool CanRestore);

/// <summary>Lists, restores and deletes the backups in data\backups.</summary>
public static class BackupManager
{
    public static IReadOnlyList<BackupItem> List()
    {
        var items = new List<BackupItem>();
        foreach (var (file, backup) in StartupManager.ReadBackups())
        {
            var files = backup.FileCopy is { } copy && File.Exists(copy) ? new[] { file, copy } : [file];
            items.Add(new BackupItem("startup", backup.Name, file, files, Size(files), backup.DeletedUtc, true));
        }

        foreach (var (file, backup) in ServicesManager.ReadBackups())
        {
            items.Add(new BackupItem("services", backup.DisplayName, file, [file], Size([file]), backup.ChangedUtc, true));
        }

        foreach (var (file, backup) in TweakEngineFactory.Create().ReadBackups())
        {
            items.Add(new BackupItem("tweaks", $"{backup.Values.Select(v => v.TweakId).Distinct().Count()} × {string.Join(", ", backup.Values.Select(v => v.TweakId).Distinct().Take(3))}",
                file, [file], Size([file]), backup.CreatedUtc, true));
        }

        foreach (var kind in new[] { "firefox", "eventlogs" })
        {
            var root = System.IO.Path.Combine(AppPaths.BackupsDirectory, kind);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                items.Add(new BackupItem(kind, System.IO.Path.GetFileName(dir), dir, files, Size(files), Directory.GetCreationTimeUtc(dir), false));
            }
        }

        return items.OrderByDescending(i => i.TimeUtc).ToList();
    }

    public static bool Restore(BackupItem item)
    {
        switch (item.Kind)
        {
            case "startup":
                return StartupManager.ReadBackups().FirstOrDefault(b => b.File == item.Path) is { Backup: { } s } && StartupManager.Restore(item.Path, s);
            case "services":
                return ServicesManager.ReadBackups().FirstOrDefault(b => b.File == item.Path) is { Backup: { } v } && ServicesManager.Restore(item.Path, v);
            case "tweaks":
                var engine = TweakEngineFactory.Create();
                if (engine.ReadBackups().FirstOrDefault(b => b.File == item.Path) is { Backup: { } t })
                {
                    engine.Restore(t);
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>Deletes the files of a backup (only inside the backup folder).</summary>
    public static bool Delete(BackupItem item)
    {
        var root = System.IO.Path.GetFullPath(AppPaths.BackupsDirectory).TrimEnd('\\') + "\\";
        try
        {
            if (Directory.Exists(item.Path) && System.IO.Path.GetFullPath(item.Path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(item.Path, recursive: true);
                return true;
            }

            foreach (var file in item.Files.Where(f => System.IO.Path.GetFullPath(f).StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(file);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Deletes backups older than the given number of days; returns how many were removed.</summary>
    public static int DeleteOlderThan(int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        return List().Where(i => i.TimeUtc < cutoff).Count(Delete);
    }

    public static long TotalBytes() => List().Sum(i => i.Bytes);

    private static long Size(IEnumerable<string> files) => files.Sum(f =>
    {
        try
        {
            return new FileInfo(f).Length;
        }
        catch (Exception)
        {
            return 0L;
        }
    });
}

/// <summary>Tweak engine for this PC (Windows registry, edition-aware).</summary>
public static class TweakEngineFactory
{
    public static TweakEngine Create() => new(new WindowsRegistryStore(), SystemInfo.IsEnterpriseEdition());
}
