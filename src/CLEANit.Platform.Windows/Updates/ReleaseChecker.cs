using System.Net.Http.Headers;
using System.Text.Json;

namespace CLEANit.Platform.Windows;

public sealed record ReleaseInfo(string Version, string Url);

/// <summary>Opt-in check for a newer CLEANit release on GitHub. Only the request itself is sent; nothing is downloaded.</summary>
public static class ReleaseChecker
{
    public const string LatestApi = "https://api.github.com/repos/complicatiion/CLEANit/releases/latest";

    public static async Task<ReleaseInfo?> GetNewerAsync(string currentVersion, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CLEANit", currentVersion));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.GetAsync(LatestApi, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
        if (tag is null || url is null || !url.StartsWith("https://github.com/complicatiion/CLEANit/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return IsNewer(tag.TrimStart('v', 'V'), currentVersion) ? new ReleaseInfo(tag.TrimStart('v', 'V'), url) : null;
    }

    internal static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate.Split('-')[0], out var a) && Version.TryParse(current.Split('-', '+')[0], out var b) && a > b;
}
