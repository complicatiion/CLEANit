using Avalonia.Controls;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Duplicate finder and folder comparison. Folders can be dropped on the folder card.</summary>
public partial class DuplicatesView : UserControl
{
    public DuplicatesView()
    {
        InitializeComponent();
        Controls.FolderDrop.Attach(FolderCard, folders => (DataContext as DuplicatesViewModel)?.AddRoots(folders));
    }
}
