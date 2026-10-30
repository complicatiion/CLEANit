using System.Diagnostics;
using CLEANit.Core.Abstractions;
using CLEANit.Core.Models;

namespace CLEANit.Core.Engine;

public readonly record struct ScanProgress(int Done, int Total, string Current);

/// <summary>Analysis: read only, computes what a cleaning run would remove.</summary>
public sealed class Scanner(PlatformServices platform, TargetResolver resolver)
{
    private const int SampleCount = 200;

    public async Task<IReadOnlyList<RuleScanResult>> ScanAsync(IReadOnlyList<CleanRule> rules, IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var results = new RuleScanResult?[rules.Count];
        var done = 0;
        // No token in the options: cancelling ends the loop quietly; callers check the token afterwards.
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6) };

        await Parallel.ForEachAsync(Enumerable.Range(0, rules.Count), parallel, (i, _) =>
        {
            if (ct.IsCancellationRequested)
            {
                return ValueTask.CompletedTask;
            }

            results[i] = ScanRule(rules[i], ct);
            var count = Interlocked.Increment(ref done);
            progress?.Report(new ScanProgress(count, rules.Count, rules[i].Name));
            return ValueTask.CompletedTask;
        });

        return results.OfType<RuleScanResult>().ToList();
    }

    public RuleScanResult ScanRule(CleanRule rule, CancellationToken ct)
    {
        var scanned = DateTime.UtcNow;
        var running = RunningProcesses(rule.Processes);

        if (rule.Action != CleanAction.None)
        {
            var estimate = platform.Actions?.Estimate(rule.Action) ?? new ActionEstimate(0, 0, false, false);
            return new RuleScanResult
            {
                Rule = rule,
                Bytes = estimate.Available ? estimate.Bytes : 0,
                Files = estimate.Available ? estimate.Items : 0,
                SizeUnknown = estimate.Available && estimate.Unknown,
                ScannedUtc = scanned,
                RunningProcesses = running
            };
        }

        var (allowed, blocked) = resolver.Resolve(rule);
        long bytes = 0, files = 0;
        var samples = new PriorityQueue<FileSample, long>();
        foreach (var target in allowed)
        {
            foreach (var match in resolver.Enumerate(target, scanned, ct))
            {
                bytes += match.Bytes;
                files++;
                samples.Enqueue(new FileSample(match.Path, match.Bytes, match.LastWriteUtc), match.Bytes);
                if (samples.Count > SampleCount)
                {
                    samples.Dequeue();
                }
            }
        }

        var top = new List<FileSample>(samples.Count);
        while (samples.Count > 0)
        {
            top.Add(samples.Dequeue());
        }

        top.Reverse();
        return new RuleScanResult
        {
            Rule = rule,
            Bytes = bytes,
            Files = files,
            ScannedUtc = scanned,
            Samples = top,
            Roots = allowed.Select(a => a.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Blocked = blocked,
            RunningProcesses = running
        };
    }

    public static IReadOnlyList<string> RunningProcesses(IReadOnlyList<string> names)
    {
        var running = new List<string>();
        foreach (var name in names)
        {
            try
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    running.Add(name);
                }

                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
            catch (Exception)
            {
                // Access denied for some system processes: ignore.
            }
        }

        return running;
    }
}
