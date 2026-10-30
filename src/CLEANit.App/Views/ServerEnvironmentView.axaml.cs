using Avalonia.Controls;

namespace CLEANit.App.Views;

public partial class ServerEnvironmentView : UserControl
{
    public ServerEnvironmentView()
    {
        InitializeComponent();

        // Equal columns flush with the toolbar: 4 from about 1200 px, 2 from about 700 px, else 1 (the list has a -12 px right margin
        // for the card gaps, so its width already includes one gap).
        Cards.SizeChanged += (_, e) =>
        {
            var width = e.NewSize.Width;
            var columns = width >= 1212 ? 4 : width >= 712 ? 2 : 1;
            Cards.Tag = Math.Floor(width / columns);
        };
    }
}
