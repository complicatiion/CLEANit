using Avalonia.Controls;
using Avalonia.Input;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Rule list with a resizable details panel; the width is remembered.</summary>
public partial class RulesPanel : UserControl
{
    public RulesPanel()
    {
        InitializeComponent();
        SplitGrid.ColumnDefinitions[0].MinWidth = 360;
        SplitGrid.ColumnDefinitions[2].MinWidth = 280;
        CleanerViewModel? subscribed = null;
        void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CleanerViewModel.DetailsWidth))
            {
                ApplyWidth();
            }
        }

        void Subscribe()
        {
            ApplyWidth();
            if (subscribed is not null)
            {
                subscribed.PropertyChanged -= OnVmChanged;
            }

            subscribed = DataContext as CleanerViewModel;
            if (subscribed is not null)
            {
                subscribed.PropertyChanged += OnVmChanged;
            }
        }

        DataContextChanged += (_, _) => Subscribe();

        // Tab switches detach and re-attach the panel without a DataContext change.
        AttachedToVisualTree += (_, _) => Subscribe();

        // Released views must not stay referenced by the long-lived view model.
        DetachedFromVisualTree += (_, _) =>
        {
            if (subscribed is not null)
            {
                subscribed.PropertyChanged -= OnVmChanged;
                subscribed = null;
            }
        };
        Splitter.DragCompleted += (_, _) => SaveWidth();
        Splitter.KeyUp += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right)
            {
                SaveWidth();
            }
        };
    }

    private void ApplyWidth()
    {
        if (DataContext is CleanerViewModel vm)
        {
            SplitGrid.ColumnDefinitions[2].Width = new GridLength(vm.DetailsWidth);
        }
    }

    private void SaveWidth()
    {
        if (DataContext is CleanerViewModel vm)
        {
            var width = SplitGrid.ColumnDefinitions[2].ActualWidth;
            vm.DetailsWidth = width;
            SplitGrid.ColumnDefinitions[2].Width = new GridLength(vm.DetailsWidth);
        }
    }
}
