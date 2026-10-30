using CLEANit.Core.Analysis;
using CLEANit.Core.Models;
using CLEANit.Core.Rules;
using CLEANit.Platform.Windows;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.5: tree corrections after deleting, startup commands, protected services, terminal server profile.</summary>
public class ServerTests
{
    [Fact]
    public async Task TreeTotalsFollowDeletedFoldersAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanit-tree-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "deep"));
            Directory.CreateDirectory(Path.Combine(root, "b"));
            File.WriteAllBytes(Path.Combine(root, "a", "one.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(root, "a", "deep", "two.bin"), new byte[50]);
            File.WriteAllBytes(Path.Combine(root, "b", "three.bin"), new byte[30]);

            var tree = await FolderTree.ScanAsync(root, null, CancellationToken.None);
            Assert.Equal(180, tree.Bytes);
            Assert.Equal(3, tree.Files);
            Assert.Equal(3, tree.Folders);

            var a = Assert.Single(tree.Children, c => c.Name == "a");
            FolderTree.RemoveFolder(a);
            Assert.Equal(30, tree.Bytes);
            Assert.Equal(1, tree.Files);
            Assert.Equal(1, tree.Folders);
            Assert.DoesNotContain(tree.Children, c => c.Name == "a");

            var b = Assert.Single(tree.Children);
            var file = Assert.Single(b.LargestFiles);
            FolderTree.RemoveFile(b, file);
            Assert.Equal(0, b.Bytes);
            Assert.Equal(0, tree.Bytes);
            Assert.Equal(0, tree.Files);

            // Removing twice changes nothing.
            FolderTree.RemoveFile(b, file);
            Assert.Equal(0, tree.Bytes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StartupCommandsAreQuoted()
    {
        Assert.Equal("\"C:\\Tools\\app.exe\" --tray", StartupManager.BuildCommand(@"C:\Tools\app.exe", " --tray "));
        Assert.Equal("\"C:\\Tools\\app.exe\"", StartupManager.BuildCommand(@"C:\Tools\app.exe", null));
        Assert.Equal("cmd.exe /c \"\"C:\\My Scripts\\start.cmd\" /q\"", StartupManager.BuildCommand(@"C:\My Scripts\start.cmd", "/q"));
    }

    [Fact]
    public void CoreServicesAreProtected()
    {
        Assert.True(ServicesManager.IsProtected("RpcSs"));
        Assert.True(ServicesManager.IsProtected("termservice"));
        Assert.False(ServicesManager.IsProtected("Spooler"));
        Assert.Equal("'it''s'", ServicesManager.Quote("it's"));
    }

    [Fact]
    public void TerminalServerProfileSelectsSafeUserCaches()
    {
        var rds = SelectionProfiles.Find(SelectionProfiles.RdsId, null);
        Assert.NotNull(rds);
        Assert.True(rds.Expert);
        Assert.True(rds.Includes(new CleanRule { Id = "browser.edge.cache", Name = "x", Risk = RiskLevel.Safe }));
        Assert.True(rds.Includes(new CleanRule { Id = "sys.temp.user", Name = "x", Risk = RiskLevel.Safe }));
        Assert.False(rds.Includes(new CleanRule { Id = "browser.edge.cookies", Name = "x", Risk = RiskLevel.Deep }));
        Assert.False(rds.Includes(new CleanRule { Id = "sys.recyclebin", Name = "x", Risk = RiskLevel.Moderate }));
        Assert.False(rds.Includes(new CleanRule { Id = "sys.windowsold", Name = "x", Risk = RiskLevel.Deep }));
    }

    [Fact]
    public void SystemFoldersBelowUsersAreNotProfiles()
    {
        Assert.True(ServerProfiles.IsSystemFolder("Public"));
        Assert.True(ServerProfiles.IsSystemFolder("Default User"));
        Assert.False(ServerProfiles.IsSystemFolder("local_jdoe"));
        Assert.True(ServerProfiles.IsSystemFolder("UMFD-0"));
        Assert.True(ServerProfiles.IsSystemFolder("TEMP.Font Driver Host.000"));
        Assert.True(ServerProfiles.IsSystemFolder("DWM-1"));
        Assert.True(ServerProfiles.IsSystemFolder("TEMP.UMFD-0"));
        Assert.False(ServerProfiles.IsSystemFolder("TEMP"));
    }
}
