using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CLEANit.App.Localization;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Own startup entry: program, script or shortcut, name, arguments and (expert) all users.</summary>
public partial class AddStartupWindow : Window
{
    public AddStartupWindow() : this(false)
    {
    }

    public AddStartupWindow(bool allowAllUsers)
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this, maximize: false);
        Title = Loc.T("StartupAddTitle");
        ScopeLabel.IsVisible = allowAllUsers;
        AllUsersSwitch.IsVisible = allowAllUsers;
        NameBox.TextChanged += (_, _) => Validate();
    }

    public StartupAddRequest? Result { get; private set; }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("StartupAddPick"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.T("StartupAddFileTypes")) { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"] }
            ]
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        TargetBox.Text = path;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Text = Path.GetFileNameWithoutExtension(path);
        }

        // Shortcuts carry their own arguments.
        ArgumentsBox.IsEnabled = !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        Validate();
    }

    private void Validate() =>
        AddButton.IsEnabled = !string.IsNullOrWhiteSpace(TargetBox.Text) && !string.IsNullOrWhiteSpace(NameBox.Text);

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        Result = new StartupAddRequest(NameBox.Text!.Trim(), TargetBox.Text!, ArgumentsBox.IsEnabled ? ArgumentsBox.Text ?? "" : "",
            AllUsersSwitch.IsVisible && AllUsersSwitch.IsChecked == true);
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
