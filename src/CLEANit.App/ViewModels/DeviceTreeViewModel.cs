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

/// <summary>One visible row of the tree: a folder, one of its largest files, or the "n more files" line.</summary>
public sealed partial class TreeRowViewModel : ObservableObject, IAnimatedRow
{
    public long EnterTicks { get; set; }

    public int EnterDelay { get; set; }

    private TreeRowViewModel(FolderNode? folder, FileLeaf? file, FolderNode parent, int depth, long otherFiles, long parentBytes)
    {
        Folder = folder;
        File = file;
        Parent = parent;
        Depth = depth;
        OtherFiles = otherFiles;
        ParentBytes = parentBytes;
    }

    public static TreeRowViewModel ForFolder(FolderNode folder, int depth) =>
        new(folder, null, folder.Parent ?? folder, depth, 0, folder.Parent?.Bytes ?? folder.Bytes);

    public static TreeRowViewModel ForFile(FileLeaf file, FolderNode parent, int depth) => new(null, file, parent, depth, 0, parent.Bytes);

    public static TreeRowViewModel ForMore(FolderNode parent, int depth) => new(null, null, parent, depth, parent.OtherFiles, parent.Bytes);

    public FolderNode? Folder { get; }

    public FileLeaf? File { get; }

    public FolderNode Parent { get; }

    public int Depth { get; }

    public long OtherFiles { get; }

    private long ParentBytes { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Ticked for deletion (edit mode).</summary>
    [ObservableProperty]
    private bool _isChecked;

    public Action? CheckedChanged { get; set; }

    partial void OnIsCheckedChanged(bool value) => CheckedChanged?.Invoke();

    /// <summary>Folders and listed files can be deleted; the "n more files" line and the root cannot.</summary>
    public bool CanDelete => !IsMore && (File is not null || Folder?.Parent is not null);

    public bool IsFolder => Folder is not null;

    public bool IsFile => File is not null;

    public bool IsMore => Folder is null && File is null;

    public bool CanExpand => Folder is { } f && (f.Children.Count > 0 || f.LargestFiles.Count > 0);

    public Avalonia.Thickness Indent => new(Depth * 18, 0, 0, 0);

    public string Name => Folder?.Name ?? File?.Name ?? Loc.F("TreeMoreFiles", OtherFiles.ToString("N0"));

    public long Bytes => Folder?.Bytes ?? File?.Bytes ?? 0;

    public string Size => IsMore ? "" : ByteSize.Format(Bytes);

    public double Percent => ParentBytes > 0 && !IsMore ? Bytes * 100d / ParentBytes : 0;

    public string PercentText => IsMore ? "" : $"{Percent:0.0} %";

    public string Files => Folder is { } f ? f.Files.ToString("N0") : "";

    public string Folders => Folder is { } f ? f.Folders.ToString("N0") : "";

    public DateTime Newest => Folder?.NewestUtc ?? File?.LastWriteUtc ?? DateTime.MinValue;

    public string Modified => Newest == DateTime.MinValue ? "" : Newest.ToLocalTime().ToString("d");

    public bool Denied => Folder?.Denied == true;

    public string? FullPath => Folder?.Path ?? (File is { } file ? Path.Combine(Parent.Path, file.Name) : null);

    public void RefreshText() => OnPropertyChanged(nameof(Name));
}

/// <summary>
/// Device tree: folder tree of a drive or folder with sizes (TreeSize-light). Read only by default; the edit mode
/// (off on every start) allows deleting folders and files through the safety guard, to the recycle bin by default.
/// Only visible rows exist in the list; folders keep their 25 largest files.
/// </summary>
public sealed partial class DeviceTreeViewModel(MainViewModel main) : PageViewModel(main)
{
    private CancellationTokenSource? _cts;
    private FolderNode? _root;

    public override string TitleKey => "NavDeviceTree";

    public ObservableCollection<DriveRowViewModel> Drives { get; } = [];

    public ObservableCollection<TreeRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private DriveRowViewModel? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ClearFolderCommand))]
    private string _rootPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(ClearFolderCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private TreeRowViewModel? _selected;

    public bool HasSelection => Selected?.FullPath is not null;

    // ------------------------------------------------------------------ Edit mode

    /// <summary>Delete in the tree. Never remembered: the page always starts read only.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCheckedCommand), nameof(DeleteSelectedCommand))]
    private bool _editMode;

    partial void OnEditModeChanged(bool value)
    {
        if (!value)
        {
            foreach (var row in Rows.Where(r => r.IsChecked))
            {
                row.IsChecked = false;
            }
        }

        OnPropertyChanged(nameof(CheckedText));
    }

    public int CheckedCount => Rows.Count(r => r.IsChecked);

    public string CheckedText => Loc.F("TreeChecked", CheckedCount, ByteSize.Format(Rows.Where(r => r.IsChecked).Sum(r => r.Bytes)));

    public string ModeText => Loc.T(Ui.RecycleBinForFolders ? "ModeRecycleBin" : "ModePermanent");

    private void OnRowChecked()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CheckedText));
        DeleteCheckedCommand.NotifyCanExecuteChanged();
    }

    private bool CanDeleteChecked() => EditMode && !IsBusy && Rows.Any(r => r.IsChecked && r.CanDelete);

    private bool CanDeleteSelected() => EditMode && !IsBusy && Selected is { CanDelete: true };

    [RelayCommand(CanExecute = nameof(CanDeleteChecked))]
    private Task DeleteCheckedAsync() => DeleteAsync(Rows.Where(r => r.IsChecked && r.CanDelete).ToList());

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private Task DeleteSelectedAsync() => Selected is { } row ? DeleteAsync([row]) : Task.CompletedTask;

    [RelayCommand]
    private Task DeleteRowAsync(TreeRowViewModel? row) =>
        EditMode && row is { CanDelete: true } ? DeleteAsync([row]) : Task.CompletedTask;

    /// <summary>
    /// Deletes folders and files after a confirmation (recycle bin by default, safety guard for every path), saves a report
    /// and corrects the sizes in the tree without a new scan.
    /// </summary>
    private async Task DeleteAsync(IReadOnlyList<TreeRowViewModel> rows)
    {
        // A folder covers everything below it: drop ticked children of ticked folders.
        var folders = rows.Where(r => r.Folder is not null).Select(r => r.FullPath!).ToList();
        rows = rows.Where(r => !folders.Any(f => r.FullPath != f && r.FullPath!.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var recycle = Ui.RecycleBinForFolders;
        var bytes = rows.Sum(r => r.Bytes);
        var list = string.Join("\n", rows.Take(10).Select(r => $"• {r.FullPath} ({r.Size})")) + (rows.Count > 10 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(Loc.F("TreeDeleteTitle", rows.Count), Loc.F("TreeDeleteText", ByteSize.Format(bytes), list) + "\n\n" +
            Loc.T(recycle ? "ModeRecycleBin" : "ModePermanent"), Loc.T("BtnDelete"), danger: true, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusDeleting");
        var engine = Main.Engine;
        var report = new CleanReport { Area = "DeviceTree" };
        var result = new RuleCleanResult { RuleId = "devicetree", RuleName = Loc.T("AreaDeviceTree") };
        report.Rules.Add(result);
        try
        {
            await Task.Run(() =>
            {
                var files = new List<(string Path, long Bytes)>();
                foreach (var row in rows)
                {
                    if (row.Folder is { } folder)
                    {
                        var deleted = SafeDelete.DeleteFolder(folder.Path, folder.Bytes, engine.Guard, engine.Platform.RecycleBin, recycle);
                        if (deleted.Deleted)
                        {
                            result.DeletedFiles += folder.Files;
                            result.DeletedBytes += deleted.Bytes;
                        }

                        if (deleted.Error is { } error)
                        {
                            result.AddError(error);
                        }
                    }
                    else if (row.FullPath is { } path)
                    {
                        files.Add((path, row.Bytes));
                    }
                }

                var (count, deletedBytes, errors) = SafeDelete.DeleteFiles(files, engine.Guard, engine.Platform.RecycleBin, recycle);
                result.DeletedFiles += count;
                result.DeletedBytes += deletedBytes;
                foreach (var error in errors)
                {
                    result.AddError(error);
                }
            });

            // Correct the tree for everything that is really gone.
            foreach (var row in rows)
            {
                if (row.Folder is { } folder && !Directory.Exists(folder.Path))
                {
                    FolderTree.RemoveFolder(folder);
                }
                else if (row.File is { } file && row.FullPath is { } path && !System.IO.File.Exists(path))
                {
                    FolderTree.RemoveFile(row.Parent, file);
                }
            }

            report.FinishedUtc = DateTime.UtcNow;
            await Task.Run(() => ReportStore.Save(report));
            Main.Reports.MarkDirty();
            Main.Overview.Refresh();
            Main.ShowToast(Loc.F("ResultText", ByteSize.Format(result.DeletedBytes), result.DeletedFiles.ToString("N0"), "0"),
                isError: result.DeletedFiles == 0 && result.Errors.Count > 0);
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
            Selected = null;
            Rebuild();
            UpdateSummary();
            OnRowChecked();
        }
    }

    partial void OnSelectedDriveChanged(DriveRowViewModel? value)
    {
        if (value is not null)
        {
            RootPath = value.Drive.Name;
        }
    }

    partial void OnSelectedChanged(TreeRowViewModel? oldValue, TreeRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }
    }

    // ------------------------------------------------------------------ Sorting

    public IReadOnlyList<string> SortOptions => [Loc.T("SortSize"), Loc.T("SortName"), Loc.T("SortFiles"), Loc.T("SortModified")];

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private bool _sortDescending = true;

    partial void OnSortIndexChanged(int value)
    {
        if (value < 0)
        {
            SortIndex = 0;
            return;
        }

        SortDescending = value != 1;
        Rebuild();
    }

    partial void OnSortDescendingChanged(bool value) => Rebuild();

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    private IEnumerable<FolderNode> SortFolders(IEnumerable<FolderNode> folders)
    {
        var sorted = SortIndex switch
        {
            1 => folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => folders.OrderBy(f => f.Files),
            3 => folders.OrderBy(f => f.NewestUtc),
            _ => folders.OrderBy(f => f.Bytes)
        };
        return SortDescending ? sorted.Reverse() : sorted;
    }

    private IEnumerable<FileLeaf> SortFiles(IEnumerable<FileLeaf> files)
    {
        var sorted = SortIndex switch
        {
            1 => files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase),
            3 => files.OrderBy(f => f.LastWriteUtc),
            _ => files.OrderBy(f => f.Bytes)
        };
        return SortDescending ? sorted.Reverse() : sorted;
    }

    // ------------------------------------------------------------------ Load

    public override void OnActivated()
    {
        if (Drives.Count == 0)
        {
            foreach (var drive in DiskUsage.Drives())
            {
                Drives.Add(new DriveRowViewModel(drive));
            }

            SelectedDrive = Drives.FirstOrDefault();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(SortOptions));
        foreach (var row in Rows.Where(r => r.IsMore))
        {
            row.RefreshText();
        }

        UpdateSummary();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await Host.PickFolderAsync(Loc.T("DiskPickTitle"), RootPath);
        if (!string.IsNullOrEmpty(folder))
        {
            RootPath = folder;
            await ScanAsync();
        }
    }

    /// <summary>A folder (not a drive root) is set – chosen with "Choose folder" or typed.</summary>
    private bool CanClearFolder() => !IsBusy && !string.IsNullOrWhiteSpace(RootPath) &&
                                     !string.Equals(Path.GetPathRoot(RootPath.Trim())?.TrimEnd('\\'), RootPath.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Clears the chosen folder and the tree (nothing is deleted on disk).</summary>
    [RelayCommand(CanExecute = nameof(CanClearFolder))]
    private void ClearFolder()
    {
        _root = null;
        Selected = null;
        Rows.Clear();
        OnPropertyChanged(nameof(HasRows));
        SelectedDrive = null;
        RootPath = "";
        UpdateSummary();
        OnRowChecked();
    }

    private bool CanScan() => !IsBusy && Directory.Exists(RootPath);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        Rows.Clear();
        Selected = null;
        _root = null;
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        var progress = new Progress<TreeProgress>(p => Status = Loc.F("StatusTree", p.Folders.ToString("N0"), p.Files.ToString("N0"), p.Current));
        try
        {
            _root = await FolderTree.ScanAsync(RootPath, progress, token);
            var top = TreeRowViewModel.ForFolder(_root, 0);
            top.IsExpanded = true;
            top.CheckedChanged = OnRowChecked;
            Rows.Add(top);
            InsertChildren(top, 1);
            UpdateSummary(token.IsCancellationRequested);
            if (token.IsCancellationRequested)
            {
                Main.ShowCancelled();
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
            OnPropertyChanged(nameof(HasRows));
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void UpdateSummary(bool partial = false)
    {
        Summary = _root is null
            ? ""
            : Loc.F("TreeSummary", ByteSize.Format(_root.Bytes), _root.Files.ToString("N0"), _root.Folders.ToString("N0"))
              + (partial ? "  ·  " + Loc.T("DiskPartial") : "");
    }

    // ------------------------------------------------------------------ Expand / collapse

    [RelayCommand]
    private void Toggle(TreeRowViewModel? row)
    {
        if (row is null || !row.CanExpand)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        if (row.IsExpanded)
        {
            row.IsExpanded = false;
            while (index + 1 < Rows.Count && Rows[index + 1].Depth > row.Depth)
            {
                Rows.RemoveAt(index + 1);
            }
        }
        else
        {
            row.IsExpanded = true;
            InsertChildren(row, index + 1);
        }
    }

    private int InsertChildren(TreeRowViewModel row, int at)
    {
        var children = ChildRows(row).ToList();
        AnimatedRows.MarkEntering(children);
        foreach (var child in children)
        {
            child.CheckedChanged = OnRowChecked;
            Rows.Insert(at++, child);
        }

        return at;
    }

    /// <summary>Sub folders (sorted), the largest files (sorted), then the "n more files" line.</summary>
    private IEnumerable<TreeRowViewModel> ChildRows(TreeRowViewModel row)
    {
        if (row.Folder is not { } folder)
        {
            yield break;
        }

        var depth = row.Depth + 1;
        foreach (var child in SortFolders(folder.Children))
        {
            yield return TreeRowViewModel.ForFolder(child, depth);
        }

        foreach (var file in SortFiles(folder.LargestFiles))
        {
            yield return TreeRowViewModel.ForFile(file, folder, depth);
        }

        if (folder.OtherFiles > 0)
        {
            yield return TreeRowViewModel.ForMore(folder, depth);
        }
    }

    /// <summary>New sort order: rebuild the visible rows, open folders stay open.</summary>
    private void Rebuild()
    {
        if (_root is null)
        {
            return;
        }

        var open = Rows.Where(r => r.IsExpanded && r.Folder is not null).Select(r => r.Folder!).ToHashSet();
        open.Add(_root);
        var list = new List<TreeRowViewModel>();
        Append(list, TreeRowViewModel.ForFolder(_root, 0), open);
        Rows.Clear();
        foreach (var row in list)
        {
            row.CheckedChanged = OnRowChecked;
            Rows.Add(row);
        }
    }

    private void Append(List<TreeRowViewModel> list, TreeRowViewModel row, HashSet<FolderNode> open)
    {
        list.Add(row);
        if (row.Folder is { } folder && open.Contains(folder))
        {
            row.IsExpanded = true;
            foreach (var child in ChildRows(row))
            {
                Append(list, child, open);
            }
        }
    }

    // ------------------------------------------------------------------ Actions

    [RelayCommand]
    private void Select(TreeRowViewModel? row) => Selected = row;

    [RelayCommand]
    private void ShowInExplorer(TreeRowViewModel? row) => AppEnvironment.OpenFolder((row ?? Selected)?.FullPath);

    [RelayCommand]
    private async Task CopyPathAsync(TreeRowViewModel? row)
    {
        if ((row ?? Selected)?.FullPath is { } path)
        {
            await Host.CopyAsync(path);
            Main.ShowToast(Loc.F("ToastCopiedValue", path));
        }
    }

    /// <summary>Opens the folder in the Disk usage page (list, treemap, file types).</summary>
    [RelayCommand]
    private void OpenInDiskUsage(TreeRowViewModel? row)
    {
        if ((row ?? Selected)?.Folder is { } folder)
        {
            Main.NavigateTo(Main.DiskUsage);
            _ = Main.DiskUsage.AnalyzeFolderAsync(folder.Path);
        }
    }
}
