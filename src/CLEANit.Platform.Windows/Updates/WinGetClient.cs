using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CLEANit.Core.Diagnostics;

namespace CLEANit.Platform.Windows;

public sealed record PackageUpdate(string Name, string Id, string Installed, string Available, string Source, bool ExplicitOnly);

/// <summary>
/// Software updates through winget (Windows Package Manager). CLEANit only lists and starts winget;
/// packages come from winget's sources and keep their own licenses. Nothing runs without the user.
/// </summary>
public static partial class WinGetClient
{
    private static readonly TimeSpan ListTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(30);

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex Ansi();

    public static async Task<string?> VersionAsync(CancellationToken ct)
    {
        var run = await ProcessRunner.RunAsync("winget.exe", ["--version"], TimeSpan.FromSeconds(20), Encoding.UTF8, ct);
        var text = run.Output.Trim();
        return run.Success && text.StartsWith('v') ? text : null;
    }

    /// <param name="acceptSourceAgreements">Only true after the user accepted the source agreements in CLEANit.</param>
    public static async Task<(IReadOnlyList<PackageUpdate> Updates, string Output)> ListUpgradesAsync(bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string> { "upgrade", "--include-unknown", "--disable-interactivity" };
        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, ListTimeout, Encoding.UTF8, ct);
        var updates = ParseTable(run.Output);

        // Long IDs are cut with "…" in the table: resolve them from winget export.
        if (updates.Any(u => u.Id.EndsWith('…')))
        {
            var ids = await ExportIdsAsync(acceptSourceAgreements, ct);
            updates = updates.Select(u => u.Id.EndsWith('…')
                    ? u with { Id = ids.FirstOrDefault(id => id.StartsWith(u.Id.TrimEnd('…'), StringComparison.OrdinalIgnoreCase)) ?? u.Id }
                    : u)
                .Where(u => !u.Id.EndsWith('…'))
                .ToList();
        }

        return (updates, Clean(run.Output));
    }

    /// <summary>Upgrades one package silently. The user accepted the package agreements in the confirmation dialog.</summary>
    public static async Task<(bool Success, string Output)> UpgradeAsync(string id, bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string>
        {
            "upgrade", "--id", id, "--exact", "--silent", "--accept-package-agreements", "--disable-interactivity"
        };
        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, UpgradeTimeout, Encoding.UTF8, ct);
        return (run.Success, Clean(run.Output));
    }

    /// <summary>Inventory of installed packages (winget export, JSON with versions).</summary>
    public static async Task<(bool Success, string Output)> ExportAsync(string file, bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string> { "export", "-o", file, "--include-versions", "--disable-interactivity" };
        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, ListTimeout, Encoding.UTF8, ct);
        return (File.Exists(file), Clean(run.Output));
    }

    /// <summary>Installs the packages of an inventory file. The user accepted the package agreements in the confirmation dialog.</summary>
    public static async Task<(bool Success, string Output)> ImportAsync(string file, bool acceptSourceAgreements, CancellationToken ct)
    {
        var args = new List<string>
        {
            "import", "-i", file, "--ignore-unavailable", "--ignore-versions", "--accept-package-agreements", "--disable-interactivity"
        };
        if (acceptSourceAgreements)
        {
            args.Add("--accept-source-agreements");
        }

        var run = await ProcessRunner.RunAsync("winget.exe", args, TimeSpan.FromHours(2), Encoding.UTF8, ct);
        return (run.Success, Clean(run.Output));
    }

    /// <summary>Parses the localized winget table by the column positions of its header.</summary>
    internal static List<PackageUpdate> ParseTable(string output)
    {
        var lines = Clean(output).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var result = new List<PackageUpdate>();
        var table = 0;
        for (var i = 1; i < lines.Count; i++)
        {
            var separator = lines[i].Trim();
            if (separator.Length < 10 || separator.Any(c => c != '-'))
            {
                continue;
            }

            var header = lines[i - 1];
            var starts = ColumnStarts(header);
            if (starts.Count < 4)
            {
                continue;
            }

            table++;
            for (var r = i + 1; r < lines.Count; r++)
            {
                var row = lines[r];
                if (string.IsNullOrWhiteSpace(row) || row.Length <= starts[3])
                {
                    break;
                }

                var cells = Cut(row, starts);
                var id = cells.ElementAtOrDefault(1) ?? "";
                if (id.Length == 0 || id.Contains(' ') || cells.Count < 4 || string.IsNullOrWhiteSpace(cells[3]))
                {
                    break;
                }

                result.Add(new PackageUpdate(cells[0], id, cells[2], cells[3], cells.ElementAtOrDefault(4) ?? "", table > 1));
            }
        }

        return result;
    }

    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    private static List<string> Cut(string row, List<int> starts)
    {
        var cells = new List<string>();
        for (var c = 0; c < starts.Count; c++)
        {
            var start = starts[c];
            if (start >= row.Length)
            {
                cells.Add("");
                continue;
            }

            var end = c + 1 < starts.Count ? Math.Min(starts[c + 1], row.Length) : row.Length;
            cells.Add(row[start..end].Trim());
        }

        return cells;
    }

    private static async Task<List<string>> ExportIdsAsync(bool acceptSourceAgreements, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"cleanit-winget-{Environment.ProcessId}.json");
        try
        {
            var args = new List<string> { "export", "-o", file, "--disable-interactivity" };
            if (acceptSourceAgreements)
            {
                args.Add("--accept-source-agreements");
            }

            await ProcessRunner.RunAsync("winget.exe", args, ListTimeout, Encoding.UTF8, ct);
            if (!File.Exists(file))
            {
                return [];
            }

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
            var ids = new List<string>();
            foreach (var source in doc.RootElement.GetProperty("Sources").EnumerateArray())
            {
                foreach (var package in source.GetProperty("Packages").EnumerateArray())
                {
                    if (package.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { } value)
                    {
                        ids.Add(value);
                    }
                }
            }

            return ids;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [];
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Removes progress spinners and colors (only the text after the last carriage return counts).</summary>
    private static string Clean(string output)
    {
        var sb = new StringBuilder();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var cr = line.LastIndexOf('\r');
            if (cr >= 0)
            {
                line = line[(cr + 1)..];
            }

            line = Ansi().Replace(line, "");
            if (line.Trim() is "-" or "\\" or "|" or "/" || line.Contains('█') || line.Contains('▒'))
            {
                continue;
            }

            sb.Append(line.TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }
}
