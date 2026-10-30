using CLEANit.Core.Storage;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.6: Settings › Reset CLEANit.</summary>
public class ResetTests
{
    [Fact]
    public void Argument_RoundTrips()
    {
        var parts = ResetParts.Settings | ResetParts.Caches;
        Assert.Equal(parts, AppDataReset.Parse(["--tray", AppDataReset.ToArgument(parts)]));
        Assert.Equal(ResetParts.None, AppDataReset.Parse(["--tray"]));
        Assert.Equal(ResetParts.All, AppDataReset.Parse(["--reset=31"]));
    }

    [Fact]
    public void Targets_StayInsideOwnFolders()
    {
        var data = Path.Combine(Path.GetTempPath(), "cleanit-reset-data");
        var settings = Path.Combine(Path.GetTempPath(), "cleanit-reset-appdata", "settings.json");
        var reports = AppDataReset.Targets(ResetParts.Reports, data, settings);
        Assert.All(reports, t => Assert.StartsWith(data, t));
        Assert.False(reports.Contains(Path.Combine(data, "backups")));
        Assert.True(AppDataReset.Targets(ResetParts.Backups, data, settings).Contains(Path.Combine(data, "backups")));
        Assert.True(new[] { settings }.SequenceEqual(AppDataReset.Targets(ResetParts.Settings, data, settings)));
        Assert.True(new[] { data, settings, Path.GetDirectoryName(settings)! }.SequenceEqual(AppDataReset.Targets(ResetParts.All, data, settings)));
    }

    [Fact]
    public void Run_DeletesOnlySelectedParts()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanit-reset-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        var settings = Path.Combine(data, "settings.json");
        Directory.CreateDirectory(Path.Combine(data, "logs"));
        Directory.CreateDirectory(Path.Combine(data, "backups"));
        File.WriteAllText(Path.Combine(data, "logs", "a.json"), "{}");
        File.WriteAllText(Path.Combine(data, "backups", "b.json"), "{}");
        File.WriteAllText(Path.Combine(data, "stats.json"), "{}");
        File.WriteAllText(settings, "{}");
        try
        {
            Assert.Empty(AppDataReset.Run(ResetParts.Reports | ResetParts.Settings, data, settings));
            Assert.False(Directory.Exists(Path.Combine(data, "logs")));
            Assert.False(File.Exists(Path.Combine(data, "stats.json")));
            Assert.False(File.Exists(settings));
            Assert.True(File.Exists(Path.Combine(data, "backups", "b.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
