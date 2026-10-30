using CLEANit.Core.Abstractions;
using CLEANit.Core.Analysis;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;
using CLEANit.Core.Storage;
using CLEANit.Core.Tweaks;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.4: folder comparison and copy, cleaning totals, edition filter of the Windows settings.</summary>
public class CompareTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cleanit-cmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string root, string relative, string content, DateTime? time = null)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, time ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComparesFoldersBySizeAndTime()
    {
        var left = NewDir();
        var right = NewDir();
        try
        {
            Write(left, "same.txt", "hello");
            Write(right, "same.txt", "hello");
            Write(left, "size.txt", "short");
            Write(right, "size.txt", "much longer");
            Write(left, "time.txt", "abc", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            Write(right, "time.txt", "abc", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Write(left, "sub/only-left.txt", "x");
            Write(right, "sub/only-right.txt", "y");
            Write(left, "leftfolder/a.bin", "1234");
            Write(left, "leftfolder/deep/b.bin", "56");
            Write(left, "mixed", "file on the left");
            Directory.CreateDirectory(Path.Combine(right, "mixed"));

            var result = FolderCompare.Compare(left, right, byContent: false, null, CancellationToken.None);
            CompareEntry Get(string rel) => Assert.Single(result.Entries, e => e.RelativePath == rel.Replace('/', Path.DirectorySeparatorChar));

            Assert.Equal(CompareState.Identical, Get("same.txt").State);
            Assert.Equal(CompareReason.Size, Get("size.txt").Reason);
            var time = Get("time.txt");
            Assert.Equal(CompareReason.Time, time.Reason);
            Assert.True(time.LeftIsNewer == true);
            Assert.Equal(CompareState.LeftOnly, Get("sub/only-left.txt").State);
            Assert.Equal(CompareState.RightOnly, Get("sub/only-right.txt").State);

            // A folder that exists on one side only is a single entry with its totals.
            var folder = Get("leftfolder");
            Assert.True(folder.IsFolder);
            Assert.Equal(CompareState.LeftOnly, folder.State);
            Assert.Equal(2, folder.Files);
            Assert.Equal(6, folder.LeftBytes);
            Assert.DoesNotContain(result.Entries, e => e.RelativePath.StartsWith("leftfolder" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

            Assert.Equal(CompareReason.Type, Get("mixed").Reason);
            Assert.False(result.Cancelled);
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void ContentModeFindsChangedBytesWithSameSizeAndTime()
    {
        var left = NewDir();
        var right = NewDir();
        try
        {
            Write(left, "a.txt", "AAAA");
            Write(right, "a.txt", "AAAB");
            Write(left, "b.txt", "same");
            Write(right, "b.txt", "same");

            var quick = FolderCompare.Compare(left, right, byContent: false, null, CancellationToken.None);
            Assert.All(quick.Entries, e => Assert.Equal(CompareState.Identical, e.State));

            var exact = FolderCompare.Compare(left, right, byContent: true, null, CancellationToken.None);
            Assert.Equal(CompareReason.Content, Assert.Single(exact.Entries, e => e.Name == "a.txt").Reason);
            Assert.Equal(CompareState.Identical, Assert.Single(exact.Entries, e => e.Name == "b.txt").State);
            Assert.True(FolderCompare.SameContent(Path.Combine(left, "b.txt"), Path.Combine(right, "b.txt")));
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void CancelledCompareReturnsPartialResult()
    {
        var left = NewDir();
        var right = NewDir();
        try
        {
            Write(left, "a.txt", "1");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = FolderCompare.Compare(left, right, false, null, cts.Token);
            Assert.True(result.Cancelled);
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void CopyRespectsGuardAndOverwrite()
    {
        var left = NewDir();
        var right = NewDir();
        try
        {
            Write(left, "new.txt", "new");
            Write(left, "both.txt", "left version");
            Write(right, "both.txt", "right");
            Write(left, "folder/x.txt", "x");
            var entries = FolderCompare.Compare(left, right, false, null, CancellationToken.None).Entries
                .Where(e => e.State != CompareState.Identical).ToList();
            var guard = new PathGuard([new UserProfile("tester", Path.Combine(KnownFolders.UsersRoot, "tester"))]);

            var result = FolderCopy.Copy(left, right, entries, overwrite: false, guard, null, CancellationToken.None);
            if (!OperatingSystem.IsWindows())
            {
                // The guard only accepts local Windows paths: nothing is written anywhere else.
                Assert.Equal(0, result.Files);
                Assert.NotEmpty(result.Errors);
                Assert.False(File.Exists(Path.Combine(right, "new.txt")));
                return;
            }

            Assert.True(File.Exists(Path.Combine(right, "new.txt")));
            Assert.True(File.Exists(Path.Combine(right, "folder", "x.txt")));
            Assert.Equal("right", File.ReadAllText(Path.Combine(right, "both.txt")));
            Assert.Equal(1, result.Skipped);

            FolderCopy.Copy(left, right, entries, overwrite: true, guard, null, CancellationToken.None);
            Assert.Equal("left version", File.ReadAllText(Path.Combine(right, "both.txt")));
        }
        finally
        {
            Directory.Delete(left, true);
            Directory.Delete(right, true);
        }
    }

    [Fact]
    public void TotalsStartFromExistingReports()
    {
        var totals = CleaningStats.FromReports(
        [
            new HistoryEntry(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), "System", 1000, 10, null),
            new HistoryEntry(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "Browser", 500, 5, null)
        ]);
        Assert.Equal(2, totals.Runs);
        Assert.Equal(1500, totals.Bytes);
        Assert.Equal(15, totals.Files);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), totals.SinceUtc);
        Assert.Null(CleaningStats.FromReports([]).SinceUtc);
    }

    [Fact]
    public void SpotlightPolicyOnlyOnEnterprise()
    {
        Assert.Contains(TweakCatalog.For(26100, expert: false, enterprise: true), t => t.Id == "lockscreen.spotlight");
        Assert.DoesNotContain(TweakCatalog.For(26100, expert: false, enterprise: false), t => t.Id == "lockscreen.spotlight");
        Assert.Contains(TweakCatalog.For(26100, expert: false, enterprise: false), t => t.Id == "lockscreen.tips");
        Assert.DoesNotContain(TweakCatalog.For(19045, expert: false), t => t.Id is "privacy.diaglogs" or "privacy.dumps" or "taskbar.touchkeyboard");
        Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.All(TweakCatalog.All, t => Assert.StartsWith("https://", t.DocUrl));
    }
}
