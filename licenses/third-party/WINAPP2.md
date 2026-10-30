# Winapp2.ini

| | |
| --- | --- |
| Work | Winapp2.ini – a database of declarative cleaning routines for Windows (Non-CCleaner variant) |
| Version | 260915 (header of the file) |
| Authors | The Winapp2 project and its contributors (MoscaDotTo/Winapp2) |
| License | Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) |
| Source | https://github.com/MoscaDotTo/Winapp2 (file `Non-CCleaner/Winapp2.ini`) |
| License text | [LICENSE-CC-BY-SA-4.0.md](LICENSE-CC-BY-SA-4.0.md) (copy of the project's `License.md`) |
| Shipped as | `rules\winapp2\Winapp2.ini` next to `CLEANit.exe`, together with `rules\winapp2\License.md` |
| Changes | **None.** The file is distributed exactly as published by the Winapp2 project (verified by SHA-256 during the build). |

## How CLEANit uses the file

CLEANit is a separate program (MIT License) that **reads** Winapp2.ini at runtime. It does not include, translate or redistribute a modified version of the database. While reading, CLEANit applies its own filters in memory only:

- `RegKey` entries are ignored – CLEANit never cleans the registry. `Detect` keys are only read to find out whether a program is installed.
- Entries for passwords, autofill data, bookmarks and sync data are not offered.
- Entries for browsers that CLEANit cleans with its own rules are not offered (no duplicates).
- Every entry is shown as a **Community** rule, risk *Moderate* (entries with a warning or for cookies/history/sessions: *Deep*), and is **never preselected**.
- All paths pass the CLEANit safety guard (protected system folders, user libraries, credentials, exclusions).

Users can replace the bundled file with their own copy (*Settings → Cleaning → Use own Winapp2.ini*). CLEANit never downloads Winapp2.ini by itself; maintainers update the bundled copy with `build\fetch-winapp2.ps1`.

## Attribution

> Winapp2.ini by the Winapp2 project (https://github.com/MoscaDotTo/Winapp2), licensed under CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/). Used unmodified.

The Winapp2 project is not affiliated with CLEANit and does not endorse it. CCleaner is a trademark of its respective owner; CLEANit is not affiliated with it.
