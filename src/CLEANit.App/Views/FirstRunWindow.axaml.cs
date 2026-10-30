using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CLEANit.App.Views;

/// <summary>First start: show expert features or keep CLEANit simple; optionally start in the system tray at sign-in.</summary>
public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this, maximize: false);
    }

    /// <summary>Only an explicit choice counts; closing with the title bar keeps the tray start off.</summary>
    public bool TrayAtSignIn => _answered && TrayToggle.IsChecked == true;

    private bool _answered;

    private void OnSimple(object? sender, RoutedEventArgs e)
    {
        _answered = true;
        Close(false);
    }

    private void OnExpert(object? sender, RoutedEventArgs e)
    {
        _answered = true;
        Close(true);
    }
}
