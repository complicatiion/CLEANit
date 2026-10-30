using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;

namespace CLEANit.App.ViewModels;

public sealed partial class ReportRowViewModel(HistoryEntry entry) : ObservableObject
{
    public HistoryEntry Entry { get; } = entry;

    public string Time => Entry.TimeUtc.ToLocalTime().ToString("g");

    public string Area => Loc.T("Area" + Entry.Area);

    public string Freed => ByteSize.Format(Entry.Bytes);

    public string Files => Entry.Files.ToString("N0");

    public string Skipped => Entry.Skipped > 0 ? Entry.Skipped.ToString("N0") : "–";

    public bool Cancelled => Entry.Cancelled;

    public DateTime TimeUtc => Entry.TimeUtc;

    public long Bytes => Entry.Bytes;

    public long FileCount => Entry.Files;

    public long SkippedCount => Entry.Skipped;

    [ObservableProperty]
    private bool _isActive;
}

public sealed class ReportRuleRowViewModel(RuleCleanResult result)
{
    public string Name => result.RuleName;

    public string Freed => ByteSize.Format(result.DeletedBytes);

    public string Files => result.DeletedFiles.ToString("N0");

    public string Skipped
    {
        get
        {
            var skipped = result.SkippedInUse + result.SkippedDenied + result.SkippedChanged;
            return result.SkippedRunning ? Loc.T("ReportSkippedRunning") : skipped > 0 ? skipped.ToString("N0") : "";
        }
    }

    public string Errors => string.Join("\n", result.Errors);

    public bool HasErrors => result.Errors.Count > 0;
}

/// <summary>Cleaning reports: list (SQLite index), details, export as CSV or HTML, delete.</summary>
public sealed partial class ReportsViewModel(MainViewModel main) : PageViewModel(main)
{
    private bool _dirty = true;

    public override string TitleKey => "NavReports";

    public ObservableCollection<ReportRowViewModel> Rows { get; } = [];

    public ObservableCollection<ReportRuleRowViewModel> Details { get; } = [];

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand), nameof(ExportHtmlCommand), nameof(DeleteCommand))]
    private ReportRowViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _detailTitle = "";

    [ObservableProperty]
    private string _detailSummary = "";

    private CleanReport? _report;

    public string TotalText => Loc.F("ReportsTotal", Rows.Count, ByteSize.Format(Rows.Sum(r => r.Entry.Bytes)));

    /// <summary>A new report was written; reload on the next visit.</summary>
    public void MarkDirty() => _dirty = true;

    public override void OnActivated()
    {
        if (_dirty)
        {
            Load();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Load();
    }

    [RelayCommand]
    private void Refresh()
    {
        ReportStore.SyncIndex();
        Load();
    }

    private void Load()
    {
        _dirty = false;
        var selectedFile = Selected?.Entry.ReportFile;
        Rows.Clear();
        foreach (var entry in ReportStore.LoadHistory())
        {
            Rows.Add(new ReportRowViewModel(entry));
        }

        Select(Rows.FirstOrDefault(r => r.Entry.ReportFile == selectedFile));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(TotalText));
    }

    [RelayCommand]
    private void Select(ReportRowViewModel? row) => Selected = row;

    /// <summary>Selection from the list (DataGrid) loads the details.</summary>
    partial void OnSelectedChanged(ReportRowViewModel? oldValue, ReportRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        ShowDetails(newValue);
    }

    private void ShowDetails(ReportRowViewModel? row)
    {
        Details.Clear();
        _report = null;
        if (row is null)
        {
            return;
        }

        row.IsActive = true;
        DetailTitle = $"{row.Area} · {row.Time}";
        _report = row.Entry.ReportFile is { } file ? ReportStore.Load(file) : null;
        if (_report is null)
        {
            DetailSummary = Loc.T("ReportMissing");
            return;
        }

        var duration = _report.FinishedUtc > _report.StartedUtc ? _report.FinishedUtc - _report.StartedUtc : TimeSpan.Zero;
        DetailSummary = Loc.F("ReportSummary", ByteSize.Format(_report.DeletedBytes), _report.DeletedFiles.ToString("N0"), _report.Skipped.ToString("N0"),
                            duration.TotalSeconds.ToString("0.0"))
                        + (string.IsNullOrEmpty(_report.Profile) ? "" : "  ·  " + Loc.F("ReportProfile", _report.Profile))
                        + (_report.Cancelled ? "  ·  " + Loc.T("ResultCancelled") : "");
        foreach (var rule in _report.Rules)
        {
            Details.Add(new ReportRuleRowViewModel(rule));
        }
    }

    private bool HasReport() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasReport))]
    private Task ExportCsvAsync() => ExportAsync("csv", "CSV", r => ReportStore.ToCsv(r));

    [RelayCommand(CanExecute = nameof(HasReport))]
    private Task ExportHtmlAsync() => ExportAsync("html", "HTML", r => ReportStore.ToHtml(r, $"CLEANit – {DetailTitle}"));

    private async Task ExportAsync(string extension, string typeName, Func<CleanReport, string> render)
    {
        if (_report is null)
        {
            Main.ShowToast(Loc.T("ReportMissing"), isError: true);
            return;
        }

        try
        {
            var name = Path.GetFileNameWithoutExtension(Selected?.Entry.ReportFile ?? "cleanit-report") + "." + extension;
            var path = await Host.SaveTextAsync(Loc.T("BtnExport"), name, extension, typeName, render(_report));
            if (path is not null)
            {
                Main.ShowToast(Loc.F("ToastExported", path));
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasReport))]
    private async Task DeleteAsync()
    {
        if (Selected?.Entry.ReportFile is not { } file)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("ReportDeleteTitle"), Loc.F("ReportDeleteText", DetailTitle), Loc.T("BtnDelete"), danger: true,
            confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        if (!ReportStore.Delete(file))
        {
            Main.ShowToast(Loc.T("ToastDeleteFailed"), isError: true);
        }

        Selected = null;
        Load();
        Main.Overview.Refresh();
    }

    [RelayCommand]
    private void OpenFolder() => AppEnvironment.OpenFolder(AppPaths.LogsDirectory, create: true);

    /// <summary>Deletes every cleaning report after a confirmation; the totals on the overview stay.</summary>
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (!HasRows)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("ReportsClearTitle"), Loc.F("ReportsClearText", Rows.Count), Loc.T("BtnClearAll"), danger: true,
            confirmIcon: "IconDeleteSweep");
        if (!answer.Confirmed)
        {
            return;
        }

        var deleted = await Task.Run(ReportStore.DeleteAll);
        Selected = null;
        Load();
        Main.Overview.Refresh();
        Main.ShowToast(Loc.F("ToastReportsCleared", deleted));
    }
}
