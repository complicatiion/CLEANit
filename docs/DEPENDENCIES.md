# Dependencies

| Component | Version | License | Use |
| --- | --- | --- | --- |
| .NET | 10.0 (SDK per `global.json`) | MIT | runtime, self-contained |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 11.3.14 | MIT | user interface |
| Avalonia.Controls.DataGrid | 11.3.13 | MIT | reorderable, sortable tables (package deprecated upstream, still maintained with bug fixes for 11.x) |
| Avalonia.Diagnostics | 11.3.14 | MIT | Debug builds only |
| SkiaSharp (transitive through Avalonia, no own reference) | as shipped with Avalonia | MIT (+ Skia BSD-3-Clause) | rendering; also used directly to encode the treemap export as JPEG |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | MVVM source generators |
| Winapp2.ini (Non-CCleaner) | 260915 | CC BY-SA 4.0 | community rules, shipped unmodified |
| Material Design Icons (`@mdi/js`) | 7.4.47 | Apache-2.0 | icon paths (copied into Icons.axaml) |
| xUnit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | 2.9.3 / 3.0.2 / 17.12.0 | Apache-2.0 / MIT | tests only |
| Inno Setup | 6.x | Inno Setup License | installer build only |

Central versions: `Directory.Packages.props`.

## Windows components used at runtime (part of Windows, not shipped)

`sc.exe` (stop/start services for a rule, start type of services), `Dism.exe` (component store, analysis), `icacls.exe` (Windows.old ownership), `schtasks.exe` (sign-in task, startup tasks), reg values via the .NET registry API (Windows settings page, DWORD values only), `powershell.exe` (Store apps, scheduled task list/export, restore point, drive health, clipboard history, `Stop-Service` / `Start-Service` on the services page, `Get-WindowsFeature RDS-RD-Server` on the server environment page), `wevtutil.exe` (event log backup and clearing), `msiexec.exe` (MSI uninstall/modify/repair), `winget.exe` (updates, software catalog, bundles, optional one-click install of the official `Chocolatey.Chocolatey` package – if installed), `choco.exe` (optional catalog source, only if installed and enabled), `explorer.exe` (start programs with normal user rights), `kernel32.dll` `DeviceIoControl` (read-only storage IOCTLs for drive health: device descriptor, temperature, NVMe health log, SMART data, volume extents), `userenv.dll` `DeleteProfileW` (delete user profiles, expert server page), `user32.dll` `GetKeyboardLayoutList` (first-start language), `wtsapi32.dll` `WTSEnumerateSessions` / `WTSQuerySessionInformation` (list user sessions, read only), `winsqlite3.dll` (SQLite: report index, hash cache, Firefox history).

No NuGet package was added for SQLite: `Microsoft.Data.Sqlite` would ship its own native library; the Windows copy is enough for the small, local databases.
