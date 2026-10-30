using System.Text.Json;
using System.Text.Json.Nodes;

namespace CLEANit.Core.Software;

/// <summary>Package of a bundle: winget id, optional fixed version, source name ("winget" or "msstore").</summary>
public sealed record BundlePackage(string Id, string? Version = null, string Source = "winget");

/// <summary>
/// Package list in the native format of <c>winget export</c> (schema 2.0), so it can also be used with <c>winget import</c>.
/// The bundle name is the file name.
/// </summary>
public sealed class PackageBundle
{
    public const string SchemaUrl = "https://aka.ms/winget-packages.schema.2.0.json";

    private static readonly Dictionary<string, (string Argument, string Identifier, string Type)> KnownSources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winget"] = ("https://cdn.winget.microsoft.com/cache", "Microsoft.Winget.Source_8wekyb3d8bbwe", "Microsoft.PreIndexed.Package"),
        ["msstore"] = ("https://storeedgefd.dsx.mp.microsoft.com/v9.0", "StoreEdgeFD", "Microsoft.Rest")
    };

    public string Name { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<BundlePackage> Packages { get; } = [];

    public static PackageBundle Load(string file)
    {
        var bundle = Parse(File.ReadAllText(file));
        bundle.Name = Path.GetFileNameWithoutExtension(file);
        return bundle;
    }

    /// <summary>Reads a winget export/import file (sources with packages; unknown fields are ignored).</summary>
    public static PackageBundle Parse(string json)
    {
        var bundle = new PackageBundle();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.TryGetProperty("CreationDate", out var created) && created.ValueKind == JsonValueKind.String
                                                                 && DateTime.TryParse(created.GetString(), out var date))
        {
            bundle.CreatedUtc = date.ToUniversalTime();
        }

        if (!root.TryGetProperty("Sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Not a winget package list (\"Sources\" missing).");
        }

        foreach (var source in sources.EnumerateArray())
        {
            var sourceName = source.TryGetProperty("SourceDetails", out var details) && details.TryGetProperty("Name", out var name)
                ? name.GetString() ?? "winget"
                : "winget";
            if (!source.TryGetProperty("Packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var package in packages.EnumerateArray())
            {
                if (package.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { Length: > 0 } value)
                {
                    var version = package.TryGetProperty("Version", out var v) ? v.GetString() : null;
                    bundle.Add(new BundlePackage(value, string.IsNullOrWhiteSpace(version) ? null : version, sourceName));
                }
            }
        }

        return bundle;
    }

    /// <summary>Adds a package once (same id and source); returns false when it was already there.</summary>
    public bool Add(BundlePackage package)
    {
        if (Packages.Any(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase) && p.Source.Equals(package.Source, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Packages.Add(package);
        return true;
    }

    public string ToJson()
    {
        var sources = new JsonArray();
        foreach (var group in Packages.GroupBy(p => p.Source, StringComparer.OrdinalIgnoreCase))
        {
            var packages = new JsonArray();
            foreach (var package in group.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                var item = new JsonObject { ["PackageIdentifier"] = package.Id };
                if (!string.IsNullOrWhiteSpace(package.Version))
                {
                    item["Version"] = package.Version;
                }

                packages.Add(item);
            }

            var known = KnownSources.TryGetValue(group.Key, out var k) ? k : (Argument: "", Identifier: group.Key, Type: "");
            sources.Add(new JsonObject
            {
                ["Packages"] = packages,
                ["SourceDetails"] = new JsonObject
                {
                    ["Argument"] = known.Argument,
                    ["Identifier"] = known.Identifier,
                    ["Name"] = group.Key,
                    ["Type"] = known.Type
                }
            });
        }

        var root = new JsonObject
        {
            ["$schema"] = SchemaUrl,
            ["CreationDate"] = CreatedUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
            ["Sources"] = sources,
            ["WinGetVersion"] = "1.9"
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public void Save(string file)
    {
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(file, ToJson(), new System.Text.UTF8Encoding(false));
    }
}
