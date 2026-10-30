# Changelog

## [0.0.7] – 2026-10-30

### Added

- **Updates**: eye button right of the inventory buttons shows / hides ignored packages (tooltip with the count); shown packages are dimmed and can be un-ignored.
- **Chocolatey**: *Install* next to the Chocolatey switch (settings) when it is missing – installs the official winget package `Chocolatey.Chocolatey` after a confirmation with source, publisher, license and terms; nothing is shipped with CLEANit, the source stays off until switched on.
- **Language** on the first start (and after a reset): German when Windows uses German as display language, region (DE/AT/LI) or keyboard layout, otherwise English. The installer still preselects English.
- **Folder comparison**: select all / none in the toolbar and as header check box; rows tinted by state (only left: accent, only right: blue, different: amber) with a coloured stripe.
- Sidebar: divider line above every group (Cleanup, Tools, Expert, Server), hidden together with its group.

### Changed

- Shorter names: **Programs**, **Software**, **Updates**, **Startup**, **Compare** (tooltip *Compare & duplicates*).
- Expert order: Services, Rule editor, Large files, Reports, Command line.
- **Windows settings**: coloured filter pills (Recommended pink, Safe green, Moderate amber, Microsoft policy blue, Applied green, Pending amber); the reset button sits left of them.
- Cleanup pages: filter symbol before the risk chips.
- **Drive health**: data source and chevron in the accent colour; wider SMART columns (current, worst, threshold, raw value).
- **Compare**: *Compare content* switch right of the state pills; wider *Actions* column; check boxes no longer clipped.
- Cleanup pages: details panel 480 px by default (also for existing settings that still had the old default).
- *Closing the window keeps CLEANit in the system tray* is off by default for new installations (existing settings are kept).
- **About**: app icon (white logo on the gradient tile) instead of the plain logo.
- **Server › Environment**: four cards per row over the full width (two / one in narrow windows).
- **Software**: select all / none right of the search (Installed); Bundles: *New*, *Import* and *Check* as grey buttons with text, *Export* highlighted; *Install new* stays in the action bar.

## [0.0.6] – 2026-10-29 · release candidate

### Added

- **Server admin mode**: separate switch in Settings → General below expert mode (off by default). The *Server* pages are no longer part of expert mode, and expert mode does not switch them on.
- **Reset CLEANit** card in the settings (below *Data & reports*): switches for settings (incl. the sign-in task), reports and statistics, caches, backups, own rules and bundles, or a complete reset like a new installation. Red button with confirmation; CLEANit restarts (`--reset=<parts>`), waits for the old instance, deletes only its own data and opens fresh.
- **Disk usage**: heart icon on every drive tile opens *Drive health*; right click on the treemap (or its view button) → *Export treemap as image …* saves a JPEG.
- **Device tree**: *Clear* button left of *Choose folder* (enabled when a folder is set) empties the chosen folder and the tree.
- **User profiles**: header check box and toolbar buttons to select all / none of the deletable profiles; a message when *Select unused* finds nothing.

### Changed

- **Drive health** is a normal tool (below *Disk usage*); **Reports** moved to the expert group. Sidebar order: Programs (installed), Software install, Software updates, Startup apps, Windows settings, Disk usage, Drive health, Device tree, Compare & duplicates · Expert: Services, Large files, Reports, Rule editor, Command line. Drive health refresh setting visible without expert mode.
- **Software install**: tab order *Search & install*, *Installed*, *Bundles*.
- **User profiles**: profiles that cannot be selected show a lock with the reason instead of a disabled check box; Windows' own folders (UMFD-n / DWM-n accounts of the font driver host and the window manager, incl. their TEMP copies, defaultuser…) are no longer listed.
- **Environment**: installed / active values in the accent colour, missing ones grey.
- **Services**: the *Unsigned* badge sits at the right edge of the publisher column (same position in every row).
- **Windows settings**: the documentation link has the same position in every card.
- **Device tree**: edit-mode icon and label in the accent colour.
- **Duplicates**: "Note:" before the hint about dropping more folders.
- **Appearance**: themes Dark (default), Shadow, Light, System – "(graphite)" removed, "Like Windows" renamed to *System*.
- **About**: logo without the glow.
- First-start text lists the expert features without drive health.

### Fixed

- User profiles: the select-none button looked like an empty check box and select-all was missing; check boxes of locked profiles looked active but could not be ticked.

## [0.0.5] – 2026-10-29

### Added

- **Server** area (expert mode) with three pages:
  - **Environment** (read only): Windows version, multi-user/multi-session, domain, RD Session Host role, active and disconnected sessions with users, FSLogix (version, profile containers, configuration values), Citrix VDA version, Citrix Profile Management (service and policy values), MCS/PVS provisioning, local profiles, folders without profile and temporary profiles.
  - **User profiles**: all local profiles with type (local, roaming, temporary, mandatory, Citrix UPM, folder without profile), state (signed in / not), last use, age and size; *Select unused* (older than *n* days) and **delete through the Windows function DeleteProfile** (folder and ProfileList entry). Profiles in use and the own profile are locked; folders without a profile entry are only listed. Report saved.
  - **Server cleanup**: all cleanup rules for the profiles of **signed-out users** (signed-in users optionally), with its own selection that starts from the new profile *Terminal server (RDS / Citrix VDA)*.
- Command line: `--signed-out` cleans only the profiles of signed-out users (e.g. as a scheduled task on a terminal server).
- **Services**: start and stop (running dependent services are listed in the confirmation; Windows core services and Remote Desktop are protected), state column, *Actions* column.
- **Startup apps**: *Add entry* – program, script or shortcut for the current user (Run key or startup folder), for all users in expert mode.
- **Windows settings**: filter pills (Recommended, Safe, Moderate, Policy, Windows Settings switch, Active, Pending change).
- **Device tree**: edit mode (always off on start) – tick folders and files or use the context menu to delete them (safety guard, recycle bin by default, report); sizes in the tree are corrected without a new scan.

### Changed

- Clean confirmation: header centred, sentence about permanent deletion removed, restore point as a switch (off unless set in the settings) in the button row; the Deep acknowledgement check box is gone – with Deep items the button turns red and reads *Clean now (cannot be undone)*.
- All option check boxes in confirmations (restore point before uninstalling, replace files …) are switches.
- *Start program* uses a rocket icon (play is used for starting services).
- Drive health: the whole card opens the details.
- Sidebar: counters sit on the icon also when the sidebar is expanded (99+ at most); renamed *Programs (installed)*, *Startup apps*, *Software updates*, *Software install*, *Compare & duplicates*; order: programs, startup apps, software updates, software install, Windows settings, disk usage, device tree, compare & duplicates, reports. *Large files* moved to the expert group; *Drive health* is the first expert page.
- Compare & duplicates: *Compare folders* is the first tab; the hint is on two lines; *Compare content* is a switch.
- Device tree: sort, direction, drive, path and buttons in one row; "read only" removed from the hint.
- About: logo like in the sidebar (tinted, with glow); *Show license* and *Project page* in the neutral grey.
- Settings: sublines under start in the system tray, closing to the system tray and the analysis reminder removed. Rule editor: note starts with "Note:".
- Reports: *Clear all reports* in the neutral grey.
- Services: the expert note card was removed.
- Rule detection looks at all profiles on servers and multi-session PCs in expert mode (only which rules are listed; what is cleaned is unchanged).

## [0.0.4] – 2026-10-29

### Added

- **Duplicates & compare**: the duplicates page has a second tab **Compare folders** (WinMerge-like): left and right folder (choose or drop them), compare by size and change time or by content (byte by byte), filters for different / only left / only right / identical with counts, name filter, which side is newer, a folder that exists on one side only is one entry with its size and file count. **Copy to right / left** (existing files are only replaced when the confirmation option is ticked; times are kept; target folders pass the safety guard) and **delete left / right** (recycle bin by default, report saved).
- **Drive health** extended: native, read-only storage queries without PowerShell – model, serial number, firmware, interface, SSD/HDD, volumes per drive, temperature with the drive's own limits, **NVMe health log** (life left, spare blocks, data read/written, power cycles, unsafe shutdowns, media errors, critical warnings) and **SATA SMART attributes** (table with current/worst/threshold/raw). Assessment *Good / Caution / Bad* with findings (reallocated, pending and uncorrectable sectors, spare, wear, thresholds). Up to four cards per row, details panel per drive. Windows' own view (health status, latencies, rotation speed) is still merged in.
- **Auto refresh** for drive health (Settings › Drive health: off / 10 s / 30 s / 1 min / 5 min, off by default; only while the page is visible, native queries only).
- **Shadow** theme (lighter graphite between dark and light); dark stays the default.
- Windows settings: new documented switches – no diagnostic logs, only small crash dumps, no device name in diagnostic data, no additional error report data, no inking and typing data, websites cannot read the language list, Windows spotlight off (policy, Enterprise/Education), lock screen without tips (Home/Pro), search on the taskbar as icon only, hide the touch keyboard icon. The page is split into *Recommended* and *Optional*.
- Reports: **Clear all reports** (with confirmation). The totals on the overview are now kept in `data\stats.json` and survive deleting reports.
- First start: option *Start CLEANit in the system tray at sign-in* (off by default).
- Tray menu: the CLEANit logo in front of the name and version.
- Installer: CLEANit logo in the wizard (all DPI sizes), language dialog always shown with English preselected and German as second choice; build notes in `build\installer\cleanit.iss`.

### Changed

- The window starts maximized on the first start and then remembers maximized/normal state and size.
- Logo with a soft brand glow in the sidebar, on the About page and on the first-start window.
- First-start window: everything centred, larger padding.
- Smooth animations: page changes fade and slide in (also for pages opened for the first time), categories and device-tree folders slide in when expanded and fade out when collapsed, the chevron turns.
- Action buttons in lists show only their glyph – no backdrop, also when disabled; colours: update accent, restore green, delete red, everything else neutral (start program, ignore, open folder).
- Overview: *Total space freed* card without logo and subline.
- Software catalog renamed **Software**; tab order *Installed* (loads on opening), *Search & install*, *Bundles*; search bar left-aligned, shorter placeholder, hint text removed.
- Windows settings: explanation card removed, Windows edition/version/build in a pill next to Refresh, *Recommended* in the accent colour, search switch renamed to "No web and Bing results in Windows search".
- Services: *Show Windows services* is a switch (off by default).
- Settings: "Tray" is now "System tray"; sublines under backups, automatic backup cleanup, Chocolatey, update check, winget agreements and background check removed (the last check time and the Chocolatey hint moved into tooltips).
- Sidebar: **FULL CLEANUP** in capitals.
- About: shorter description.
- Duplicates: folder card rebuilt (drop folders, open, remove, options and Scan in one card).

### Fixed

- Icon buttons in lists got a grey backdrop when disabled and the update button a filled accent backdrop (Fluent's `accent` class).
- New and re-created pages appeared without the page transition.

## [0.0.3] – 2026-10-29

### Added

- **Software catalog** (new page) with three tabs:
  - *Search & install*: search winget (and Chocolatey, if enabled), details per package (version, source, publisher, license, homepage, installer type), multi-select, install after a confirmation that lists every package with its license, or add to a bundle.
  - *Installed*: all installed programs, marked whether winget knows them; filter, select the known ones and create a bundle from them.
  - *Bundles*: create, edit, delete, import and export package bundles in the **native winget export format** (`winget import` works with them); *Check* compares a bundle with the installed programs (installed / new), *Install new* installs only the missing ones after confirmation. Stored in `data\bundles`.
  - Expert mode: fixed version per package and *machine scope* (all users).
  - Hint when winget is missing (Windows Server 2016–2022, LTSC).
- **Chocolatey** as an optional additional source (Settings, off by default; only used if it is already installed – CLEANit never installs it).
- **Device tree** (new page): folder tree with sizes, file and folder counts, last change and the largest files per folder; sort by size, name, files or date; open in Explorer, copy path, hand over to *Disk usage*. Read only; a cancelled scan keeps the partial tree.
- **Windows settings** (new page): 17 documented switches (Microsoft policies or the value the Windows Settings switch writes) for appearance, privacy, search, taskbar, Edge and system; risk levels, *Recommended*, select all/none, pending-change counter, discard. Every change is backed up first (`backups\tweaks`), switching off restores the previous value; values that exist with another type are skipped; enterprise editions get the matching policy value; domain members see a hint that group policies may override. Moderate switches only in expert mode.
- **Backups** in Settings: all of CLEANit's backups (startup, services, Windows settings, Firefox history, event logs) with restore, open and delete; optional automatic cleanup of backups older than *n* days (off by default, enabling asks once).
- **First start**: one question whether to enable expert mode (can be changed later in Settings).
- **Updates**: *Start program* button per row (found via the Start menu shortcut, started with normal user rights).
- **Command line**: `--export-bundle <file>`, `--install-bundle <file> [--confirm] [--machine] [--accept-source-agreements]` – without `--confirm` only the preview is shown.
- Reorderable, resizable and sortable columns (Avalonia DataGrid) in Programs, Startup, Updates, Reports, Large files and the Software catalog.

### Changed

- Button colours: confirm actions in dialogs (Clean, Install, Apply) in the accent colour, Delete red, Cancel in dialogs grey; Cancel of a running operation on a page stays red. Refresh, Analyze and Scan in the accent colour. Trash icon buttons: red glyph without backdrop.
- Sidebar: *Software updates* is now *Updates*; scrollable; when collapsed, the count badge sits on the icon and icons keep their column.
- Cleanup pages: one *Expand / collapse all* button; the rule list is virtualized.
- Clean confirmation: the Deep warning scrolls instead of overlapping on small windows; the window never exceeds the screen; restore-point note removed.
- Overview: hero card and tool buttons removed; large *Freed so far* card next to the six statistics.
- Settings: appearance *Dark* by default on first start; expert-mode subline and restore-point limit text removed; interval list as wide as *Revoke*; exclusions can be discarded.
- Programs: actions right-aligned in fixed slots (modify, repair, more, uninstall). Duplicates: folder list top left. Disk usage and Drive health: wider tiles aligned with the toolbar.
- About: “commercial use” removed from the license summary.
- Memory: at most five pages are kept in memory, released pages unsubscribe from their data, workstation GC with *ConserveMemory*, memory is compacted after reloading rules and when hiding to the tray.

### Fixed

- `OpenFolder` created missing folders when only opening them.

## [0.0.2] – 2026-10-29

### Added

- **Full cleanup** page: system, browser and development rules in one analysis, grouped by area and category.
- **Selection profiles** on every cleanup page: *Recommended*, *Daily*, *Developer workstation* and (expert) *Prepare master image (MCS/PVS)*; save the current selection as an own profile, delete own profiles. The selection is shared between the full cleanup and the area pages.
- **Large files**: largest files below a folder (default: Downloads) with minimum size and “unchanged for n days”; delete selected after confirmation (recycle bin by default).
- **Duplicates**: size → hash of the first and last 64 KB → full SHA-256; hashes cached in SQLite (`data\cache\hashes.db`); *Keep oldest/newest*; at least one copy of every group always stays.
- **Reports**: all cleaning reports (index in SQLite, `data\reports.db`), details per rule with errors, export as CSV or HTML, delete, open folder.
- **Disk usage**: treemap (squarified, click a folder to open it) and file-type breakdown next to the list; cancelling keeps the partial result.
- **Expert mode** (off by default) – shows administrator features: *all user profiles*, expert rules, the master-image profile and the expert pages:
  - **Services**: automatic third-party services (Windows services on request) → *Manual* with backup and restore; nothing is stopped or deleted.
  - **Drive health**: health, temperature, wear, power-on hours, read/write errors (`Get-PhysicalDisk`, `Get-StorageReliabilityCounter`), read only.
  - **Rule editor**: create own file rules with live preview through the real engine, including the folders the safety guard refuses.
  - **Command line** help with copyable examples.
- **Command line**: `--analyze`, `--clean --confirm`, `--profile`, `--area`, `--all-profiles`, `--report <file.json>`, `--list-profiles`, `--help`; exit codes 0/1/2. `--clean` without `--confirm` only analyzes.
- New rules: Firefox history via SQLite (Deep; bookmarks stay, `places.sqlite` is backed up first), event logs (expert; each log is backed up as `.evtx` before it is cleared), new **Privacy** category (recent items, jump lists, Office recent documents, clipboard history). *DISM reset base* is now an expert rule.
- **DISM pre-analysis**: *Analyze component store* (`/AnalyzeComponentStore`, read only) in the details of the DISM rules.
- **Software updates**: ignore list per package, *Update all* (secondary button), select all/none, export/import the package list with `winget export/import` (expert).
- **Programs**: list cached as JSON with timestamp (shown instantly, refreshed after a day or on demand), sortable columns, CSV export.
- **Overview**: statistics with count-up animation (space freed, cleaning runs, files removed, built-in and community rules, installed programs, available updates) and a quick analysis for the full cleanup.
- **Analysis reminder** (opt-in): analyzes in the background every *n* days and shows the result in the tray – never cleans.
- **CLEANit update check** (opt-in): asks GitHub once a day for the latest release and links to the release page; nothing is downloaded.

### Changed

- Smooth transitions for page switches, window opening, sidebar, categories and the winget output.
- Cancel buttons are solid red; uninstall and delete icon buttons are red; count pills in the accent colour.
- Intro texts under the page titles removed; *Select all / none* as icon buttons; the details panel of the cleanup pages is resizable (width remembered) and scrolls horizontally for long paths.
- Disk usage: icon-only analyze buttons, equal drive tiles and buttons.
- Settings: data folder in a pill with right-aligned buttons, own Winapp2.ini in its own block, *all user profiles* only in expert mode.
- About: German translation of the component list, sksdesign.de button, SQLite listed (part of Windows, not shipped).
- Reports: up to 500 are kept (was 50); the overview reads the SQLite index instead of `history.json`.

### Fixed

- Cancelling the disk usage analysis no longer raises `OperationCanceledException` from `SafeDelete.Measure` (Visual Studio broke on it as “user-unhandled”). All engines now stop cooperatively and return partial results.

## [0.0.1] – 2026-10-29

First version.

### Added

- **Cleaning engine** (`CLEANit.Core`): rules as JSON (built-in, embedded) and own rule files in `%ProgramData%\CLEANit\rules`; tokens per user profile (`%LocalAppData%` …), `%ProgramFiles%` for both views, wildcards in folder names; fast enumeration with `FileSystemEnumerable` (links are never followed, hidden/system cache files included).
- **Analyze → preview → confirm**: analysis per rule (size, files, largest files, folders, refused folders, running apps), summary dialog before any deletion, Deep acknowledgement, cleaning report as JSON (`logs\clean-*.json`, last 50) and history.
- **Safety guard** independent of the rules: system folders (only allow-listed paths below `%WinDir%`), user libraries, OneDrive, credential stores, `.ssh`, browser password/autofill/bookmark files, Outlook data files, network paths and user exclusions are refused; files changed after the analysis are not deleted; locked files are skipped; services are stopped and restored.
- **Risk levels** Safe / Moderate / Deep; only Safe rules can be preselected.
- **Built-in rules**: 27 system, 34 browser and 19 development rules (EN/DE texts).
- **Actions through Windows tools**: empty recycle bin (shell API), DISM `/StartComponentCleanup` and `/ResetBase`, removal of `Windows.old` (ownership via icacls, link-safe deletion).
- **Winapp2.ini** (Non-CCleaner variant 260915, CC BY-SA 4.0) shipped unmodified and converted at runtime: registry keys ignored, detection keys only read, password/autofill/bookmark entries dropped, protected file masks removed, natively covered browsers skipped, community rules Moderate (warnings/cookies/history/sessions Deep) and never preselected.
- **Workspace scanner** for build output of .NET, Visual Studio, Unity, Gradle/Android, Node.js, Rust, Maven and Python projects with idle filter; deletion to the recycle bin (option).
- **Programs**: desktop programs (Uninstall keys, read only) and Store apps; uninstall/modify/repair with the program's own routine (MSI via msiexec); leftover folder search (exact name matches, shared folders excluded); optional restore point.
- **Startup**: Run keys (user, machine, 32-bit), startup folders, scheduled tasks with logon/boot trigger; enable/disable via `StartupApproved` like the Task Manager; delete with backup (JSON + shortcut copy / task XML); restore.
- **Software updates** via winget: list (localized table parsed by column positions, truncated IDs resolved via `winget export`), update selected after confirmation, source agreements accepted once in the app, optional background check (off by default).
- **Disk usage**: drives and folder sizes with drill-down (read only).
- **User interface** (Avalonia 11.3): grouped, collapsible sidebar (Overview · Cleanup · Tools · Settings/About), dark and light theme “Spectrum” (WCAG AA contrasts), accent colour for check marks and selections, icons on every button, main action right, English (default) and German.
- **System tray** with right-click menu; start at sign-in via a scheduled task with highest privileges (`--register-autostart`, `--unregister-autostart`, `--unregister-autostart-all`).
- **Installer** (Inno Setup 6, English default, German optional) and portable ZIP, GitHub Actions build.
- Tests for rules, Winapp2 conversion, safety guard, winget parsing and command line splitting.


