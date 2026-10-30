using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CLEANit.Platform.Windows;

/// <summary>
/// Finds the Start menu shortcut of an installed program and starts it through Explorer,
/// so the program runs with normal user rights and not with CLEANit's administrator rights.
/// </summary>
public static partial class AppLauncher
{
    private static List<string>? _shortcuts;

    [GeneratedRegex(@"[\s\-_.()\[\]]+|\b(x64|x86|64-bit|32-bit|v?\d+(\.\d+)*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    /// <summary>Shortcut whose name matches the program name (first match wins); null when none.</summary>
    public static string? FindShortcut(string programName)
    {
        var wanted = Normalize(programName);
        if (wanted.Length < 3)
        {
            return null;
        }

        var shortcuts = _shortcuts ??= ReadShortcuts();
        return shortcuts.FirstOrDefault(s => Normalize(Path.GetFileNameWithoutExtension(s)) == wanted)
               ?? shortcuts.FirstOrDefault(s =>
               {
                   var name = Normalize(Path.GetFileNameWithoutExtension(s));
                   return name.Length >= 4 && (name.StartsWith(wanted, StringComparison.Ordinal) || wanted.StartsWith(name, StringComparison.Ordinal));
               });
    }

    /// <summary>The shortcut list is read once; call after installing or uninstalling.</summary>
    public static void Refresh() => _shortcuts = null;

    public static bool Launch(string shortcut)
    {
        try
        {
            // Explorer opens the shortcut in the user's (non-elevated) context.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{shortcut}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static List<string> ReadShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };
        var list = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                list.AddRange(Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                    .Where(f => !Path.GetFileName(f).Contains("uninstall", StringComparison.OrdinalIgnoreCase)
                                && !Path.GetFileName(f).Contains("deinstall", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception)
            {
                // Folder not readable.
            }
        }

        return list;
    }

    internal static string Normalize(string name) => Noise().Replace(name.ToLowerInvariant(), "");
}
