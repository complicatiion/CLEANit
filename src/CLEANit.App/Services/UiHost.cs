using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using CLEANit.App.ViewModels;
using CLEANit.App.Views;

namespace CLEANit.App.Services;

public readonly record struct ConfirmResult(bool Confirmed, bool OptionChecked);

/// <summary>Windows, dialogs, clipboard and pickers for view models.</summary>
public sealed class UiHost
{
    public Window? MainWindow { get; set; }

    public Action? ShowMainWindow { get; set; }

    public Action? ExitApplication { get; set; }

    private static ResourceInclude? _shadow;

    /// <summary>
    /// system, dark, light or shadow. Shadow runs on the Dark variant (all Fluent templates stay dark) with CLEANit's
    /// lighter graphite tokens merged on top – merged dictionaries added last win.
    /// </summary>
    public void ApplyTheme(string theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = theme switch
        {
            "dark" or "shadow" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };

        var merged = app.Resources.MergedDictionaries;
        if (theme == "shadow")
        {
            _shadow ??= new ResourceInclude(new Uri("avares://CLEANit/")) { Source = new Uri("avares://CLEANit/Theme/Shadow.axaml") };
            if (!merged.Contains(_shadow))
            {
                merged.Add(_shadow);
            }
        }
        else if (_shadow is not null)
        {
            merged.Remove(_shadow);
        }

        WindowChrome.RefreshAll();
    }

    public async Task CopyAsync(string? text)
    {
        if (!string.IsNullOrEmpty(text) && MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> PickFolderAsync(string title, string? startPath)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
        {
            start = await window.StorageProvider.TryGetFolderFromPathAsync(startPath);
        }

        var result = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }]
        });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Settings › Reset CLEANit: starts a new CLEANit process with <c>--reset=&lt;parts&gt;</c> (it waits until this one has
    /// ended, deletes the data and opens normally) and exits this one without saving its settings.
    /// </summary>
    public bool RestartForReset(CLEANit.Core.Storage.ResetParts parts)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        UiSettings.Frozen = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, CLEANit.Core.Storage.AppDataReset.ToArgument(parts))
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Exception)
        {
            UiSettings.Frozen = false;
            throw;
        }

        ExitApplication?.Invoke();
        return true;
    }

    public async Task<ConfirmResult> ConfirmAsync(string title, string message, string confirmText, bool danger, string? optionText = null,
        bool optionDefault = false, string? confirmIcon = null)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return default;
        }

        var dialog = new ConfirmWindow(title, message, confirmText, danger, optionText, optionDefault, confirmIcon);
        var confirmed = await dialog.ShowDialog<bool>(window);
        return new ConfirmResult(confirmed, confirmed && dialog.OptionChecked);
    }

    /// <summary>Summary of a cleaning run; nothing is deleted without this confirmation.</summary>
    public async Task<CleanConfirmResult> ConfirmCleanAsync(CleanConfirmModel model)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return default;
        }

        var dialog = new CleanConfirmWindow { DataContext = model };
        var confirmed = await dialog.ShowDialog<bool>(window);
        return new CleanConfirmResult(confirmed, confirmed && model.CreateRestorePoint);
    }

    /// <summary>List with check boxes (e.g. uninstall leftovers); returns the checked items or null when cancelled.</summary>
    public async Task<IReadOnlyList<ChecklistItem>?> ChecklistAsync(string title, string message, IReadOnlyList<ChecklistItem> items,
        string confirmText, string confirmIcon, bool danger)
    {
        var window = EnsureWindow();
        if (window is null || items.Count == 0)
        {
            return null;
        }

        var dialog = new ChecklistWindow(title, message, items, confirmText, confirmIcon, danger);
        return await dialog.ShowDialog<bool>(window) ? items.Where(i => i.IsChecked).ToList() : null;
    }

    /// <summary>First start: expert features yes/no and start in the system tray at sign-in; null = no window available.</summary>
    public async Task<FirstRunResult?> FirstRunAsync()
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var dialog = new FirstRunWindow();
        var expert = await dialog.ShowDialog<bool>(window);
        return new FirstRunResult(expert, dialog.TrayAtSignIn);
    }

    /// <summary>"Add startup entry" dialog; null = cancelled. All users only in expert mode.</summary>
    public async Task<StartupAddRequest?> AddStartupAsync(bool allowAllUsers)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var dialog = new AddStartupWindow(allowAllUsers);
        return await dialog.ShowDialog<bool>(window) ? dialog.Result : null;
    }

    /// <summary>Asks for one value; null = cancelled.</summary>
    public async Task<string?> PromptAsync(string title, string message, string confirmText, string watermark, string? initial = null)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var dialog = new ConfirmWindow(title, message, confirmText, watermark, initial);
        return await dialog.ShowDialog<bool>(window) ? dialog.Input : null;
    }

    /// <summary>Save dialog; writes the text (UTF-8) and returns the path, null = cancelled.</summary>
    public async Task<string?> SaveTextAsync(string title, string suggestedName, string extension, string typeName, string content)
    {
        var path = await PickSavePathAsync(title, suggestedName, extension, typeName);
        if (path is null)
        {
            return null;
        }

        await File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(false));
        return path;
    }

    public async Task<string?> PickSavePathAsync(string title, string suggestedName, string extension, string typeName)
    {
        var window = EnsureWindow();
        if (window is null)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*." + extension] }]
        });

        return file?.TryGetLocalPath();
    }

    public async Task ShowTextAsync(string title, string text)
    {
        var window = EnsureWindow();
        if (window is not null)
        {
            await new TextWindow(title, text).ShowDialog(window);
        }
    }

    private Window? EnsureWindow()
    {
        if (MainWindow is { IsVisible: false })
        {
            ShowMainWindow?.Invoke();
        }

        // An open dialog owns nested dialogs and pickers.
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dialog = desktop.Windows.LastOrDefault(w => w.IsVisible && w.IsActive && w != MainWindow);
            if (dialog is not null)
            {
                return dialog;
            }
        }

        return MainWindow;
    }
}
