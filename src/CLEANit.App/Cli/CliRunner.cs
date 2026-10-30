using System.Runtime.InteropServices;
using CLEANit.App.Services;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Rules;
using CLEANit.Core.Software;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.Cli;

/// <summary>Analysis report written by --report.</summary>
public sealed class CliReport
{
    public string Version { get; init; } = "";

    public string Machine { get; init; } = Environment.MachineName;

    public DateTime TimeUtc { get; init; } = DateTime.UtcNow;

    public string Profile { get; init; } = "";

    public string Area { get; init; } = "";

    public bool AllProfiles { get; init; }

    public long TotalBytes { get; init; }

    public long TotalFiles { get; init; }

    public List<CliRuleResult> Rules { get; init; } = [];

    public CleanReport? Clean { get; set; }
}

public sealed record CliRuleResult(string Id, string Name, string Area, string Risk, long Bytes, long Files, bool SizeUnknown, IReadOnlyList<string> Running);

/// <summary>
/// Command line without a window: --analyze never deletes; --clean deletes only together with --confirm.
/// Exit codes: 0 success, 1 errors or nothing done, 2 invalid arguments.
/// </summary>
public static class CliRunner
{
    public const string UsageText =
        """
        CLEANit command line

          CLEANit.exe --analyze [options]          analyze only, nothing is deleted
          CLEANit.exe --clean --confirm [options]  analyze, then clean the selection
          CLEANit.exe --list-profiles              list selection profiles
          CLEANit.exe --export-bundle <file.json>  installed winget packages as bundle (winget export format)
          CLEANit.exe --install-bundle <file.json> [--confirm] [--machine]
                                                   show the bundle; installs only with --confirm
          CLEANit.exe --help                       this help

        Options
          --profile <id|name>    selection profile (default: recommended)
          --area <area>          all | system | browser | developer (default: all)
          --all-profiles         all local user profiles (administrator)
          --signed-out           only the profiles of signed-out users (terminal servers; implies --all-profiles)
          --report <file.json>   write the result as JSON
          --machine              install for all users (bundles)
          --accept-source-agreements
                                 accept the winget source agreements (else: accepted in the app)
        """;

    private static readonly string[] Commands =
        ["--analyze", "--clean", "--list-profiles", "--export-bundle", "--install-bundle", "--help", "-h", "-?", "/?"];

    public static bool IsCliCall(string[] args) => args.Any(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        AttachToParentConsole();
        try
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
        finally
        {
            Console.Out.Flush();
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        string? Value(string flag)
        {
            var i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
        }

        var ui = UiSettings.Load();
        if (Has("--help") || Has("-h") || Has("-?") || Has("/?"))
        {
            Console.WriteLine(UsageText);
            return 0;
        }

        if (Has("--list-profiles"))
        {
            foreach (var p in SelectionProfiles.All(ui.CustomProfiles, expert: true))
            {
                Console.WriteLine($"{p.Id,-28} {p.Name}");
            }

            return 0;
        }

        if (Has("--export-bundle") || Has("--install-bundle"))
        {
            return await BundleAsync(args, ui, Has, Value);
        }

        var clean = Has("--clean");
        var confirm = Has("--confirm");
        var profileArg = Value("--profile") ?? SelectionProfiles.RecommendedId;
        var profile = SelectionProfiles.Find(profileArg, ui.CustomProfiles);
        if (profile is null)
        {
            Console.Error.WriteLine($"Unknown profile: {profileArg}. Use --list-profiles.");
            return 2;
        }

        var areaArg = (Value("--area") ?? "all").ToLowerInvariant();
        CleanArea? area = null;
        switch (areaArg)
        {
            case "all":
                break;
            case "system":
                area = CleanArea.System;
                break;
            case "browser":
                area = CleanArea.Browser;
                break;
            case "developer" or "development":
                area = CleanArea.Developer;
                break;
            default:
                Console.Error.WriteLine($"Unknown area: {areaArg}.");
                return 2;
        }

        if (Has("--report") && Value("--report") is null)
        {
            Console.Error.WriteLine("--report needs a file name.");
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var signedOut = Has("--signed-out");
        var allProfiles = Has("--all-profiles") || signedOut;
        EngineHost.InitializeReports();
        var engine = new EngineHost(ui, allProfiles);
        await engine.ReloadAsync(includeExpert: true);
        foreach (var error in engine.Catalog?.Errors ?? [])
        {
            Console.Error.WriteLine("Rules: " + error);
        }

        var rules = engine.RulesFor(area).Where(profile.Includes).ToList();
        Console.WriteLine($"CLEANit {AppEnvironment.Version} · profile {profile.Id} · area {areaArg} · {rules.Count} rules"
                          + (signedOut ? " · signed-out user profiles" : allProfiles ? " · all user profiles" : ""));
        if (rules.Count == 0)
        {
            Console.WriteLine("No matching rules.");
            return 1;
        }

        var targets = signedOut && OperatingSystem.IsWindows()
            ? engine.Platform.Profiles.All().Where(p => !ServerProfiles.LoadedProfilePaths().Contains(p.ProfilePath)).ToList()
            : engine.Profiles;
        var results = await engine.CreateScanner(targets).ScanAsync(rules, null, cts.Token);
        if (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 1;
        }

        var found = results.Where(r => r.HasContent).OrderByDescending(r => r.Bytes).ToList();
        foreach (var r in found)
        {
            var size = r.SizeUnknown ? "unknown" : ByteSize.Format(r.Bytes);
            var running = r.RunningProcesses.Count > 0 ? $"  [skipped: {string.Join(", ", r.RunningProcesses)} running]" : "";
            Console.WriteLine($"  {size,12}  {r.Files,9:N0}  {r.Rule.Name}{running}");
        }

        var totalBytes = found.Sum(r => r.Bytes);
        var totalFiles = found.Sum(r => r.Files);
        Console.WriteLine($"Total: {ByteSize.Format(totalBytes)} in {totalFiles:N0} files ({found.Count} rules with content).");

        var report = new CliReport
        {
            Version = AppEnvironment.Version,
            Profile = profile.Id,
            Area = areaArg,
            AllProfiles = allProfiles,
            TotalBytes = totalBytes,
            TotalFiles = totalFiles,
            Rules = found.Select(r => new CliRuleResult(r.Rule.Id, r.Rule.Name, r.Rule.Area.ToString(), r.Rule.Risk.ToString(), r.Bytes, r.Files, r.SizeUnknown,
                r.RunningProcesses)).ToList()
        };

        var exit = 0;
        if (clean && !confirm)
        {
            Console.WriteLine("Nothing deleted: add --confirm to clean.");
            exit = 1;
        }
        else if (clean && found.Count > 0)
        {
            var progress = new Progress<CleanProgress>(p => Console.WriteLine($"  [{p.Rule}/{p.RuleCount}] {p.RuleName}"));
            var cleanReport = await engine.CreateCleaner(targets).CleanAsync(found, signedOut ? "Server" : area?.ToString() ?? "Full", progress, cts.Token);
            cleanReport.Profile = profile.Id + " (cli)";
            ReportStore.Save(cleanReport);
            report.Clean = cleanReport;
            Console.WriteLine($"Freed: {ByteSize.Format(cleanReport.DeletedBytes)} · {cleanReport.DeletedFiles:N0} files · {cleanReport.Skipped:N0} skipped"
                              + (cleanReport.Cancelled ? " · cancelled" : ""));
            foreach (var error in cleanReport.Rules.SelectMany(r => r.Errors.Select(e => $"{r.RuleName}: {e}")).Take(20))
            {
                Console.Error.WriteLine("  " + error);
            }

            if (cleanReport.Cancelled || cleanReport.Rules.Any(r => r.Errors.Count > 0))
            {
                exit = 1;
            }
        }

        if (Value("--report") is { } file)
        {
            var path = Path.GetFullPath(file);
            JsonFile.Write(path, report);
            Console.WriteLine("Report: " + path);
        }

        return exit;
    }

    /// <summary>Package bundles in the native winget format: export the installed packages, or install a bundle (only with --confirm).</summary>
    private static async Task<int> BundleAsync(string[] args, UiSettings ui, Func<string, bool> has, Func<string, string?> value)
    {
        if (!OperatingSystem.IsWindows() || await WinGetClient.VersionAsync(CancellationToken.None) is not { } version)
        {
            Console.Error.WriteLine("winget is not available on this PC (App Installer missing).");
            return 1;
        }

        var accept = ui.WingetAgreementsAccepted || has("--accept-source-agreements");
        if (!accept)
        {
            Console.Error.WriteLine("The winget source agreements are not accepted yet: accept them once in CLEANit or add --accept-source-agreements.");
            return 2;
        }

        if (has("--export-bundle"))
        {
            if (value("--export-bundle") is not { } target)
            {
                Console.Error.WriteLine("--export-bundle needs a file name.");
                return 2;
            }

            var path = Path.GetFullPath(target);
            var (ok, output) = await WinGetClient.ExportAsync(path, accept, CancellationToken.None);
            Console.WriteLine(ok ? $"Bundle written: {path} (winget {version})" : output);
            return ok ? 0 : 1;
        }

        if (value("--install-bundle") is not { } file || !File.Exists(file))
        {
            Console.Error.WriteLine("--install-bundle needs an existing file.");
            return 2;
        }

        PackageBundle bundle;
        try
        {
            bundle = PackageBundle.Load(file);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Not a winget package list: " + ex.Message);
            return 2;
        }

        var (installed, _) = await WinGetClient.ListInstalledAsync(accept, CancellationToken.None);
        var known = installed.Where(p => p.IsKnown).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = bundle.Packages.Where(p => !known.Contains(p.Id)).ToList();
        Console.WriteLine($"Bundle \"{bundle.Name}\": {bundle.Packages.Count} packages, {bundle.Packages.Count - missing.Count} installed, {missing.Count} new.");
        foreach (var package in bundle.Packages)
        {
            Console.WriteLine($"  {(known.Contains(package.Id) ? "installed" : "new      ")}  {package.Id}{(package.Version is null ? "" : " " + package.Version)}  [{package.Source}]");
        }

        if (missing.Count == 0)
        {
            return 0;
        }

        if (!has("--confirm"))
        {
            Console.WriteLine("Nothing installed: add --confirm to install the new packages (their licenses apply).");
            return 1;
        }

        var machine = has("--machine");
        var failed = 0;
        foreach (var package in missing)
        {
            Console.WriteLine($"Installing {package.Id} …");
            var (ok, output) = await WinGetClient.InstallAsync(package.Id, new InstallOptions(package.Version, machine, package.Source), accept, CancellationToken.None);
            Console.WriteLine(ok ? "  done" : "  failed: " + output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim());
            failed += ok ? 0 : 1;
        }

        Console.WriteLine($"{missing.Count - failed} of {missing.Count} installed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>CLEANit is a window app; output goes to the console it was started from.</summary>
    private static void AttachToParentConsole()
    {
        if (!OperatingSystem.IsWindows() || !Native.AttachConsole(-1))
        {
            return;
        }

        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            }

            if (!Console.IsErrorRedirected)
            {
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }

            Console.WriteLine();
        }
        catch (Exception)
        {
            // No console available.
        }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachConsole(int processId);
    }
}
