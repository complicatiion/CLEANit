using Avalonia.Controls;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Folder comparison: folders can be dropped on the left and right card.</summary>
public partial class FolderCompareView : UserControl
{
    public FolderCompareView()
    {
        InitializeComponent();

        // Tint each row by its state. Rows are recycled, so the tint is set again on every load: a dynamic resource binding
        // (follows theme changes) at local priority, which wins over the DataGrid's own row background.
        ResultGrid.LoadingRow += (_, e) =>
        {
            (e.Row.Tag as IDisposable)?.Dispose();
            e.Row.Tag = null;
            var row = e.Row.DataContext as CompareRowViewModel;
            var key = row is null ? null
                : row.IsLeftOnly ? "BrushAccentSoft"
                : row.IsRightOnly ? "BrushInfoSoft"
                : row.IsDifferent ? "BrushModerateSoft"
                : null;
            if (key is null)
            {
                e.Row.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty);
            }
            else
            {
                e.Row.Tag = e.Row.Bind(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, e.Row.GetResourceObservable(key));
            }
        };
        Controls.FolderDrop.Attach(LeftCard, folders => (DataContext as FolderCompareViewModel)?.SetFolder("left", folders[0]));
        Controls.FolderDrop.Attach(RightCard, folders =>
        {
            if (DataContext is not FolderCompareViewModel vm)
            {
                return;
            }

            // Two folders dropped at once: left and right in one go.
            if (folders.Count >= 2)
            {
                vm.SetFolder("left", folders[0]);
                vm.SetFolder("right", folders[1]);
            }
            else
            {
                vm.SetFolder("right", folders[0]);
            }
        });
    }
}
