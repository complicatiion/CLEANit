![CLEANit](/preview/favicon.png)

# CLEANit

**A modern, open-source cleaning tool for Windows 10/11 and Windows Server – no telemetry, no ads, no bullshit.**

CLEANit finds temporary files, caches, logs and crash dumps of Windows, browsers, multimedia apps and development tools – and shows you per rule which folders, how many files and how much space are affected. **Nothing is deleted before you confirm the summary.** Rules are graded *Safe*, *Moderate* and *Deep*; only *Safe* rules are preselected, and *Deep* items (cookies, history, Windows.old …) are listed with their warnings and confirmed with a red *cannot be undone* button. A safety guard protects system folders, your libraries, credentials, browser passwords and bookmarks – whatever a rule says. On top, CLEANit manages **installed programs**, **startup entries** and **updates (winget)**, installs **software** with reusable **package bundles**, offers documented **Windows settings** with backup, **compares folders** like WinMerge, finds **duplicates**, shows **disk usage** as list, treemap, file types and a **device tree**, and checks **drive health** (NVMe/SMART). It can run in the **system tray**; admins get an optional **expert mode** (services, large files, reports, rule editor, **command line**) and a separate **server admin mode** for terminal servers, Citrix and FSLogix.

![CLEANit](/preview/preview-desktop-collage1.png)
![CLEANit](/preview/preview-desktop-collage2.png)

## Highlights

- **Full cleanup** — System, browsers and development in one analysis, with **selection profiles** (*Recommended*, *Daily*, *Developer workstation*, own profiles) shared by all cleanup pages.
- **Analyze → preview → confirm** — Every cleaning run starts with an analysis. You see each rule's folders, file count, size and largest files. A summary dialog lists everything again; with Deep items it shows their warnings and the button turns red (*cannot be undone*).
- **Three risk levels** — *Safe* (pure temp/cache data, preselected), *Moderate* (rebuild or re-download needed), *Deep* (not reversible, e.g. sign-out of all websites, no rollback to the previous Windows). Risk badges never look like buttons.
- **System cleanup** — Temp folders, thumbnail cache, error reports, crash and memory dumps, Windows logs, Delivery Optimization and Windows Update caches, Check Disk fragments (to the recycle bin), recycle bin, component store cleanup with DISM, Windows.old – plus multimedia and GPU shader caches, Teams, Discord, Slack, Steam and more.
- **Browser cleanup** — Edge, Chrome, Brave, Vivaldi, Chromium, Opera, Opera GX and Firefox, all profiles. Caches by default; cookies, history and last session only as Deep options. **Saved passwords, autofill data and bookmarks are never touched.**
- **Development cleanup** — Visual Studio, VS Code, JetBrains IDEs, Android Studio, NuGet, Gradle, Android SDK, emulator snapshots, npm, Yarn, pip, Go, Cargo and Unity caches – combined on one page.
- **Workspace scanner** — Finds your projects (.NET, Visual Studio, Unity, Gradle/Android, Node.js, Rust, Maven, Python) and lists their build output (`bin`, `obj`, `.vs`, `Library`, `build`, `node_modules` …) with size and last change; preselects only projects you haven't touched for *n* days.
- **Community rules** — Thousands of program-specific rules from **Winapp2.ini** (CC BY-SA 4.0), shipped unmodified. Shown only for programs found on your PC, marked *Community*, never preselected; their registry entries are ignored.
- **Programs** — All desktop programs and Store apps with version, size and install date. **Uninstall, modify and repair** run the program's own routine; afterwards CLEANit offers to remove leftover folders (to the recycle bin). Optional system restore point.
- **Startup** — Registry Run keys, startup folders and scheduled tasks with publisher and signature. Switch entries off like in the Task Manager (reversible), delete with backup, restore later, **add your own entry** (program, script or shortcut; for all users in expert mode).
- **Updates** — Lists available updates with **winget** and installs the ones you select; start a program right from the list. Background check is optional and **off by default**; nothing is ever installed automatically.
- **Software** — Search for an app (e.g. *Godot*), see version, source, publisher and license, select several and install them after one confirmation. See which installed programs winget knows, and save sets of apps as **bundles** in the native winget format – import, export, check what is new on this PC and install only the missing ones. Chocolatey can be added as a source (off by default) – if it is missing, *Settings → Updates → Install* installs it through winget from its official package.
- **Windows settings** — **Documented** Windows switches – *Recommended* (diagnostic data to the minimum, no web/Bing results in search, no lock-screen tips, Edge background off …) and *Optional* (search icon only, hide the touch keyboard icon, widgets …) – with risk level, filter pills and a link to Microsoft's documentation. Every change is backed up and can be switched back.
- **Device tree** — Every folder of a drive with size, file count and the largest files, sortable – like a light TreeSize. Read only until you switch on **edit mode**; then checked folders and files can be deleted after a confirmation.
- **Backups** — All backups CLEANit made (startup, services, Windows settings, Firefox history, event logs) in one list in the settings: restore, open, delete, optional automatic cleanup.
- **Compare** — Compare two folders (drop them in), by size and date or byte by byte: different, only left, only right, identical; copy to the other side or delete on one side. Find identical files across folders (SHA-256, cached); one copy of every duplicate always stays. Everything only after a confirmation, to the recycle bin by default.
- **Drive health** — Every drive with assessment, life left, temperature, power-on hours, data written and errors; click a card for the NVMe health log or SATA SMART table; optional auto refresh. Also reachable from every drive tile in *Disk usage*.
- **Disk usage** — Drives and the largest folders as list or **treemap**, plus a **file-type** breakdown; drill down by click – read only. Right-click the treemap to **export it as a JPEG** image.
- **Flexible tables** — Move, resize and sort the columns of the program, startup, update, report and catalog lists.
- **Reports** *(expert mode)* — Every cleaning run with details per rule; export as CSV or HTML.
- **System tray** — Optional start at sign-in, right-click menu for every page and quick analyses, closing the window keeps CLEANit in the tray.
- **Built for admins** — **Expert mode** (off by default): cleaning of **all user profiles**, event logs (backed up first), a *Prepare master image (MCS/PVS)* profile, services (start, stop, set to manual with restore), large files, reports, a rule editor with live preview, winget export/import, fixed versions and *all users* installs in the catalog. **Command line** for scripts (`--analyze`, `--clean --confirm`, `--signed-out`, `--report`, `--install-bundle`). Exclusions list, portable mode.
- **Server & terminal server** — Shown with the separate **server admin mode** (Settings → General): pages for Windows Server 2019/2022/2025, Remote Desktop Session Hosts and Citrix VDA (MCS/PVS, VDI): an **environment** overview (RDS role, sessions, FSLogix, Citrix VDA, Profile Management, provisioning), **local profiles** with size and last use – delete unused profiles through the Windows API – and a **server cleanup** for the profiles of signed-out users. FSLogix containers are never opened.
- **Modern interface** — Dark mode by default, *Shadow*, light or *System* (follows Windows), smooth page and list animations, collapsible and scrollable sidebar (click the logo), icons on every button, English and German (chosen automatically on the first start: German when Windows uses German as display language, region or keyboard layout, otherwise English), switch without restart.
- **Reset** — Settings → *Reset CLEANit*: delete settings, reports, caches, backups or own rules – or everything, like a new installation.
- **Local-first** — No account, no telemetry, no online scans. Only winget (when you check for updates or use the Software page), Chocolatey (if you enable it) and the optional CLEANit release check (off by default) go online.

## Navigation

| Area | What you'll find |
| --- | --- |
| **Overview** | Space freed and statistics, quick analyses (everything, system, browsers, development), drives, last cleanings. |
| **Full cleanup** | All rules of system, browsers and development on one page. |
| **System** | Windows, multimedia/GPU and application rules – analyze, select, check details, clean. |
| **Browsers** | Caches, offline data, cookies, history and sessions of all installed browsers and profiles. |
| **Development** | *Caches & SDKs*: IDE, SDK and package caches. *Workspace*: build output of your projects. |
| **Programs** | Search and filter installed programs; uninstall, modify, repair, open location, search leftovers. |
| **Software** | *Search & install*, *Installed* (known to winget?), *Bundles* (create, import/export, check, install new). |
| **Updates** | Check with winget, select, update, start programs; winget output for details. |
| **Startup** | All startup entries with switch, location, publisher and signature; add entry, delete with backup, restore deleted. |
| **Windows settings** | Documented switches, *Recommended* and *Optional*, with risk level, filters and backup. |
| **Disk usage** | Drives, folder sizes as list or treemap (export as JPEG), file types, drill down, show in Explorer, link to drive health. |
| **Drive health** | Assessment, life left, temperature and SMART/NVMe details of every drive. |
| **Device tree** | Folder tree with sizes and largest files, sorting, show in Explorer; edit mode for deleting. |
| **Compare** | Compare two folders, copy or delete; identical files in several folders (keep oldest/newest, delete selected). |
| **Expert** *(expert mode)* | Services, rule editor, large files, reports, command line. |
| **Server** *(server admin mode)* | Environment, user profiles, server cleanup (signed-out users). |
| **Settings** | Language, appearance (dark, shadow, light, system), expert mode, server admin mode, system tray and sign-in start, drive health refresh, analysis reminder, all user profiles (expert), community rules, recycle bin, exclusions, restore points, winget background check, Chocolatey source, CLEANit update check, data folders, **backups**, **reset CLEANit**. |
| **About** | Version, MIT license explained, all components with license and link. |

Every page with a list has a search field. The main action of a page or dialog is always on the right, *Cancel* to its left.

## Requirements

- Windows 10 / 11 or Windows Server 2016 / 2019 / 2022 / 2025, 64-bit.
- Administrator rights (CLEANit asks for them at start – system folders, services and programs of all users need them).
- Nothing else: the .NET runtime is included.
- For updates and the software catalog: **winget** (part of the Microsoft *App Installer*; on Windows Server 2016–2022 and LTSC editions it is usually missing and has to be installed separately) and internet access.

## How To – Step by Step

**1 · Download & install**

1. Download the latest `cleanit_vx.x.x_setup.exe` or `cleanit_vx.x.x_release.zip` from the [releases](https://github.com/complicatiion/CLEANit/releases).
2. **Setup:** run it (English is preselected, German can be chosen), optionally tick *Start CLEANit in the system tray at sign-in*.
   **Portable:** extract it, e.g. to `C:\Tools\CLEANit`, and start **`CLEANit.exe`**. Settings and reports are stored in the `data` folder next to it.
3. At the first start CLEANit asks whether you want to see the expert features – you can change this later in **Settings**. Optional: **Settings → Language** and **Appearance**.

**2 · Clean the system**

1. Open **System** and click **Analyze**.
2. Check the list: click a rule to see its folders, size and largest files on the right (drag the divider to widen the panel). Tick or untick rules, or pick a **profile** and apply it – *Recommended* selects the Safe rules again. Tip: **Full cleanup** does system, browsers and development in one go.
3. Click **Clean …**, check the summary and confirm with **Clean now**. With Deep items, read the warnings – the button then reads **Clean now (cannot be undone)**. A restore point can be switched on in the same row.
4. The result (space freed, files skipped because they are in use) appears below the list; the report is saved in the data folder.

**3 · Clean browsers**

1. Close your browsers (running browsers are detected and skipped).
2. **Browsers → Analyze**. Caches are preselected; cookies, history and sessions are Deep options you have to tick yourself.

**4 · Developer workstation**

1. **Development → Caches & SDKs → Analyze** for IDE and package caches.
2. **Development → Workspace**: choose your projects folder (e.g. `C:\Users\Name\source\repos`), set *Untouched for (days)*, click **Scan**, check the list and **Clean …** – build folders go to the recycle bin by default.

**5 · Uninstall a program**

1. **Programs**, search the program, click the uninstall icon and confirm (optionally with a restore point).
2. Follow the program's own uninstaller. Afterwards CLEANit offers the leftover folders it found – check each one and click **Remove selected**.

**6 · Startup and software updates**

1. **Startup**: switch off what you don't need at sign-in. Deleted entries can be restored with **Restore deleted**; **Add entry** starts a program of your choice at sign-in.
2. **Updates → Check for updates** (accept the winget source agreements once), select packages and click **Update selected** – or **Update all**. Packages you never want to update go on the ignore list (eye icon).

**7 · Install software and build bundles**

1. **Software → Search & install**: type e.g. `godot`, click **Search**, select a result to see publisher and license, tick one or more packages and click **Install selected** – the confirmation lists every package and its license.
2. **Installed**: click **Refresh**, tick the programs winget knows (or use the select-known button) and **Create bundle**.
3. **Bundles**: **Check** shows what is already installed on this PC, **Install new** installs only the rest. **Export bundle** writes a winget JSON file you can take to another PC (or use with `winget import`).

**8 · Windows settings**

1. **Windows settings**: click **Recommended** or switch individual settings (the filter pills narrow the list), check the risk and effect, **Apply**. CLEANit backs up the previous values first.
2. To undo, switch a setting off and **Apply** – or restore a backup in **Settings → Backups**.

**9 · Free space and compare: folders, duplicates, disk usage, device tree, large files**

1. **Compare → Compare folders**: drop a folder on the left and on the right card, **Compare**, filter (*Different*, *Only left* …), tick entries and **Copy to right** / **Copy to left** – existing files are only replaced if you tick that in the confirmation.
2. **Compare → Duplicates**: add or drop folders, **Scan**, use **Keep oldest** or **Keep newest**, check the selection, **Delete selected**.
3. **Disk usage**: analyze a drive, switch between list, treemap and file types. Right-click the treemap → **Export treemap as image …** saves a JPEG. The heart-beat icon on a drive tile opens **Drive health**.
4. **Device tree**: choose a drive or folder, **Scan**, expand folders and sort by size, name, files or date. To delete, switch on **Edit mode**, tick folders or files and click **Delete** (recycle bin by default). **Clear** empties a chosen folder and the tree again.
5. **Large files** *(expert mode)*: choose a folder (Downloads by default), set the minimum size and age, **Scan**, tick files, **Delete selected**.

**10 · For admins (optional)**

1. **Settings → General → Expert mode** shows *all user profiles*, expert rules and the *Expert* pages. **Server admin mode** (same card, separate switch) shows the *Server* pages.
2. Before sealing a Citrix MCS/PVS master image: **Full cleanup → profile *Prepare master image* → Apply → Analyze → Clean …**.
3. Terminal server / VDI: **Server → Environment** for an overview, **Server → User profiles**, set the days (90 by default), **Select unused → Delete selected** for unused profiles, **Server → Server cleanup** for the caches of signed-out users. As a nightly scheduled task: `CLEANit.exe --clean --profile rds --signed-out --confirm`.
4. From scripts: `CLEANit.exe --analyze --profile daily --report C:\Reports\cleanit.json` or `CLEANit.exe --clean --profile recommended --confirm` (elevated prompt). New PC: `CLEANit.exe --install-bundle D:\workstation.json --confirm --machine`.

## Uninstall

- **Setup:** *Windows Settings → Apps → CLEANit*. The uninstaller removes the sign-in task and asks whether settings, reports and backups should be deleted as well.
- **Portable:** switch off *Start CLEANit in the system tray at sign-in* in the settings, exit CLEANit via the system tray and delete the folder.

## Privacy & Security

- Everything runs locally. CLEANit sends no data anywhere, has no telemetry and no online scans; fonts and icons are embedded. Only winget connects to its sources when you check for or install updates, and – only if you switch it on – CLEANit asks GitHub once a day whether a new release exists (nothing is downloaded).
- **The registry is never cleaned.** It is only read to list programs and startup entries; switching a startup entry off writes the same value the Task Manager writes. The *Windows settings* page writes only documented values, backs them up first and can restore them.
- The safety guard refuses system folders (`System32`, `WinSxS` …), user libraries (Documents, Desktop, Downloads, OneDrive …), credential stores, browser passwords, autofill data, bookmarks and your exclusions – for every rule, including community rules. Links (junctions, symlinks) are never followed.
- Files in use are skipped, files changed after the analysis are not deleted, services stopped for a rule are restored, running apps are detected.
- Settings: `%AppData%\CLEANit`, reports and backups: `%ProgramData%\CLEANit` (portable: `data` next to the app). Details: [licenses/PRIVACY.md](licenses/PRIVACY.md).

## Important Notes

- **Deleted files are gone.** Check the summary before confirming – especially Deep items. A system restore point protects system files and programs, but does not bring back deleted caches, cookies, user files or Windows.old.
- *Clean all user profiles* affects every local profile on the machine – inform the users of a terminal server. With FSLogix or UPM only locally cached profiles are affected; FSLogix containers are never opened.
- **Deleting a profile** removes the user's local folder and its Windows registration. Roaming, UPM or FSLogix copies stay where they are; a user without one loses local data – check before deleting.
- Community rules are maintained by the Winapp2 project. They are never preselected – read what a rule cleans before you tick it.
- Installing and updating with winget (or Chocolatey) installs software from third-party sources under their own licenses; save your work first, programs may close.
- Windows settings in a company network may be controlled by group policies; ask your administrator before changing them.
- Closing the window ends CLEANit. If you switch on *Closing the window keeps CLEANit in the system tray*, it stays in the tray instead; **Exit CLEANit** in the tray menu ends it.
- **Reset CLEANit** deletes CLEANit's own data permanently (backups only if you select them) and restarts the app.
- Drive health shows what the drives report. The assessment is a warning sign, not a guarantee – keep backups of important data.
- Expert and server features change more of the system (event logs, services, all user profiles, user profiles on servers). They are off until you enable expert mode or server admin mode; event logs and service settings are backed up before any change.

More: [docs/FEATURES.md](docs/FEATURES.md) · Rules: [docs/RULES.md](docs/RULES.md) · Build: [docs/BUILD.md](docs/BUILD.md) · Changes: [docs/CHANGELOG.md](docs/CHANGELOG.md)

## Credits

CLEANit uses the community cleaning rules of **[Winapp2.ini](https://github.com/MoscaDotTo/Winapp2)** by the Winapp2 project (CC BY-SA 4.0, shipped unmodified) and **[winget](https://learn.microsoft.com/windows/package-manager/)** for updates and the software catalog. The interface is made with **[Avalonia UI](https://avaloniaui.net/)** (incl. DataGrid) and **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** on **[.NET](https://dotnet.microsoft.com/)**. Icons: **[Material Design Icons](https://pictogrammers.com/library/mdi/)**, font: **[Inter](https://rsms.me/inter/)**. Thank you to all authors – licenses in [licenses/README.md](licenses/README.md).

## Project & Copyright

CLEANit is a personal software project by **Sven Klocke** (*sksdesign | complicatiion*).

**Project repository:** [github.com/complicatiion/CLEANit](https://github.com/complicatiion/CLEANit)  
**Website:** [sksdesign.de](https://sksdesign.de)

CLEANit is released under the **MIT License** ([licenses/LICENSE.md](licenses/LICENSE.md)). It is provided without warranty; liability is limited to the extent permitted by law – see [licenses/LEGAL-NOTICE.md](licenses/LEGAL-NOTICE.md) and [licenses/COPYRIGHT.md](licenses/COPYRIGHT.md). Third-party components keep their own copyrights and licenses: [licenses/README.md](licenses/README.md).

Note: Windows, Windows Server, Microsoft Edge, Visual Studio, Google Chrome, Firefox, Unity, JetBrains, CCleaner and all other names are trademarks of their respective owners. CLEANit is not affiliated with or endorsed by them.
