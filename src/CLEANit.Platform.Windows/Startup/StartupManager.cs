using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CLEANit.Core.Diagnostics;
using CLEANit.Core.Storage;
using Microsoft.Win32;

namespace CLEANit.Platform.Windows;

public enum StartupLocation
{
    RunUser,
    RunMachine,
    RunMachine32,
    FolderUser,
    FolderCommon,
    Task
}

/// <summary>Program that starts with Windows or at sign-in.</summary>
public sealed record StartupEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required StartupLocation Location { get; init; }

    public string Command { get; init; } = "";

    /// <summary>Resolved executable (for signature and "open location").</summary>
    public string? TargetPath { get; init; }

    public bool Enabled { get; init; }

    public string? Publisher { get; init; }

    public bool Signed { get; init; }

    public bool TargetMissing { get; init; }

    /// <summary>Registry value name, file path (startup folder) or full task path.</summary>
    public required string Key { get; init; }
}

/// <summary>Backup of a deleted startup entry (restorable).</summary>
public sealed record StartupBackup(string Name, StartupLocation Location, string Key, string Command, string? FileCopy, DateTime DeletedUtc);

/// <summary>
/// Startup entries from Run keys, startup folders and scheduled tasks.
/// Enable/disable works like the Task Manager (StartupApproved, reversible); delete keeps a backup.
/// </summary>
public static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static string BackupDirectory => Path.Combine(AppPaths.BackupsDirectory, "startup");

    public static async Task<IReadOnlyList<StartupEntry>> ReadAllAsync(CancellationToken ct)
    {
        var list = new List<StartupEntry>();
        ReadRun(Registry.CurrentUser, RunPath, StartupLocation.RunUser, "Run", list);
        ReadRun(Registry.LocalMachine, RunPath, StartupLocation.RunMachine, "Run", list);
        ReadRun(Registry.LocalMachine, Run32Path, StartupLocation.RunMachine32, "Run32", list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.FolderUser, list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.FolderCommon, list);
        list.AddRange(await ReadTasksAsync(ct));
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(RegistryKey hive, string path, StartupLocation location, string approvedName, List<StartupEntry> list)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (string.IsNullOrEmpty(name) || key!.GetValue(name) is not string command)
                {
                    continue;
                }

                var target = ExecutableFromCommand(command);
                list.Add(Describe(new StartupEntry
                {
                    Id = $"{location}:{name}",
                    Name = name,
                    Location = location,
                    Command = command,
                    TargetPath = target,
                    Key = name,
                    Enabled = IsApproved(hive, approvedName, name)
                }));
            }
        }
        catch (Exception)
        {
            // No access.
        }
    }

    private static void ReadFolder(string folder, StartupLocation location, List<StartupEntry> list)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        var hive = location == StartupLocation.FolderUser ? Registry.CurrentUser : Registry.LocalMachine;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) : file;
            list.Add(Describe(new StartupEntry
            {
                Id = $"{location}:{fileName}",
                Name = Path.GetFileNameWithoutExtension(file),
                Location = location,
                Command = target ?? file,
                TargetPath = target,
                Key = file,
                Enabled = IsApproved(hive, "StartupFolder", fileName)
            }));
        }
    }

    /// <summary>Non-Microsoft tasks with a logon or boot trigger.</summary>
    private static async Task<IReadOnlyList<StartupEntry>> ReadTasksAsync(CancellationToken ct)
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' -and ($_.Triggers | Where-Object { $_.CimClass.CimClassName -in 'MSFT_TaskLogonTrigger','MSFT_TaskBootTrigger' }) } | " +
            "ForEach-Object { $a = $_.Actions | Select-Object -First 1; [pscustomobject]@{ Path=$_.TaskPath + $_.TaskName; Name=$_.TaskName; State=[string]$_.State; Exec=$a.Execute; Args=$a.Arguments } } | " +
            "ConvertTo-Json -Compress";

        var run = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
            TimeSpan.FromMinutes(1), Encoding.UTF8, ct);
        var json = run.Output.Trim();
        if (json.Length == 0 || (json[0] != '[' && json[0] != '{'))
        {
            return [];
        }

        var list = new List<StartupEntry>();
        using var doc = JsonDocument.Parse(json[0] == '[' ? json : "[" + json + "]");
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var path = Str(item, "Path");
            if (path is null || path.StartsWith(@"\CLEANit\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var exec = Environment.ExpandEnvironmentVariables(Str(item, "Exec") ?? "").Trim('"');
            var args = Str(item, "Args");
            list.Add(Describe(new StartupEntry
            {
                Id = "Task:" + path,
                Name = Str(item, "Name") ?? path,
                Location = StartupLocation.Task,
                Command = string.IsNullOrEmpty(args) ? exec : $"{exec} {args}",
                TargetPath = exec.Length > 0 ? exec : null,
                Key = path,
                Enabled = !string.Equals(Str(item, "State"), "Disabled", StringComparison.OrdinalIgnoreCase)
            }));
        }

        return list;
    }

    /// <summary>Like the Task Manager: StartupApproved value (02 = on, 03 = off). Tasks: schtasks /Enable|/Disable.</summary>
    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Location == StartupLocation.Task)
        {
            return ProcessRunner.Run("schtasks.exe", ["/Change", "/TN", entry.Key, enabled ? "/Enable" : "/Disable"]).Success;
        }

        var (hive, name) = ApprovedTarget(entry);
        try
        {
            using var key = hive.CreateSubKey($@"{ApprovedPath}\{name}", writable: true);
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
            {
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            }

            key.SetValue(ValueName(entry), data, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Removes the entry after writing a backup (JSON, plus the shortcut file or task XML).</summary>
    public static bool Delete(StartupEntry entry)
    {
        Directory.CreateDirectory(BackupDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safeName = string.Concat(entry.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string? copy = null;

        try
        {
            switch (entry.Location)
            {
                case StartupLocation.FolderUser:
                case StartupLocation.FolderCommon:
                    copy = Path.Combine(BackupDirectory, $"{stamp}-{Path.GetFileName(entry.Key)}");
                    File.Copy(entry.Key, copy, overwrite: true);
                    break;
                case StartupLocation.Task:
                    copy = Path.Combine(BackupDirectory, $"{stamp}-{safeName}.xml");
                    var sep = entry.Key.LastIndexOf('\\');
                    var export = $"Export-ScheduledTask -TaskPath '{Quote(entry.Key[..(sep + 1)])}' -TaskName '{Quote(entry.Key[(sep + 1)..])}' " +
                                 $"| Out-File -Encoding Unicode -FilePath '{Quote(copy)}'";
                    ProcessRunner.Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", export], TimeSpan.FromMinutes(1));
                    if (!File.Exists(copy) || new FileInfo(copy).Length == 0)
                    {
                        return false;
                    }

                    break;
            }

            JsonFile.Write(Path.Combine(BackupDirectory, $"{stamp}-{safeName}.json"),
                new StartupBackup(entry.Name, entry.Location, entry.Key, entry.Command, copy, DateTime.UtcNow));

            switch (entry.Location)
            {
                case StartupLocation.RunUser:
                case StartupLocation.RunMachine:
                case StartupLocation.RunMachine32:
                    using (var key = RunHive(entry.Location).OpenSubKey(RunKeyPath(entry.Location), writable: true))
                    {
                        key?.DeleteValue(entry.Key, throwOnMissingValue: false);
                    }

                    break;
                case StartupLocation.FolderUser:
                case StartupLocation.FolderCommon:
                    File.Delete(entry.Key);
                    break;
                case StartupLocation.Task:
                    return ProcessRunner.Run("schtasks.exe", ["/Delete", "/TN", entry.Key, "/F"]).Success;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static IReadOnlyList<(string File, StartupBackup Backup)> ReadBackups()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        var list = new List<(string, StartupBackup)>();
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*.json").OrderDescending())
        {
            try
            {
                if (JsonFile.Read<StartupBackup>(file) is { } backup)
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

    public static bool Restore(string backupFile, StartupBackup backup)
    {
        try
        {
            switch (backup.Location)
            {
                case StartupLocation.RunUser:
                case StartupLocation.RunMachine:
                case StartupLocation.RunMachine32:
                    using (var key = RunHive(backup.Location).CreateSubKey(RunKeyPath(backup.Location), writable: true))
                    {
                        key.SetValue(backup.Key, backup.Command, RegistryValueKind.String);
                    }

                    break;
                case StartupLocation.FolderUser:
                case StartupLocation.FolderCommon:
                    if (backup.FileCopy is null || !File.Exists(backup.FileCopy))
                    {
                        return false;
                    }

                    File.Copy(backup.FileCopy, backup.Key, overwrite: false);
                    break;
                case StartupLocation.Task:
                    if (backup.FileCopy is null || !ProcessRunner.Run("schtasks.exe", ["/Create", "/TN", backup.Key, "/XML", backup.FileCopy]).Success)
                    {
                        return false;
                    }

                    break;
            }

            File.Delete(backupFile);
            if (backup.FileCopy is not null && File.Exists(backup.FileCopy))
            {
                File.Delete(backup.FileCopy);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds an own startup entry: programs and scripts as a Run value (current user or all users), shortcuts (.lnk) as a
    /// copy in the startup folder. Existing entries are never overwritten.
    /// </summary>
    public static (bool Ok, string? Error) Add(string name, string target, string? arguments, bool allUsers)
    {
        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return (false, "invalid name");
        }

        if (!File.Exists(target))
        {
            return (false, "file not found");
        }

        try
        {
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var folder = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
                Directory.CreateDirectory(folder);
                var destination = Path.Combine(folder, name + ".lnk");
                if (File.Exists(destination))
                {
                    return (false, "exists");
                }

                File.Copy(target, destination);
                return (true, null);
            }

            var location = allUsers ? StartupLocation.RunMachine : StartupLocation.RunUser;
            using var key = RunHive(location).CreateSubKey(RunKeyPath(location), writable: true);
            if (key.GetValue(name) is not null)
            {
                return (false, "exists");
            }

            var command = BuildCommand(target, arguments);
            key.SetValue(name, command, RegistryValueKind.String);
            return (true, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>"C:\path\app.exe" args – scripts run through cmd so they start like a double click.</summary>
    internal static string BuildCommand(string target, string? arguments)
    {
        var args = string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments.Trim();
        return target.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? $"cmd.exe /c \"\"{target}\"{args}\""
            : $"\"{target}\"{args}";
    }

    public static void OpenLocation(StartupEntry entry)
    {
        try
        {
            if (entry.Location == StartupLocation.Task)
            {
                Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
                return;
            }

            var file = entry.Location is StartupLocation.FolderUser or StartupLocation.FolderCommon ? entry.Key : entry.TargetPath;
            if (file is not null && File.Exists(file))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false });
            }
        }
        catch (Exception)
        {
            // Ignore.
        }
    }

    /// <summary>Publisher (signature, otherwise version info) and missing target.</summary>
    private static StartupEntry Describe(StartupEntry entry)
    {
        var target = entry.TargetPath;
        if (string.IsNullOrEmpty(target))
        {
            return entry;
        }

        if (!File.Exists(target))
        {
            return entry with { TargetMissing = true };
        }

        var (trusted, signer) = Authenticode.GetSignature(target);
        var publisher = signer;
        if (publisher is null)
        {
            try
            {
                publisher = FileVersionInfo.GetVersionInfo(target).CompanyName;
            }
            catch (Exception)
            {
                // No version info.
            }
        }

        return entry with { Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher, Signed = trusted };
    }

    private static bool IsApproved(RegistryKey hive, string approvedName, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey($@"{ApprovedPath}\{approvedName}");
            return key?.GetValue(valueName) is not byte[] { Length: > 0 } data || (data[0] & 0x01) == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static (RegistryKey Hive, string Name) ApprovedTarget(StartupEntry entry) => entry.Location switch
    {
        StartupLocation.RunUser => (Registry.CurrentUser, "Run"),
        StartupLocation.RunMachine => (Registry.LocalMachine, "Run"),
        StartupLocation.RunMachine32 => (Registry.LocalMachine, "Run32"),
        StartupLocation.FolderUser => (Registry.CurrentUser, "StartupFolder"),
        _ => (Registry.LocalMachine, "StartupFolder")
    };

    private static string ValueName(StartupEntry entry) =>
        entry.Location is StartupLocation.FolderUser or StartupLocation.FolderCommon ? Path.GetFileName(entry.Key) : entry.Key;

    private static RegistryKey RunHive(StartupLocation location) =>
        location == StartupLocation.RunUser ? Registry.CurrentUser : Registry.LocalMachine;

    private static string RunKeyPath(StartupLocation location) => location == StartupLocation.RunMachine32 ? Run32Path : RunPath;

    /// <summary>Executable of a command line ("C:\x\a.exe" -min, C:\x\a.exe /b, rundll32 …).</summary>
    internal static string? ExecutableFromCommand(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            return text[..(exe + 4)];
        }

        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    /// <summary>Target of a .lnk file via the shell (WScript.Shell).</summary>
    private static string? ResolveShortcut(string file)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(file);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Quote(string value) => value.Replace("'", "''");
}
