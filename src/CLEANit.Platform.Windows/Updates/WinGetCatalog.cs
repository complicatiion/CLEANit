using System.Text;
using System.Text.RegularExpressions;
using CLEANit.Core.Diagnostics;

namespace CLEANit.Platform.Windows;

/// <summary>Search result of a package source.</summary>
public sealed record CatalogPackage(string Name, string Id, string Version, string Source, string? Match = null);

/// <summary>Installed program as winget sees it; <see cref="IsKnown"/> = available from a winget source.</summary>
public sealed record InstalledPackage(string Name, string Id, string Version, string? Available, string Source)
{
    public bool IsKnown => Source.Length > 0;
}

/// <summary>Details from <c>winget show</c> (shown before installing).</summary>
public sealed record PackageDetails(string Id, string Name, string? Version, string? Publisher, string? Description, string? Homepage,
    string? License, string? LicenseUrl, string? InstallerType, string RawOutput);

/// <summary>Install options. Machine scope and fixed versions are expert features.</summary>
public sealed record InstallOptions(string? Version = null, bool MachineScope = false, string Source = "winget");

/// <summary>Search, details, install and installed list through winget. Packages keep their own licenses.</summary>
public static partial class WinGetClient
{
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(45);

    // "Found Godot Engine [GodotEngine.GodotEngine]" in any display language.
    [GeneratedRegex(@"^\S.*\[(?<id>[^\[\]\s]+)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex FoundLine();

    [GeneratedRegex(@"^(?<key>[^:\s][^:]{0,40}):\s*(?<value>.*)$")]
    private static partial Regex KeyValue();

    public static async Task<(IReadOnlyList<CatalogPackage> Packages, string Output)> SearchAsync(string query, string source, bool acceptSourceAgreements,
        CancellationToken ct)
    {
        var args = new List<string> { "search", query, "--source", source, "--disable-interactivity" };
        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, SearchTimeout, Encoding.UTF8, ct);
        return (ParseSearch(run.Output, source), Clean(run.Output));
    }

    /// <summary>Details of one package (by exact id); also resolves ids that the table cut off.</summary>
    public static async Task<PackageDetails?> ShowAsync(string idOrName, bool byName, string source, bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string> { "show", byName ? "--name" : "--id", idOrName.TrimEnd('…'), "--source", source, "--disable-interactivity" };
        if (!byName && !idOrName.EndsWith('…'))
        {
            args.Add("--exact");
        }

        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, SearchTimeout, Encoding.UTF8, ct);
        return ParseShow(Clean(run.Output));
    }

    /// <summary>Installs one package silently. The user confirmed the package and its license in CLEANit.</summary>
    public static async Task<(bool Success, string Output)> InstallAsync(string id, InstallOptions options, bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string>
        {
            "install", "--id", id, "--exact", "--source", options.Source, "--silent", "--accept-package-agreements", "--disable-interactivity"
        };
        if (!string.IsNullOrWhiteSpace(options.Version))
        {
            args.Add("--version");
            args.Add(options.Version);
        }

        if (options.MachineScope)
        {
            args.Add("--scope");
            args.Add("machine");
        }

        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, InstallTimeout, Encoding.UTF8, ct);
        return (run.Success, Clean(run.Output));
    }

    /// <summary>All installed programs (Apps &amp; features, Store apps) with the winget id when a source knows them.</summary>
    public static async Task<(IReadOnlyList<InstalledPackage> Packages, string Output)> ListInstalledAsync(bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string> { "list", "--disable-interactivity" };
        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, ListTimeout, Encoding.UTF8, ct);
        var packages = ParseList(run.Output);
        if (packages.Any(p => p.IsKnown && p.Id.EndsWith('…')))
        {
            var ids = await ExportIdsAsync(acceptSourceAgreements, ct);
            packages = packages.Select(p => p.IsKnown && p.Id.EndsWith('…')
                    ? p with { Id = ids.FirstOrDefault(id => id.StartsWith(p.Id.TrimEnd('…'), StringComparison.OrdinalIgnoreCase)) ?? p.Id }
                    : p)
                .ToList();
        }

        return (packages, Clean(run.Output));
    }

    /// <summary>Search table: Name, Id, Version, [Match], [Source] – positions from the header line.</summary>
    internal static List<CatalogPackage> ParseSearch(string output, string source)
    {
        var result = new List<CatalogPackage>();
        foreach (var (starts, rows) in Tables(output))
        {
            if (starts.Count < 3)
            {
                continue;
            }

            foreach (var row in rows)
            {
                var cells = Cut(row, starts);
                var id = cells.ElementAtOrDefault(1) ?? "";
                if (id.Length == 0 || id.Contains(' '))
                {
                    continue;
                }

                // With --source the table has no source column; the 4th column is "Match" (tag/moniker) when present.
                var match = cells.Count > 3 && !string.IsNullOrWhiteSpace(cells[3]) ? cells[3] : null;
                result.Add(new CatalogPackage(cells[0], id, cells.ElementAtOrDefault(2) ?? "", source, match));
            }
        }

        return result;
    }

    /// <summary>List table: Name, Id, Version, [Available], [Source].</summary>
    internal static List<InstalledPackage> ParseList(string output)
    {
        var result = new List<InstalledPackage>();
        foreach (var (starts, rows) in Tables(output))
        {
            if (starts.Count < 3)
            {
                continue;
            }

            foreach (var row in rows)
            {
                var cells = Cut(row, starts);
                var id = cells.ElementAtOrDefault(1) ?? "";
                if (id.Length == 0)
                {
                    continue;
                }

                string available = "", source = "";
                if (starts.Count >= 5)
                {
                    available = cells.ElementAtOrDefault(3) ?? "";
                    source = cells.ElementAtOrDefault(4) ?? "";
                }
                else if (starts.Count == 4)
                {
                    // Without updates the "Available" column is missing.
                    source = cells.ElementAtOrDefault(3) ?? "";
                }

                // ARP\… and MSIX\… ids are local entries without a source.
                if (id.StartsWith(@"ARP\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase))
                {
                    source = "";
                }

                result.Add(new InstalledPackage(cells[0], id, cells.ElementAtOrDefault(2) ?? "", available.Length > 0 ? available : null, source));
            }
        }

        return result;
    }

    internal static PackageDetails? ParseShow(string output)
    {
        var found = FoundLine().Match(output);
        if (!found.Success)
        {
            return null;
        }

        var header = found.Value.Trim();
        var id = found.Groups["id"].Value;
        var name = header[..header.LastIndexOf('[')].Trim();
        var firstSpace = name.IndexOf(' ');
        name = firstSpace > 0 ? name[(firstSpace + 1)..].Trim() : name;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? lastKey = null;
        foreach (var raw in output[(found.Index + found.Length)..].Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var kv = KeyValue().Match(line);
            if (kv.Success && !line.StartsWith(' '))
            {
                lastKey = kv.Groups["key"].Value.Trim();
                values.TryAdd(lastKey, kv.Groups["value"].Value.Trim());
            }
            else if (lastKey is not null && line.StartsWith("  ") && values.TryGetValue(lastKey, out var previous))
            {
                // Multi-line values (description) are indented.
                values[lastKey] = (previous + " " + line.Trim()).Trim();
            }
        }

        string? Get(params string[] keys) => keys.Select(k => values.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        return new PackageDetails(id, name,
            Get("Version"),
            Get("Publisher", "Herausgeber"),
            Get("Description", "Beschreibung"),
            Get("Homepage", "Startseite"),
            Get("License", "Lizenz"),
            Get("License Url", "Lizenz-URL", "Lizenz-Url"),
            Get("Installer Type", "Installertyp", "Installationsprogrammtyp"),
            output.Trim());
    }

    /// <summary>All tables of a winget output: column starts from the header above the dashed line, then the rows.</summary>
    private static IEnumerable<(List<int> Starts, List<string> Rows)> Tables(string output)
    {
        var lines = Clean(output).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        for (var i = 1; i < lines.Count; i++)
        {
            var separator = lines[i].Trim();
            if (separator.Length < 10 || separator.Any(c => c != '-'))
            {
                continue;
            }

            var starts = ColumnStarts(lines[i - 1]);
            var rows = new List<string>();
            for (var r = i + 1; r < lines.Count; r++)
            {
                var row = lines[r];
                if (string.IsNullOrWhiteSpace(row) || starts.Count < 2 || row.Length <= starts[1])
                {
                    break;
                }

                rows.Add(row);
            }

            yield return (starts, rows);
        }
    }
}

/// <summary>Optional Chocolatey source: only when choco.exe is already installed (CLEANit never installs Chocolatey).</summary>
public static class ChocolateyClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static string? FindExecutable()
    {
        var candidates = new List<string>();
        var root = Environment.GetEnvironmentVariable("ChocolateyInstall");
        if (!string.IsNullOrEmpty(root))
        {
            candidates.Add(Path.Combine(root, "bin", "choco.exe"));
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", "choco.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsInstalled => FindExecutable() is not null;

    public static async Task<IReadOnlyList<CatalogPackage>> SearchAsync(string query, CancellationToken ct)
    {
        var exe = FindExecutable();
        if (exe is null)
        {
            return [];
        }

        var run = await ProcessRunner.RunAsync(exe, ["search", query, "--limit-output", "--page-size=40"], Timeout, Encoding.UTF8, ct);
        return ParseLimitOutput(run.Output).Select(p => new CatalogPackage(p.Id, p.Id, p.Version, "chocolatey")).ToList();
    }

    public static async Task<IReadOnlyList<InstalledPackage>> ListInstalledAsync(CancellationToken ct)
    {
        var exe = FindExecutable();
        if (exe is null)
        {
            return [];
        }

        var run = await ProcessRunner.RunAsync(exe, ["list", "--limit-output"], Timeout, Encoding.UTF8, ct);
        return ParseLimitOutput(run.Output).Select(p => new InstalledPackage(p.Id, p.Id, p.Version, null, "chocolatey")).ToList();
    }

    public static async Task<(bool Success, string Output)> InstallAsync(string id, string? version, CancellationToken ct)
    {
        var exe = FindExecutable();
        if (exe is null)
        {
            return (false, "choco.exe not found.");
        }

        var args = new List<string> { "install", id, "-y", "--no-progress" };
        if (!string.IsNullOrWhiteSpace(version))
        {
            args.Add("--version");
            args.Add(version);
        }

        var run = await ProcessRunner.RunAsync(exe, args, TimeSpan.FromMinutes(45), Encoding.UTF8, ct);
        return (run.Success, run.Output.Trim());
    }

    /// <summary>"id|version" lines (choco --limit-output).</summary>
    internal static List<(string Id, string Version)> ParseLimitOutput(string output) =>
        output.Split('\n')
            .Select(l => l.Trim())
            .Select(l => l.Split('|'))
            .Where(p => p.Length >= 2 && p[0].Length > 0 && !p[0].Contains(' '))
            .Select(p => (p[0], p[1]))
            .ToList();
}
