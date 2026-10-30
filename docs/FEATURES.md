# Features

## Cleanup

| Page | Content |
| --- | --- |
| **Full cleanup** | All system, browser and development rules in one analysis, grouped as *Area · Category* (collapsed by default). |
| **System** | Windows (temp folders, thumbnail cache, WER reports, crash dumps, memory dumps, logs, Delivery Optimization, Windows Update download cache, WinINet cache, Check Disk fragments, recycle bin, DISM component cleanup, Windows.old), Privacy (recent items, jump lists, Office recent documents, clipboard history), Multimedia & GPU (DirectX, NVIDIA, AMD, Intel shader caches, Spotify, Adobe media cache), Applications (Teams, Discord, Slack, Steam, OneDrive logs, Java) and community rules. Expert: DISM reset base, event logs. |
| **Browsers** | Per browser: cache (Safe, preselected), offline website data (Moderate), cookies, history, last session (Deep). Edge, Chrome, Brave, Vivaldi, Chromium, Opera, Opera GX, Firefox (history via SQLite: visits and unbookmarked pages, bookmarks stay, `places.sqlite` is backed up first). |
| **Development** | IDEs & editors (Visual Studio component and symbol cache, VS Code, JetBrains, Android Studio), SDKs & package caches (NuGet, Gradle, Android SDK, AVD snapshots, npm, Yarn, pip, Go, Cargo, .NET CLI), Unity (package cache, GI cache, editor logs). Tab **Workspace**: project build output. |

Common to all cleanup pages:

- **Analyze** all rules of the page; per rule size, files, folders, refused folders and up to 60 largest files. Cancelling keeps the rules analyzed so far.
- **Selection profiles**: *Recommended* (Safe defaults), *Daily*, *Developer workstation*, *Prepare master image (MCS/PVS)* (expert) and own profiles (save current selection, delete). Select all / none (visible rules), expand/collapse all, filter chips per risk and for community rules (with a filter symbol), search.
- The selection is remembered per rule and shared between the full cleanup and the area pages.
- Details panel: resizable (width remembered), scrolls horizontally for long paths; DISM rules offer *Analyze component store* (read only).
- **Clean …** → summary dialog (rules, risk, files, size, notes like “skipped while running”, recycle bin) → Deep warnings (red *cannot be undone* button) → optional restore point (switch) → cleaning with progress → result and fresh analysis of the cleaned rules.
- **Cancel** stops analysis or cleaning between files.

## Tools

| Page | Content |
| --- | --- |
| **Programs** | Cached list (JSON with timestamp) shown instantly, refreshed after a day or with *Refresh*; filter all / desktop / Store, search, movable, resizable and sortable columns, CSV export; uninstall (confirmation, optional restore point), modify, repair (MSI), open location, copy uninstall command, search leftover folders. |
| **Startup** | Switch per entry (reversible), location badge, publisher and signature check, missing target badge, open location, delete with backup, restore deleted. *Add entry*: program, script or shortcut via file picker, name and arguments (current user: `HKCU\…\Run` or the user's startup folder; expert: *for all users* via `HKLM\…\Run` or the common startup folder). |
| **Software** | Bundles: *New*, *Import*, *Check* (grey), *Export* (pink), *Install new* in the action bar. Installed: select all / none right of the search. Tabs *Search & install* (first; winget, optional Chocolatey; details with version, source, publisher, license, homepage; multi-select; install after confirmation or add to a bundle), *Installed* (loaded when the tab opens: all programs, marked whether winget knows them; create a bundle), *Bundles* (create, edit, delete, import/export in the native winget export format; *Check* shows installed vs new; *Install new* after confirmation). Hint when winget is missing (Server 2016–2022, LTSC). |
| **Updates** | Eye button (right of the inventory buttons) shows / hides ignored packages; shown ones are dimmed and can be un-ignored. winget availability and version, check for updates, select all/none, update one, selected or all (ignored and explicit-only packages excluded), ignore list, start the program (Start menu shortcut, normal user rights), state per package, animated winget output, compact legal note. Expert: export/import the package list (`winget export/import`). |
| **Compare** | Tab *Compare folders* first (see below). Tab *Duplicates*: folder card (add or drop folders, open, remove), minimum size; size → partial hash (first/last 64 KB) → SHA-256 (cached in SQLite); *Keep oldest / newest*; at least one copy per group always stays. *Compare folders*: left and right folder (choose or drop), by size and change time or by content (switch right of the filters, byte by byte); different / only left / only right / identical with counts and coloured filters; rows tinted by state (only left: accent, only right: blue, different: amber) with a stripe; select all / none (toolbar and header check box); newer side; folder-only entries with size and file count; copy to the right or left (replace existing files only when ticked in the confirmation), delete on either side (recycle bin by default), open in Explorer. |
| **Device tree** | Folder tree of a drive or folder with size, files, folders, last change and the largest files per folder; sort by size/name/files/date, ascending or descending; open in Explorer, copy path, open in *Disk usage*. Sort, direction, drive and path in one toolbar row; *Clear* (enabled when a folder is set) empties the chosen folder and the tree. Edit mode label and lock icon in the accent colour. **Edit mode** (switch, off by default, not remembered): check boxes per folder/file, delete checked or via context menu after confirmation (recycle bin by default, report *Device tree*); the tree is corrected without a new scan. Cancelling keeps the partial tree. |
| **Windows settings** | Documented switches only (Microsoft policies or the value the Windows Settings switch writes), split into *Recommended* and *Optional*: appearance, privacy and diagnostic data, lock screen and spotlight, search and taskbar (incl. search icon only, touch keyboard icon), Edge, system. Windows edition/version/build in a pill. Coloured filter pills next to *Refresh* (badge colours; reset button left of them): Safe / Moderate, Recommended, Policy / Settings, Applied / Pending (OR within a group, AND between groups). Risk level, effect (immediately / Explorer restart / sign-out / restart), documentation link (same position in every card – the values column shares its width), *Recommended*, all/none, discard, apply after confirmation. Every change is backed up; switching off restores the previous value. |
| **Disk usage** | Drive tiles (with a link to *Drive health*), list with share bar, **treemap** (click a folder to open it; right click on the treemap or its view button → *Export treemap as image …* as JPEG) and **file types**; drill down, up, show in Explorer, choose folder. Cancelling keeps the partial result. |
| **Drive health** | Up to four drive cards per row: assessment Good / Caution / Bad with findings, life left, temperature (with the drive's limits), power-on hours, power cycles, data written, errors, volumes. Data source and chevron in the accent colour; click a card for the details panel (SMART columns wide enough for their headers): model, serial, firmware, interface, NVMe health log (spare, read/written, unsafe shutdowns, media errors, critical warnings, temperature time) or SATA SMART table (current / worst / threshold / raw), Windows health status, latencies. Native IOCTL queries plus Windows' storage cmdlets; optional auto refresh. Read only. |

## Expert mode (Settings → General, off by default)

| Item | Content |
| --- | --- |
| **All user profiles** | Clean the profiles of all local users (terminal servers). |
| **Expert rules** | Event logs (each log backed up as `.evtx` before clearing), DISM reset base. |
| **Master-image profile** | Temp, dumps, logs, update caches, recycle bin, DISM, event logs and browser caches before sealing an MCS/PVS image. |
| **Services** | Publisher with the *Unsigned* badge aligned at the right of the column. Automatic services (third-party by default, Windows services with the *Show Windows services* switch). *Actions* column: start / stop (after confirmation; running dependent services are listed; core Windows services are locked) and *Set to manual* (start type backed up, can be restored). Nothing is deleted. |
| **Rule editor** | Own file rules (stored as `editor-*.json` in the rules folder) with preview through the real engine, including the folders the safety guard refuses. Actions and services stay reserved for built-in rules. |
| **Large files** | Largest files below a folder (default: Downloads), minimum size, unchanged for *n* days; delete selected after confirmation (recycle bin by default). System, program and AppData folders are skipped. |
| **Reports** | All cleaning reports (SQLite index), details per rule incl. errors, export CSV/HTML, delete one or clear all (the overview totals stay), open folder. |
| **Command line** | Usage, profile ids and copyable examples. |
| **winget inventory** | Export/import on the *Updates* page. |
| **Software** | Fixed version per package, install for all users (machine scope). |
| **Windows settings** | Moderate switches (Delivery Optimization download mode, Windows Error Reporting). |

## Server (server admin mode)

Shown with the separate *Server admin mode* switch (Settings → General), independent of expert mode. Meant for Windows Server 2019 / 2022 / 2025, Remote Desktop Session Hosts, Windows multi-session and Citrix VDA machines (MCS / PVS, VDI); works on single-user machines as well.

| Page | Content |
| --- | --- |
| **Environment** | Four cards per row over the full width (two / one in narrow windows). Read only (installed / active values in the accent colour, missing ones grey): Windows edition, multi-user, domain; Remote Desktop Session Host role, active and disconnected sessions with user names; FSLogix version and profile container settings (policy values win); Citrix VDA version, Profile Management (UPM) and its policy values, provisioning (MCS / PVS); number of profiles, orphaned and temporary profiles. |
| **User profiles** | Local user profiles with user, type (local, roaming, temporary, mandatory, Citrix UPM, orphaned folder), state (current / signed in / signed out), last use, age and size (measured in the background). Header check box and toolbar buttons select all / none of the deletable profiles; *Select unused* (not used for *n* days, default 90) reports when nothing matches. Delete selected through the Windows API (`DeleteProfile`: folder and ProfileList entry) after confirmation; signed-in, current and special profiles show a lock with the reason instead of a check box, orphaned folders are only listed. Windows' own folders (Public, Default, UMFD-n / DWM-n font driver and window manager accounts) are not listed. Report *Profiles*. |
| **Server cleanup** | Cleanup rules for the profiles of **signed-out** users (signed-in users optional via switch) with its own selection, preselected from the *Terminal server (RDS / Citrix VDA)* profile: temp folders, thumbnail cache, WER, crash dumps, WinINet, DirectX shader cache, browser caches and service workers, Teams cache. FSLogix VHD(X) container contents are not touched. Command line: `--signed-out` for a scheduled task. |

## Overview

Large *Freed so far* card next to six statistics with count-up animation (cleaning runs, files removed, built-in and community rules, installed programs from the cache, available updates), quick analyses for full cleanup, system, browsers and development, drives, last cleanings, banner for a new CLEANit release (opt-in check).

## Settings

Language (flags; first start: German when the Windows display language, region (DE/AT/LI) or an installed keyboard layout is German, else English), appearance (Dark by default / Shadow / Light / System), expert mode (asked once at first start, together with the system-tray start), server admin mode (separate switch, off by default), start in the system tray at sign-in (scheduled task, repair when the folder moved), close to tray (off by default for new installations), notifications, analysis reminder and interval, clean all user profiles (expert), community rules on/off and own Winapp2.ini, recycle bin for folders and files, exclusions (with discard), restore point defaults (not on Windows Server), winget background check and interval, revoke source agreements, Chocolatey as optional source (off by default; when missing, *Install* installs the official winget package `Chocolatey.Chocolatey` after a confirmation – the source stays off), CLEANit update check (opt-in) with *Check now*, data/report/backup/rule folders, drive health auto refresh (off / 10 s / 30 s / 1 min / 5 min).

**Reset CLEANit** (below *Data & reports*): switches for settings (incl. the sign-in task), reports and statistics, caches, backups (off by default), own rules and bundles (off by default), or *Complete reset (like a new installation)*; red button, confirmation, then CLEANit restarts with `--reset=<parts>`, waits for the old instance, deletes the data and opens fresh.

**Backups**: list of all backups (startup, services, Windows settings, Firefox history, event logs) with restore, open and delete; optional automatic deletion of backups older than *n* days (off by default).

## System tray

Left click opens CLEANit. Right click: version header with the CLEANit logo, new release (if found), reminder result (if on), open, overview, analyze everything/system/browsers/development, programs, startup, updates (with count), software, Windows settings, large files, duplicates & compare, disk usage, device tree, reports, settings, language, about, exit. Analyses from the tray open the page – cleaning always needs the confirmation in the window.

## Command line

| Argument | Effect |
| --- | --- |
| `--analyze` | Analyze only (nothing is deleted) and print the result. |
| `--clean --confirm` | Analyze, then clean everything found. Without `--confirm` it only analyzes. |
| `--profile <id\|name>` | Selection profile (default `recommended`; see `--list-profiles`). |
| `--area <all\|system\|browser\|developer>` | Limit to one area (default `all`). |
| `--all-profiles` | All local user profiles (administrator). |
| `--report <file.json>` | Write analysis (and cleaning report) as JSON. |
| `--list-profiles`, `--help` | Show profiles / usage. |
| `--export-bundle <file>` | Save the installed winget packages as a bundle (winget export format). |
| `--install-bundle <file>` | Show which bundle packages are installed or new; with `--confirm` install the new ones, `--machine` for all users, `--accept-source-agreements` for unattended use. |
| `--tray` | Start without window (sign-in task). |
| `--register-autostart` | Create the sign-in task for the current user and exit (setup). |
| `--unregister-autostart` | Remove the current user's task and exit. |
| `--unregister-autostart-all` | Remove the tasks of all users and exit (uninstaller). |

Exit codes: `0` success, `1` errors, cancelled or nothing done, `2` invalid arguments. Cleaning from the command line writes a normal report (profile `<id> (cli)`).
