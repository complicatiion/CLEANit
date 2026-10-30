using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

public sealed class ServiceRowViewModel(ServiceEntry entry)
{
    public ServiceEntry Entry { get; } = entry;

    public string Name => Entry.DisplayName;

    public string ServiceName => Entry.Name;

    public string Publisher => Entry.Publisher ?? Loc.T("UnknownPublisher");

    public string StartText => Entry.Delayed ? Loc.T("StartAutoDelayed") : Loc.T("StartAuto");

    public string State => Entry.State switch
    {
        "Running" => Loc.T("ServiceStateRunning"),
        "Stopped" => Loc.T("ServiceStateStopped"),
        _ => Entry.State
    };

    public bool IsRunning => Entry.State.Equals("Running", StringComparison.OrdinalIgnoreCase);

    public bool IsStopped => Entry.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase);

    public bool IsProtected => ServicesManager.IsProtected(Entry.Name);

    /// <summary>Stop: running, accepts stop requests, not a protected Windows core service.</summary>
    public bool CanStop => IsRunning && Entry.AcceptStop && !IsProtected;

    public bool CanStart => IsStopped;

    public string StopTip => IsProtected ? Loc.T("TipServiceProtected") : IsRunning && !Entry.AcceptStop ? Loc.T("TipServiceNoStop") : Loc.T("TipServiceStop");

    public bool IsMicrosoft => Entry.IsMicrosoft;

    public bool IsUnsigned => !Entry.Signed;

    public string Path => Entry.Path ?? "";
}

public sealed class ServiceBackupRowViewModel(string file, ServiceBackup backup)
{
    public string File { get; } = file;

    public ServiceBackup Backup { get; } = backup;

    public string Text => $"{Backup.DisplayName}  ·  {Backup.ChangedUtc.ToLocalTime():g}";
}

/// <summary>
/// Expert: automatically starting services – start, stop (protected Windows core services excluded) and "Manual" with
/// backup and restore. Nothing is deleted.
/// </summary>
public sealed partial class ServicesViewModel(MainViewModel main) : PageViewModel(main)
{
    private List<ServiceRowViewModel> _all = [];
    private bool _loaded;

    public override string TitleKey => "NavServices";

    public ObservableCollection<ServiceRowViewModel> Rows { get; } = [];

    public ObservableCollection<ServiceBackupRowViewModel> Backups { get; } = [];

    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    private bool _showMicrosoft;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private bool _isBusy;

    public string CountText => Loc.F("ServicesCount", Rows.Count, _all.Count);

    partial void OnShowMicrosoftChanged(bool value) => ApplyFilter();

    partial void OnSearchChanged(string value) => ApplyFilter();

    public override void OnActivated()
    {
        if (!_loaded)
        {
            _ = RefreshAsync();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IsBusy = true;
        try
        {
            _all = (await ServicesManager.ReadAutomaticAsync(CancellationToken.None)).Select(s => new ServiceRowViewModel(s)).ToList();
            _loaded = true;
            LoadBackups();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadBackups()
    {
        Backups.Clear();
        if (OperatingSystem.IsWindows())
        {
            foreach (var (file, backup) in ServicesManager.ReadBackups())
            {
                Backups.Add(new ServiceBackupRowViewModel(file, backup));
            }
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    private void ApplyFilter()
    {
        var term = Search.Trim();
        Rows.Clear();
        foreach (var row in _all.Where(r => (ShowMicrosoft || !r.IsMicrosoft)
                                            && (term.Length == 0 || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                                                 || r.ServiceName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                                                 || r.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase))))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(CountText));
    }

    [RelayCommand]
    private async Task SetManualAsync(ServiceRowViewModel? row)
    {
        if (row is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("ServiceManualTitle", row.Name),
            Loc.F("ServiceManualText", row.ServiceName) + (row.IsMicrosoft ? "\n\n" + Loc.T("ServiceMicrosoftWarning") : ""),
            Loc.T("BtnSetManual"), danger: row.IsMicrosoft, confirmIcon: "IconServices");
        if (!answer.Confirmed)
        {
            return;
        }

        if (ServicesManager.SetManual(row.Entry))
        {
            Main.ShowToast(Loc.F("ToastServiceManual", row.Name));
        }
        else
        {
            Main.ShowToast(Loc.F("ToastServiceFailed", row.Name), isError: true);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task StopAsync(ServiceRowViewModel? row)
    {
        if (row is null || !row.CanStop || !OperatingSystem.IsWindows())
        {
            return;
        }

        IsBusy = true;
        IReadOnlyList<string> dependents;
        try
        {
            dependents = await ServicesManager.RunningDependentsAsync(row.ServiceName, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }

        var text = Loc.F("ServiceStopText", row.Name)
                   + (dependents.Count > 0 ? "\n\n" + Loc.T("ServiceStopDependents") + "\n" + string.Join("\n", dependents.Select(d => "• " + d)) : "")
                   + (row.IsMicrosoft ? "\n\n" + Loc.T("ServiceMicrosoftWarning") : "");
        var answer = await Host.ConfirmAsync(Loc.F("ServiceStopTitle", row.Name), text, Loc.T("BtnStop"), danger: true, confirmIcon: "IconStop");
        if (!answer.Confirmed)
        {
            return;
        }

        await RunAsync(row, () => ServicesManager.StopAsync(row.ServiceName, CancellationToken.None), "ToastServiceStopped");
    }

    [RelayCommand]
    private async Task StartAsync(ServiceRowViewModel? row)
    {
        if (row is null || !row.CanStart || !OperatingSystem.IsWindows())
        {
            return;
        }

        await RunAsync(row, () => ServicesManager.StartAsync(row.ServiceName, CancellationToken.None), "ToastServiceStarted");
    }

    private async Task RunAsync(ServiceRowViewModel row, Func<Task<(bool Ok, string? Error)>> action, string successKey)
    {
        IsBusy = true;
        try
        {
            var (ok, error) = await action();
            Main.ShowToast(ok ? Loc.F(successKey, row.Name) : Loc.F("ToastServiceFailed", row.Name) + (error is null ? "" : " – " + error), isError: !ok);
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RestoreAsync(ServiceBackupRowViewModel? backup)
    {
        if (backup is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("ServiceRestoreTitle", backup.Backup.DisplayName),
            Loc.F("ServiceRestoreText", backup.Backup.Name), Loc.T("BtnRestore"), danger: false, confirmIcon: "IconUndo");
        if (!answer.Confirmed)
        {
            return;
        }

        if (ServicesManager.Restore(backup.File, backup.Backup))
        {
            Main.ShowToast(Loc.F("ToastServiceRestored", backup.Backup.DisplayName));
        }
        else
        {
            Main.ShowToast(Loc.F("ToastServiceFailed", backup.Backup.DisplayName), isError: true);
        }

        await RefreshAsync();
    }
}
