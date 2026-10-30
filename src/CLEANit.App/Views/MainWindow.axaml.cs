using Avalonia.Controls;
using Avalonia.Threading;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Services.WindowChrome.Apply(this, maximize: false);

        // Notifications slide in from the right.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                // Last size; first start maximized (after the first layout pass, see WindowChrome).
                Width = Math.Max(MinWidth, vm.Ui.WindowWidth);
                Height = Math.Max(MinHeight, vm.Ui.WindowHeight);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.Toast) && vm.HasToast)
                    {
                        Controls.Motion.FadeIn(ToastHost, offsetX: 24, offsetY: 0, milliseconds: 220);
                    }
                };
            }
        };
    }

    private bool _maximizeApplied;
    private bool _hiddenToTray;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_maximizeApplied && DataContext is MainViewModel { Ui.WindowMaximized: true })
        {
            _maximizeApplied = true;
            Dispatcher.UIThread.Post(() => WindowState = WindowState.Maximized, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Called after the window was shown again from the tray.</summary>
    public void NotifyShown()
    {
        if (_hiddenToTray && DataContext is MainViewModel vm)
        {
            _hiddenToTray = false;
            vm.OnWindowVisibilityChanged(true);
        }
    }

    /// <summary>Remembers maximized/normal state and the normal size.</summary>
    public void SaveWindowState()
    {
        if (DataContext is not MainViewModel vm || WindowState == WindowState.Minimized)
        {
            return;
        }

        vm.Ui.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            vm.Ui.WindowWidth = Math.Round(Bounds.Width);
            vm.Ui.WindowHeight = Math.Round(Bounds.Height);
        }

        vm.Ui.Save();
    }

    /// <summary>Set on app exit; otherwise closing hides the window to the tray (setting).</summary>
    public bool AllowClose { get; set; }

    public event Action? ExitRequested;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveWindowState();
        if (AllowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (DataContext is MainViewModel { Ui.CloseToTray: true } main)
        {
            Hide();
            _hiddenToTray = true;
            main.OnWindowVisibilityChanged(false);

            // In the tray only the icon is needed: give unused memory back to Windows.
            MainViewModel.ReleaseMemory();
        }
        else
        {
            ExitRequested?.Invoke();
        }
    }
}
