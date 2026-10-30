using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.App.Services;
using CLEANit.Core.Software;
using CLEANit.Core.Storage;
using CLEANit.Platform.Windows;

namespace CLEANit.App.ViewModels;

/// <summary>Search result row.</summary>
public sealed partial class CatalogRowViewModel(CatalogPackage package) : ObservableObject
{
    public CatalogPackage Package { get; } = package;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Publisher), nameof(License), nameof(HasDetails))]
    private PackageDetails? _details;

    [ObservableProperty]
    private string _state = "";

    public string Name => Details?.Name is { Length: > 0 } name ? name : Package.Name;

    public string Id => Details?.Id ?? Package.Id;

    public string Version => Package.Version;

    public string Source => Package.Source;

    public string? Match => Package.Match;

    public bool IsWinget => Package.Source is "winget" or "msstore";

    public string Publisher => Details?.Publisher ?? "";

    public string License => Details?.License ?? "";

    public bool HasDetails => Details is not null;
}

/// <summary>Installed program as winget sees it.</summary>
public sealed partial class InstalledRowViewModel(InstalledPackage package) : ObservableObject
{
    public InstalledPackage Package { get; } = package;

    [ObservableProperty]
    private bool _isChecked;

    public string Name => Package.Name;

    public string Id => Package.Id;

    public string Version => Package.Version;

    public string Available => Package.Available ?? "";

    public string Source => Package.Source;

    public bool IsKnown => Package.IsKnown;

    public bool IsUnknown => !Package.IsKnown;

    /// <summary>Only winget and Store packages fit the bundle format.</summary>
    public bool CanBundle => Package.Source is "winget" or "msstore";
}

/// <summary>Package of the selected bundle with its preview state.</summary>
public sealed partial class BundleRowViewModel(BundlePackage package) : ObservableObject
{
    public BundlePackage Package { get; } = package;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(StateText))]
    private bool? _installed;

    [ObservableProperty]
    private string? _installedName;

    [ObservableProperty]
    private string _state = "";

    public string Id => Package.Id;

    public string Version => Package.Version ?? "";

    public string Source => Package.Source;

    public bool IsNew => Installed == false;

    public string StateText => Installed switch
    {
        true => Loc.T("BundleStateInstalled"),
        false => Loc.T("BundleStateNew"),
        _ => ""
    };

    public void RefreshText() => OnPropertyChanged(nameof(StateText));
}

/// <summary>
/// Software catalog on top of winget: search and install, installed programs, package bundles in the native winget format.
/// Packages come from winget's sources under their own licenses; nothing is installed without confirmation.
/// </summary>
public sealed partial class SoftwareCatalogViewModel(MainViewModel main) : PageViewModel(main)
{
    private bool _probed;
    private CancellationTokenSource? _cts;
    private List<InstalledRowViewModel> _installed = [];
    private bool _installedLoaded;

    public override string TitleKey => "NavCatalog";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable), nameof(IsMissing), nameof(VersionText))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(LoadInstalledCommand), nameof(PreviewBundleCommand))]
    private string? _wingetVersion;

    public bool IsAvailable => WingetVersion is not null;

    public bool IsMissing => _probed && WingetVersion is null;

    public string VersionText => WingetVersion is null ? "" : Loc.F("WingetVersion", WingetVersion);

    public bool ChocolateyActive => Ui.ChocolateyEnabled && OperatingSystem.IsWindows() && ChocolateyClient.IsInstalled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(InstallSelectedCommand), nameof(LoadInstalledCommand), nameof(InstallBundleCommand),
        nameof(PreviewBundleCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _output = "";

    [ObservableProperty]
    private int _tabIndex;

    /// <summary>Expert: install for all users of the PC.</summary>
    [ObservableProperty]
    private bool _machineScope;

    public override void OnActivated()
    {
        _ = ActivateAsync();
        LoadBundles();
        OnPropertyChanged(nameof(ChocolateyActive));
    }

    /// <summary>Tabs: Search &amp; install (first), Installed, Bundles.</summary>
    private const int InstalledTab = 1;

    /// <summary>Reads the installed programs when the Installed tab is open and winget is known to be there.</summary>
    private async Task ActivateAsync()
    {
        await ProbeAsync();
        if (TabIndex == InstalledTab && !_installedLoaded && IsAvailable && !IsBusy)
        {
            await LoadInstalledAsync();
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(InstalledSummary));
        foreach (var row in BundlePackages)
        {
            row.RefreshText();
        }
    }

    private async Task ProbeAsync()
    {
        if (_probed)
        {
            return;
        }

        try
        {
            WingetVersion = OperatingSystem.IsWindows() ? await WinGetClient.VersionAsync(CancellationToken.None) : null;
        }
        catch (Exception)
        {
            WingetVersion = null;
        }

        _probed = true;
        OnPropertyChanged(nameof(IsMissing));
    }

    /// <summary>winget contacts its sources: the user accepts their agreements once (shared with the Updates page).</summary>
    private async Task<bool> EnsureAgreementsAsync()
    {
        if (Ui.WingetAgreementsAccepted)
        {
            return true;
        }

        var answer = await Host.ConfirmAsync(Loc.T("WingetConsentTitle"), Loc.T("WingetConsentText"), Loc.T("BtnAcceptContinue"), danger: false,
            confirmIcon: "IconShieldCheck");
        if (!answer.Confirmed)
        {
            return false;
        }

        Ui.WingetAgreementsAccepted = true;
        Ui.Save();
        Main.RestartUpdateTimer();
        return true;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    // ================================================================== Search & install

    public ObservableCollection<CatalogRowViewModel> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedResult))]
    private CatalogRowViewModel? _selectedResult;

    public bool HasSelectedResult => SelectedResult is not null;

    /// <summary>Expert: fixed version for the selected package.</summary>
    [ObservableProperty]
    private string _pinVersion = "";

    public string SearchSummary => Results.Count == 0 ? "" : Loc.F("CatalogResults", Results.Count, Results.Count(r => r.IsChecked));

    partial void OnSelectedResultChanged(CatalogRowViewModel? oldValue, CatalogRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        PinVersion = "";
        if (newValue is null)
        {
            return;
        }

        newValue.IsActive = true;
        if (newValue.Details is null && newValue.IsWinget)
        {
            _ = LoadDetailsAsync(newValue);
        }
    }

    private bool CanSearch() => !IsBusy && IsAvailable && Query.Trim().Length >= 2;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (!await EnsureAgreementsAsync())
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Status = Loc.F("StatusSearching", Query.Trim());
        Results.Clear();
        SelectedResult = null;
        try
        {
            var query = Query.Trim();
            var (packages, output) = await WinGetClient.SearchAsync(query, "winget", Ui.WingetAgreementsAccepted, _cts.Token);
            Output = output.Trim();
            var all = packages.ToList();
            if (ChocolateyActive)
            {
                all.AddRange(await ChocolateyClient.SearchAsync(query, _cts.Token));
            }

            foreach (var package in all.Take(200))
            {
                AddResult(new CatalogRowViewModel(package));
            }

            if (Results.Count == 0)
            {
                Main.ShowToast(Loc.F("ToastNothingFound", query));
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
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(SearchSummary));
        }
    }

    private void AddResult(CatalogRowViewModel row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogRowViewModel.IsChecked))
            {
                OnPropertyChanged(nameof(SearchSummary));
                InstallSelectedCommand.NotifyCanExecuteChanged();
                AddToBundleCommand.NotifyCanExecuteChanged();
            }
        };
        Results.Add(row);
    }

    private async Task LoadDetailsAsync(CatalogRowViewModel row)
    {
        try
        {
            row.Details = await WinGetClient.ShowAsync(row.Package.Id, byName: false, row.Package.Source, Ui.WingetAgreementsAccepted, CancellationToken.None)
                          ?? await WinGetClient.ShowAsync(row.Package.Name, byName: true, row.Package.Source, Ui.WingetAgreementsAccepted, CancellationToken.None);
        }
        catch (Exception)
        {
            // Details are optional.
        }
    }

    [RelayCommand]
    private void SelectResult(CatalogRowViewModel? row) => SelectedResult = row;

    [RelayCommand]
    private void OpenHomepage() => AppEnvironment.OpenUrl(SelectedResult?.Details?.Homepage);

    [RelayCommand]
    private void OpenLicense() => AppEnvironment.OpenUrl(SelectedResult?.Details?.LicenseUrl);

    private bool CanInstall() => !IsBusy && Results.Any(r => r.IsChecked);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedAsync()
    {
        var rows = Results.Where(r => r.IsChecked).ToList();
        IsBusy = true;
        Status = Loc.T("StatusLoadingDetails");
        try
        {
            // Licenses are shown before anything is installed.
            foreach (var row in rows.Where(r => r.Details is null && r.IsWinget))
            {
                await LoadDetailsAsync(row);
            }
        }
        finally
        {
            IsBusy = false;
            Status = "";
        }

        var items = rows.Select(r => new InstallItem(r.Id, r.Name, r.Source, PinFor(r), r.License, r)).ToList();
        await InstallAsync(items);
    }

    private string? PinFor(CatalogRowViewModel row) =>
        IsExpert && ReferenceEquals(row, SelectedResult) && !string.IsNullOrWhiteSpace(PinVersion) ? PinVersion.Trim() : null;

    // ================================================================== Installed

    public ObservableCollection<InstalledRowViewModel> Installed { get; } = [];

    public bool HasInstalled => Installed.Count > 0;

    public IReadOnlyList<string> InstalledFilters => [Loc.T("FilterAll"), Loc.T("FilterWingetKnown"), Loc.T("FilterWingetUnknown")];

    [ObservableProperty]
    private int _installedFilter;

    [ObservableProperty]
    private string _installedSearch = "";

    partial void OnInstalledFilterChanged(int value) => ApplyInstalledFilter();

    partial void OnInstalledSearchChanged(string value) => ApplyInstalledFilter();

    public string InstalledSummary => _installed.Count == 0
        ? ""
        : Loc.F("InstalledSummary", _installed.Count, _installed.Count(r => r.IsKnown), _installed.Count(r => r.IsChecked));

    partial void OnTabIndexChanged(int value)
    {
        if (value == InstalledTab && !_installedLoaded && IsAvailable && !IsBusy)
        {
            _ = LoadInstalledAsync();
        }
    }

    private bool CanLoadInstalled() => !IsBusy && IsAvailable;

    [RelayCommand(CanExecute = nameof(CanLoadInstalled))]
    private async Task LoadInstalledAsync()
    {
        if (!await EnsureAgreementsAsync())
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusReadingInstalled");
        try
        {
            var list = (await ReadInstalledAsync()).Select(p => new InstalledRowViewModel(p)).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (var row in list)
            {
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(InstalledRowViewModel.IsChecked))
                    {
                        OnPropertyChanged(nameof(InstalledSummary));
                        CreateBundleFromInstalledCommand.NotifyCanExecuteChanged();
                    }
                };
            }

            _installed = list;
            _installedLoaded = true;
            ApplyInstalledFilter();
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

    private async Task<List<InstalledPackage>> ReadInstalledAsync()
    {
        var (packages, output) = await WinGetClient.ListInstalledAsync(Ui.WingetAgreementsAccepted, CancellationToken.None);
        Output = output.Trim();
        var list = packages.ToList();
        if (ChocolateyActive)
        {
            list.AddRange(await ChocolateyClient.ListInstalledAsync(CancellationToken.None));
        }

        return list;
    }

    private void ApplyInstalledFilter()
    {
        var term = InstalledSearch.Trim();
        Installed.Clear();
        foreach (var row in _installed.Where(r => (InstalledFilter switch { 1 => r.IsKnown, 2 => r.IsUnknown, _ => true })
                                                  && (term.Length == 0 || r.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                                                                       || r.Id.Contains(term, StringComparison.OrdinalIgnoreCase))))
        {
            Installed.Add(row);
        }

        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(InstalledSummary));
    }

    [RelayCommand]
    private void SelectAllKnown()
    {
        foreach (var row in Installed)
        {
            row.IsChecked = row.CanBundle;
        }
    }

    [RelayCommand]
    private void SelectNoneInstalled()
    {
        foreach (var row in Installed)
        {
            row.IsChecked = false;
        }
    }

    private bool CanCreateFromInstalled() => _installed.Any(r => r.IsChecked && r.CanBundle);

    /// <summary>New bundle from the checked programs that winget knows.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateFromInstalled))]
    private async Task CreateBundleFromInstalledAsync()
    {
        var name = await AskBundleNameAsync(null);
        if (name is null)
        {
            return;
        }

        var bundle = new PackageBundle { Name = name };
        foreach (var row in _installed.Where(r => r.IsChecked && r.CanBundle))
        {
            bundle.Add(new BundlePackage(row.Id, IsExpert ? row.Version : null, row.Source));
        }

        SaveBundle(bundle);
        Main.ShowToast(Loc.F("ToastBundleSaved", name, bundle.Packages.Count));
        TabIndex = 2;
    }

    // ================================================================== Bundles

    public ObservableCollection<string> Bundles { get; } = [];

    public ObservableCollection<BundleRowViewModel> BundlePackages { get; } = [];

    public bool HasBundles => Bundles.Count > 0;

    public bool HasBundlePackages => BundlePackages.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedBundle))]
    [NotifyCanExecuteChangedFor(nameof(AddToBundleCommand), nameof(PreviewBundleCommand), nameof(InstallBundleCommand), nameof(ExportBundleCommand),
        nameof(DeleteBundleCommand))]
    private string? _selectedBundle;

    public bool HasSelectedBundle => SelectedBundle is not null;

    private PackageBundle? _bundle;

    public string BundleSummary => _bundle is null
        ? ""
        : Loc.F("BundleSummary", _bundle.Packages.Count, BundlePackages.Count(p => p.Installed == true), BundlePackages.Count(p => p.IsNew));

    partial void OnSelectedBundleChanged(string? value) => LoadBundle(value);

    private static string BundleFile(string name) => Path.Combine(AppPaths.BundlesDirectory, name + ".json");

    private void LoadBundles()
    {
        var selected = SelectedBundle;
        Bundles.Clear();
        if (Directory.Exists(AppPaths.BundlesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.BundlesDirectory, "*.json").Order(StringComparer.CurrentCultureIgnoreCase))
            {
                Bundles.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        SelectedBundle = Bundles.Contains(selected ?? "") ? selected : Bundles.FirstOrDefault();
        OnPropertyChanged(nameof(HasBundles));
    }

    private void LoadBundle(string? name)
    {
        BundlePackages.Clear();
        _bundle = null;
        if (name is not null && File.Exists(BundleFile(name)))
        {
            try
            {
                _bundle = PackageBundle.Load(BundleFile(name));
                foreach (var package in _bundle.Packages)
                {
                    BundlePackages.Add(new BundleRowViewModel(package));
                }
            }
            catch (Exception ex)
            {
                Main.ShowToast(Loc.F("ToastBundleInvalid", ex.Message), isError: true);
            }
        }

        OnPropertyChanged(nameof(HasBundlePackages));
        OnPropertyChanged(nameof(BundleSummary));
        InstallBundleCommand.NotifyCanExecuteChanged();
    }

    private void SaveBundle(PackageBundle bundle)
    {
        bundle.Save(BundleFile(bundle.Name));
        LoadBundles();
        SelectedBundle = bundle.Name;
        LoadBundle(bundle.Name);
    }

    private async Task<string?> AskBundleNameAsync(string? initial)
    {
        var name = await Host.PromptAsync(Loc.T("BundleNameTitle"), Loc.T("BundleNameText"), Loc.T("BtnSave"), Loc.T("WatermarkBundleName"), initial);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (File.Exists(BundleFile(name)) && name != initial)
        {
            var answer = await Host.ConfirmAsync(Loc.T("BundleOverwriteTitle"), Loc.F("BundleOverwriteText", name), Loc.T("BtnOverwrite"), danger: true,
                confirmIcon: "IconSave");
            if (!answer.Confirmed)
            {
                return null;
            }
        }

        return name;
    }

    [RelayCommand]
    private async Task NewBundleAsync()
    {
        var name = await AskBundleNameAsync(null);
        if (name is not null)
        {
            SaveBundle(new PackageBundle { Name = name });
        }
    }

    private bool CanAddToBundle() => Results.Any(r => r.IsChecked);

    /// <summary>Adds the checked search results to the selected bundle (or a new one).</summary>
    [RelayCommand(CanExecute = nameof(CanAddToBundle))]
    private async Task AddToBundleAsync()
    {
        var rows = Results.Where(r => r.IsChecked).ToList();
        var skipped = rows.Count(r => !r.IsWinget);
        if (_bundle is null)
        {
            var name = await AskBundleNameAsync(null);
            if (name is null)
            {
                return;
            }

            _bundle = new PackageBundle { Name = name };
        }

        var added = rows.Where(r => r.IsWinget).Count(r => _bundle.Add(new BundlePackage(r.Id, PinFor(r), r.Source)));
        SaveBundle(_bundle);
        Main.ShowToast(Loc.F("ToastBundleAdded", added, _bundle.Name) + (skipped > 0 ? " " + Loc.F("ToastBundleSkippedChoco", skipped) : ""));
    }

    [RelayCommand]
    private void RemoveFromBundle(BundleRowViewModel? row)
    {
        if (row is null || _bundle is null)
        {
            return;
        }

        _bundle.Packages.Remove(row.Package);
        _bundle.Save(BundleFile(_bundle.Name));
        BundlePackages.Remove(row);
        OnPropertyChanged(nameof(HasBundlePackages));
        OnPropertyChanged(nameof(BundleSummary));
    }

    private bool HasBundle() => SelectedBundle is not null;

    [RelayCommand(CanExecute = nameof(HasBundle))]
    private async Task DeleteBundleAsync()
    {
        if (SelectedBundle is not { } name)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("BundleDeleteTitle"), Loc.F("BundleDeleteText", name), Loc.T("BtnDelete"), danger: true,
            confirmIcon: "IconDelete");
        if (!answer.Confirmed)
        {
            return;
        }

        try
        {
            File.Delete(BundleFile(name));
        }
        catch (Exception ex)
        {
            Main.ShowError(ex);
        }

        SelectedBundle = null;
        LoadBundles();
    }

    /// <summary>Imports a winget export/import file as a bundle (validated first).</summary>
    [RelayCommand]
    private async Task ImportBundleAsync()
    {
        var file = await Host.PickFileAsync(Loc.T("BtnImportBundle"), "JSON", ["*.json"]);
        if (file is null)
        {
            return;
        }

        try
        {
            var bundle = PackageBundle.Load(file);
            var name = await AskBundleNameAsync(bundle.Name);
            if (name is null)
            {
                return;
            }

            bundle.Name = name;
            SaveBundle(bundle);
            TabIndex = 2;
            Main.ShowToast(Loc.F("ToastBundleImported", name, bundle.Packages.Count));
            await PreviewBundleAsync();
        }
        catch (Exception ex)
        {
            Main.ShowToast(Loc.F("ToastBundleInvalid", ex.Message), isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasBundle))]
    private async Task ExportBundleAsync()
    {
        if (_bundle is null)
        {
            return;
        }

        try
        {
            var path = await Host.SaveTextAsync(Loc.T("BtnExportBundle"), _bundle.Name + ".json", "json", "JSON (winget)", _bundle.ToJson());
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

    private bool CanPreview() => !IsBusy && IsAvailable && SelectedBundle is not null;

    /// <summary>Compares the bundle with the installed programs: already installed or new.</summary>
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewBundleAsync()
    {
        if (_bundle is null || !await EnsureAgreementsAsync())
        {
            return;
        }

        IsBusy = true;
        Status = Loc.T("StatusReadingInstalled");
        try
        {
            var installed = await ReadInstalledAsync();
            var byId = installed.Where(p => p.IsKnown).GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var row in BundlePackages)
            {
                row.Installed = byId.ContainsKey(row.Id);
                row.InstalledName = byId.TryGetValue(row.Id, out var p) ? p.Name : null;
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
            OnPropertyChanged(nameof(BundleSummary));
            InstallBundleCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanInstallBundle() => !IsBusy && BundlePackages.Any(p => p.IsNew);

    /// <summary>Installs the new packages of the bundle (after the preview and a confirmation).</summary>
    [RelayCommand(CanExecute = nameof(CanInstallBundle))]
    private async Task InstallBundleAsync()
    {
        var items = BundlePackages.Where(p => p.IsNew)
            .Select(p => new InstallItem(p.Id, p.Id, p.Source, IsExpert ? p.Package.Version : null, null, p))
            .ToList();
        await InstallAsync(items);
        await PreviewBundleAsync();
    }

    // ================================================================== Install queue

    private sealed record InstallItem(string Id, string Name, string Source, string? Version, string? License, ObservableObject Row);

    private async Task InstallAsync(IReadOnlyList<InstallItem> items)
    {
        if (items.Count == 0 || IsBusy || !await EnsureAgreementsAsync())
        {
            return;
        }

        var list = string.Join("\n", items.Take(12).Select(i =>
            $"• {i.Name} ({i.Id}{(i.Version is null ? "" : " " + i.Version)}) – {(string.IsNullOrEmpty(i.License) ? Loc.T("LicenseUnknown") : i.License)}"))
                   + (items.Count > 12 ? "\n…" : "");
        var scope = IsExpert && MachineScope ? "\n\n" + Loc.T("InstallMachineScopeNote") : "";
        var answer = await Host.ConfirmAsync(Loc.F("InstallConfirmTitle", items.Count), Loc.F("InstallConfirmText", list) + scope, Loc.T("BtnInstall"),
            danger: false, confirmIcon: "IconDownload");
        if (!answer.Confirmed)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        var ok = 0;
        var log = new StringBuilder();
        try
        {
            foreach (var item in items)
            {
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                SetState(item.Row, Loc.T("StateInstalling"));
                Status = Loc.F("StatusInstalling", item.Name);
                var (success, output) = item.Source == "chocolatey"
                    ? await ChocolateyClient.InstallAsync(item.Id, item.Version, CancellationToken.None)
                    : await WinGetClient.InstallAsync(item.Id, new InstallOptions(item.Version, IsExpert && MachineScope, item.Source),
                        Ui.WingetAgreementsAccepted, CancellationToken.None);
                SetState(item.Row, Loc.T(success ? "StateInstalled" : "StateInstallFailed"));
                log.AppendLine($"=== {item.Id} ===").AppendLine(output.Trim()).AppendLine();
                if (success)
                {
                    ok++;
                }
            }

            Output = log.ToString().Trim();
            Main.ShowToast(Loc.F("ToastInstallDone", ok, items.Count), isError: ok < items.Count);
            if (OperatingSystem.IsWindows())
            {
                AppLauncher.Refresh();
                ProgramCache.Invalidate();
            }

            _installedLoaded = false;
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

    private static void SetState(ObservableObject row, string state)
    {
        switch (row)
        {
            case CatalogRowViewModel c:
                c.State = state;
                break;
            case BundleRowViewModel b:
                b.State = state;
                break;
        }
    }
}
