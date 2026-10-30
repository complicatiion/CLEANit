using System.Text;
using CLEANit.Core.Diagnostics;

namespace CLEANit.Platform.Windows;

public enum RestorePointResult
{
    Created,

    /// <summary>Windows creates at most one restore point per 24 hours by default.</summary>
    SkippedRecent,

    /// <summary>System protection is off for the system drive.</summary>
    Disabled,

    Failed
}

/// <summary>System restore point (clients only) via Checkpoint-Computer.</summary>
public static class RestorePoint
{
    public static bool IsSupported => !SystemInfo.IsServer();

    public static async Task<(RestorePointResult Result, string Output)> CreateAsync(string description, bool uninstall, CancellationToken ct)
    {
        if (!IsSupported)
        {
            return (RestorePointResult.Disabled, "Windows Server has no System Restore.");
        }

        var type = uninstall ? "APPLICATION_UNINSTALL" : "MODIFY_SETTINGS";
        var command = $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' -RestorePointType {type} -WarningVariable w -ErrorAction Stop; " +
                      "if ($w) { Write-Output ('WARN: ' + $w) } else { Write-Output 'OK' }";
        var run = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command],
            TimeSpan.FromMinutes(10), Encoding.UTF8, ct);

        var output = run.Output.Trim();
        if (run.Success && output.EndsWith("OK", StringComparison.Ordinal))
        {
            return (RestorePointResult.Created, output);
        }

        // Warning about the 1440-minute frequency (text is localized; the number is not).
        if (output.Contains("WARN:", StringComparison.Ordinal) && output.Contains("1440", StringComparison.Ordinal))
        {
            return (RestorePointResult.SkippedRecent, output);
        }

        return output.Contains("0x80070422", StringComparison.OrdinalIgnoreCase) || output.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            ? (RestorePointResult.Disabled, output)
            : (RestorePointResult.Failed, output);
    }
}
