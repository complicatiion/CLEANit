using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CLEANit.App.Views;

/// <summary>Last step before any file is deleted: summary, Deep warnings and acknowledgement.</summary>
public partial class CleanConfirmWindow : Window
{
    public CleanConfirmWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this, maximize: false);

        // Never taller than the screen (small displays, scaled desktops).
        Opened += (_, _) =>
        {
            if (Screens?.ScreenFromWindow(this)?.WorkingArea is { } area)
            {
                var maxHeight = area.Height / Math.Max(1, RenderScaling) * 0.92;
                if (Height > maxHeight)
                {
                    MinHeight = Math.Min(MinHeight, maxHeight);
                    Height = maxHeight;
                }
            }
        };
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
