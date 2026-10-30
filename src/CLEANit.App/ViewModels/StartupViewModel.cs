using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

public sealed partial class StartupRowViewModel : ObservableObject
{
    private readonly StartupViewModel _owner;
    private bool _suppress;

    public StartupRowViewModel(StartupEntry entry, StartupViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        _isEnabled = entry.Enabled;
    }

    public StartupEntry Entry { get; private set; }

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_suppress)
        {
            _owner.Toggle(this, value);
        }
    }

    /// <summary>Reverts the switch when Windows refused the change.</summary>
    public void Revert(bool value)
    {
        _suppress = true;
        IsEnabled = value;
        _suppress = false;
    }

    public string Name => Entry.Name;

    public string Command => Entry.Command;

    public string LocationText => Loc.T("Startup" + Entry.Location);

    public string Publisher => Entry.Publisher ?? Loc.T("UnknownPublisher");

    public bool Signed => Entry.Signed;

    public bool TargetMissing => Entry.TargetMissing;

    public bool IsMachineWide => Entry.Location is StartupLocation.RunMachine or StartupLocation.RunMachine32 or StartupLocation.FolderCommon;
}

public sealed record StartupBackupRow(string File, StartupBackup Backup)
{
    public string Text => $"{Backup.Name}  ·  {Backup.DeletedUtc.ToLocalTime():g}";
}

/// <summary>Autostart management: enable/disable like the Task Manager, delete with backup, restore.</summary>
public sealed partial class StartupViewModel(MainViewModel main) : PageViewModel(main)
{
    private List<StartupRowViewModel> _all = [];
    private bool _loaded;

    public override string TitleKey => "NavStartup";

    public ObservableCollection<StartupRowViewModel> Rows { get; } = [];

    public ObservableCollection<StartupBackupRow> Backups { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool HasBackups => Backups.Count > 0;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private bool _isBusy;

    public string CountText => Loc.F("StartupCount", _all.Count(r => r.IsEnabled), _all.Count);

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
        IsBusy = true;
        try
        {
            var entries = await StartupManager.ReadAllAsync(CancellationToken.None);
            _all = entries.Select(e => new StartupRowViewModel(e, this)).ToList();
            _loaded = true;
            ApplyFilter();
            LoadBackups();
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

    private void ApplyFilter()
    {
        var term = Search.Trim();
        Rows.Clear();
        foreach (var row in _all.Where(r => term.Length == 0
                                            || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                            || r.Command.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                            || r.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasRows));
    }

    private void LoadBackups()
    {
        Backups.Clear();
        foreach (var (file, backup) in StartupManager.ReadBackups())
        {
            Backups.Add(new StartupBackupRow(file, backup));
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    internal void Toggle(StartupRowViewModel row, bool enabled)
    {
        if (StartupManager.SetEnabled(row.Entry, enabled))
        {
            Main.ShowToast(Loc.F(enabled ? "ToastStartupEnabled" : "ToastStartupDisabled", row.Name));
        }
        else
        {
            row.Revert(!enabled);
            Main.ShowToast(Loc.F("ToastStartupFailed", row.Name), isError: true);
        }

        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>Own program, script or shortcut at sign-in (current user; all users in expert mode).</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var request = await Host.AddStartupAsync(Ui.ExpertMode);
        if (request is null)
        {
            return;
        }

        var (ok, error) = StartupManager.Add(request.Name, request.Target, request.Arguments, request.AllUsers);
        Main.ShowToast(ok ? Loc.F("ToastStartupAdded", request.Name) : Loc.F("ToastStartupAddFailed", request.Name, error ?? ""), isError: !ok);
        if (ok)
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(StartupRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("StartupDeleteTitle", row.Name), Loc.F("StartupDeleteText", row.Command), Loc.T("BtnDelete"),
            danger: true, confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        if (StartupManager.Delete(row.Entry))
        {
            _all.Remove(row);
            Rows.Remove(row);
            LoadBackups();
            Main.ShowToast(Loc.F("ToastStartupDeleted", row.Name));
        }
        else
        {
            Main.ShowToast(Loc.F("ToastStartupFailed", row.Name), isError: true);
        }

        OnPropertyChanged(nameof(CountText));
    }

    [RelayCommand]
    private async Task RestoreAsync(StartupBackupRow? backup)
    {
        if (backup is null)
        {
            return;
        }

        if (StartupManager.Restore(backup.File, backup.Backup))
        {
            Main.ShowToast(Loc.F("ToastStartupRestored", backup.Backup.Name));
            await RefreshAsync();
        }
        else
        {
            Main.ShowToast(Loc.F("ToastStartupFailed", backup.Backup.Name), isError: true);
        }
    }

    [RelayCommand]
    private void OpenLocation(StartupRowViewModel? row)
    {
        if (row is not null)
        {
            StartupManager.OpenLocation(row.Entry);
        }
    }
}
