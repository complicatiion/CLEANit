using Avalonia;
using Avalonia.Controls;
using CLEANit.App.ViewModels;
using CLEANit.App.Views;

namespace CLEANit.App.Controls;

/// <summary>
/// Hosts all page views at once and only toggles their visibility: switching pages neither re-creates nor re-styles them.
/// Each view is created on its first visit.
/// </summary>
public sealed class PageHost : Grid
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageHost, PageViewModel?>(nameof(Page));

    /// <summary>Views kept alive; older ones are released to keep memory low (re-created on the next visit).</summary>
    private const int MaxViews = 5;

    private readonly Dictionary<PageViewModel, Control> _views = new();
    private readonly List<PageViewModel> _recent = [];

    static PageHost()
    {
        PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.ShowPage());
    }

    public PageViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private void ShowPage()
    {
        var page = Page;
        if (page is not null && !_views.ContainsKey(page))
        {
            var view = Create(page);
            view.DataContext = page;

            // Starts hidden so a new (or re-created) view gets the same entrance animation as a cached one.
            view.IsVisible = false;
            _views[page] = view;
            Children.Add(view);
        }

        if (page is not null)
        {
            _recent.Remove(page);
            _recent.Add(page);
            while (_recent.Count > MaxViews)
            {
                var oldest = _recent[0];
                _recent.RemoveAt(0);
                if (_views.Remove(oldest, out var old))
                {
                    Children.Remove(old);
                    old.DataContext = null;
                }
            }
        }

        foreach (var (model, view) in _views)
        {
            var visible = ReferenceEquals(model, page);
            if (visible && !view.IsVisible)
            {
                view.IsVisible = true;
                Motion.FadeIn(view, offsetY: 16, milliseconds: 260);
            }
            else
            {
                view.IsVisible = visible;
            }
        }
    }

    private static Control Create(PageViewModel page) => page switch
    {
        OverviewViewModel => new OverviewView(),
        CleanerViewModel => new CleanerView(),
        ProgramsViewModel => new ProgramsView(),
        StartupViewModel => new StartupView(),
        UpdatesViewModel => new UpdatesView(),
        SoftwareCatalogViewModel => new SoftwareCatalogView(),
        WindowsSettingsViewModel => new WindowsSettingsView(),
        DeviceTreeViewModel => new DeviceTreeView(),
        LargeFilesViewModel => new LargeFilesView(),
        DuplicatesViewModel => new DuplicatesView(),
        DiskUsageViewModel => new DiskUsageView(),
        ReportsViewModel => new ReportsView(),
        ServicesViewModel => new ServicesView(),
        DriveHealthViewModel => new DriveHealthView(),
        ServerProfilesViewModel => new ServerProfilesView(),
        ServerEnvironmentViewModel => new ServerEnvironmentView(),
        RuleEditorViewModel => new RuleEditorView(),
        CommandLineViewModel => new CommandLineView(),
        SettingsViewModel => new SettingsView(),
        AboutViewModel => new AboutView(),
        _ => new TextBlock { Text = page.GetType().Name }
    };
}
