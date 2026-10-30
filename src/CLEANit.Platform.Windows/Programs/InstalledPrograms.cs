using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CLEANit.Core.Diagnostics;
using CLEANit.Core.Engine;
using CLEANit.Core.Paths;
using Microsoft.Win32;

namespace CLEANit.Platform.Windows;

public enum ProgramKind
{
    Win32,
    Msi,
    Store
}

/// <summary>Installed program as listed in the Uninstall keys (read only) or as Store package.</summary>
public sealed record InstalledProgram
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Version { get; init; }

    public string? Publisher { get; init; }

    public DateTime? InstallDate { get; init; }

    public long? SizeBytes { get; init; }

    public string? InstallLocation { get; init; }

    public string? DisplayIcon { get; init; }

    public ProgramKind Kind { get; init; }

    public string? UninstallCommand { get; init; }

    public string? ModifyCommand { get; init; }

    /// <summary>MSI product code or Store package full name.</summary>
    public string? ProductCode { get; init; }

    public bool PerUser { get; init; }

    public bool CanUninstall => Kind == ProgramKind.Store || ProductCode is not null || !string.IsNullOrWhiteSpace(UninstallCommand);

    public bool CanModify { get; init; }

    public bool CanRepair { get; init; }
}

public sealed record LeftoverFolder(string Path, long Bytes, long Files);

/// <summary>Reads installed programs and starts their own uninstall/modify/repair routines.</summary>
public static partial class InstalledPrograms
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Folder names never offered as leftovers (shared by many programs).</summary>
    private static readonly HashSet<string> SharedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Common Files", "Microsoft Shared", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "Google", "Mozilla",
        "Adobe", "Apple", "Packages", "Programs", "Temp", "Microsoft.NET", "dotnet", "WindowsApps", "Windows NT", "Internet Explorer",
        "Windows Defender", "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar", "Reference Assemblies",
        "MSBuild", "IIS", "IIS Express", "Package Cache", "Uninstall Information", "Installer", "CLEANit"
    };

    [GeneratedRegex(@"\s*(\(x64\)|\(x86\)|\(64-bit\)|\(32-bit\)|x64|x86|64-bit|32-bit|v?\d+(\.\d+)+.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\{[0-9A-Fa-f\-]{36}\}")]
    private static partial Regex ProductCodeRegex();

    public static IReadOnlyList<InstalledProgram> ReadWin32()
    {
        var list = new List<InstalledProgram>();
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64", false, list);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32", false, list);
        Read(RegistryHive.CurrentUser, RegistryView.Default, "HKCU", true, list);

        // Same product registered in both views: keep one.
        return list.GroupBy(p => (p.Name.ToUpperInvariant(), p.Version))
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Read(RegistryHive hive, RegistryView view, string tag, bool perUser, List<InstalledProgram> list)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(UninstallKey);
            foreach (var name in key?.GetSubKeyNames() ?? [])
            {
                try
                {
                    using var sub = key!.OpenSubKey(name);
                    var program = sub is null ? null : FromKey(sub, name, tag, perUser);
                    if (program is not null)
                    {
                        list.Add(program);
                    }
                }
                catch (Exception)
                {
                    // Broken entry.
                }
            }
        }
        catch (Exception)
        {
            // No access.
        }
    }

    private static InstalledProgram? FromKey(RegistryKey key, string keyName, string tag, bool perUser)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(name)
            || Int(key, "SystemComponent") == 1
            || key.GetValue("ParentKeyName") is not null
            || (key.GetValue("ReleaseType") as string) is "Update" or "Hotfix" or "Security Update")
        {
            return null;
        }

        var isMsi = Int(key, "WindowsInstaller") == 1 && ProductCodeRegex().IsMatch(keyName);
        var uninstall = key.GetValue("UninstallString") as string;
        var modify = key.GetValue("ModifyPath") as string;
        var noModify = Int(key, "NoModify") == 1;
        var noRepair = Int(key, "NoRepair") == 1;
        if (string.IsNullOrWhiteSpace(uninstall) && !isMsi)
        {
            return null;
        }

        long? size = key.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null;
        DateTime? date = DateTime.TryParseExact(key.GetValue("InstallDate") as string, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : null;

        return new InstalledProgram
        {
            Id = $"{tag}\\{keyName}",
            Name = name,
            Version = key.GetValue("DisplayVersion") as string,
            Publisher = (key.GetValue("Publisher") as string)?.Trim(),
            InstallDate = date,
            SizeBytes = size,
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"'),
            DisplayIcon = key.GetValue("DisplayIcon") as string,
            Kind = isMsi ? ProgramKind.Msi : ProgramKind.Win32,
            UninstallCommand = uninstall,
            ModifyCommand = modify,
            ProductCode = isMsi ? keyName : null,
            PerUser = perUser,
            CanModify = !noModify && (isMsi || !string.IsNullOrWhiteSpace(modify)),
            CanRepair = isMsi && !noRepair
        };
    }

    /// <summary>Store apps of the current user (no frameworks, no system or non-removable packages).</summary>
    public static async Task<IReadOnlyList<InstalledProgram>> ReadStoreAsync(CancellationToken ct)
    {
        const string script =
            "$ErrorActionPreference='SilentlyContinue';" +
            "Get-AppxPackage -PackageTypeFilter Main | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | " +
            "ForEach-Object { $d = $null; try { $d = (Get-AppxPackageManifest $_).Package.Properties.DisplayName } catch {}; " +
            "[pscustomobject]@{ Name=$_.Name; DisplayName=$d; FullName=$_.PackageFullName; Version=$_.Version; Publisher=$_.Publisher; Location=$_.InstallLocation } } | " +
            "ConvertTo-Json -Compress";

        var run = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
            TimeSpan.FromMinutes(2), Encoding.UTF8, ct);
        var json = run.Output.Trim();
        if (json.Length == 0 || (json[0] != '[' && json[0] != '{'))
        {
            return [];
        }

        var list = new List<InstalledProgram>();
        using var doc = JsonDocument.Parse(json[0] == '[' ? json : "[" + json + "]");
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var full = Str(item, "FullName");
            if (full is null)
            {
                continue;
            }

            var display = Str(item, "DisplayName");
            var name = string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
                ? Str(item, "Name") ?? full
                : display;
            list.Add(new InstalledProgram
            {
                Id = "APPX\\" + full,
                Name = name,
                Version = Str(item, "Version"),
                Publisher = PublisherName(Str(item, "Publisher")),
                InstallLocation = Str(item, "Location"),
                Kind = ProgramKind.Store,
                ProductCode = full,
                PerUser = true
            });
        }

        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Starts the program's own uninstaller and waits for it (null = could not start).</summary>
    public static Task<int?> UninstallAsync(InstalledProgram program, CancellationToken ct) => program.Kind switch
    {
        ProgramKind.Store => RunPowerShellAsync($"Remove-AppxPackage -Package '{program.ProductCode!.Replace("'", "''")}'", ct),
        ProgramKind.Msi => RunAndWaitAsync("msiexec.exe", $"/x {program.ProductCode}", ct),
        _ => RunCommandAsync(program.UninstallCommand, ct)
    };

    public static Task<int?> ModifyAsync(InstalledProgram program, CancellationToken ct) => program.Kind switch
    {
        ProgramKind.Msi when string.IsNullOrWhiteSpace(program.ModifyCommand) => RunAndWaitAsync("msiexec.exe", $"/i {program.ProductCode}", ct),
        ProgramKind.Store => Task.FromResult<int?>(null),
        _ => RunCommandAsync(program.ModifyCommand, ct)
    };

    public static Task<int?> RepairAsync(InstalledProgram program, CancellationToken ct) =>
        program.Kind == ProgramKind.Msi ? RunAndWaitAsync("msiexec.exe", $"/fa {program.ProductCode}", ct) : Task.FromResult<int?>(null);

    /// <summary>Folders that typically remain after an uninstall (only exact name matches; nothing shared).</summary>
    public static IReadOnlyList<LeftoverFolder> FindLeftovers(InstalledProgram program, PathGuard guard)
    {
        var names = CandidateNames(program);
        var publisher = CleanPublisher(program.Publisher);
        var bases = new[]
        {
            KnownFolders.ProgramFiles, KnownFolders.ProgramFilesX86, KnownFolders.ProgramData,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        }.Where(b => !string.IsNullOrEmpty(b)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(program.InstallLocation) && program.Kind != ProgramKind.Store)
        {
            candidates.Add(program.InstallLocation);
        }

        foreach (var root in bases)
        {
            foreach (var name in names)
            {
                candidates.Add(Path.Combine(root, name));
                if (publisher is not null)
                {
                    candidates.Add(Path.Combine(root, publisher, name));
                }
            }
        }

        var result = new List<LeftoverFolder>();
        foreach (var path in candidates.Select(PathExpander.Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(path) || PathExpander.IsReparsePoint(path) || SharedFolders.Contains(Path.GetFileName(path))
                || !guard.CheckUserTarget(path).Allowed || bases.Any(b => b.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var (bytes, files, _) = SafeDelete.Measure(path);
            result.Add(new LeftoverFolder(path, bytes, files));
        }

        // A folder inside another candidate is covered by its parent.
        return result.Where(r => !result.Any(o => o != r && PathGuard.IsStrictAncestor(r.Path, o.Path))).ToList();
    }

    private static IReadOnlyList<string> CandidateNames(InstalledProgram program)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = program.Name.Trim();
        names.Add(name);
        var stripped = VersionSuffix().Replace(name, "").Trim();
        if (stripped.Length >= 3)
        {
            names.Add(stripped);
        }

        var publisher = CleanPublisher(program.Publisher);
        if (publisher is not null && stripped.StartsWith(publisher + " ", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(stripped[(publisher.Length + 1)..].Trim());
        }

        return names.Where(n => n.Length >= 3 && !SharedFolders.Contains(n) && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0).ToList();
    }

    private static string? CleanPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return null;
        }

        var clean = Regex.Replace(publisher, @"[,.]?\s*(Inc|Ltd|LLC|GmbH|Corp|Corporation|Co|AG|S\.A|B\.V)\.?$", "", RegexOptions.IgnoreCase).Trim();
        return clean.Length >= 2 && clean.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !SharedFolders.Contains(clean) ? clean : null;
    }

    /// <summary>Splits "C:\Program Files\X\unins000.exe" /SILENT (quoted or not) into file and arguments.</summary>
    internal static (string File, string Args)? SplitCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? (text[1..end], text[(end + 1)..].Trim()) : null;
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            var file = text[..(exe + 4)];
            return (file, text[(exe + 4)..].Trim());
        }

        var space = text.IndexOf(' ');
        return space > 0 ? (text[..space], text[(space + 1)..]) : (text, "");
    }

    private static Task<int?> RunCommandAsync(string? command, CancellationToken ct)
    {
        var split = SplitCommand(command);
        if (split is null)
        {
            return Task.FromResult<int?>(null);
        }

        // MsiExec.exe /I{GUID} in UninstallString: keep, it shows the MSI maintenance dialog.
        return RunAndWaitAsync(split.Value.File, split.Value.Args, ct);
    }

    private static async Task<int?> RunAndWaitAsync(string file, string args, CancellationToken ct)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<int?> RunPowerShellAsync(string command, CancellationToken ct)
    {
        var run = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], TimeSpan.FromMinutes(10), ct: ct);
        return run.ExitCode;
    }

    private static int Int(RegistryKey key, string name) => key.GetValue(name) is int value ? value : 0;

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>"CN=Microsoft Corporation, O=…" → "Microsoft Corporation".</summary>
    private static string? PublisherName(string? distinguished)
    {
        if (string.IsNullOrEmpty(distinguished))
        {
            return null;
        }

        var match = Regex.Match(distinguished, @"(?:^|,\s*)(?:O|CN)=(""[^""]+""|[^,]+)");
        return match.Success ? match.Groups[1].Value.Trim('"') : distinguished;
    }
}
