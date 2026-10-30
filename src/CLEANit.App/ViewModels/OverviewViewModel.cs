using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.Core.Analysis;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

public sealed class HistoryRowViewModel(HistoryEntry entry)
{
    public HistoryEntry Entry { get; } = entry;

    public string Time => Entry.TimeUtc.ToLocalTime().ToString("g");

    public string Area => Loc.T("Area" + Entry.Area);

    public string Freed => ByteSize.Format(Entry.Bytes);

    public string Files => Loc.F("FilesCount", Entry.Files.ToString("N0"));
}

/// <summary>Overview: statistics, quick actions, drives, last cleaning runs.</summary>
public sealed partial class OverviewViewModel(MainViewModel main) : PageViewModel(main)
{
    public override string TitleKey => "NavOverview";

    public ObservableCollection<DriveRowViewModel> Drives { get; } = [];

    public ObservableCollection<HistoryRowViewModel> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    // ------------------------------------------------------------------ Statistics (count up in the view)

    [ObservableProperty]
    private double _freedBytes;

    [ObservableProperty]
    private double _runs;

    [ObservableProperty]
    private double _filesRemoved;

    [ObservableProperty]
    private double _rules;

    [ObservableProperty]
    private double _communityRules;

    [ObservableProperty]
    private double _programs;

    [ObservableProperty]
    private double _updates;

    public override void OnActivated() => Refresh();

    public override void OnCatalogChanged() => Refresh();

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Refresh();
    }

    public void Refresh()
    {
        Drives.Clear();
        foreach (var drive in DiskUsage.Drives())
        {
            Drives.Add(new DriveRowViewModel(drive));
        }

        History.Clear();
        var history = ReportStore.LoadHistory();
        foreach (var entry in history.Take(8))
        {
            History.Add(new HistoryRowViewModel(entry));
        }

        // Totals live in stats.json, so clearing the reports does not reset them.
        var totals = CleaningStats.Load(history);
        FreedBytes = totals.Bytes;
        Runs = totals.Runs;
        FilesRemoved = totals.Files;

        var catalog = Engine.Catalog;
        Rules = catalog?.Rules.Count(r => r.Source != RuleSource.Winapp2) ?? 0;
        CommunityRules = catalog is null || !Ui.UseWinapp2 ? 0 : catalog.Winapp2Detected;
        Programs = OperatingSystem.IsWindows() ? ProgramCache.Load()?.Programs.Count ?? 0 : 0;
        Updates = int.TryParse(Main.UpdatesNav.Badge, out var updates) ? updates : 0;
        OnPropertyChanged(nameof(HasHistory));
    }

    [RelayCommand]
    private async Task AnalyzeAreaAsync(string? area)
    {
        var page = area switch
        {
            "Full" => Main.FullClean,
            "Browser" => Main.BrowserClean,
            "Developer" => Main.DeveloperClean,
            _ => Main.SystemClean
        };

        Main.NavigateTo(page);
        if (page.AnalyzeCommand.CanExecute(null))
        {
            await page.AnalyzeAsync();
        }
    }

    [RelayCommand]
    private void Open(string? target)
    {
        PageViewModel page = target switch
        {
            "Programs" => Main.Programs,
            "Startup" => Main.Startup,
            "Updates" => Main.Updates,
            "LargeFiles" => Main.LargeFiles,
            "Duplicates" => Main.Duplicates,
            "Disk" => Main.DiskUsage,
            "Reports" => Main.Reports,
            _ => Main.Settings
        };
        Main.NavigateTo(page);
    }
}
