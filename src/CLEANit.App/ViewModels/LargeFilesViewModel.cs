using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Analysis;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;

namespace CLEANit.App.ViewModels;

/// <summary>Found file with selection (large files, duplicates).</summary>
public sealed partial class FileRowViewModel(FoundFile file) : ObservableObject
{
    public FoundFile File { get; } = file;

    [ObservableProperty]
    private bool _isChecked;

    public string Name => Path.GetFileName(File.Path);

    public string Folder => Path.GetDirectoryName(File.Path) ?? "";

    public string Size => ByteSize.Format(File.Bytes);

    public long Bytes => File.Bytes;

    public DateTime LastWriteUtc => File.LastWriteUtc;

    public DateTime LastAccessUtc => File.LastAccessUtc;

    public string Modified => File.LastWriteUtc.ToLocalTime().ToString("d");

    public string Accessed => File.LastAccessUtc == DateTime.MinValue ? "" : File.LastAccessUtc.ToLocalTime().ToString("d");

    public string AgeText => Loc.F("AgeDays", Math.Max(0, (int)(DateTime.UtcNow - File.LastWriteUtc).TotalDays));
}

/// <summary>Large and old files below a folder: review, then delete selected after confirmation (recycle bin by default).</summary>
public sealed partial class LargeFilesViewModel(MainViewModel main) : PageViewModel(main)
{
    private CancellationTokenSource? _cts;

    public override string TitleKey => "NavLargeFiles";

    public ObservableCollection<FileRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _root = "";

    [ObservableProperty]
    private decimal _minMb = 100;

    [ObservableProperty]
    private decimal _olderDays;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(DeleteCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    public string SummaryText
    {
        get
        {
            var selected = Rows.Where(r => r.IsChecked).ToList();
            return Rows.Count == 0
                ? Loc.T("LargeFilesHint")
                : Loc.F("FilesSelectedSummary", selected.Count, Rows.Count, ByteSize.Format(selected.Sum(r => r.File.Bytes)));
        }
    }

    public string ModeText => Loc.T(Ui.RecycleBinForFolders ? "ModeRecycleBin" : "ModePermanent");

    public override void OnActivated()
    {
        if (string.IsNullOrEmpty(Root))
        {
            Root = Ui.LargeFilesRoot is { Length: > 0 } stored && Directory.Exists(stored)
                ? stored
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            MinMb = Math.Clamp(Ui.LargeFilesMinMb, 1, 1_000_000);
            OlderDays = Math.Clamp(Ui.LargeFilesOlderDays, 0, 3650);
        }

        OnPropertyChanged(nameof(ModeText));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ModeText));
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await Host.PickFolderAsync(Loc.T("LargeFilesPickTitle"), Root);
        if (!string.IsNullOrEmpty(folder))
        {
            Root = folder;
        }
    }

    private bool CanScan() => !IsBusy && Directory.Exists(Root);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        Ui.LargeFilesRoot = Root;
        Ui.LargeFilesMinMb = (int)MinMb;
        Ui.LargeFilesOlderDays = (int)OlderDays;
        Ui.Save();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        Rows.Clear();
        Status = Loc.T("StatusSearchingFiles");
        var progress = new Progress<FinderProgress>(p => Status = Loc.F("StatusFilesChecked", p.Done.ToString("N0"), p.Current ?? ""));
        try
        {
            var files = await FileFinder.FindLargeAsync(Root, (long)MinMb * 1024 * 1024, (int)OlderDays, 2000, Engine.Guard, progress, token);
            foreach (var file in files)
            {
                Add(new FileRowViewModel(file));
            }

            if (token.IsCancellationRequested)
            {
                Main.ShowCancelled();
            }
            else
            {
                Main.ShowToast(Loc.F("ToastLargeFilesFound", Rows.Count, ByteSize.Format(Rows.Sum(r => r.File.Bytes))));
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            Status = "";
            Changed();
        }
    }

    private void Add(FileRowViewModel row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileRowViewModel.IsChecked))
            {
                Changed();
            }
        };
        Rows.Add(row);
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(SummaryText));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows)
        {
            row.IsChecked = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in Rows)
        {
            row.IsChecked = false;
        }
    }

    [RelayCommand]
    private void OpenFolder(FileRowViewModel? row) => AppEnvironment.OpenFolder(row?.Folder);

    private bool CanDelete() => !IsBusy && Rows.Any(r => r.IsChecked);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var selected = Rows.Where(r => r.IsChecked).ToList();
        var recycle = Ui.RecycleBinForFolders;
        var list = string.Join("\n", selected.Take(10).Select(r => $"• {r.Name} ({r.Size})")) + (selected.Count > 10 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(Loc.F("DeleteFilesTitle", selected.Count),
            Loc.F("DeleteFilesText", ByteSize.Format(selected.Sum(r => r.File.Bytes)), list) + "\n\n" + Loc.T(recycle ? "ModeRecycleBin" : "ModePermanent"),
            Loc.T("BtnDelete"), danger: !recycle, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusDeleting");
        try
        {
            var result = await DeleteAndReportAsync(Main, selected.Select(r => r.File).ToList(), "LargeFiles", recycle);
            foreach (var row in selected.Where(r => !File.Exists(r.File.Path)))
            {
                Rows.Remove(row);
            }

            Main.ShowToast(Loc.F("ResultText", ByteSize.Format(result.DeletedBytes), result.DeletedFiles.ToString("N0"), result.SkippedInUse.ToString("N0")),
                isError: result.Errors.Count > 0 && result.DeletedFiles == 0);
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            Status = "";
            Changed();
        }
    }

    /// <summary>Deletes picked files through the guard and saves a report (shared with the duplicate finder).</summary>
    internal static async Task<RuleCleanResult> DeleteAndReportAsync(MainViewModel main, IReadOnlyList<FoundFile> files, string area, bool recycle)
    {
        var engine = main.Engine;
        var report = new CleanReport { Area = area };
        var result = new RuleCleanResult { RuleId = area.ToLowerInvariant(), RuleName = Loc.T("Area" + area) };
        report.Rules.Add(result);
        var (count, bytes, errors) = await Task.Run(() =>
            SafeDelete.DeleteFiles(files.Select(f => (f.Path, f.Bytes)).ToList(), engine.Guard, engine.Platform.RecycleBin, recycle));
        result.DeletedFiles = count;
        result.DeletedBytes = bytes;
        result.SkippedInUse = Math.Max(0, files.Count - count);
        foreach (var error in errors)
        {
            result.AddError(error);
        }

        report.FinishedUtc = DateTime.UtcNow;
        await Task.Run(() => ReportStore.Save(report));
        main.Overview.Refresh();
        main.Reports.MarkDirty();
        return result;
    }
}
