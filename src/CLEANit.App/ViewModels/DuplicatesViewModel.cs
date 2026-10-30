using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Analysis;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>Files with identical content (SHA-256).</summary>
public sealed partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroupViewModel(DuplicateGroup group, Action changed)
    {
        Group = group;
        Files = new ObservableCollection<FileRowViewModel>(group.Files.OrderBy(f => f.LastWriteUtc).Select(f => new FileRowViewModel(f)));
        foreach (var file in Files)
        {
            file.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileRowViewModel.IsChecked))
                {
                    OnPropertyChanged(nameof(KeepsOne));
                    changed();
                }
            };
        }
    }

    public DuplicateGroup Group { get; }

    public ObservableCollection<FileRowViewModel> Files { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    public string Header => Loc.F("DuplicateHeader", Files.Count, ByteSize.Format(Group.Bytes), ByteSize.Format(Group.Wasted));

    public string HashText => Group.Hash.Length > 16 ? Group.Hash[..16] + "…" : Group.Hash;

    /// <summary>At least one copy stays.</summary>
    public bool KeepsOne => Files.Any(f => !f.IsChecked);

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(KeepsOne));
    }
}

/// <summary>Duplicate finder: size → partial hash → full SHA-256 (cached in SQLite). One copy per group always stays.</summary>
public sealed partial class DuplicatesViewModel(MainViewModel main) : PageViewModel(main)
{
    private CancellationTokenSource? _cts;

    public override string TitleKey => "NavDuplicates";

    /// <summary>First tab: left/right folder comparison with copy.</summary>
    public FolderCompareViewModel Compare { get; } = new(main);

    [ObservableProperty]
    private int _tabIndex;

    /// <summary>Folders dropped on the folder card.</summary>
    public void AddRoots(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths.Where(Directory.Exists))
        {
            if (!Roots.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Roots.Add(path);
                added = true;
            }
        }

        if (added)
        {
            SaveRoots();
        }
    }

    public ObservableCollection<string> Roots { get; } = [];

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public bool HasRoots => Roots.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    [ObservableProperty]
    private decimal _minMb = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(DeleteCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    public string SummaryText
    {
        get
        {
            if (Groups.Count == 0)
            {
                return Loc.T("DuplicatesHint");
            }

            var selected = Groups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
            return Loc.F("DuplicatesSummary", Groups.Count, ByteSize.Format(Groups.Sum(g => g.Group.Wasted)), selected.Count,
                ByteSize.Format(selected.Sum(f => f.File.Bytes)));
        }
    }

    public string ModeText => Loc.T(Ui.RecycleBinForFolders ? "ModeRecycleBin" : "ModePermanent");

    public override void OnActivated()
    {
        Compare.OnActivated();
        if (Roots.Count == 0)
        {
            var stored = Ui.DuplicateRoots.Where(Directory.Exists).ToList();
            if (stored.Count == 0)
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                stored = new[] { "Downloads", "Documents", "Pictures", "Videos", "Music" }.Select(f => Path.Combine(profile, f)).Where(Directory.Exists).ToList();
            }

            foreach (var root in stored)
            {
                Roots.Add(root);
            }

            MinMb = Math.Clamp(Ui.DuplicateMinMb, 0, 100_000);
            OnPropertyChanged(nameof(HasRoots));
            ScanCommand.NotifyCanExecuteChanged();
        }

        OnPropertyChanged(nameof(ModeText));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var group in Groups)
        {
            group.RefreshText();
        }

        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ModeText));
        Compare.OnLanguageChanged();
    }

    [RelayCommand]
    private async Task AddRootAsync()
    {
        var folder = await Host.PickFolderAsync(Loc.T("DuplicatesPickTitle"), Roots.LastOrDefault());
        if (!string.IsNullOrEmpty(folder) && !Roots.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            Roots.Add(folder);
            SaveRoots();
        }
    }

    [RelayCommand]
    private void RemoveRoot(string? root)
    {
        if (root is not null && Roots.Remove(root))
        {
            SaveRoots();
        }
    }

    private void SaveRoots()
    {
        Ui.DuplicateRoots = Roots.ToList();
        Ui.Save();
        OnPropertyChanged(nameof(HasRoots));
        ScanCommand.NotifyCanExecuteChanged();
    }

    private bool CanScan() => !IsBusy && Roots.Count > 0;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        Ui.DuplicateMinMb = (int)MinMb;
        Ui.Save();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsBusy = true;
        IsIndeterminate = true;
        Groups.Clear();
        Status = Loc.T("StatusSearchingFiles");
        var progress = new Progress<FinderProgress>(p =>
        {
            if (p.Stage == "hash" && p.Total > 0)
            {
                IsIndeterminate = false;
                Progress = p.Done * 100d / p.Total;
                Status = Loc.F("StatusHashing", p.Done.ToString("N0"), p.Total.ToString("N0"));
            }
            else
            {
                Status = Loc.F("StatusFilesChecked", p.Done.ToString("N0"), p.Current ?? "");
            }
        });

        SqliteHashCache? cache = null;
        try
        {
            try
            {
                cache = OperatingSystem.IsWindows() ? new SqliteHashCache() : null;
            }
            catch (Exception)
            {
                // Without cache every file is hashed again.
            }

            var groups = await FileFinder.FindDuplicatesAsync(Roots.ToList(), (long)(MinMb * 1024 * 1024), Engine.Guard, cache, progress, token);
            foreach (var group in groups.OrderByDescending(g => g.Wasted).Take(1000))
            {
                Groups.Add(new DuplicateGroupViewModel(group, Changed) { IsExpanded = Groups.Count < 30 });
            }

            if (token.IsCancellationRequested)
            {
                Main.ShowCancelled();
            }
            else
            {
                Main.ShowToast(Loc.F("ToastDuplicatesFound", Groups.Count, ByteSize.Format(Groups.Sum(g => g.Group.Wasted))));
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            cache?.Dispose();
            IsBusy = false;
            Status = "";
            OnPropertyChanged(nameof(HasGroups));
            Changed();
        }
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(SummaryText));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Marks all copies except the oldest (or newest) one.</summary>
    [RelayCommand]
    private void KeepOldest() => Mark(newest: false);

    [RelayCommand]
    private void KeepNewest() => Mark(newest: true);

    private void Mark(bool newest)
    {
        foreach (var group in Groups)
        {
            var keep = newest ? group.Files.MaxBy(f => f.File.LastWriteUtc) : group.Files.MinBy(f => f.File.LastWriteUtc);
            foreach (var file in group.Files)
            {
                file.IsChecked = !ReferenceEquals(file, keep);
            }
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var file in Groups.SelectMany(g => g.Files))
        {
            file.IsChecked = false;
        }
    }

    [RelayCommand]
    private void OpenFolder(FileRowViewModel? row) => AppEnvironment.OpenFolder(row?.Folder);

    [RelayCommand]
    private void OpenRoot(string? root) => AppEnvironment.OpenFolder(root);

    private bool CanDelete() => !IsBusy && Groups.Any(g => g.Files.Any(f => f.IsChecked));

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        // Safety: a group whose copies are all marked keeps its oldest file.
        var selected = new List<FileRowViewModel>();
        foreach (var group in Groups)
        {
            var marked = group.Files.Where(f => f.IsChecked).ToList();
            if (marked.Count == group.Files.Count)
            {
                marked.Remove(marked.MinBy(f => f.File.LastWriteUtc)!);
            }

            selected.AddRange(marked);
        }

        if (selected.Count == 0)
        {
            return;
        }

        var recycle = Ui.RecycleBinForFolders;
        var list = string.Join("\n", selected.Take(10).Select(r => $"• {r.File.Path} ({r.Size})")) + (selected.Count > 10 ? "\n…" : "");
        var answer = await Host.ConfirmAsync(Loc.F("DeleteFilesTitle", selected.Count),
            Loc.F("DeleteFilesText", ByteSize.Format(selected.Sum(r => r.File.Bytes)), list) + "\n\n" + Loc.T("DuplicatesKeepNote") + " "
            + Loc.T(recycle ? "ModeRecycleBin" : "ModePermanent"),
            Loc.T("BtnDelete"), danger: !recycle, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        Status = Loc.T("StatusDeleting");
        try
        {
            var result = await LargeFilesViewModel.DeleteAndReportAsync(Main, selected.Select(r => r.File).ToList(), "Duplicates", recycle);
            foreach (var group in Groups.ToList())
            {
                foreach (var file in group.Files.Where(f => !File.Exists(f.File.Path)).ToList())
                {
                    group.Files.Remove(file);
                }

                if (group.Files.Count < 2)
                {
                    Groups.Remove(group);
                }
                else
                {
                    group.RefreshText();
                }
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
            OnPropertyChanged(nameof(HasGroups));
            Changed();
        }
    }
}
