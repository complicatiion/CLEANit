using CLEANit.Core.Models;

namespace CLEANit.Core.Tweaks;

/// <summary>
/// Built-in Windows settings. Only documented switches: Microsoft policies (Policy CSP / ADMX / Edge policies)
/// or the value written by the corresponding Windows Settings switch. Moderate entries are expert-only.
/// </summary>
public static class TweakCatalog
{
    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        new()
        {
            Id = "appearance.transparency",
            Name = "Turn off transparency effects",
            NameDe = "Transparenzeffekte ausschalten",
            Description = "Same switch as Settings › Personalization › Colors › Transparency effects.",
            DescriptionDe = "Gleicher Schalter wie Einstellungen › Personalisierung › Farben › Transparenzeffekte.",
            Category = "Appearance",
            CategoryDe = "Darstellung",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Settings",
            DocUrl = "https://support.microsoft.com/windows/change-colors-in-windows-d26ef4d6-819a-581c-1581-493cfcc005fe",
            Values =
            [
            new(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0)
            ],
        },
        new()
        {
            Id = "appearance.fileext",
            Name = "Show file name extensions",
            NameDe = "Dateinamenerweiterungen anzeigen",
            Description = "Same option as File Explorer › View › File name extensions. Helps to recognise disguised files.",
            DescriptionDe = "Gleiche Option wie Explorer › Ansicht › Dateinamenerweiterungen. Hilft, getarnte Dateien zu erkennen.",
            Category = "Appearance",
            CategoryDe = "Darstellung",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.RestartExplorer,
            Source = "Settings",
            DocUrl = "https://support.microsoft.com/windows/common-file-name-extensions-in-windows-da4a4430-8e76-89c5-59f7-1cdbbc75cb01",
            Values =
            [
            new(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0)
            ],
        },
        new()
        {
            Id = "privacy.telemetry",
            Name = "Diagnostic data to the minimum",
            NameDe = "Diagnosedaten auf das Minimum",
            Description = "Policy AllowTelemetry: Required (1) on Home/Pro, Security/Off (0) on Enterprise, Education and Server.",
            DescriptionDe = "Richtlinie AllowTelemetry: Erforderlich (1) auf Home/Pro, Sicherheit/Aus (0) auf Enterprise, Education und Server.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Restart,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/privacy/configure-windows-diagnostic-data-in-your-organization",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1)
            ],
            EnterpriseValues =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)
            ],
            Note = "Home and Pro cannot go below Required.",
            NoteDe = "Home und Pro können nicht unter „Erforderlich“.",
        },
        new()
        {
            Id = "privacy.feedback",
            Name = "No feedback requests",
            NameDe = "Keine Feedback-Anfragen",
            Description = "Policy DoNotShowFeedbackNotifications: Windows no longer asks for feedback.",
            DescriptionDe = "Richtlinie DoNotShowFeedbackNotifications: Windows fragt nicht mehr nach Feedback.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-experience#donotshowfeedbacknotifications",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1)
            ],
        },
        new()
        {
            Id = "privacy.adid",
            Name = "Turn off the advertising ID",
            NameDe = "Werbe-ID ausschalten",
            Description = "Policy DisableAdvertisingId: apps cannot use the advertising ID for personalised ads.",
            DescriptionDe = "Richtlinie DisableAdvertisingId: Apps können die Werbe-ID nicht für personalisierte Werbung nutzen.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-privacy#disableadvertisingid",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1)
            ],
        },
        new()
        {
            Id = "privacy.tailored",
            Name = "No tailored experiences",
            NameDe = "Keine maßgeschneiderten Erfahrungen",
            Description = "Policy: Microsoft does not use diagnostic data for personalised tips, ads and recommendations.",
            DescriptionDe = "Richtlinie: Microsoft nutzt Diagnosedaten nicht für personalisierte Tipps, Werbung und Empfehlungen.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-experience#allowtailoredexperienceswithdiagnosticdata",
            Values =
            [
            new(RegHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1)
            ],
        },
        new()
        {
            Id = "privacy.activity",
            Name = "Turn off activity history",
            NameDe = "Aktivitätsverlauf ausschalten",
            Description = "Policies PublishUserActivities and UploadUserActivities: no activity history is collected or uploaded.",
            DescriptionDe = "Richtlinien PublishUserActivities und UploadUserActivities: kein Aktivitätsverlauf wird gesammelt oder hochgeladen.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-privacy#publishuseractivities",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)
            ],
        },
        new()
        {
            Id = "privacy.consumer",
            Name = "No suggested app installs",
            NameDe = "Keine vorgeschlagenen App-Installationen",
            Description = "Policy DisableWindowsConsumerFeatures: no automatically installed suggested apps.",
            DescriptionDe = "Richtlinie DisableWindowsConsumerFeatures: keine automatisch installierten vorgeschlagenen Apps.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-experience#allowwindowsconsumerfeatures",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1)
            ],
            Note = "Microsoft honours this policy only on Enterprise and Education.",
            NoteDe = "Microsoft beachtet diese Richtlinie nur auf Enterprise und Education.",
        },
        new()
        {
            Id = "search.suggestions",
            Name = "No web and Bing results in Windows search",
            NameDe = "Keine Web- und Bing-Ergebnisse in der Windows-Suche",
            Description = "Policy DisableSearchBoxSuggestions: Windows search shows local results only – no Bing web results and no web suggestions.",
            DescriptionDe = "Richtlinie DisableSearchBoxSuggestions: Die Windows-Suche zeigt nur lokale Ergebnisse – keine Bing-Webergebnisse und keine Webvorschläge.",
            Category = "Taskbar & search",
            CategoryDe = "Taskleiste & Suche",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-admx-windowsexplorer#disablesearchboxsuggestions",
            Values =
            [
            new(RegHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)
            ],
        },
        new()
        {
            Id = "privacy.diaglogs",
            Name = "No diagnostic logs",
            NameDe = "Keine Diagnoseprotokolle",
            Description = "Policy LimitDiagnosticLogCollection: Windows does not collect additional diagnostic logs.",
            DescriptionDe = "Richtlinie LimitDiagnosticLogCollection: Windows sammelt keine zusätzlichen Diagnoseprotokolle.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Restart,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-system#limitdiagnosticlogcollection",
            MinBuild = 22000,
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "LimitDiagnosticLogCollection", 1)
            ],
        },
        new()
        {
            Id = "privacy.dumps",
            Name = "Only small crash dumps",
            NameDe = "Nur kleine Absturzabbilder",
            Description = "Policy LimitDumpCollection: error reports may only contain kernel mini dumps and user mode triage dumps, no full memory contents.",
            DescriptionDe = "Richtlinie LimitDumpCollection: Fehlerberichte enthalten nur Kernel-Minidumps und Triage-Dumps, keine vollständigen Speicherinhalte.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Restart,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-system#limitdumpcollection",
            MinBuild = 22000,
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "LimitDumpCollection", 1)
            ],
        },
        new()
        {
            Id = "privacy.devicename",
            Name = "No device name in diagnostic data",
            NameDe = "Kein Gerätename in Diagnosedaten",
            Description = "Policy AllowDeviceNameInDiagnosticData = 0: the computer name is not sent with diagnostic data.",
            DescriptionDe = "Richtlinie AllowDeviceNameInDiagnosticData = 0: Der Computername wird nicht mit den Diagnosedaten gesendet.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Restart,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-system#allowdevicenameindiagnosticdata",
            MinBuild = 17763,
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowDeviceNameInTelemetry", 0)
            ],
        },
        new()
        {
            Id = "privacy.werdata",
            Name = "No additional error report data",
            NameDe = "Keine zusätzlichen Fehlerberichtsdaten",
            Description = "Policy \"Do not send additional data\": requests from Microsoft for more data about an error report are declined automatically.",
            DescriptionDe = "Richtlinie „Keine zusätzlichen Daten senden“: Nachfragen von Microsoft nach weiteren Daten zu einem Fehlerbericht werden automatisch abgelehnt.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-errorreporting#donotsendadditionaldata",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "DontSendAdditionalData", 1)
            ],
        },
        new()
        {
            Id = "privacy.inking",
            Name = "No inking and typing data",
            NameDe = "Keine Freihand- und Eingabedaten",
            Description = "Policy AllowLinguisticDataCollection = 0: no inking and typing data is sent to improve recognition.",
            DescriptionDe = "Richtlinie AllowLinguisticDataCollection = 0: Es werden keine Freihand- und Eingabedaten zur Verbesserung der Erkennung gesendet.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-textinput#allowlinguisticdatacollection",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\TextInput", "AllowLinguisticDataCollection", 0)
            ],
        },
        new()
        {
            Id = "privacy.languagelist",
            Name = "Websites cannot read the language list",
            NameDe = "Websites lesen die Sprachliste nicht",
            Description = "Same switch as Settings › Privacy › General › \"Let websites show me locally relevant content by accessing my language list\".",
            DescriptionDe = "Gleicher Schalter wie Einstellungen › Datenschutz › Allgemein › „Websites den Zugriff auf die eigene Sprachliste gestatten“.",
            Category = "Privacy",
            CategoryDe = "Datenschutz",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Settings",
            DocUrl = "https://learn.microsoft.com/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#181-general",
            Values =
            [
            new(RegHive.CurrentUser, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1)
            ],
        },
        new()
        {
            Id = "lockscreen.spotlight",
            Name = "Turn off Windows spotlight",
            NameDe = "Windows-Blickpunkt ausschalten",
            Description = "Policy \"Turn off all Windows spotlight features\": no spotlight images, tips or suggestions on the lock screen, desktop and in Settings.",
            DescriptionDe = "Richtlinie „Alle Windows-Blickpunkt-Features deaktivieren“: keine Blickpunkt-Bilder, Tipps oder Vorschläge auf Sperrbildschirm, Desktop und in den Einstellungen.",
            Category = "Lock screen & spotlight",
            CategoryDe = "Sperrbildschirm & Blickpunkt",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-experience#allowwindowsspotlight",
            Editions = TweakEdition.EnterpriseOnly,
            Values =
            [
            new(RegHive.CurrentUser, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1)
            ],
        },
        new()
        {
            Id = "lockscreen.tips",
            Name = "Lock screen without tips and suggestions",
            NameDe = "Sperrbildschirm ohne Tipps und Vorschläge",
            Description = "Same switch as Settings › Personalization › Lock screen › \"Get fun facts, tips, tricks, and more on your lock screen\".",
            DescriptionDe = "Gleicher Schalter wie Einstellungen › Personalisierung › Sperrbildschirm › „Unterhaltsame Fakten, Tipps, Tricks und mehr auf dem Sperrbildschirm“.",
            Category = "Lock screen & spotlight",
            CategoryDe = "Sperrbildschirm & Blickpunkt",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Settings",
            DocUrl = "https://support.microsoft.com/help/17185",
            Values =
            [
            new(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0),
            new(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338387Enabled", 0)
            ],
            Note = "Windows spotlight itself can only be switched off by policy on Enterprise and Education. On Home and Pro choose \"Picture\" or \"Slideshow\" in Settings › Personalization › Lock screen – with spotlight as background Windows keeps showing its tips.",
            NoteDe = "Windows-Blickpunkt selbst lässt sich nur auf Enterprise und Education per Richtlinie abschalten. Auf Home und Pro unter Einstellungen › Personalisierung › Sperrbildschirm „Bild“ oder „Diashow“ wählen – mit Blickpunkt als Hintergrund zeigt Windows weiter Tipps.",
        },
        new()
        {
            Id = "taskbar.searchicon",
            Name = "Search on the taskbar: icon only",
            NameDe = "Suche in der Taskleiste: nur Symbol",
            Description = "Same option as Settings › Personalization › Taskbar › Search › \"Search icon only\". Users can still change it there.",
            DescriptionDe = "Gleiche Option wie Einstellungen › Personalisierung › Taskleiste › Suche › „Nur Suchsymbol“. Lässt sich dort weiterhin ändern.",
            Category = "Taskbar & search",
            CategoryDe = "Taskleiste & Suche",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.RestartExplorer,
            Source = "Settings",
            DocUrl = "https://support.microsoft.com/help/12385",
            Values =
            [
            new(RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1)
            ],
        },
        new()
        {
            Id = "taskbar.touchkeyboard",
            Name = "Hide the touch keyboard icon",
            NameDe = "Bildschirmtastatur-Symbol ausblenden",
            Description = "Same option as Settings › Personalization › Taskbar › Touch keyboard › \"Never\" (instead of \"When no keyboard attached\" or \"Always\").",
            DescriptionDe = "Gleiche Option wie Einstellungen › Personalisierung › Taskleiste › Bildschirmtastatur › „Nie“ (statt „Wenn keine Tastatur angeschlossen ist“ oder „Immer“).",
            Category = "Taskbar & search",
            CategoryDe = "Taskleiste & Suche",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.RestartExplorer,
            Source = "Settings",
            DocUrl = "https://support.microsoft.com/windows/enable-and-disable-the-touch-keyboard-in-windows",
            MinBuild = 22000,
            Values =
            [
            new(RegHive.CurrentUser, @"SOFTWARE\Microsoft\TabletTip\1.7", "TipbandDesiredVisibility", 0)
            ],
        },
        new()
        {
            Id = "taskbar.widgets",
            Name = "Remove widgets from the taskbar",
            NameDe = "Widgets aus der Taskleiste entfernen",
            Description = "Policy AllowNewsAndInterests: removes the widgets button and board (Windows 11). The user switch itself is protected by Windows against tools, the policy is the documented way.",
            DescriptionDe = "Richtlinie AllowNewsAndInterests: entfernt Widgets-Schaltfläche und -Board (Windows 11). Den Benutzerschalter selbst schützt Windows vor Tools, die Richtlinie ist der dokumentierte Weg.",
            Category = "Taskbar & search",
            CategoryDe = "Taskleiste & Suche",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.RestartExplorer,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-newsandinterests",
            MinBuild = 22000,
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0)
            ],
        },
        new()
        {
            Id = "taskbar.feeds",
            Name = "Turn off news and interests",
            NameDe = "Neuigkeiten und interessante Themen ausschalten",
            Description = "Policy EnableFeeds: removes news and interests from the taskbar (Windows 10).",
            DescriptionDe = "Richtlinie EnableFeeds: entfernt Neuigkeiten und interessante Themen aus der Taskleiste (Windows 10).",
            Category = "Taskbar & search",
            CategoryDe = "Taskleiste & Suche",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.RestartExplorer,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-feeds",
            MaxBuild = 21999,
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0)
            ],
        },
        new()
        {
            Id = "search.cortana",
            Name = "Turn off Cortana",
            NameDe = "Cortana ausschalten",
            Description = "Policy AllowCortana (Windows 10).",
            DescriptionDe = "Richtlinie AllowCortana (Windows 10).",
            Category = "Taskbar & search",
            CategoryDe = "Taskleiste & Suche",
            Risk = RiskLevel.Safe,
            Recommended = false,
            Effect = TweakEffect.SignOut,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-experience#allowcortana",
            MaxBuild = 21999,
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0)
            ],
        },
        new()
        {
            Id = "edge.firstrun",
            Name = "Skip the Edge first-run experience",
            NameDe = "Edge-Ersteinrichtung überspringen",
            Description = "Edge policy HideFirstRunExperience: no welcome and import wizard at the first start.",
            DescriptionDe = "Edge-Richtlinie HideFirstRunExperience: kein Willkommens- und Import-Assistent beim ersten Start.",
            Category = "Microsoft Edge",
            CategoryDe = "Microsoft Edge",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/deployedge/microsoft-edge-policies#hidefirstrunexperience",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", 1)
            ],
            Note = "Edge then shows \"Managed by your organization\".",
            NoteDe = "Edge zeigt danach „Von Ihrer Organisation verwaltet“.",
        },
        new()
        {
            Id = "edge.startupboost",
            Name = "Turn off Edge startup boost",
            NameDe = "Edge-Startbeschleunigung ausschalten",
            Description = "Edge policy StartupBoostEnabled: Edge no longer starts in the background at sign-in.",
            DescriptionDe = "Edge-Richtlinie StartupBoostEnabled: Edge startet bei der Anmeldung nicht mehr im Hintergrund.",
            Category = "Microsoft Edge",
            CategoryDe = "Microsoft Edge",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/deployedge/microsoft-edge-policies#startupboostenabled",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0)
            ],
            Note = "Edge then shows \"Managed by your organization\".",
            NoteDe = "Edge zeigt danach „Von Ihrer Organisation verwaltet“.",
        },
        new()
        {
            Id = "edge.background",
            Name = "Edge does not keep running after closing",
            NameDe = "Edge läuft nach dem Schließen nicht weiter",
            Description = "Edge policy BackgroundModeEnabled: no background apps after the last window is closed.",
            DescriptionDe = "Edge-Richtlinie BackgroundModeEnabled: keine Hintergrund-Apps nach dem Schließen des letzten Fensters.",
            Category = "Microsoft Edge",
            CategoryDe = "Microsoft Edge",
            Risk = RiskLevel.Safe,
            Recommended = true,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/deployedge/microsoft-edge-policies#backgroundmodeenabled",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0)
            ],
            Note = "Edge then shows \"Managed by your organization\".",
            NoteDe = "Edge zeigt danach „Von Ihrer Organisation verwaltet“.",
        },
        new()
        {
            Id = "system.doDownload",
            Name = "Delivery Optimization: no peer downloads",
            NameDe = "Übermittlungsoptimierung: keine Peer-Downloads",
            Description = "Policy DODownloadMode = 0 (HTTP only): updates are not shared with other PCs.",
            DescriptionDe = "Richtlinie DODownloadMode = 0 (nur HTTP): Updates werden nicht mit anderen PCs geteilt.",
            Category = "System",
            CategoryDe = "System",
            Risk = RiskLevel.Moderate,
            Recommended = false,
            Effect = TweakEffect.Restart,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/deployment/do/waas-delivery-optimization-reference#download-mode",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)
            ],
            Note = "Can increase internet traffic in networks with many PCs.",
            NoteDe = "Kann in Netzen mit vielen PCs den Internetverkehr erhöhen.",
        },
        new()
        {
            Id = "system.wer",
            Name = "Turn off Windows Error Reporting",
            NameDe = "Windows-Fehlerberichterstattung ausschalten",
            Description = "Policy DisableWindowsErrorReporting: no error reports are sent to Microsoft.",
            DescriptionDe = "Richtlinie DisableWindowsErrorReporting: keine Fehlerberichte an Microsoft.",
            Category = "System",
            CategoryDe = "System",
            Risk = RiskLevel.Moderate,
            Recommended = false,
            Effect = TweakEffect.Immediately,
            Source = "Policy",
            DocUrl = "https://learn.microsoft.com/windows/client-management/mdm/policy-csp-admx-windowserrorreporting#disablewindowserrorreporting",
            Values =
            [
            new(RegHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)
            ],
            Note = "Microsoft can no longer offer solutions for crashes.",
            NoteDe = "Microsoft kann keine Lösungen für Abstürze mehr anbieten.",
        }
    ];

    /// <summary>Tweaks for this Windows build and edition; Moderate/Deep only in expert mode.</summary>
    public static IReadOnlyList<TweakDefinition> For(int build, bool expert, bool enterprise = false) =>
        All.Where(t => t.AppliesTo(build, enterprise) && (expert || t.Risk == RiskLevel.Safe)).ToList();
}
