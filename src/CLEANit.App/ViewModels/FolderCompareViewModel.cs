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

/// <summary>One compared path (file, or a folder that exists on one side only).</summary>
public sealed partial class CompareRowViewModel(CompareEntry entry, Action changed) : ObservableObject
{
    public CompareEntry Entry { get; } = entry;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();

    public string Name => Entry.Name;

    public string Folder => Entry.Folder;

    public string RelativePath => Entry.RelativePath;

    public bool IsFolder => Entry.IsFolder;

    public bool HasLeft => Entry.State != CompareState.RightOnly;

    public bool HasRight => Entry.State != CompareState.LeftOnly;

    public bool IsIdentical => Entry.State == CompareState.Identical;

    public bool IsDifferent => Entry.State == CompareState.Different;

    public bool IsLeftOnly => Entry.State == CompareState.LeftOnly;

    public bool IsRightOnly => Entry.State == CompareState.RightOnly;

    public string StateText => Loc.T("CompareState" + Entry.State);

    public string ReasonText => Entry.Reason switch
    {
        CompareReason.Size => Loc.T("CompareReasonSize"),
        CompareReason.Time => Entry.LeftIsNewer switch
        {
            true => Loc.T("CompareLeftNewer"),
            false => Loc.T("CompareRightNewer"),
            _ => Loc.T("CompareReasonTime")
        },
        CompareReason.Content => Loc.T("CompareReasonContent"),
        CompareReason.Type => Loc.T("CompareReasonType"),
        _ => IsFolder ? Loc.F("CompareFolderFiles", Entry.Files.ToString("N0")) : ""
    };

    public long LeftBytes => Entry.LeftBytes;

    public long RightBytes => Entry.RightBytes;

    public DateTime LeftSort => Entry.LeftTimeUtc ?? DateTime.MinValue;

    public DateTime RightSort => Entry.RightTimeUtc ?? DateTime.MinValue;

    public string LeftSize => HasLeft ? ByteSize.Format(Entry.LeftBytes) : "";

    public string RightSize => HasRight ? ByteSize.Format(Entry.RightBytes) : "";

    public string LeftTime => Entry.LeftTimeUtc is { } t && HasLeft ? t.ToLocalTime().ToString("g") : "";

    public string RightTime => Entry.RightTimeUtc is { } t && HasRight ? t.ToLocalTime().ToString("g") : "";

    public int StateSort => (int)Entry.State;

    public void RefreshText() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Folder comparison (WinMerge-like): left and right folder, identical / different / only left / only right, copy in
/// either direction and delete on either side – every write after a confirmation, existing files only replaced on request.
/// </summary>
public sealed partial class FolderCompareViewModel(MainViewModel main) : ObservableObject
{
    private readonly List<CompareRowViewModel> _all = [];
    private CancellationTokenSource? _cts;
    private bool _loaded;

    public MainViewModel Main { get; } = main;

    private UiHost Host => Main.Host;

    private UiSettings Ui => Main.Ui;

    public ObservableCollection<CompareRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeft), nameof(LeftName))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private string _left = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRight), nameof(RightName))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private string _right = "";

    [ObservableProperty]
    private bool _byContent;

    [ObservableProperty]
    private bool _showIdentical;

    [ObservableProperty]
    private bool _showDifferent = true;

    [ObservableProperty]
    private bool _showLeftOnly = true;

    [ObservableProperty]
    private bool _showRightOnly = true;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand), nameof(CopyToRightCommand), nameof(CopyToLeftCommand), nameof(DeleteLeftCommand),
        nameof(DeleteRightCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _leftInfo = "";

    [ObservableProperty]
    private string _rightInfo = "";

    public bool HasLeft => Left.Length > 0;

    public bool HasRight => Right.Length > 0;

    public string LeftName => HasLeft ? Path.GetFileName(Left.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Left : Loc.T("CompareNoFolder");

    public string RightName => HasRight ? Path.GetFileName(Right.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Right : Loc.T("CompareNoFolder");

    public bool HasResult => _all.Count > 0;

    public bool HasRows => Rows.Count > 0;

    public int IdenticalCount => _all.Count(r => r.IsIdentical);

    public int DifferentCount => _all.Count(r => r.IsDifferent);

    public int LeftOnlyCount => _all.Count(r => r.IsLeftOnly);

    public int RightOnlyCount => _all.Count(r => r.IsRightOnly);

    public string SummaryText => !HasResult
        ? Loc.T("CompareHint")
        : Loc.F("CompareSummary", IdenticalCount, DifferentCount, LeftOnlyCount, RightOnlyCount, _all.Count(r => r.IsChecked));

    public string ModeText => Loc.T(Ui.RecycleBinForFolders ? "ModeRecycleBin" : "ModePermanent");

    public void OnActivated()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Left = Ui.CompareLeft is { } l && Directory.Exists(l) ? l : "";
        Right = Ui.CompareRight is { } r && Directory.Exists(r) ? r : "";
        ByContent = Ui.CompareByContent;
        UpdateInfo();
    }

    public void OnLanguageChanged()
    {
        foreach (var row in _all)
        {
            row.RefreshText();
        }

        OnPropertyChanged(nameof(LeftName));
        OnPropertyChanged(nameof(RightName));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ModeText));
        UpdateInfo();
    }

    partial void OnByContentChanged(bool value)
    {
        Ui.CompareByContent = value;
        Ui.Save();
    }

    partial void OnShowIdenticalChanged(bool value) => ApplyFilter();

    partial void OnShowDifferentChanged(bool value) => ApplyFilter();

    partial void OnShowLeftOnlyChanged(bool value) => ApplyFilter();

    partial void OnShowRightOnlyChanged(bool value) => ApplyFilter();

    partial void OnSearchChanged(string value) => ApplyFilter();

    /// <summary>Folder dropped on a card or picked; side = "left" or "right".</summary>
    public void SetFolder(string side, string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        if (side == "left")
        {
            Left = path;
            Ui.CompareLeft = path;
        }
        else
        {
            Right = path;
            Ui.CompareRight = path;
        }

        Ui.Save();
        ClearResult();
        UpdateInfo();
    }

    [RelayCommand]
    private async Task BrowseAsync(string? side)
    {
        var current = side == "left" ? Left : Right;
        var folder = await Host.PickFolderAsync(Loc.T(side == "left" ? "ComparePickLeft" : "ComparePickRight"), current.Length > 0 ? current : null);
        if (!string.IsNullOrEmpty(folder))
        {
            SetFolder(side == "left" ? "left" : "right", folder);
        }
    }

    [RelayCommand]
    private void Swap()
    {
        (Left, Right) = (Right, Left);
        Ui.CompareLeft = Left;
        Ui.CompareRight = Right;
        Ui.Save();
        ClearResult();
        UpdateInfo();
    }

    [RelayCommand]
    private void OpenFolder(string? side) => AppEnvironment.OpenFolder(side == "left" ? Left : Right);

    private bool CanCompare() => !IsBusy && HasLeft && HasRight && !string.Equals(Left.TrimEnd('\\'), Right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (IsInside(Left, Right) || IsInside(Right, Left))
        {
            Main.ShowToast(Loc.T("CompareNested"), isError: true);
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = Loc.T("StatusComparing");
        ClearResult();
        var progress = new Progress<CompareProgress>(p => Status = Loc.F("StatusCompared", p.Compared.ToString("N0"), p.Current));
        try
        {
            var result = await FolderCompare.CompareAsync(Left, Right, ByContent, progress, _cts.Token);
            foreach (var entry in result.Entries)
            {
                _all.Add(new CompareRowViewModel(entry, Changed));
            }

            ApplyFilter();
            if (result.Cancelled)
            {
                Main.ShowToast(Loc.T("ToastCancelled"));
            }

            foreach (var error in result.Errors.Take(3))
            {
                Main.ShowToast(error, isError: true);
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
            _cts = null;
            Changed();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Rows.Where(r => !r.IsIdentical))
        {
            row.IsChecked = true;
        }
    }

    /// <summary>Header check box: true = all visible differing entries checked, false = none, null = some. A click selects all, or none when all are checked.</summary>
    public bool? AllChecked
    {
        get
        {
            var selectable = Rows.Where(r => !r.IsIdentical).ToList();
            var count = selectable.Count(r => r.IsChecked);
            return count == 0 ? false : count == selectable.Count ? true : null;
        }
        set
        {
            if (AllChecked == true)
            {
                SelectNone();
            }
            else
            {
                SelectAll();
            }
        }
    }

    public bool HasSelectable => Rows.Any(r => !r.IsIdentical);

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var row in _all)
        {
            row.IsChecked = false;
        }
    }

    [RelayCommand]
    private void OpenLeft(CompareRowViewModel? row) => OpenIn(Left, row);

    [RelayCommand]
    private void OpenRight(CompareRowViewModel? row) => OpenIn(Right, row);

    private static void OpenIn(string root, CompareRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var path = Path.Combine(root, row.RelativePath);
        AppEnvironment.OpenFolder(Directory.Exists(path) ? path : Path.GetDirectoryName(path));
    }

    // ------------------------------------------------------------------ Copy

    private bool CanCopyToRight() => !IsBusy && _all.Any(r => r.IsChecked && r.HasLeft && !r.IsIdentical);

    private bool CanCopyToLeft() => !IsBusy && _all.Any(r => r.IsChecked && r.HasRight && !r.IsIdentical);

    [RelayCommand(CanExecute = nameof(CanCopyToRight))]
    private Task CopyToRightAsync() => CopyAsync(toRight: true);

    [RelayCommand(CanExecute = nameof(CanCopyToLeft))]
    private Task CopyToLeftAsync() => CopyAsync(toRight: false);

    private async Task CopyAsync(bool toRight)
    {
        var rows = _all.Where(r => r.IsChecked && !r.IsIdentical && (toRight ? r.HasLeft : r.HasRight)).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var source = toRight ? Left : Right;
        var target = toRight ? Right : Left;
        var replacing = rows.Count(r => r.IsDifferent);
        var bytes = rows.Sum(r => toRight ? r.LeftBytes : r.RightBytes);
        var list = string.Join("\n", rows.Take(10).Select(r => "• " + r.RelativePath)) + (rows.Count > 10 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(
            Loc.F(toRight ? "CopyRightTitle" : "CopyLeftTitle", rows.Count),
            Loc.F("CopyText", ByteSize.Format(bytes), target, list),
            Loc.T("BtnCopy"), danger: false,
            optionText: replacing > 0 ? Loc.F("CopyOverwriteOption", replacing) : null,
            confirmIcon: "IconCopy");
        if (!answer.Confirmed)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = Loc.T("StatusCopying");
        var progress = new Progress<CopyProgress>(p => Status = Loc.F("StatusCopied", p.Files.ToString("N0"), ByteSize.Format(p.Bytes)));
        try
        {
            var result = await FolderCopy.CopyAsync(source, target, rows.Select(r => r.Entry).ToList(), answer.OptionChecked, Main.Engine.Guard,
                progress, _cts.Token);
            Main.ShowToast(Loc.F("ToastCopied", result.Files.ToString("N0"), ByteSize.Format(result.Bytes), result.Skipped.ToString("N0")),
                isError: result.Files == 0 && result.Errors.Count > 0);
            foreach (var error in result.Errors.Take(3))
            {
                Main.ShowToast(error, isError: true);
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
            _cts = null;
        }

        await CompareAsync();
    }

    // ------------------------------------------------------------------ Delete

    private bool CanDeleteLeft() => !IsBusy && _all.Any(r => r.IsChecked && r.HasLeft);

    private bool CanDeleteRight() => !IsBusy && _all.Any(r => r.IsChecked && r.HasRight);

    [RelayCommand(CanExecute = nameof(CanDeleteLeft))]
    private Task DeleteLeftAsync() => DeleteAsync(left: true);

    [RelayCommand(CanExecute = nameof(CanDeleteRight))]
    private Task DeleteRightAsync() => DeleteAsync(left: false);

    private async Task DeleteAsync(bool left)
    {
        var rows = _all.Where(r => r.IsChecked && (left ? r.HasLeft : r.HasRight)).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var root = left ? Left : Right;
        var recycle = Ui.RecycleBinForFolders;
        var bytes = rows.Sum(r => left ? r.LeftBytes : r.RightBytes);
        var list = string.Join("\n", rows.Take(10).Select(r => "• " + r.RelativePath)) + (rows.Count > 10 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(Loc.F(left ? "CompareDeleteLeftTitle" : "CompareDeleteRightTitle", rows.Count),
            Loc.F("CompareDeleteText", ByteSize.Format(bytes), root, list) + "\n\n" + Loc.T(recycle ? "ModeRecycleBin" : "ModePermanent"),
            Loc.T("BtnDelete"), danger: true, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusDeleting");
        try
        {
            var engine = Main.Engine;
            var report = new CleanReport { Area = "Compare" };
            var result = new RuleCleanResult { RuleId = "compare", RuleName = Loc.T("AreaCompare") };
            report.Rules.Add(result);
            await Task.Run(() =>
            {
                var files = new List<(string Path, long Bytes)>();
                foreach (var row in rows)
                {
                    var path = Path.Combine(root, row.RelativePath);
                    var size = left ? row.LeftBytes : row.RightBytes;
                    if (row.IsFolder && Directory.Exists(path))
                    {
                        var folder = SafeDelete.DeleteFolder(path, size, engine.Guard, engine.Platform.RecycleBin, recycle);
                        if (folder.Deleted)
                        {
                            result.DeletedFiles += row.Entry.Files;
                            result.DeletedBytes += folder.Bytes;
                        }

                        if (folder.Error is { } error)
                        {
                            result.AddError(error);
                        }
                    }
                    else if (File.Exists(path))
                    {
                        files.Add((path, size));
                    }
                }

                var (count, deleted, errors) = SafeDelete.DeleteFiles(files, engine.Guard, engine.Platform.RecycleBin, recycle);
                result.DeletedFiles += count;
                result.DeletedBytes += deleted;
                foreach (var error in errors)
                {
                    result.AddError(error);
                }
            });

            report.FinishedUtc = DateTime.UtcNow;
            await Task.Run(() => ReportStore.Save(report));
            Main.Overview.Refresh();
            Main.Reports.MarkDirty();
            Main.ShowToast(Loc.F("ResultText", ByteSize.Format(result.DeletedBytes), result.DeletedFiles.ToString("N0"), "0"),
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
        }

        await CompareAsync();
    }

    // ------------------------------------------------------------------ Helpers

    private void ApplyFilter()
    {
        var search = Search.Trim();
        Rows.Clear();
        foreach (var row in _all.Where(r =>
                     ((r.IsIdentical && ShowIdentical) || (r.IsDifferent && ShowDifferent) || (r.IsLeftOnly && ShowLeftOnly) ||
                      (r.IsRightOnly && ShowRightOnly)) &&
                     (search.Length == 0 || r.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase))))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
    }

    private void ClearResult()
    {
        _all.Clear();
        Rows.Clear();
        Changed();
        OnPropertyChanged(nameof(HasRows));
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IdenticalCount));
        OnPropertyChanged(nameof(DifferentCount));
        OnPropertyChanged(nameof(LeftOnlyCount));
        OnPropertyChanged(nameof(RightOnlyCount));
        OnPropertyChanged(nameof(AllChecked));
        OnPropertyChanged(nameof(HasSelectable));
        CopyToRightCommand.NotifyCanExecuteChanged();
        CopyToLeftCommand.NotifyCanExecuteChanged();
        DeleteLeftCommand.NotifyCanExecuteChanged();
        DeleteRightCommand.NotifyCanExecuteChanged();
    }

    private void UpdateInfo()
    {
        LeftInfo = HasLeft ? Left : Loc.T("CompareDropHint");
        RightInfo = HasRight ? Right : Loc.T("CompareDropHint");
    }

    private static bool IsInside(string parent, string child)
    {
        var p = Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(child).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
