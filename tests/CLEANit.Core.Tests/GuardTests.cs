using CLEANit.Core.Abstractions;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>The safety guard works on Windows paths; tests return early elsewhere.</summary>
public class GuardTests
{
    private static readonly CleanTarget Broad = new() { Path = "x", Pattern = "*", Recursive = true };
    private static readonly CleanTarget Logs = new() { Path = "x", Pattern = "*.log", Recursive = false };

    private static (PathGuard Guard, UserProfile Profile) Create()
    {
        var profile = new UserProfile("tester", Path.Combine(KnownFolders.UsersRoot, "tester"));
        return (new PathGuard([profile], [@"C:\KeepMe"]), profile);
    }

    [Fact]
    public void SystemFoldersAreRefused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (guard, _) = Create();
        Assert.False(guard.CheckRuleTarget(@"C:\", Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(KnownFolders.Windows, Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(Path.Combine(KnownFolders.Windows, "System32"), Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(Path.Combine(KnownFolders.Windows, "WinSxS"), Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(KnownFolders.ProgramFiles, Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(Path.Combine(KnownFolders.ProgramFiles, "Vendor"), Broad).Allowed);
    }

    [Fact]
    public void CacheFoldersAreAllowed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (guard, profile) = Create();
        Assert.True(guard.CheckRuleTarget(Path.Combine(KnownFolders.Windows, "Temp"), Broad).Allowed);
        Assert.True(guard.CheckRuleTarget(Path.Combine(KnownFolders.Windows, "SoftwareDistribution", "Download"), Broad).Allowed);
        Assert.True(guard.CheckRuleTarget(profile.Temp, Broad).Allowed);
        Assert.True(guard.CheckRuleTarget(Path.Combine(profile.LocalAppData, "Vendor", "Cache"), Broad).Allowed);
        Assert.True(guard.CheckRuleTarget(Path.Combine(KnownFolders.ProgramFiles, "Vendor"), Logs).Allowed);
        Assert.True(guard.CheckRuleTarget(KnownFolders.Windows, new CleanTarget { Pattern = "MEMORY.DMP", Recursive = false }).Allowed);
    }

    [Fact]
    public void UserLibrariesCredentialsAndExclusionsAreRefused()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (guard, profile) = Create();
        Assert.False(guard.CheckRuleTarget(Path.Combine(profile.ProfilePath, "Documents"), Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(Path.Combine(profile.ProfilePath, "OneDrive - Company", "x"), Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(Path.Combine(profile.ProfilePath, ".ssh"), Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(profile.ProfilePath, Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(@"C:\KeepMe\Cache", Broad).Allowed);
        Assert.False(guard.CheckRuleTarget(@"\\server\share\cache", Broad).Allowed);
    }

    [Fact]
    public void ProtectedFilesAreRecognized()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (guard, profile) = Create();
        Assert.True(guard.IsProtectedFile(@"C:\x\User Data\Default\Login Data"));
        Assert.True(guard.IsProtectedFile(@"C:\x\Profiles\abc\places.sqlite"));
        Assert.True(guard.IsProtectedFile(@"C:\x\mail.pst"));
        Assert.True(guard.IsProtectedFile(@"C:\KeepMe\file.tmp"));
        Assert.True(guard.IsProtectedFile(Path.Combine(profile.ProfilePath, ".ssh", "known_hosts")));
        Assert.False(guard.IsProtectedFile(Path.Combine(profile.Temp, "file.tmp")));
    }

    [Fact]
    public void UserTargetsAllowAppFoldersButNotSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (guard, profile) = Create();
        Assert.True(guard.CheckUserTarget(Path.Combine(KnownFolders.ProgramFiles, "Old App")).Allowed);
        Assert.True(guard.CheckUserTarget(Path.Combine(profile.ProfilePath, "source", "repos", "App", "bin")).Allowed);
        Assert.False(guard.CheckUserTarget(Path.Combine(KnownFolders.Windows, "Temp")).Allowed);
        Assert.False(guard.CheckUserTarget(KnownFolders.ProgramFiles).Allowed);
        Assert.False(guard.CheckUserTarget(profile.LocalAppData).Allowed);
    }
}
