using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Engine;
using CLEANit.Core.Models;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

public sealed class ProgramRowViewModel(InstalledProgram program)
{
    public InstalledProgram Program { get; } = program;

    public string Name => Program.Name;

    public string Publisher => Program.Publisher ?? "";

    public string Version => Program.Version ?? "";

    public string Size => Program.SizeBytes is { } bytes ? ByteSize.Format(bytes) : "";

    /// <summary>Sort keys for the DataGrid columns.</summary>
    public long SizeBytes => Program.SizeBytes ?? -1;

    public DateTime InstalledDate => Program.InstallDate ?? DateTime.MinValue;

    public string Installed => Program.InstallDate?.ToString("d") ?? "";

    public string KindText => Loc.T("Kind" + Program.Kind);

    public bool IsStore => Program.Kind == ProgramKind.Store;

    public bool CanModify => Program.CanModify;

    public bool CanRepair => Program.CanRepair;

    public bool HasLocation => !string.IsNullOrWhiteSpace(Program.InstallLocation) && Directory.Exists(Program.InstallLocation);

    public bool HasCommand => !string.IsNullOrWhiteSpace(Program.UninstallCommand);
}

/// <summary>Installed programs: uninstall, modify, repair with the program's own routines, then optional leftover cleanup.</summary>
public sealed partial class ProgramsViewModel(MainViewModel main) : PageViewModel(main)
{
    private List<ProgramRowViewModel> _all = [];
    private bool _loaded;

    public override string TitleKey => "NavPrograms";

    public ObservableCollection<ProgramRowViewModel> Rows { get; } = [];

    public IReadOnlyList<string> Filters => [Loc.T("FilterAll"), Loc.T("FilterDesktop"), Loc.T("FilterStore")];

    [ObservableProperty]
    private int _filterIndex;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    public string CountText => Loc.F("ProgramsCount", Rows.Count, _all.Count);

    /// <summary>Age of the cached list ("as of …"); empty after a fresh scan.</summary>
    [ObservableProperty]
    private string? _cacheText;

    public bool HasCacheText => !string.IsNullOrEmpty(CacheText);

    partial void OnCacheTextChanged(string? value) => OnPropertyChanged(nameof(HasCacheText));

    // ------------------------------------------------------------------ Sorting

    [ObservableProperty]
    private string _sortColumn = "Name";

    [ObservableProperty]
    private bool _sortDescending;

    public bool SortName => SortColumn == "Name";

    public bool SortVersion => SortColumn == "Version";

    public bool SortSize => SortColumn == "Size";

    public bool SortInstalled => SortColumn == "Installed";

    public bool SortType => SortColumn == "Type";

    [RelayCommand]
    private void Sort(string? column)
    {
        if (string.IsNullOrEmpty(column))
        {
            return;
        }

        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column is "Size" or "Installed";
        }

        foreach (var name in new[] { nameof(SortName), nameof(SortVersion), nameof(SortSize), nameof(SortInstalled), nameof(SortType) })
        {
            OnPropertyChanged(name);
        }

        ApplyFilter();
    }

    private IEnumerable<ProgramRowViewModel> Sorted(IEnumerable<ProgramRowViewModel> rows)
    {
        var sorted = SortColumn switch
        {
            "Version" => rows.OrderBy(r => r.Version, StringComparer.CurrentCultureIgnoreCase),
            "Size" => rows.OrderBy(r => r.Program.SizeBytes ?? -1),
            "Installed" => rows.OrderBy(r => r.Program.InstallDate ?? DateTime.MinValue),
            "Type" => rows.OrderBy(r => r.KindText, StringComparer.CurrentCultureIgnoreCase),
            _ => rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        return SortDescending ? sorted.Reverse() : sorted;
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    partial void OnFilterIndexChanged(int value)
    {
        if (value < 0)
        {
            FilterIndex = 0;
            return;
        }

        ApplyFilter();
    }

    /// <summary>Shows the cached list instantly; scans when there is no cache or it is older than a day.</summary>
    public override void OnActivated()
    {
        if (_loaded)
        {
            return;
        }

        var cache = OperatingSystem.IsWindows() ? ProgramCache.Load() : null;
        if (cache is { Programs.Count: > 0 })
        {
            _all = cache.Programs.Select(p => new ProgramRowViewModel(p)).ToList();
            _loaded = true;
            CacheText = Loc.F("ProgramsCacheAge", cache.SavedUtc.ToLocalTime().ToString("g"));
            ApplyFilter();
        }

        if (cache is null || DateTime.UtcNow - cache.SavedUtc > TimeSpan.FromDays(1))
        {
            _ = RefreshAsync();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(Filters));
        ApplyFilter();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        Status = Loc.T("StatusLoadingPrograms");
        try
        {
            var win32 = await Task.Run(InstalledPrograms.ReadWin32);
            var store = await InstalledPrograms.ReadStoreAsync(CancellationToken.None);
            var programs = win32.Concat(store).ToList();
            _all = programs.Select(p => new ProgramRowViewModel(p)).ToList();
            _loaded = true;
            CacheText = null;
            ApplyFilter();
            await Task.Run(() => ProgramCache.Save(programs));
            Main.Overview.Refresh();
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
    }

    private void ApplyFilter()
    {
        var term = Search.Trim();
        Rows.Clear();
        foreach (var row in Sorted(_all.Where(r =>
                     (FilterIndex == 0 || (FilterIndex == 1 ? !r.IsStore : r.IsStore))
                     && (term.Length == 0 || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                          || r.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase)))))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(CountText));
    }

    [RelayCommand]
    private async Task UninstallAsync(ProgramRowViewModel? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        var restore = OperatingSystem.IsWindows() && RestorePoint.IsSupported;
        var answer = await Host.ConfirmAsync(Loc.F("UninstallTitle", row.Name), Loc.T(row.IsStore ? "UninstallStoreText" : "UninstallText"),
            Loc.T("BtnUninstall"), danger: true,
            optionText: restore ? Loc.T("OptionRestorePoint") : null, optionDefault: Ui.RestorePointBeforeUninstall, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (answer.OptionChecked)
            {
                Status = Loc.T("StatusRestorePoint");
                var (result, output) = await RestorePoint.CreateAsync($"CLEANit – uninstall {row.Name}", uninstall: true, CancellationToken.None);
                if (result is RestorePointResult.Failed or RestorePointResult.Disabled)
                {
                    var go = await Host.ConfirmAsync(Loc.T("RestoreFailedTitle"), Loc.F("RestoreFailedText", output.Trim()), Loc.T("BtnContinueAnyway"),
                        danger: true, confirmIcon: "IconDelete");
                    if (!go.Confirmed)
                    {
                        return;
                    }
                }
                else
                {
                    Main.ShowToast(Loc.T(result == RestorePointResult.Created ? "ToastRestorePointCreated" : "ToastRestorePointRecent"));
                }
            }

            Status = Loc.F("StatusUninstalling", row.Name);
            ProgramCache.Invalidate();
            var code = await InstalledPrograms.UninstallAsync(row.Program, CancellationToken.None);
            if (code is null)
            {
                Main.ShowToast(Loc.T("ToastUninstallerMissing"), isError: true);
                return;
            }
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
            return;
        }
        finally
        {
            IsBusy = false;
            Status = "";
        }

        await RefreshAsync();
        var stillInstalled = _all.Any(r => r.Program.Id == row.Program.Id);
        if (stillInstalled)
        {
            Main.ShowToast(Loc.F("ToastStillInstalled", row.Name));
            return;
        }

        Main.ShowToast(Loc.F("ToastUninstalled", row.Name));
        if (!row.IsStore)
        {
            await OfferLeftoversAsync(row.Program);
        }
    }

    [RelayCommand]
    private Task LeftoversAsync(ProgramRowViewModel? row) => row is null ? Task.CompletedTask : OfferLeftoversAsync(row.Program);

    private async Task OfferLeftoversAsync(InstalledProgram program)
    {
        try
        {
            await OfferLeftoversCoreAsync(program);
        }
        catch (Exception ex)
        {
            IsBusy = false;
            Status = "";
            Main.ShowError(ex);
        }
    }

    private async Task OfferLeftoversCoreAsync(InstalledProgram program)
    {
        Status = Loc.T("StatusSearchingLeftovers");
        IsBusy = true;
        IReadOnlyList<LeftoverFolder> leftovers;
        try
        {
            var guard = Engine.Guard;
            leftovers = await Task.Run(() => InstalledPrograms.FindLeftovers(program, guard));
        }
        finally
        {
            IsBusy = false;
            Status = "";
        }

        if (leftovers.Count == 0)
        {
            Main.ShowToast(Loc.T("ToastNoLeftovers"));
            return;
        }

        var recycle = Ui.RecycleBinForFolders;
        var items = leftovers.Select(l => new ChecklistItem
        {
            Text = l.Path,
            Detail = Loc.F("LeftoverDetail", ByteSize.Format(l.Bytes), l.Files.ToString("N0")),
            Tag = l
        }).ToList();
        var chosen = await Host.ChecklistAsync(Loc.F("LeftoversTitle", program.Name),
            Loc.T("LeftoversText") + " " + Loc.T(recycle ? "ModeRecycleBin" : "ModePermanent"), items, Loc.T("BtnRemoveSelected"), "IconFolderRemove",
            danger: !recycle);
        if (chosen is null || chosen.Count == 0)
        {
            return;
        }

        var report = new CleanReport { Area = "Programs" };
        var result = new RuleCleanResult { RuleId = "leftovers", RuleName = program.Name };
        report.Rules.Add(result);
        foreach (var item in chosen)
        {
            var folder = (LeftoverFolder)item.Tag!;
            var deleted = await Task.Run(() => SafeDelete.DeleteFolder(folder.Path, folder.Bytes, Engine.Guard, Engine.Platform.RecycleBin, recycle));
            if (deleted.Deleted)
            {
                result.DeletedBytes += deleted.Bytes;
                result.DeletedFiles += folder.Files;
            }
            else
            {
                result.AddError($"{folder.Path}: {deleted.Error}");
                result.SkippedInUse++;
            }
        }

        report.FinishedUtc = DateTime.UtcNow;
        await Task.Run(() => ReportStore.Save(report));
        Main.ShowToast(Loc.F("ResultText", ByteSize.Format(result.DeletedBytes), result.DeletedFiles.ToString("N0"), result.SkippedInUse));
        Main.Overview.Refresh();
    }

    [RelayCommand]
    private async Task ModifyAsync(ProgramRowViewModel? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.F("StatusModifying", row.Name);
        try
        {
            if (await InstalledPrograms.ModifyAsync(row.Program, CancellationToken.None) is null)
            {
                Main.ShowToast(Loc.T("ToastUninstallerMissing"), isError: true);
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
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RepairAsync(ProgramRowViewModel? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("RepairTitle", row.Name), Loc.T("RepairText"), Loc.T("BtnRepair"), danger: false, confirmIcon: "IconWrench");
        if (!answer.Confirmed)
        {
            return;
        }

        IsBusy = true;
        Status = Loc.F("StatusRepairing", row.Name);
        try
        {
            var code = await InstalledPrograms.RepairAsync(row.Program, CancellationToken.None);
            Main.ShowToast(code == 0 ? Loc.F("ToastRepaired", row.Name) : Loc.F("ToastRepairCode", row.Name, code?.ToString() ?? "–"), isError: code != 0);
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
    }

    /// <summary>Exports the visible list as CSV (semicolon, UTF-8).</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        static string Q(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { string.Join(";", Q(Loc.T("ColProgram")), Q(Loc.T("ColPublisher")), Q(Loc.T("ColVersion")), Q(Loc.T("ColSize")),
            Q(Loc.T("ColInstalled")), Q(Loc.T("ColType"))) };
        lines.AddRange(Rows.Select(r => string.Join(";", Q(r.Name), Q(r.Publisher), Q(r.Version), Q(r.Program.SizeBytes?.ToString()),
            Q(r.Program.InstallDate?.ToString("yyyy-MM-dd")), Q(r.KindText))));
        try
        {
            var path = await Host.SaveTextAsync(Loc.T("BtnExportCsv"), $"cleanit-programs-{DateTime.Now:yyyyMMdd}.csv", "csv", "CSV",
                string.Join("\r\n", lines));
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

    [RelayCommand]
    private void OpenLocation(ProgramRowViewModel? row) => AppEnvironment.OpenFolder(row?.Program.InstallLocation);

    [RelayCommand]
    private Task CopyUninstallStringAsync(ProgramRowViewModel? row) =>
        row?.Program.UninstallCommand is { } command ? CopyAndToast(command) : Task.CompletedTask;

    private async Task CopyAndToast(string text)
    {
        await Host.CopyAsync(text);
        Main.ShowToast(Loc.T("ToastCopied"));
    }
}
