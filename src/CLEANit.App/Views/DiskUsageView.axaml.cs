using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

public partial class DiskUsageView : UserControl
{
    public DiskUsageView()
    {
        InitializeComponent();
    }

    /// <summary>Right click on the treemap (or its mode button) → Export: renders the treemap and saves it as JPEG.</summary>
    private async void OnExportTreemap(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiskUsageViewModel vm)
        {
            return;
        }

        if (!vm.HasRows)
        {
            vm.Main.ShowToast(Loc.T("ToastTreemapEmpty"));
            return;
        }

        try
        {
            if (!vm.IsTreemapMode)
            {
                await vm.SetModeCommand.ExecuteAsync("Treemap");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(80);
            }

            var path = await vm.PickTreemapFileAsync();
            if (path is null)
            {
                return;
            }

            var top = TopLevel.GetTopLevel(this);
            var background = this.TryFindResource("BrushSurfaceCard", ActualThemeVariant, out var brush) && brush is ISolidColorBrush solid
                ? solid.Color
                : Colors.White;
            ImageExport.Save(Treemap, top?.RenderScaling ?? 1, background, path);
            vm.Main.ShowToast(Loc.F("ToastTreemapExported", Path.GetFileName(path)));
        }
        catch (InvalidOperationException)
        {
            vm.Main.ShowToast(Loc.T("ToastTreemapFailed"), isError: true);
        }
        catch (Exception ex)
        {
            vm.Main.ShowError(ex);
        }
    }
}
