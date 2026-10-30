using Avalonia;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App;

internal static class Program
{
    /// <summary>Signal to the running instance: show window.</summary>
    internal static EventWaitHandle? ShowSignal { get; private set; }

    internal static bool StartInTray { get; private set; }

    /// <summary>Settings › Reset CLEANit was carried out at this start (null = no reset).</summary>
    internal static IReadOnlyList<string>? ResetFailures { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        // Command line for the installer (no window).
        if (OperatingSystem.IsWindows())
        {
            if (Has("--register-autostart"))
            {
                return SelfAutostart.Register(SystemInfo.CurrentUser(), Environment.ProcessPath ?? "") ? 0 : 1;
            }

            if (Has("--unregister-autostart"))
            {
                return SelfAutostart.Unregister(SystemInfo.CurrentUser()) ? 0 : 1;
            }

            if (Has("--unregister-autostart-all"))
            {
                SelfAutostart.UnregisterAll();
                return 0;
            }
        }

        // Command line (analysis, cleaning with --confirm): no window, no single-instance check.
        if (Cli.CliRunner.IsCliCall(args))
        {
            return Cli.CliRunner.Run(args);
        }

        StartInTray = Has("--tray");

        // Settings › Reset CLEANit: the old instance restarts CLEANit with --reset=<parts> and exits. Wait until it has
        // released the single-instance mutex, then delete the selected data before anything is opened.
        var reset = AppDataReset.Parse(args);
        if (reset != ResetParts.None && OperatingSystem.IsWindows())
        {
            using var previous = new Mutex(false, @"Local\CLEANit.App");
            var owned = false;
            try
            {
                owned = previous.WaitOne(TimeSpan.FromSeconds(20));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            try
            {
                // The old instance did not end in time: do not delete data it is still using; it will just be shown.
                if (!owned)
                {
                    reset = ResetParts.None;
                }

                if (reset.HasFlag(ResetParts.Settings))
                {
                    SelfAutostart.Unregister(SystemInfo.CurrentUser());
                }

                if (reset != ResetParts.None)
                {
                    ResetFailures = AppDataReset.Run(reset);
                }
            }
            finally
            {
                if (owned)
                {
                    previous.ReleaseMutex();
                }
            }
        }

        Mutex? mutex = null;
        if (OperatingSystem.IsWindows())
        {
            // One instance per session; a second start brings the window to front.
            mutex = new Mutex(true, @"Local\CLEANit.App", out var createdNew);
            if (!createdNew)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(@"Local\CLEANit.App.Show");
                    existing.Set();
                }
                catch
                {
                    // First instance not ready yet.
                }

                mutex.Dispose();
                return 0;
            }

            ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CLEANit.App.Show");
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            ShowSignal?.Dispose();
            mutex?.ReleaseMutex();
            mutex?.Dispose();
        }
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
