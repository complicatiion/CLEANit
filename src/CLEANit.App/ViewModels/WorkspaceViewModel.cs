using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;
using CLEANit.Core.Workspace;

namespace CLEANit.App.ViewModels;

public sealed partial class WorkspaceRowViewModel(WorkspaceArtifact artifact, bool isChecked) : ObservableObject
{
    public WorkspaceArtifact Artifact { get; } = artifact;

    [ObservableProperty]
    private bool _isChecked = isChecked;

    public string Project => Path.GetFileName(Artifact.ProjectPath.TrimEnd('\\'));

    public string ProjectPath => Artifact.ProjectPath;

    public string Folder => Path.GetFileName(Artifact.ArtifactPath);

    public string Kind => Artifact.ProjectKind;

    public string Size => ByteSize.Format(Artifact.Bytes);

    public string LastActivity => Artifact.LastActivityUtc.ToLocalTime().ToString("d");

    public int IdleDays => (int)(DateTime.UtcNow - Artifact.LastActivityUtc).TotalDays;

    public string IdleText => Loc.F("WorkspaceIdle", IdleDays);
}

/// <summary>Developer workspace scanner: build output of projects below a root folder.</summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cts;

    public WorkspaceViewModel(MainViewModel main)
    {
        _main = main;
        var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
        _root = main.Ui.WorkspaceRoot is { Length: > 0 } saved ? saved : Directory.Exists(defaultRoot) ? defaultRoot : "";
        _idleDays = main.Ui.WorkspaceIdleDays;
    }

    public ObservableCollection<WorkspaceRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _root;

    [ObservableProperty]
    private int _idleDays;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(CleanCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    partial void OnRootChanged(string value)
    {
        _main.Ui.WorkspaceRoot = value;
        _main.Ui.Save();
    }

    partial void OnIdleDaysChanged(int value)
    {
        _main.Ui.WorkspaceIdleDays = Math.Max(0, value);
        _main.Ui.Save();
        foreach (var row in Rows)
        {
            row.IsChecked = IsIdle(row.Artifact);
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    public string SummaryText
    {
        get
        {
            var selected = Rows.Where(r => r.IsChecked).ToList();
            return Loc.F("WorkspaceSummary", selected.Count, Rows.Count, ByteSize.Format(selected.Sum(r => r.Artifact.Bytes)));
        }
    }

    private bool IsIdle(WorkspaceArtifact artifact) => IdleDays <= 0 || artifact.LastActivityUtc < DateTime.UtcNow.AddDays(-IdleDays);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await _main.Host.PickFolderAsync(Loc.T("WorkspacePickTitle"), Root);
        if (!string.IsNullOrEmpty(folder))
        {
            Root = folder;
        }
    }

    private bool CanScan() => !IsBusy && Directory.Exists(Root);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Rows.Clear();
        var progress = new Progress<string>(p => Status = p);
        try
        {
            var found = await WorkspaceScanner.ScanAsync(Root, 8, progress, _cts.Token);
            foreach (var artifact in found)
            {
                var row = new WorkspaceRowViewModel(artifact, IsIdle(artifact));
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(WorkspaceRowViewModel.IsChecked))
                    {
                        OnPropertyChanged(nameof(SummaryText));
                        CleanCommand.NotifyCanExecuteChanged();
                    }
                };
                Rows.Add(row);
            }

            // Cancelling keeps what was found so far (no exception).
            if (_cts.IsCancellationRequested)
            {
                _main.ShowCancelled();
            }
            else
            {
                _main.ShowToast(Loc.F("ToastWorkspaceScanned", Rows.Count, ByteSize.Format(Rows.Sum(r => r.Artifact.Bytes))));
            }
        }
        catch (Exception ex)
        {
            _main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            Status = "";
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(SummaryText));
            CleanCommand.NotifyCanExecuteChanged();
        }
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
    private void OpenFolder(WorkspaceRowViewModel? row) => AppEnvironment.OpenFolder(row?.Artifact.ArtifactPath);

    private bool CanClean() => !IsBusy && Rows.Any(r => r.IsChecked);

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selected = Rows.Where(r => r.IsChecked).ToList();
        var bytes = selected.Sum(r => r.Artifact.Bytes);
        var recycle = _main.Ui.RecycleBinForFolders;
        var answer = await _main.Host.ConfirmAsync(Loc.T("WorkspaceConfirmTitle"),
            Loc.F("WorkspaceConfirmText", selected.Count, ByteSize.Format(bytes)) + "\n\n" + Loc.T(recycle ? "ModeRecycleBin" : "ModePermanent"),
            Loc.T("BtnCleanNow"), danger: !recycle, confirmIcon: "IconBroom");
        if (!answer.Confirmed)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        var report = new CleanReport { Area = "Workspace" };
        var result = new RuleCleanResult { RuleId = "workspace", RuleName = "Workspace" };
        report.Rules.Add(result);
        try
        {
            var engine = _main.Engine;
            foreach (var row in selected)
            {
                // Cancel stops between folders.
                if (token.IsCancellationRequested)
                {
                    report.Cancelled = true;
                    break;
                }

                Status = row.Artifact.ArtifactPath;
                var deleted = await Task.Run(() =>
                    SafeDelete.DeleteFolder(row.Artifact.ArtifactPath, row.Artifact.Bytes, engine.Guard, engine.Platform.RecycleBin, recycle));
                if (deleted.Deleted)
                {
                    result.DeletedFiles += row.Artifact.Files;
                    result.DeletedBytes += deleted.Bytes;
                    Rows.Remove(row);
                }
                else
                {
                    result.SkippedInUse++;
                    result.AddError($"{row.Artifact.ArtifactPath}: {deleted.Error}");
                }
            }

            report.FinishedUtc = DateTime.UtcNow;
            await Task.Run(() => ReportStore.Save(report));
            _main.ShowToast(Loc.F("ResultText", ByteSize.Format(result.DeletedBytes), result.DeletedFiles.ToString("N0"), result.SkippedInUse));
            _main.Overview.Refresh();
            _main.Reports.MarkDirty();
        }
        catch (Exception ex)
        {
            _main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            Status = "";
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(SummaryText));
        }
    }
}
