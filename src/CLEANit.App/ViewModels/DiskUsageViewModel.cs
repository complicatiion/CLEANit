using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Analysis;
using CLEANit.Core.Storage;

namespace CLEANit.App.ViewModels;

public sealed class DriveRowViewModel(DriveUsage drive)
{
    public DriveUsage Drive { get; } = drive;

    public string Name => string.IsNullOrWhiteSpace(Drive.Label) ? Drive.Name.TrimEnd('\\') : $"{Drive.Label} ({Drive.Name.TrimEnd('\\')})";

    public double UsedPercent => Drive.UsedPercent;

    public string FreeText => Loc.F("DriveFree", ByteSize.Format(Drive.Free), ByteSize.Format(Drive.Total));

    public bool IsLow => Drive.Total > 0 && Drive.Free * 100d / Drive.Total < 10;
}

public sealed class FileTypeRowViewModel(FileTypeUsage type, long totalBytes)
{
    public FileTypeUsage Type { get; } = type;

    public string Name => Type.Extension switch
    {
        "" => Loc.T("TypeNoExtension"),
        "*" => Loc.T("TypeOther"),
        _ => Type.Extension
    };

    public string Size => ByteSize.Format(Type.Bytes);

    public string Files => Type.Files.ToString("N0");

    public double Percent => totalBytes > 0 ? Type.Bytes * 100d / totalBytes : 0;

    public string PercentText => $"{Percent:0.0} %";
}

public sealed class UsageRowViewModel(UsageItem item, long parentBytes)
{
    public UsageItem Item { get; } = item;

    public string Name => Item.Name;

    public bool IsFolder => Item.IsFolder;

    public string Size => ByteSize.Format(Item.Bytes);

    public double Percent => parentBytes > 0 ? Item.Bytes * 100d / parentBytes : 0;

    public string PercentText => $"{Percent:0.0} %";

    public string Files => Item.IsFolder ? Item.Files.ToString("N0") : "";

    public string Modified => Item.LastWriteUtc == DateTime.MinValue ? "" : Item.LastWriteUtc.ToLocalTime().ToString("d");
}

/// <summary>Disk usage (read only): drives, the largest folders as list or treemap, file types; drill down by folder.</summary>
public sealed partial class DiskUsageViewModel(MainViewModel main) : PageViewModel(main)
{
    private CancellationTokenSource? _cts;
    private string? _typesPath;

    public override string TitleKey => "NavDisk";

    public ObservableCollection<DriveRowViewModel> Drives { get; } = [];

    public ObservableCollection<UsageRowViewModel> Rows { get; } = [];

    public ObservableCollection<FileTypeRowViewModel> Types { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool HasTypes => Types.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPath), nameof(CanGoUp))]
    private string? _currentPath;

    public bool HasPath => !string.IsNullOrEmpty(CurrentPath);

    public bool CanGoUp => HasPath && Directory.GetParent(CurrentPath!.TrimEnd('\\') + "\\")?.Parent is not null;

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    // ------------------------------------------------------------------ View mode: list, treemap, file types

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListMode), nameof(IsTreemapMode), nameof(IsTypesMode))]
    private string _mode = "List";

    public bool IsListMode => Mode == "List";

    public bool IsTreemapMode => Mode == "Treemap";

    public bool IsTypesMode => Mode == "Types";

    [RelayCommand]
    private async Task SetModeAsync(string? mode)
    {
        Mode = mode is "Treemap" or "Types" ? mode : "List";
        if (IsTypesMode && HasPath && !IsBusy && !string.Equals(_typesPath, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            await LoadTypesAsync(CurrentPath!);
        }
    }

    public override void OnActivated() => LoadDrives();

    /// <summary>Drive tiles per row (fill the full width).</summary>
    public int DriveColumns => Math.Clamp(Drives.Count, 1, 4);

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var drive in DiskUsage.Drives())
        {
            Drives.Add(new DriveRowViewModel(drive));
        }

        OnPropertyChanged(nameof(DriveColumns));
    }

    /// <summary>Drive health is a normal tool now; the drive tiles link to it.</summary>
    [RelayCommand]
    private void OpenDriveHealth() => Main.NavigateTo(Main.DriveHealth);

    /// <summary>Asks for a file name (JPEG) for the treemap export; null = cancelled.</summary>
    public Task<string?> PickTreemapFileAsync()
    {
        var name = string.IsNullOrEmpty(CurrentPath) ? "treemap" : new string(CurrentPath.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        return Host.PickSavePathAsync(Loc.T("TreemapExportTitle"), $"CLEANit-treemap-{name}-{DateTime.Now:yyyyMMdd-HHmm}.jpg", "jpg", "JPEG");
    }

    [RelayCommand]
    private Task AnalyzeDriveAsync(DriveRowViewModel? drive) => drive is null ? Task.CompletedTask : AnalyzeAsync(drive.Drive.Name);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = await Host.PickFolderAsync(Loc.T("DiskPickTitle"), CurrentPath);
        if (!string.IsNullOrEmpty(folder))
        {
            await AnalyzeAsync(folder);
        }
    }

    [RelayCommand]
    private Task OpenAsync(UsageRowViewModel? row) =>
        row is { IsFolder: true } ? AnalyzeAsync(row.Item.Path) : Task.CompletedTask;

    [RelayCommand]
    private Task UpAsync()
    {
        var parent = CurrentPath is null ? null : Directory.GetParent(CurrentPath.TrimEnd('\\'))?.FullName;
        return parent is null ? Task.CompletedTask : AnalyzeAsync(parent);
    }

    [RelayCommand]
    private void ShowInExplorer(UsageRowViewModel? row) => AppEnvironment.OpenFolder(row?.Item.Path ?? CurrentPath);

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Opens a folder from another page (device tree).</summary>
    public Task AnalyzeFolderAsync(string path) => AnalyzeAsync(path);

    private async Task AnalyzeAsync(string path)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        IsBusy = true;
        CurrentPath = path;
        Rows.Clear();
        Types.Clear();
        _typesPath = null;
        OnPropertyChanged(nameof(HasTypes));
        var progress = new Progress<string>(p => Status = p);
        try
        {
            // Cancelling returns partial sizes (no exception).
            var items = await DiskUsage.AnalyzeAsync(path, progress, token);
            if (!ReferenceEquals(cts, _cts))
            {
                // A newer analysis replaced this one: drop the stale result.
                return;
            }

            var total = items.Sum(i => i.Bytes);
            foreach (var item in items.Take(500))
            {
                Rows.Add(new UsageRowViewModel(item, total));
            }

            TotalText = Loc.F("DiskTotal", ByteSize.Format(total), items.Count) + (token.IsCancellationRequested ? "  ·  " + Loc.T("DiskPartial") : "");
            if (token.IsCancellationRequested)
            {
                Main.ShowCancelled();
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(cts, _cts))
            {
                Main.ShowError(ex);
            }
        }
        finally
        {
            if (ReferenceEquals(cts, _cts))
            {
                IsBusy = false;
                Status = "";
                OnPropertyChanged(nameof(HasRows));
            }
        }

        if (ReferenceEquals(cts, _cts) && IsTypesMode && !token.IsCancellationRequested)
        {
            await LoadTypesAsync(path);
        }
    }

    private async Task LoadTypesAsync(string path)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var token = cts.Token;
        IsBusy = true;
        Status = Loc.T("StatusFileTypes");
        Types.Clear();
        var progress = new Progress<long>(n => Status = Loc.F("StatusFileTypesCount", n.ToString("N0")));
        try
        {
            var types = await DiskUsage.FileTypesAsync(path, 40, progress, token);
            if (!ReferenceEquals(cts, _cts))
            {
                return;
            }

            var total = types.Sum(t => t.Bytes);
            foreach (var type in types)
            {
                Types.Add(new FileTypeRowViewModel(type, total));
            }

            _typesPath = token.IsCancellationRequested ? null : path;
            if (token.IsCancellationRequested)
            {
                Main.ShowCancelled();
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(cts, _cts))
            {
                Main.ShowError(ex);
            }
        }
        finally
        {
            if (ReferenceEquals(cts, _cts))
            {
                IsBusy = false;
                Status = "";
                OnPropertyChanged(nameof(HasTypes));
            }
        }
    }
}
