# Privacy

CLEANit is **local-first**:

- **No telemetry, no account, no cloud.** CLEANit does not send any usage data anywhere and contains no online scans.
- **No downloads by CLEANit.** Rules (built-in and Winapp2.ini) ship with the program. CLEANit never fetches rules or updates by itself.
- **No external resources in the app.** Fonts and icons are embedded.
- **Network connections** only occur
  - when you use **Updates** or the **Software** page (search, details, installed list, bundles): CLEANit starts *winget* (Windows Package Manager), which connects to its sources (winget community repository, Microsoft Store) under their source agreements – you accept them once in CLEANit. Your search terms go to these sources. The background update check is **off by default**;
  - when you enable **Chocolatey** as a source (off by default) and search or install with it: `choco.exe` connects to the Chocolatey community repository under its terms;
  - when you click *Install* next to Chocolatey in the settings: winget downloads the official package `Chocolatey.Chocolatey`;
  - when you switch on **Check for new CLEANit versions** (off by default): once a day CLEANit asks `api.github.com` for the latest release of the project. GitHub receives your IP address and the user agent `CLEANit/<version>` (GitHub's privacy statement applies); nothing is downloaded, a new version is only linked;
  - when you open a link (project page, release page, license pages, sksdesign.de) in your browser.

The analysis reminder and all cleaning, duplicate, folder comparison, disk and drive health analyses run locally (drive data is read from the drives through Windows, nothing is sent). The server pages (server admin mode) read user profiles (ProfileList, user names, last use, folder sizes), the user names of active and disconnected sessions and FSLogix / Citrix settings on this machine only; they are shown, not stored or sent.

## Data stored by CLEANit

| Location | Content |
| --- | --- |
| `%AppData%\CLEANit\settings.json` (portable: `data\settings.json`) | interface settings per user (language, theme, window size, rule selection and server rule selection, profile age, exclusions, folders of the duplicate finder and the folder comparison, options) |
| `%ProgramData%\CLEANit\logs\clean-*.json` (portable: `data\logs`) | cleaning reports: rule names, counts, sizes, error messages – may contain **file paths with user names**; last 500 reports are kept |
| `%ProgramData%\CLEANit\reports.db` | index of the reports (time, area, freed size, counts, file name) |
| `%ProgramData%\CLEANit\stats.json` | totals of all cleanings (runs, freed size, files, first date) – kept when reports are deleted |
| `%ProgramData%\CLEANit\cache\hashes.db` | duplicate finder: file path, size, change time and SHA-256 (entries older than 180 days are dropped) |
| `%ProgramData%\CLEANit\cache\programs.json` | list of installed programs with timestamp (faster start of the *Programs* page) |
| `%ProgramData%\CLEANit\backups\firefox\`, `…\eventlogs\`, `…\services\` | backups made before clearing Firefox history or event logs, and before changing a service start type |
| `%ProgramData%\CLEANit\backups\startup\` | backups of deleted startup entries (command line, shortcut copy or task XML) |
| `%ProgramData%\CLEANit\backups\tweaks\` | previous registry values before a change on the *Windows settings* page |
| `%ProgramData%\CLEANit\bundles\` (portable: `data\bundles`) | your package bundles (winget package IDs, optional versions) |
| `%ProgramData%\CLEANit\rules\` | your own rule files and rule-editor files (optional) |
| Task Scheduler `\CLEANit\Tray <user>` | only if you enable *Start in the tray at sign-in* |

The first-start language is chosen from the Windows display language, region and keyboard layouts – read locally, never sent. All data stays on this computer. Backups can be reviewed, restored and deleted in *Settings › Backups*; an optional automatic cleanup is off by default. Reports may contain personal data (user names in paths); delete them in the `logs` folder if your policy requires it. *Settings › Reset CLEANit* deletes selected parts of this data (or all of it) at any time; uninstalling CLEANit offers to remove all of it.
