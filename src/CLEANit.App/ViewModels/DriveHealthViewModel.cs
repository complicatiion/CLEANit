using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.Core.Diagnostics;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>One label/value line in the drive details.</summary>
public sealed record DriveDetailLine(string Label, string Value);

public sealed partial class DriveHealthRowViewModel : ObservableObject
{
    public DriveHealthRowViewModel(DriveHealthInfo info) => Info = info;

    public DriveHealthInfo Info { get; private set; }

    public int Number => Info.Number;

    public void Update(DriveHealthInfo info)
    {
        Info = info;
        OnPropertyChanged(string.Empty);
    }

    [ObservableProperty]
    private bool _isSelected;

    public string Name => Info.Name.Length > 0 ? Info.Name : $"Disk {Info.Number}";

    public string Kind => string.Join(" · ", new[] { Info.BusType, Info.MediaType, Info.Size > 0 ? ByteSize.Format(Info.Size) : "" }
        .Where(s => s.Length > 0));

    public string Volumes => Info.Volumes.Count > 0 ? string.Join("  ", Info.Volumes) : "–";

    public string AssessmentText => Loc.T("DriveState" + Info.Assessment);

    public bool IsGood => Info.Assessment == DriveAssessment.Good;

    public bool IsCaution => Info.Assessment == DriveAssessment.Caution;

    public bool IsBad => Info.Assessment == DriveAssessment.Bad;

    public bool IsUnknown => Info.Assessment == DriveAssessment.Unknown;

    public string Temperature => Info.Temperature is { } t
        ? Info.TemperatureMax is { } max && max > t ? $"{t} °C  (max {max} °C)" : $"{t} °C"
        : "–";

    /// <summary>Above the drive's own warning limit, or above 55 °C (HDD) / 70 °C (SSD) if it reports none.</summary>
    public bool IsHot => Info.Temperature is { } t && t >= (Info.TemperatureWarning ?? (Info.MediaType == "HDD" ? 55 : 70));

    public string Life => Info.Wear is { } w ? Loc.F("DriveLifeLeft", Math.Clamp(100 - w, 0, 100)) : "–";

    public double LifePercent => Info.Wear is { } w ? Math.Clamp(100 - w, 0, 100) : 0;

    public bool HasLife => Info.Wear is not null;

    public string PowerOn => Info.PowerOnHours is { } h ? Loc.F("DrivePowerOnValue", h.ToString("N0"), (h / 24).ToString("N0")) : "–";

    public string PowerCycles => Info.PowerCycles is { } c ? c.ToString("N0") : "–";

    public string Written => Info.BytesWritten is { } b ? ByteSize.Format(b) : "–";

    public string Read => Info.BytesRead is { } b ? ByteSize.Format(b) : "–";

    public bool HasTransfer => Info.BytesWritten is not null || Info.BytesRead is not null;

    public string Errors
    {
        get
        {
            var parts = new List<string>();
            if (Info.MediaErrors is { } media)
            {
                parts.Add(Loc.F("DriveMediaErrors", media.ToString("N0")));
            }

            if (Info.ReallocatedSectors is { } re)
            {
                parts.Add(Loc.F("DriveReallocated", re.ToString("N0")));
            }

            if (Info.PendingSectors is { } pending && pending > 0)
            {
                parts.Add(Loc.F("DrivePending", pending.ToString("N0")));
            }

            if (Info.UncorrectableSectors is { } unc && unc > 0)
            {
                parts.Add(Loc.F("DriveUncorrectable", unc.ToString("N0")));
            }

            if (parts.Count == 0 && (Info.ReadErrors is not null || Info.WriteErrors is not null))
            {
                parts.Add(Loc.F("ReadWriteErrors", (Info.ReadErrors ?? 0).ToString("N0"), (Info.WriteErrors ?? 0).ToString("N0")));
            }

            return parts.Count == 0 ? "–" : string.Join(" · ", parts);
        }
    }

    public bool HasErrors => (Info.MediaErrors ?? 0) + (Info.ReallocatedSectors ?? 0) + (Info.PendingSectors ?? 0) + (Info.UncorrectableSectors ?? 0) > 0;

    public string Findings => Info.Findings.Count == 0
        ? ""
        : string.Join(" · ", Info.Findings.Select(f => f.StartsWith("threshold:", StringComparison.Ordinal)
            ? Loc.F("DriveFindingThreshold", f["threshold:".Length..])
            : Loc.T("DriveFinding" + char.ToUpperInvariant(f[0]) + f[1..])));

    public bool HasFindings => Info.Findings.Count > 0;

    public bool HasAttributes => Info.Attributes.Count > 0;

    public IReadOnlyList<SmartAttributeRowViewModel> Attributes =>
        Info.Attributes.Select(a => new SmartAttributeRowViewModel(a)).ToList();

    public string SourceText => Info.Source switch
    {
        "nvme" => Loc.T("DriveSourceNvme"),
        "smart" => Loc.T("DriveSourceSmart"),
        "windows" => Loc.T("DriveSourceWindows"),
        _ => Loc.T("DriveSourceNone")
    };

    /// <summary>All values for the details panel (only those the drive reports).</summary>
    public IReadOnlyList<DriveDetailLine> Details
    {
        get
        {
            var lines = new List<DriveDetailLine>();
            void Add(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value) && value != "–")
                {
                    lines.Add(new DriveDetailLine(Loc.T(key), value));
                }
            }

            string? N(long? v) => v?.ToString("N0");
            Add("DriveModel", Info.Name);
            Add("DriveSerial", Info.Serial);
            Add("DriveFirmware", Info.Firmware);
            Add("DriveBus", Info.BusType);
            Add("DriveMedia", Info.MediaType);
            Add("ColSize", Info.Size > 0 ? ByteSize.Format(Info.Size) : null);
            Add("DriveVolumes", Info.Volumes.Count > 0 ? string.Join(", ", Info.Volumes) : null);
            Add("DriveRpm", Info.Rpm is { } rpm ? rpm.ToString("N0") + " rpm" : null);
            Add("DriveWindowsHealth", Info.WindowsHealth);
            Add("LblStatus", Info.OperationalStatus);
            Add("LblTemperature", Temperature);
            Add("DriveTempLimits", Info.TemperatureWarning is null && Info.TemperatureCritical is null
                ? null
                : $"{Info.TemperatureWarning?.ToString() ?? "–"} / {Info.TemperatureCritical?.ToString() ?? "–"} °C");
            Add("DriveLife", Life);
            Add("DriveSpare", Info.AvailableSpare is { } spare
                ? Info.Nvme is { } n ? $"{spare} % (min {n.AvailableSpareThreshold} %)" : $"{spare} %"
                : null);
            Add("LblPowerOn", PowerOn);
            Add("DrivePowerCycles", N(Info.PowerCycles));
            Add("DriveUnsafeShutdowns", N(Info.UnsafeShutdowns));
            Add("DriveWritten", Info.BytesWritten is { } w ? ByteSize.Format(w) : null);
            Add("DriveRead", Info.BytesRead is { } r ? ByteSize.Format(r) : null);
            Add("DriveMediaErrorsLabel", N(Info.MediaErrors));
            Add("DriveReallocatedLabel", N(Info.ReallocatedSectors));
            Add("DrivePendingLabel", N(Info.PendingSectors));
            Add("DriveUncorrectableLabel", N(Info.UncorrectableSectors));
            Add("DriveStartStop", N(Info.StartStopCycles));
            Add("DriveLoadUnload", N(Info.LoadUnloadCycles));
            Add("DriveLatency", Info.ReadLatencyMaxMs is null && Info.WriteLatencyMaxMs is null
                ? null
                : $"{N(Info.ReadLatencyMaxMs) ?? "–"} / {N(Info.WriteLatencyMaxMs) ?? "–"} ms");
            if (Info.Nvme is { } nvme)
            {
                Add("DriveHostReads", N(nvme.HostReadCommands));
                Add("DriveHostWrites", N(nvme.HostWriteCommands));
                Add("DriveBusyTime", Loc.F("MinutesCount", nvme.ControllerBusyMinutes.ToString("N0")));
                Add("DriveErrorLog", N(nvme.ErrorLogEntries));
                Add("DriveTempTime", Loc.F("MinutesCount", nvme.WarningTemperatureMinutes.ToString("N0")) + " / " +
                                     Loc.F("MinutesCount", nvme.CriticalTemperatureMinutes.ToString("N0")));
                Add("DriveCriticalWarning", "0x" + nvme.CriticalWarning.ToString("X2"));
            }

            Add("DriveSource", SourceText);
            return lines;
        }
    }
}

public sealed class SmartAttributeRowViewModel(SmartAttribute attribute)
{
    public string Id => attribute.IdHex;

    public int IdSort => attribute.Id;

    public string Name => attribute.Name;

    public int Current => attribute.Current;

    public int Worst => attribute.Worst;

    public int Threshold => attribute.Threshold;

    public long Raw => attribute.Raw;

    public string RawText => attribute.Raw.ToString("N0");

    public bool IsFailing => attribute.IsFailing;

    public string Type => attribute.PreFailure ? Loc.T("SmartPreFail") : Loc.T("SmartOldAge");
}

/// <summary>
/// Expert: drive health – native storage queries (NVMe health log, SATA SMART) plus Windows' own view. Read only.
/// Optional auto refresh while the page is visible (Settings › Drive health).
/// </summary>
public sealed partial class DriveHealthViewModel(MainViewModel main) : PageViewModel(main)
{
    private bool _loaded;
    private bool _active;
    private DispatcherTimer? _timer;
    private double _availableWidth = 1200;

    public override string TitleKey => "NavDriveHealth";

    public ObservableCollection<DriveHealthRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Up to four cards per row; fewer on narrow windows so the values stay readable.</summary>
    public int DriveColumns => Math.Clamp(Math.Min(Rows.Count, (int)(_availableWidth / 290)), 1, 4);

    /// <summary>Set by the view when its width changes.</summary>
    public double AvailableWidth
    {
        get => _availableWidth;
        set
        {
            if (Math.Abs(_availableWidth - value) < 1)
            {
                return;
            }

            var before = DriveColumns;
            _availableWidth = value;
            if (before != DriveColumns)
            {
                OnPropertyChanged(nameof(DriveColumns));
            }
        }
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private DriveHealthRowViewModel? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _lastRefreshText = "";

    public string AutoRefreshText => Ui.DriveHealthRefreshSeconds > 0
        ? Loc.F("DriveAutoRefreshOn", IntervalText(Ui.DriveHealthRefreshSeconds))
        : "";

    public bool HasAutoRefresh => Ui.DriveHealthRefreshSeconds > 0;

    public static string IntervalText(int seconds) => seconds >= 60 ? Loc.F("MinutesCount", seconds / 60) : Loc.F("SecondsCount", seconds);

    public override void OnActivated()
    {
        _active = true;
        if (!_loaded)
        {
            _ = RefreshAsync();
        }

        ApplyRefreshInterval();
    }

    public override void OnDeactivated()
    {
        _active = false;
        _timer?.Stop();
    }

    /// <summary>Starts or stops the timer after the setting changed (only while the page is visible).</summary>
    public void ApplyRefreshInterval()
    {
        _timer?.Stop();
        OnPropertyChanged(nameof(AutoRefreshText));
        OnPropertyChanged(nameof(HasAutoRefresh));
        var seconds = Ui.DriveHealthRefreshSeconds;
        if (!_active || seconds <= 0)
        {
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += (_, _) => _ = QuickRefreshAsync();
        }

        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, seconds));
        _timer.Start();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in Rows)
        {
            row.Update(row.Info);
        }

        OnPropertyChanged(nameof(AutoRefreshText));
    }

    [RelayCommand]
    private void Select(DriveHealthRowViewModel? row)
    {
        var next = ReferenceEquals(Selected, row) ? null : row;
        foreach (var r in Rows)
        {
            r.IsSelected = ReferenceEquals(r, next);
        }

        Selected = next;
    }

    [RelayCommand]
    private void CloseDetails() => Select(null);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!OperatingSystem.IsWindows() || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await DriveHealth.ReadAsync(CancellationToken.None));
            _loaded = true;
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

    /// <summary>Timer: native queries only, no PowerShell; nothing happens while a refresh is running.</summary>
    private async Task QuickRefreshAsync()
    {
        if (!OperatingSystem.IsWindows() || IsBusy || !_loaded || !_active)
        {
            return;
        }

        try
        {
            Apply(await DriveHealth.RefreshAsync(Rows.Select(r => r.Info).ToList(), CancellationToken.None));
        }
        catch
        {
            // A drive may be gone or busy – the next tick tries again.
        }
    }

    private void Apply(IReadOnlyList<DriveHealthInfo> drives)
    {
        var selectedNumber = Selected?.Number;
        var existing = Rows.ToDictionary(r => r.Number);
        var same = drives.Count == Rows.Count && drives.All(d => existing.ContainsKey(d.Number));
        if (same)
        {
            foreach (var drive in drives)
            {
                existing[drive.Number].Update(drive);
            }
        }
        else
        {
            Rows.Clear();
            foreach (var drive in drives)
            {
                Rows.Add(new DriveHealthRowViewModel(drive));
            }

            Selected = null;
            if (selectedNumber is { } n && Rows.FirstOrDefault(r => r.Number == n) is { } again)
            {
                Select(again);
            }
        }

        if (Selected is not null)
        {
            // Details and SMART table are computed properties: refresh the panel.
            OnPropertyChanged(nameof(Selected));
        }

        LastRefreshText = Loc.F("DriveLastRefresh", DateTime.Now.ToString("T"));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(DriveColumns));
    }
}
