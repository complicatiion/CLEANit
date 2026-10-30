using CLEANit.Core.Abstractions;
using CLEANit.Core.Models;
using CLEANit.Core.Paths;

namespace CLEANit.Core.Engine;

public readonly record struct CleanProgress(int Rule, int RuleCount, string RuleName, long DeletedFiles);

/// <summary>
/// Deletes what an analysis found – only after the user confirmed it. Re-checks every file:
/// guard, protected names, no links, unchanged since the analysis. Locked files are skipped.
/// </summary>
public sealed class Cleaner(PlatformServices platform, TargetResolver resolver)
{
    private const int RecycleBatch = 500;
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    public async Task<CleanReport> CleanAsync(IReadOnlyList<RuleScanResult> selection, string area, IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        var report = new CleanReport { Area = area, StartedUtc = DateTime.UtcNow };
        await Task.Run(() =>
        {
            for (var i = 0; i < selection.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    report.Cancelled = true;
                    break;
                }

                var scan = selection[i];
                var result = new RuleCleanResult { RuleId = scan.Rule.Id, RuleName = scan.Rule.Name };
                report.Rules.Add(result);
                progress?.Report(new CleanProgress(i + 1, selection.Count, scan.Rule.Name, report.DeletedFiles));

                try
                {
                    CleanRule(scan, result, ct);
                }
                catch (OperationCanceledException)
                {
                    report.Cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    result.AddError(ex.Message);
                }
            }
        }, CancellationToken.None);

        report.FinishedUtc = DateTime.UtcNow;
        return report;
    }

    private void CleanRule(RuleScanResult scan, RuleCleanResult result, CancellationToken ct)
    {
        var rule = scan.Rule;
        if (Scanner.RunningProcesses(rule.Processes).Count > 0)
        {
            result.SkippedRunning = true;
            return;
        }

        if (rule.Action != CleanAction.None)
        {
            if (platform.Actions is null)
            {
                result.AddError("Action not available.");
                return;
            }

            result.DeletedBytes = platform.Actions.Run(rule.Action, ct);
            result.DeletedFiles = scan.Files;
            return;
        }

        var stopped = new List<string>();
        try
        {
            foreach (var service in rule.Services)
            {
                if (platform.Services?.StopIfRunning(service, ServiceTimeout) == true)
                {
                    stopped.Add(service);
                }
            }

            var (targets, _) = resolver.Resolve(rule);
            foreach (var target in targets)
            {
                if (rule.DeleteMode == DeleteMode.RecycleBin && platform.RecycleBin is not null)
                {
                    Recycle(target, scan.ScannedUtc, result, ct);
                }
                else
                {
                    DeletePermanently(target, scan.ScannedUtc, result, ct);
                }

                if (target.Target.RemoveEmptyDirs || target.Target.RemoveSelf)
                {
                    RemoveEmptyDirectories(target.Root, target.Target.RemoveSelf);
                }
            }
        }
        finally
        {
            // Original state is always restored.
            foreach (var service in stopped)
            {
                try
                {
                    platform.Services?.Start(service, ServiceTimeout);
                }
                catch (Exception ex)
                {
                    result.AddError($"Service {service}: {ex.Message}");
                }
            }
        }
    }

    private void DeletePermanently(ResolvedTarget target, DateTime cutoff, RuleCleanResult result, CancellationToken ct)
    {
        foreach (var match in resolver.Enumerate(target, cutoff, ct).ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(match.Path);
                if (!info.Exists)
                {
                    continue;
                }

                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LastWriteTimeUtc > cutoff)
                {
                    result.SkippedChanged++;
                    continue;
                }

                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }

                info.Delete();
                result.DeletedFiles++;
                result.DeletedBytes += match.Bytes;
            }
            catch (UnauthorizedAccessException)
            {
                result.SkippedDenied++;
            }
            catch (IOException)
            {
                // Sharing violation / lock: file in use.
                result.SkippedInUse++;
            }
        }
    }

    private void Recycle(ResolvedTarget target, DateTime cutoff, RuleCleanResult result, CancellationToken ct)
    {
        var batch = new List<FileMatch>(RecycleBatch);
        foreach (var match in resolver.Enumerate(target, cutoff, ct).ToList())
        {
            batch.Add(match);
            if (batch.Count == RecycleBatch)
            {
                Flush(batch, result);
            }
        }

        Flush(batch, result);
    }

    private void Flush(List<FileMatch> batch, RuleCleanResult result)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var failed = platform.RecycleBin!.Recycle(batch.Select(b => b.Path).ToList());
        foreach (var match in batch)
        {
            if (!File.Exists(match.Path))
            {
                result.DeletedFiles++;
                result.DeletedBytes += match.Bytes;
            }
        }

        result.SkippedInUse += Math.Max(0, failed);
        batch.Clear();
    }

    /// <summary>Empty sub folders bottom-up; the root only with REMOVESELF. Links are never entered.</summary>
    private void RemoveEmptyDirectories(string root, bool removeRoot)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (var dir in Directory.EnumerateDirectories(root, "*", options).OrderByDescending(d => d.Length))
            {
                TryRemoveEmpty(dir);
            }

            if (removeRoot && resolver.Guard.CheckUserTarget(root).Allowed)
            {
                TryRemoveEmpty(root);
            }
        }
        catch (Exception)
        {
            // Folder vanished or access denied.
        }
    }

    private static void TryRemoveEmpty(string dir)
    {
        try
        {
            if (!PathExpander.IsReparsePoint(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir, recursive: false);
            }
        }
        catch (Exception)
        {
            // In use or access denied.
        }
    }
}
