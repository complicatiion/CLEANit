# Legal Notice

## Disclaimer

CLEANit is provided **“as is”**, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and non-infringement (see the MIT License in [LICENSE.md](LICENSE.md)).

To the extent permitted by applicable law, the author is not liable for any damages arising from the use of CLEANit, including data loss, lost settings or sign-ins, system or application malfunctions, or interrupted operation. Mandatory statutory liability (e.g. for intent or gross negligence, fraudulently concealed defects, or under product liability law) remains unaffected.

## Deleting data is permanent

CLEANit deletes files only after an analysis **and** an explicit confirmation by the user. Unless a rule is marked *Recycle bin*, deleted files are removed **permanently**. Before confirming, please check the summary – in particular rules marked **Deep** (e.g. cookies, browsing history, Windows.old, DISM /ResetBase), which cannot be undone. A system restore point does not restore deleted caches, cookies, user files or Windows.old.

## Operator responsibility

CLEANit is a tool for users and administrators. The operator is responsible for:

- choosing the rules and checking the summary before cleaning,
- the profiles cleaned on multi-user systems (option *Clean all user profiles*) and informing their users,
- uninstalling programs and installing software or updates with winget or Chocolatey – through the Software page, bundles or Updates (packages are third-party software subject to their own licenses and agreements; CLEANit does not check, distribute or license them),
- changes made on the *Windows settings* page (documented switches; group policies of an organization take precedence and may be overwritten locally only with the administrator's consent),
- copying and deleting files with the folder comparison and the duplicate finder (existing files are only replaced when this is confirmed; check both folders before confirming),
- deleting user profiles on the *User profiles* page (the local folder and the Windows registration are removed; data that exists only locally is lost) and the cleanup of other users' profiles on servers, including informing the users concerned,
- starting and stopping services and changing their start type (stopping a service can interrupt programs or users that depend on it),
- adding startup entries (the program runs at every sign-in; *for all users* affects every user of the machine),
- deleting folders and files in the device tree edit mode,
- installing Chocolatey through winget (official package; Chocolatey and the packages of its community repository are third-party software under their own licenses and terms of use),
- resetting CLEANit (Settings › Reset CLEANit deletes the selected parts of CLEANit's own data permanently, including backups if selected),
- interpreting drive health values (the assessment follows the drive's own data and common thresholds; it is not a guarantee against data loss – keep backups),
- compliance with company policies and applicable data protection law.

## Community rules and updates

Community rules come from Winapp2.ini (CC BY-SA 4.0, shipped unmodified) and are maintained by third parties. They are never preselected and always pass CLEANit's safety guard, but their correctness cannot be guaranteed. Software and updates are provided by winget, Chocolatey (optional) and their sources, not by CLEANit. Package names, versions, publishers and license details shown on the Software page are taken from these sources without verification.

## Contact

Sven Klocke · sksdesign | complicatiion · [sksdesign.de](https://sksdesign.de)
