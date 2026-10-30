using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.App.ViewModels;
using CLEANit.App.Views;
using CLEANit.Platform.Windows;

namespace CLEANit.App;

public partial class App : Application
{
    private TrayController? _tray;
    private MainWindow? _mainWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var ui = UiSettings.Load();
            // No language saved yet (first start, after a reset): German if Windows shows German (display language, region or keyboard), else English.
            Loc.Apply(ui.Language ?? (OperatingSystem.IsWindows() ? SystemLanguage.Detect() : Loc.DefaultLanguage));

            var host = new UiHost();
            var viewModel = new MainViewModel(host, ui);
            _mainWindow = new MainWindow { DataContext = viewModel };
            host.MainWindow = _mainWindow;
            host.ShowMainWindow = ShowMainWindow;
            host.ExitApplication = Exit;
            _mainWindow.ExitRequested += Exit;

            _tray = new TrayController(this, viewModel, ShowMainWindow, Exit);
            desktop.Exit += (_, _) => _tray?.Dispose();

            StartShowSignalListener();
            if (!Program.StartInTray)
            {
                ShowMainWindow();
            }

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        var window = _mainWindow;
        if (!window.IsVisible)
        {
            window.Show();
            window.NotifyShown();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = window.DataContext is MainViewModel { Ui.WindowMaximized: true } ? WindowState.Maximized : WindowState.Normal;
        }

        window.Activate();
    }

    private void Exit()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        _desktop?.Shutdown();
    }

    /// <summary>A second app start signals "show window".</summary>
    private void StartShowSignalListener()
    {
        var signal = Program.ShowSignal;
        if (signal is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                {
                    Dispatcher.UIThread.Post(ShowMainWindow);
                }
            }
            catch (ObjectDisposedException)
            {
                // App is exiting.
            }
        })
        {
            IsBackground = true,
            Name = "CLEANit.ShowSignal"
        };
        thread.Start();
    }
}
