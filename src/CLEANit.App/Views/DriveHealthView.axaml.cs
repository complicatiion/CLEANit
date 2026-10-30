using Avalonia.Controls;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Drive health (expert): the card grid follows the available width (up to four cards per row).</summary>
public partial class DriveHealthView : UserControl
{
    public DriveHealthView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            if (DataContext is DriveHealthViewModel vm)
            {
                vm.AvailableWidth = e.NewSize.Width;
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DriveHealthViewModel vm && Bounds.Width > 0)
            {
                vm.AvailableWidth = Bounds.Width;
            }
        };
    }
}
