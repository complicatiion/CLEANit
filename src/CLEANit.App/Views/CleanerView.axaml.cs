using Avalonia.Controls;
using CLEANit.App.Localization;
using CLEANit.App.ViewModels;

namespace CLEANit.App.Views;

/// <summary>Cleanup page: rules panel; the developer page adds the workspace scanner as a second tab.</summary>
public partial class CleanerView : UserControl
{
    public CleanerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private void Build()
    {
        if (DataContext is not CleanerViewModel vm)
        {
            return;
        }

        if (vm.IsServer)
        {
            // Server cleanup: scope (signed-out users) above the rules.
            var panel = new DockPanel();
            var scope = new ServerScopeBar();
            DockPanel.SetDock(scope, Dock.Top);
            panel.Children.Add(scope);
            panel.Children.Add(new RulesPanel());
            Content = panel;
            return;
        }

        if (!vm.IsDeveloper)
        {
            Content = new RulesPanel();
            return;
        }

        var rulesTab = new TabItem { Content = new RulesPanel() };
        var workspaceTab = new TabItem { Content = new WorkspacePanel { DataContext = vm.Workspace } };
        void Headers()
        {
            rulesTab.Header = Loc.T("TabRules");
            workspaceTab.Header = Loc.T("TabWorkspace");
        }

        Headers();

        // Static event: unsubscribe when the view is released (PageHost keeps only recent pages).
        Loc.LanguageChanged += Headers;
        DetachedFromVisualTree += (_, _) => Loc.LanguageChanged -= Headers;
        Content = new TabControl { Items = { rulesTab, workspaceTab } };
    }
}
