using Avalonia.Controls;
using Avalonia.Interactivity;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Items with check boxes; the main action is right of Cancel.</summary>
public partial class ChecklistWindow : Window
{
    public ChecklistWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this, maximize: false);
    }

    public ChecklistWindow(string title, string message, IReadOnlyList<ChecklistItem> items, string confirmText, string confirmIcon, bool danger)
        : this()
    {
        Title = title;
        HeaderText.Text = title;
        MessageText.Text = message;
        List.ItemsSource = items;
        ConfirmLabel.Text = confirmText;
        ConfirmLabel.Icon = Res.Icon(confirmIcon);
        if (danger)
        {
            ConfirmButton.Classes.Remove("primary");
            ConfirmButton.Classes.Add("danger");
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
