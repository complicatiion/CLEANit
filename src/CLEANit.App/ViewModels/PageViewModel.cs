using CommunityToolkit.Mvvm.ComponentModel;
using CLEANit.App.Localization;
using CLEANit.App.Services;

namespace CLEANit.App.ViewModels;

/// <summary>Base of all sidebar pages.</summary>
public abstract class PageViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main { get; } = main;

    protected UiHost Host => Main.Host;

    protected UiSettings Ui => Main.Ui;

    protected EngineHost Engine => Main.Engine;

    public abstract string TitleKey { get; }

    public string Title => Loc.T(TitleKey);

    public virtual void OnActivated()
    {
    }

    /// <summary>Another page was opened or the window was hidden (stop timers, polling).</summary>
    public virtual void OnDeactivated()
    {
    }

    public virtual void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Rules or settings changed (catalog reloaded).</summary>
    public virtual void OnCatalogChanged()
    {
    }

    public bool IsExpert => Ui.ExpertMode;

    public virtual void OnExpertModeChanged()
    {
        OnPropertyChanged(nameof(IsExpert));
    }
}
