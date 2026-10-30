using CommunityToolkit.Mvvm.Input;
using CLEANit.App.Localization;
using CLEANit.Core.Rules;

namespace CLEANit.App.ViewModels;

public sealed record CommandExample(string Description, string Command);

/// <summary>Expert: how to run CLEANit from scripts and scheduled tasks.</summary>
public sealed partial class CommandLineViewModel(MainViewModel main) : PageViewModel(main)
{
    public override string TitleKey => "NavCommandLine";

    public string Usage => Cli.CliRunner.UsageText;

    public IReadOnlyList<string> ProfileIds =>
        SelectionProfiles.All(Ui.CustomProfiles, expert: true).Select(p => $"{p.Id,-28} {p.DisplayName(Loc.Language)}").ToList();

    public IReadOnlyList<CommandExample> Examples =>
    [
        new(Loc.T("CliExampleAnalyze"), "CLEANit.exe --analyze --profile daily --report C:\\Reports\\cleanit.json"),
        new(Loc.T("CliExampleClean"), "CLEANit.exe --clean --profile recommended --confirm"),
        new(Loc.T("CliExampleImaging"), "CLEANit.exe --clean --profile imaging --all-profiles --confirm --report C:\\Reports\\image.json"),
        new(Loc.T("CliExampleList"), "CLEANit.exe --list-profiles"),
        new(Loc.T("CliExampleExportBundle"), "CLEANit.exe --export-bundle C:\\Setup\\workstation.json"),
        new(Loc.T("CliExampleInstallBundle"), "CLEANit.exe --install-bundle C:\\Setup\\workstation.json --machine --confirm")
    ];

    public override void OnActivated()
    {
        OnPropertyChanged(nameof(ProfileIds));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(ProfileIds));
        OnPropertyChanged(nameof(Examples));
    }

    [RelayCommand]
    private async Task CopyAsync(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            await Host.CopyAsync(text);
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }
}
