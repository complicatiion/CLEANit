using System.Text;
using CLEANit.Core.Analysis;
using CLEANit.Core.Models;
using CLEANit.Core.Rules;
using CLEANit.Platform.Windows;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.2 features: profiles, expert rules, DISM parser, release check, file types.</summary>
public class FeatureTests
{
    [Fact]
    public void ProfilesMatchRuleIdsWithWildcards()
    {
        var daily = SelectionProfiles.Find("daily", null)!;
        Assert.True(daily.Includes(new CleanRule { Id = "browser.edge.cache" }));
        Assert.True(daily.Includes(new CleanRule { Id = "sys.temp.user" }));
        Assert.False(daily.Includes(new CleanRule { Id = "browser.edge.cookies" }));
    }

    [Fact]
    public void RecommendedProfileUsesRuleDefaults()
    {
        var recommended = SelectionProfiles.Find(SelectionProfiles.RecommendedId, null)!;
        Assert.True(recommended.Includes(new CleanRule { Id = "x", Selected = true }));
        Assert.False(recommended.Includes(new CleanRule { Id = "x", Selected = false }));
    }

    [Fact]
    public void ImagingProfileIsExpertOnly()
    {
        Assert.DoesNotContain(SelectionProfiles.All(null, expert: false), p => p.Id == SelectionProfiles.ImagingId);
        Assert.Contains(SelectionProfiles.All(null, expert: true), p => p.Id == SelectionProfiles.ImagingId);
        Assert.NotNull(SelectionProfiles.Find("Master-Image vorbereiten (MCS/PVS)", null));
    }

    [Fact]
    public void CustomProfilesAreFoundByName()
    {
        var custom = new Dictionary<string, List<string>> { ["Weekly"] = ["sys.temp.*"] };
        var profile = SelectionProfiles.Find("weekly", custom);
        Assert.NotNull(profile);
        Assert.Equal("custom:Weekly", profile.Id);
        Assert.False(profile.BuiltIn);
    }

    [Fact]
    public void ExpertRulesAreMarked()
    {
        var rules = RuleCatalog.LoadBuiltin();
        Assert.True(Assert.Single(rules, r => r.Id == "sys.eventlogs").Expert);
        Assert.All(rules.Where(r => r.Expert), r => Assert.False(r.Selected));
    }

    [Fact]
    public void CustomRulesCannotRunActions()
    {
        const string json = """
            { "area": "System", "rules": [
              { "id": "custom.bad", "name": "Bad", "action": "EmptyRecycleBin" },
              { "id": "custom.ok", "name": "Ok", "targets": [ { "path": "%LocalAppData%\\Vendor\\Cache", "pattern": "*" } ] } ] }
            """;
        var errors = new List<string>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var rules = RuleCatalog.ReadRuleFile(stream, "test.json", RuleSource.Custom, errors);
        Assert.Equal("custom.ok", Assert.Single(rules).Id);
        Assert.Contains(errors, e => e.Contains("reserved"));
    }

    [Fact]
    public void DismOutputIsParsed()
    {
        const string output = "Component Store (WinSxS) information:\n\n" +
                              "Windows Explorer Reported Size of Component Store : 9.21 GB\n\n" +
                              "Actual Size of Component Store : 8.85 GB\n\n" +
                              "    Shared with Windows : 6.10 GB\n" +
                              "    Backups and Disabled Features : 2.50 GB\n" +
                              "    Cache and Temporary Data :  250.00 MB\n\n" +
                              "Component Store Cleanup Recommended : Yes\n";
        var info = DismAnalyzer.Parse(output);
        Assert.NotNull(info);
        Assert.Equal("Yes", info.Recommended);
        Assert.InRange(info.Reclaimable, (long)(2.74 * 1024 * 1024 * 1024), (long)(2.76 * 1024 * 1024 * 1024));
    }

    [Theory]
    [InlineData("0.0.3", "0.0.2", true)]
    [InlineData("0.1.0", "0.0.9", true)]
    [InlineData("0.0.2", "0.0.2", false)]
    [InlineData("0.0.1", "0.0.2", false)]
    [InlineData("0.0.3-beta", "0.0.2+abc", true)]
    public void ReleaseVersionsAreCompared(string candidate, string current, bool newer) =>
        Assert.Equal(newer, ReleaseChecker.IsNewer(candidate, current));

    [Fact]
    public async Task FileTypesAreSummed()
    {
        var dir = Directory.CreateTempSubdirectory("cleanit-types-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "a.log"), new byte[300]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "b.LOG"), new byte[200]);
            Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "sub", "c.bin"), new byte[100]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "noext"), new byte[50]);

            var types = await DiskUsage.FileTypesAsync(dir.FullName, 10, null, CancellationToken.None);

            Assert.Equal(".log", types[0].Extension);
            Assert.Equal(500, types[0].Bytes);
            Assert.Equal(2, types[0].Files);
            Assert.Contains(types, t => t.Extension == "" && t.Bytes == 50);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public async Task CancelledFileTypeScanReturnsWithoutException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var types = await DiskUsage.FileTypesAsync(Path.GetTempPath(), 10, null, cts.Token);
        Assert.Empty(types);
    }
}
