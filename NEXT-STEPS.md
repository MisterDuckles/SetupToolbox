# WingetAppDeployer — Roadmap

Native Windows 11 app voor het bulk-installeren van apps via `winget`. Pre-release (v0.5.x), op weg naar v1.0.

> WPF-historie tot en met v1.2.1 is gearchiveerd onder git tag `wpf-final-v1.2.1`. Repo is sinds v0.5.9 WinUI-only.

**Stack:** .NET 10 + Windows App SDK 1.8 + WinUI 3 + unpackaged exe. Mica backdrop, native `Microsoft.UI.Xaml` controls. Distributie via private repo + public GitHub Releases. `apps.json` is gebundeld met de exe (geen live fetch).

---

## Voltooide versies

### v0.9.11 — Notifications & Lock Screen tweaks

**7 tweaks** in de NotificationsLock-categorie (was leeg), 3 sub-groepen. Research mei 2026 (4 web-passes, Win11 24H2/25H2 geverifieerd). De twee HKLM-policy-tweaks batchen in 1 UAC; de rest is HKCU → geen UAC.

**Schets-correcties uit research:**
- Schets-item "Disable 'Suggest ways to finish setup' notifications" is bewust NIET opgenomen — `ScoobeSystemSettingEnabled` zit al in v0.9.4 (`Ads.DisableScoobePrompt` + OFGB-bundle). Niet gedupliceerd.
- Schets-items "Disable Action Center / Notification Center" en "Hide Calendar from systray click" zijn op Win11 dezelfde unified flyout → samengevoegd tot één tweak (`DisableNotificationCenter`).

**Groep "Meldingen"** (3):
- Disable all notifications — `PushNotifications\ToastEnabled=0`. Master-toggle, alle toasts uit. SignOut
- Disable notification sounds — `Notifications\Settings\NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND=0`. Toasts blijven visueel, geen geluid
- Notification display time — multi-choice — `HKCU\Control Panel\Accessibility\MessageDuration` — 5s (standaard, value-absent) / 7s / 15s / 30s / 1min / 5min. SignOut

**Groep "Notificatiecentrum"** (1):
- Disable Notification Center & Calendar flyout — `HKCU\Software\Policies\...\Explorer\DisableNotificationCenter=1`. Bel-icoon weg + klok opent geen kalender/meldingen-paneel meer (Win10-stijl tray-klok). ExplorerRestart

**Groep "Vergrendelscherm"** (3):
- Disable lock screen — HKLM `Policies\...\Personalization\NoLockScreen=1`. Direct naar inlogscherm. Caveat in omschrijving: bij Secure Sign-In niet 100% overslaanbaar. SignOut
- Disable notifications on lock screen — 2-op HKCU: `PushNotifications\LockScreenToastEnabled=0` + `Notifications\Settings\NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK=0`. SignOut
- Disable lock screen background on sign-in — HKLM `Policies\...\System\DisableLogonBackgroundImage=1`. Effen accent-kleur i.p.v. foto op het wachtwoord-scherm

`DisabledValue=null` bij de policy-tweaks (NoLockScreen / DisableNotificationCenter / DisableLogonBackgroundImage) — de policy-value is absent by default, revert deletet 'm.

**Bewust niet opgenomen:**
- **Spotlight "fun facts/tips" op lockscreen** — `RotatingLockScreenOverlayEnabled` + `SubscribedContent-338387` zitten al in de OFGB-bundle (v0.9.4). Geen standalone duplicaat (user-keuze).
- **Focus / Do Not Disturb auto-regels** (full-screen, games) — per-regel registry-structuur onder `Notifications\Settings` is fragiel/ongedocumenteerd, past niet in het toggle-model
- **Disable Quick Settings panel** — geen betrouwbare losse registry-key op Win11

### v0.9.10 — Context Menu tweaks

**10 Context Menu tweaks** in de ContextMenu-categorie, 2 sub-groepen (collapsible). Research mei 2026 (3 web-passes) — alle CLSID's geverifieerd. Alles HKCU → geen UAC; `ExplorerRestart` zodat het menu de wijziging direct oppikt.

**Win11 context-menu realiteit**: custom `shell\`-verbs verschijnen alleen onder "Toon meer opties" — TENZIJ de Classic context menu tweak (Explorer-categorie, v0.9.1) aanstaat, dan staan ze direct in het menu. De verwijder-tweaks voor Photos/Paint raken het hoofdmenu direct (via de Shell Extensions Blocked-lijst).

**Groep "Items verwijderen"** (7) — onderdrukt shell-extensions via een String-value (CLSID-naam, lege data) in `Shell Extensions\Blocked`. Niet-destructief, reversible:
- Remove 'Edit with Photos' — `{BFE0E2A4-…}` (hoofdmenu)
- Remove 'Edit with Paint' — `{2430F218-…}` (hoofdmenu)
- Remove 'Scan with Microsoft Defender' — `{09A47860-…}` (Blocked-lijst i.p.v. key-delete; Defender herstelt een verwijderde key anders)
- Remove 'Restore previous versions' — `{596AB062-…}`
- Remove 'Cast to Device' — `{7AD84985-…}` (Play To)
- Remove 'Include in library' — `{3dad6c5d-…}`
- Remove 'Give access to' / sharing — `{f81e9010-…}` (netwerk-shares blijven werken)

**Groep "Items toevoegen"** (3) — multi-op verb-keys onder `HKCU\Software\Classes`; verb-root krijgt `DeleteKeyOnAbsent` zodat revert de subtree opruimt:
- Add 'Take Ownership' — `runas`-verb (auto-elevate) op bestanden + mappen, takeown + icacls. Beschrijving waarschuwt: nooit op systeemmappen gebruiken
- Add 'Move to folder' / 'Copy to folder' — klassieke shell-handlers via `ContextMenuHandlers` (CLSID `{C2FBB631-…}` / `{C2FBB630-…}`)
- Add 'Open Terminal as Admin' — `wt.exe` elevated via PowerShell `Start-Process -Verb RunAs`, op mapachtergrond + mappen

### v0.9.9 — Performance tweaks ("de schone 9") + Tweaks sidebar-nav fix

**9 Performance tweaks** in de Performance-categorie. Research mei 2026 (2 passes via web): de roadmap-schets is uitgewerkt + extra tweaks onderzocht. Eerlijke conclusie: veel "performance-tweaks" zijn placebo of riskant — daarom alléén de geverifieerde solide set ("de schone 9"), geen snake-oil. Meeste HKLM-policies/keys → batchen in 1 UAC.

- **Disable Fast Startup** — `Session Manager\Power\HiberbootEnabled=0`. Reboot
- **Disable power throttling** — `Power\PowerThrottling\PowerThrottlingOff=1`. Reboot
- **Disable Storage Sense** — `Policies\...\StorageSense\AllowStorageSenseGlobal=0`
- **Disable background apps (UWP/Store)** — `Policies\...\AppPrivacy\LetAppsRunInBackground=2` (Force Deny). SignOut
- **Enable long path support** — `FileSystem\LongPathsEnabled=1`. Reboot
- **Prefer IPv4 over IPv6** — `Tcpip6\Parameters\DisabledComponents=0x20` (IPv6 blijft functioneel; bewust 0x20, niet 0xFF). Reboot
- **Disable Multiplane Overlay** — `Dwm\OverlayTestMode=5` (flikker/tearing-fix; undocumented debug-waarde). Reboot
- **Remove startup-app delay** — HKCU `Explorer\Serialize\StartupDelayInMSec=0` (geen UAC). SignOut
- **Disable NTFS last-access timestamps** — `FileSystem\NtfsDisableLastAccessUpdate=1`. Reboot

`DisabledValue=null` bij de tweaks waar de Windows-default "value absent" is — revert deletet de value dan i.p.v. een waarde te forceren.

**Bewust niet opgenomen** (research-onderbouwd):
- **powercfg-afhankelijk** — Ultimate Performance power plan (`powercfg /duplicatescheme`) en hibernation-met-hiberfil.sys-reclaim (`powercfg /hibernate off`) passen niet in het registry-only TweakOperation-model. Geparkeerd; eventueel later een command-tweak model-uitbreiding
- **Startup-apps cleanup** — vereist enumeratie van Run-keys + StartupApproved + Startup-folders + Task Scheduler; aparte feature, geen flat toggle
- **Visual-effects 'best performance' preset** — overlapt al met de UI/Theme animaties/transparency tweaks (v0.9.8); de UserPreferencesMask binary is onveilig om blind te schrijven
- **Placebo/riskant** — WSearch uitzetten (breekt Start-menu search), SysMain/Prefetcher/Win32PrioritySeparation/8.3-names/HwSchMode (verwaarloosbaar of debated op moderne hardware), curated services (al trigger-started → bijna geen idle-kosten). Xbox-services horen in v0.9.13 Gaming

**Tweaks sidebar-nav fix**: klikken op "Tweaks" in de NavigationView terwijl je op een `TweakCategoryDetailPage` staat → terug naar de category-grid landing. Op de landing met actieve search → search gewist (`ResetToRoot`). Spiegelt het bestaande gedrag van de Apps-sidebar.

### v0.9.8 — Tweaks-tab herstructurering (category-grid) + UI / Theme tweaks

Grote UI-herbouw van de Tweaks-tab naar het Apps-tab patroon, plus 13 UI/Theme tweaks en 2 Explorer tweaks. **De apply/detect-logica in TweakService is volledig ongemoeid** — alleen de UI-laag is herbouwd.

**Tweaks-tab herstructurering** (was: één lange pagina met expander-lijst per categorie — user vond dat "lelijk"). Nieuwe structuur, gekozen via user-Q&A:
- **Category-grid landing** — grote tiles (emoji-icoon + naam + blurb + "N/M actief"), 2-3 per rij, net als de Apps-tab. Klik tile → detail-pagina
- **`TweakCategoryDetailPage`** (nieuw) — toont de tweaks van één categorie. **Hybride sub-groepen**: geen Group → platte lijst; Group + <8 tweaks → vaste sub-headers; Group + ≥8 → inklapbare Expander per sub-groep (default dicht)
- **Globale zoekbalk** op de landing — doorzoekt alle tweaks (naam / omschrijving / use-case / categorie), toont platte resultaten met categorie-label
- **Globale footer** (pending count + Discard + Apply) op zowel de landing als elke detail-pagina
- **Progress-status per tile**: groene "Volledig actief"-pill bij 100% toegepast, anders "N / M actief"-tekst. Pending-badge wanneer een categorie openstaande wijzigingen heeft

**Nieuwe infra** (UI-laag, geen wijziging aan apply/detect):
- `TweakPendingService` — cross-page pending-store (App.TweakPending), overleeft navigatie landing ↔ detail
- `TweakCardFactory` — gedeelde card-renderer voor detail-pagina én zoekresultaten. **Checkbox-fix**: Enabled/Disabled tweaks zijn nu schone 2-state checkboxes (geen verwarrende indeterminate-minus meer bij toggle); alleen tweaks die al Partial zíjn krijgen een 3-state checkbox (daar is de minus legitiem)
- `TweakApplyRunner` — gedeelde apply-orchestratie (backup-prompt + batching + explorer-restart + re-detect), aangeroepen vanaf beide footers
- `Tweak.Group` veld (nullable string, puur cosmetisch — apply/detect leest 't nooit)

**Infra — HKU hive support**: `ParsePath` (TweakService + SnapshotService) ondersteunt nu de **HKU / HKEY_USERS** hive, nodig voor `HKU\.DEFAULT` (login-scherm profiel).

**13 UI / Theme tweaks** in 4 sub-groepen:

**Groep "Thema & kleuren":**
- **System theme** — multi-choice (Light / Dark / Custom). `Themes\Personalize\AppsUseLightTheme` + `SystemUsesLightTheme`. Custom = donkere apps + lichte shell
- **Disable transparency effects** — `Personalize\EnableTransparency=0`
- **Accent color on title bars & borders** — `DWM\ColorPrevalence=1` (toggle, geen color-picker)
- **Accent color on Start & taskbar** — `Themes\Personalize\ColorPrevalence=1` (alleen zichtbaar in Dark mode)

**Groep "Desktop & vensters":**
- **Disable window & taskbar animations** — 2-op: `WindowMetrics\MinAnimate=0` (REG_SZ) + `TaskbarAnimations=0`
- **Show This PC / Network / Control Panel on desktop** — 3 GUID-keys onder `HideDesktopIcons\NewStartPanel`
- **Disable Snap Assist suggestions** — `EnableSnapAssistFlyout` + `SnapAssist` = 0
- **Disable Aero Shake** — `DisallowShaking=1`

**Groep "Boot & login":**
- **Verbose logon / shutdown messages** — HKLM `Policies\System\verbosestatus=1`
- **Show detailed Blue Screen info** — HKLM `CrashControl\DisplayParameters=1`
- **NumLock on at boot** — 2-op: HKCU + `HKU\.DEFAULT` `Control Panel\Keyboard\InitialKeyboardIndicators=2`. SignOut
- **Disable login screen background blur** — HKLM `Policies\System\DisableAcrylicBackgroundOnLogon=1`

**Groep "Geluid":**
- **Disable Windows startup sound** — HKLM `LogonUI\BootAnimation\DisableStartupSound=1`

**+ 2 Explorer tweaks** (Explorer-categorie, geen sub-groepen):
- **Compact view in File Explorer** — `Explorer\Advanced\UseCompactMode=1`
- **Show full path in File Explorer** — `Explorer\CabinetState\FullPath=1`

**Bewust niet opgenomen / geparkeerd:**
- **Accent color override** — vereist een color-picker UI; past niet in het toggle/multi-choice TweakOperation-model. (De accent-*toggles* hierboven zijn wél opgenomen — die zetten alleen aan/uit, kiezen geen kleur)
- **Restore classic Photo Viewer** — ~15 registry-values onder `Windows Photo Viewer` + `HKLM\SOFTWARE\Classes`; fragiel op Win11 24H2+ (UCPD blokkeert programmatische default-app changes — user zou 't alsnog handmatig als default moeten kiezen)

### v0.9.7 — Privacy uitbreidingen

**8 Privacy tweaks** in de Privacy-categorie. Mix van HKLM-policies (RequiresElevation, batchen in 1 UAC dankzij de v0.9.6 batching-fix) en HKCU user-keys (geen UAC). Tailored Experiences is bewust niet gedupliceerd — zit al in Ads & Tracking (v0.9.4).

- **Disable Activity History** — 3-op HKLM policy: `System\EnableActivityFeed` + `PublishUserActivities` + `UploadUserActivities` = 0. Geen Timeline-verzameling van apps/documenten meer. SignOut-requirement
- **Disable inking & typing personalization** — 4-op HKCU: `InputPersonalization\RestrictImplicitInkCollection` + `RestrictImplicitTextCollection` = 1, `TrainedDataStore\HarvestContacts` = 0, `Personalization\Settings\AcceptedPrivacyPolicy` = 0. Stopt handschrift/typ-data verzameling
- **Disable Feedback Hub prompts** — `Siuf\Rules\NumberOfSIUFInPeriod` = 0. Geen feedback-popups
- **Disable CEIP** — HKLM `SQMClient\Windows\CEIPEnable` = 0. Schakelt het Customer Experience Improvement Program uit; de CEIP scheduled tasks blijven bestaan maar zijn inert. (Bewust geen scheduled-task disable — de TweakOperation-model is registry-only; CEIPEnable=0 dekt de telemetrie-zorg functioneel)
- **Disable Suggested Actions on clipboard** — HKCU `SmartActionPlatform\SmartClipboard\Disabled` = 1. Verwijdert de Win11 22H2+ pop-up bij kopiëren van telefoonnummers/datums
- **Disable clipboard cloud sync** — HKLM `System\AllowCrossDeviceClipboard` = 0. Clipboard verlaat de PC niet meer; lokale history blijft werken
- **Disable DiagTrack telemetry service** — HKLM `Services\DiagTrack\Start` = 4 (disabled; 2 = automatic default). De centrale Windows telemetrie-service start niet meer. Reboot-requirement
- **Disable WPBT** — HKLM `Session Manager\DisableWpbtExecution` = 1. Blokkeert OEM/firmware boot-binary injectie. Reboot-requirement

### v0.9.6 — AI / Copilot tweaks (Win11 24H2+)

**5 AI / Copilot tweaks** in de AiCopilot-categorie. Allemaal HKLM group-policy keys → `RequiresElevation=true`, batchen in 1 UAC-prompt. Research mei 2026 via Microsoft Learn Policy CSP (WindowsAI) + Manage Recall / Click to Do / Notepad docs.

- **Disable Recall** — `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis=1`. Schakelt de Copilot+ PC screenshot-snapshotting feature uit. SignOut-requirement. Alternates: HKCU user-policy + `AllowRecallEnablement=0` (de policy die Recall als optionele component blokkeert)
- **Disable Click to Do** — `WindowsAI\DisableClickToDo=1`. Verwijdert de 24H2+ AI-acties uit het right-click menu. SignOut-requirement. Alternate: HKCU user-policy
- **Disable AI features in Paint** — 3-op tweak: `CurrentVersion\Policies\Paint` met `DisableCocreator` + `DisableGenerativeFill` + `DisableImageCreator` = 1. Restart Paint om effect te zien
- **Disable AI features in Notepad** — `HKLM\SOFTWARE\Policies\WindowsNotepad\DisableAIFeatures=1` (app-level policy, let op: NIET onder `Policies\Microsoft\Windows`). Kill alle Copilot-features in Notepad. Restart Notepad
- **Disable generative AI access for apps** — `AppPrivacy\LetAppsAccessGenerativeAI=2` (Force Deny; 0=user-controlled). System-brede AppPrivacy policy die generatieve AI-toegang voor alle apps blokkeert — dekt o.a. Image Creator in de Photos-app. SignOut-requirement

**Bewust niet opgenomen** (research-onderbouwd):
- **Win+C Copilot hotkey** — `TurnOffWindowsCopilot` is door Microsoft gedeprecateerd en grotendeels inert op 24H2/25H2 (targett de legacy Copilot-pane). De bestaande "Hide Copilot button" (v0.9.2 Taskbar) dekt de zichtbare kant al
- **Copilot AppX removal** — hoort thuis in de Debloat-tab (AppX uninstall), niet bij de registry-toggles. `Microsoft.Copilot` is een normale Store-app, veilig te verwijderen via Debloat; system-packages (`MicrosoftWindows.Client.CoreAI` / `AIX`) zijn NIET veilig te verwijderen

**SignOut-melding in ResultBar**: post-apply InfoBar meldt nu expliciet "Sommige tweaks (zoals AI-policies) hebben pas effect nadat je uitlogt of de PC herstart" wanneer een toegepaste tweak `RestartRequirement.SignOut` of `Reboot` heeft — voorheen zei 'ie alleen "Done." waardoor user dacht dat er niets gebeurd was.

### v0.9.5 — Backup & Restore infrastructuur (Tweaks snapshots + System Restore Points)

User-feedback v0.9.4 → "Zijn alle changes wel veilig en stabiel voor het systeem?". v0.9.5 voegt twee complementaire veiligheidsnetten toe — registry-snapshots voor de lichte registry-mutaties (Tweaks) en Windows System Restore Points voor de zware delete-operaties (Deep Clean + Debloat).

**SnapshotService** (`Services/SnapshotService.cs`) — JSON-snapshots in `%LOCALAPPDATA%\WingetAppDeployer.WinUI\snapshots\`:
- `CaptureAsync(tweaks, description)` leest vóór elke Apply de actuele registry-state van alle ops die zouden worden geschreven (incl. `WasAbsent` markers) en parkeert die als JSON
- `RestoreAsync(snapshotId)` zet de exacte staat terug: schrijft `PreviousValue` waar mogelijk, deletet de value als die origineel absent was. Splitst lokale (HKCU non-policy) van elevated (HKLM + HKCU Policies) ops — elevated via 1 UAC reg.exe batch, zelfde patroon als TweakService.ApplyAsync
- Auto-prune op 20 snapshots; oudere worden silent verwijderd. Per snapshot: id (timestamp + guid-fragment), description (user-defined of auto-gegenereerd uit tweak-namen), createdAt, tweakIds, entries

**Tweaks tab integratie**:
- Nieuwe **"Vorige staat herstellen"** knop bovenaan (naast Apply/Discard/Restart Explorer) — alleen zichtbaar als er minstens 1 snapshot bestaat. Opent de SnapshotBrowserDialog die alle snapshots toont met description + timestamp + count, met per-snapshot **Herstel** + **Verwijder** knoppen. Confirm-dialog vóór de daadwerkelijke restore zodat een misklik geen schrijfacties triggert
- **BackupPromptDialog** vóór elke Apply (Nederlandstalig) — bevat tekstveld voor custom snapshot-naam ("Bijv. 'Voor Bing search uit'") + checkbox "Vraag dit niet meer en maak voortaan altijd backup" die de SettingsService-mode aanpast naar Always (bij Primary) of Never (bij Secondary)
- Apply-flow: respecteert de 3-state `BackupBeforeApplyMode` setting (Ask/Always/Never)

**RestorePointService** (`Services/RestorePointService.cs`) — wrapt `Checkpoint-Computer` + `Get-ComputerRestorePoint`:
- `CreateAsync(description)` runt Checkpoint-Computer in elevated PS (1 UAC), met RestorePointType=MODIFY_SETTINGS
- `GetStatusAsync()` returnt (canCreate, hoursSinceLast, blockedReason) — checkt System Protection state + 24h rate-limit
- `BuildInlineCheckpointScript(description)` is een static helper voor het embedden van de checkpoint-call in een externe elevated PS-batch (zodat 1 UAC dekt voor checkpoint + delete samen)

**Deep Clean integratie**:
- `DeepCleanService.DeleteAsync(items, restorePointDescription)` accepteert nu optionele description. Wanneer non-null prepend `RunElevatedBatchAsync` een `Checkpoint-Computer` call vóór de delete — 1 UAC, geen extra dialog
- `DeepCleanDialog` leest `Settings.RestorePointBeforeDeepClean` en stuurt description door naar DeleteAsync
- `DeepCleanPage` toont **first-run popup** (`RestorePointConfigDialog`) bij allereerste scan: vraagt eenmalig "ja restore point" / "nee skip" en zet `DeepCleanRestorePointConfigured=true`. Volgende keer geen popup — wijzigen kan via Settings

**Debloat integratie**:
- `BloatwareService.UninstallBatchAsync(..., restorePointDescription)` accepteert nu optionele description met dezelfde prepend-checkpoint logica
- `BloatwareUninstallDialog` propageert de description door
- `DebloatPage` doet dezelfde first-run flow als DeepCleanPage. RPdescription wordt bij bloatware-batch verbruikt; wanneer alleen apps-batch volgt (geen bloatware geselecteerd), aparte upfront `App.RestorePoint.CreateAsync` call

**Settings — nieuwe sectie "Backup & herstel"**:
- 3-state radio buttons voor `BackupBeforeApplyMode`: "Vraag elke keer (aanbevolen)" / "Altijd automatisch backup" / "Nooit backup maken"
- **"Bekijk snapshots..."** knop (met count) opent SnapshotBrowserDialog

**Settings — nieuwe sectie "System Restore Points"**:
- 2 toggles: "Restore point voor Deep Clean" + "Restore point voor Debloat". Toggle aanpassen markeert ook automatisch de Configured-flag zodat de first-run popup niet meer triggert
- ⚠ **Uitroepteken-glyph** naast de toggles wanneer:
  - System Protection uit staat op systeemschijf — tooltip wijst naar System Properties > System Protection
  - Laatste restore point < 24u geleden — tooltip legt uit dat Windows nieuwe punten skipt binnen 24u
- InfoBar onder de toggles voor de System Protection blokkering-melding

**SettingsService extensies** (`SettingsService.cs`):
- `BackupBeforeApplyMode` enum (Ask/Always/Never) + property (default Ask)
- `RestorePointBeforeDeepClean` + `DeepCleanRestorePointConfigured` bools
- `RestorePointBeforeDebloat` + `DebloatRestorePointConfigured` bools
- JSON-persisted naar `%LOCALAPPDATA%\WingetAppDeployer.WinUI\settings.json` zoals bestaande settings

### v0.9.4 — Ads & Tracking + Win11 24H2+ Policies-ACL fix + Partial-state UX

**5 Ads & Tracking tweaks** (category-rename van "Ads & Bloat" — "Bloat" was verwarrend i.c.m. de Debloat-tab; deze categorie gaat puur over marketing/tracking-toggles binnen Windows). Allemaal HKCU, geen UAC:
- **Mega-bundle "Disable all suggested & sponsored content"** — 18 HKCU keys in 1 toggle: lock-screen tips, Start menu suggesties, Settings-ads (3 ID's), Welcome experience, "Finish setting up" popup, auto-install OEM/Store apps, generic content delivery, Tailored Experiences, notification suggestions. Mirror van xM4ddy/OFGB. Partial-state in pill als user al subset handmatig had
- **Disable advertising ID** — `AdvertisingInfo\Enabled=0`
- **Hide File Explorer OneDrive ads** — `ShowSyncProviderNotifications=0`
- **Disable 'Finish setting up your device' popup** — `ScoobeSystemSettingEnabled=0`
- **Disable Tailored Experiences** — `TailoredExperiencesWithDiagnosticDataEnabled=0`

HKLM CloudContent policies geparkeerd voor latere iteratie (Pro/Edu only, vereisen UAC).

**Win11 24H2+ Policies-ACL fix**: `HKCU\Software\Policies\Microsoft\Windows\Explorer` is op 24H2+ ACL-hardened — user-token heeft ReadKey only, alleen `BUILTIN\Administrators` heeft FullControl. In-process `Microsoft.Win32.Registry` writes faalden met "Access to the registry key is denied". Surfaced doordat user de DisableBingSearch tweak probeerde te applyen → de diagnostic ResultBar liet `Disable web search in Start → Explorer\DisableSearchBoxSuggestions: Access to the registry key '...\Explorer' is denied. (20 writes wel succesvol)` zien. Fix: `RequiresElevation=true` op de policy-ops (HideRecommendedSection + DisableSearchBoxSuggestions). Routet via bestaande elevated reg.exe batch — admin-token passeert de ACL via de Administrators-ACE. Eén UAC-prompt per Apply-batch.

**IsThreeState CheckBox voor Partial-state UX**: pre-fix kon user een Partial-state tweak niet "completen" via de checkbox. Partial telde als IsToggleOn → checkbox visueel checked → klikken = unchecken = pending revert (niet apply) → Apply button bleef grayed-out. Nu: `IsThreeState=true` op CheckBox, Partial maps naar indeterminate vierkant. Klik op indeterminate → checked → pending=apply → Apply activeert. Cycle false→null→true→false; voor Disabled state vangt CheckBox_Toggled de null-intermediate op en springt door naar true zodat 1 klik = apply blijft (anders waren 2 klikken nodig om de cycle door te lopen).

### v0.9.3 — Start Menu category + collapsible categories + multi-path detection

**Multi-path detection** opgelost (kern-architectuur fix, niet in initiële scope): single-path detection miste de actual state als user de tweak via een ander mechanisme had geactiveerd. Voorbeelden uit user-feedback: "Disable web search in Start" en "Hide Recommended section" stonden bij de user al aan via Settings/manual regedit maar onze UI toonde 'm als off (checkbox ongekruist) — gevolg: user dacht dat-ie 'm nog moest applyen terwijl 'ie al actief was.

Architectuur:
- Nieuwe `TweakAlternateSignal` record (Path / ValueName / Kind / EnabledValue) — read-only signalen die ALLEEN voor state-detectie gebruikt worden. Schrijf-logica blijft single-path (alleen `TweakOperation.Path`)
- `TweakOperation` extended met `AlternateEnabledPaths` (default empty) en `AbsenceMeans` (default Disabled, voor zeldzame tweaks waar Windows-default exact onze EnabledValue is)
- `MatchOpState` in TweakService: walk eerst alle alternates — als ÉÉN matcht → return Enabled direct. Anders fall-through naar bestaande primary-comparison met `AbsenceMeans` ipv hardcoded Disabled. Alternates kunnen alleen voor Enabled stemmen, nooit voor Disabled (advisory reads, niet authoritative writes)
- Backward-compatible: bestaande 14 Explorer/Taskbar tweaks compileren ongewijzigd

Alternates gewired voor de problematische tweaks (research-driven, niet hardcoded):
- **HideRecommendedSection**: HKLM-policy (gpedit Pro/Ent), PolicyManager (MDM/Intune), Start_IrisRecommendations=0 (Win11 Home Settings-toggle die hetzelfde visuele effect heeft)
- **DisableBingSearch**: HKLM DisableSearchBoxSuggestions + ConnectedSearchUseWeb=0 (HKLM én HKCU policy). BingSearchEnabled bewust GEEN alternate — research wijst uit dat 'ie onbetrouwbaar werkt op 24H2+
- **HideMostUsedApps (Start_TrackProgs)**: ShowOrHideMostUsedApps=2 (alternative tweaker-key), NoInstrumentation policy
- **HideCopilot (ShowCopilotButton)**: HKCU + HKLM TurnOffWindowsCopilot=1 (vooral Enterprise/Education-honored op 24H2+)
- **HideWidgets (TaskbarDa)**: HKLM Dsh\AllowNewsAndInterests=0 + MDM PolicyManager equivalent

Bron-research (mei 2026 via web): Microsoft Learn policy-CSP docs, ElevenForum threads, Winaero / WindowsLatest writeups voor Win11 24H2/25H2-specifieke gedragsveranderingen — zie commit-log voor link-lijst.

**7 Start Menu tweaks** in TweakService.BuildAll():
- **Layout** (multi-choice ComboBox: Default / More pins / More recommendations) — mirror van Settings > Personalization > Start > Layout. Schrijft `Start_Layout` DWORD 0/1/2 onder Explorer\Advanced
- **Hide Recommended section** — policy `HKCU\Software\Policies\Microsoft\Windows\Explorer\HideRecommendedSection=1`. Verbergt de hele Recommended sectie onderaan Start (MRU + tips). Werkt op Win11 22H2+ ook op Home (policy-engine is decoupled van Pro-only UI)
- **Hide most-used apps** — `Start_TrackProgs=0`. Start toont alleen pinned items, geen automatische top-rij meer
- **Hide recently opened items** — `Start_TrackDocs=0`. MRU-files verdwijnen uit Recommended + uit taskbar/Start jump-lists
- **Disable web search in Start** — schrijft 3 HKCU keys in 1 toggle: `Policies\...\Explorer\DisableSearchBoxSuggestions=1` (modern policy) + `Search\BingSearchEnabled=0` (legacy, sub-builds 22H2/23H2) + `Search\CortanaConsent=0` (Win10 companion). Verschillende Win11 builds honoreren verschillende keys; alle drie schrijven dekt 22H2 t/m 25H2. Aggregate-detection toont Partial als user maar 1 van de 3 keys had gezet
- **Disable Search Highlights** — `Feeds\DSB\ShowDynamicContent=0` + `SearchSettings\IsDynamicSearchBoxEnabled=0` (legacy + modern key in 1 toggle). Verwijdert de roterende 'today in history' / Bing trending content
- **Disable Cortana** — HKLM `Policies\...\Windows Search\AllowCortana=0`. Vereist UAC. SignOut-requirement zodat user weet dat dit niet live live update

**Shell-host restart logica uitgebreid** in ApplyAsync + ApplyChoiceAsync:
- Was: `needsTaskbarRebind` — restart SearchHost.exe + StartMenuExperienceHost.exe alleen voor Taskbar-categorie of `\Search\` paden
- Nu: `needsShellHostRestart` — ook voor StartMenu-categorie én voor tweaks die het `\Policies\Microsoft\Windows\Explorer` pad raken (HideRecommendedSection / DisableSearchBoxSuggestions). Anders zou een Start-tweak een F5 in Settings nodig hebben om te tonen

**Categories collapsed by default** voor overzicht — was: per-categorie een TextBlock-header met cards eronder direct in beeld; nu: Expander per categorie, default `IsExpanded=false`. Header toont category-naam + "N/M active" count (Enabled + Partial geteld) zodat user direct ziet welke categorieën al deels actief zijn zonder open te klikken. `HorizontalContentAlignment=Stretch` op de Expander zodat cards de volle width pakken

**Diagnostic failure-messages** in `TweakApplyResult` + ResultBar — was: "1 change(s) failed" + "Check tweak state per item" (geen info wat). Nu: `TweakApplyResult.FailureMessages` lijst met per-op `<key>: <reason>` strings. InfoBar toont tot 4 regels met `<tweak.Name> → <key>: <reason>` formaat; bij meer failures: "…en N meer". Helpt diagnose wanneer een specifieke key op een edge-case build niet writable is (UCPD / ACL / type-mismatch / etc.)

**Disable web search in Start: multi-op write** — was: alleen `DisableSearchBoxSuggestions=1` (policy-key, niet consistent gehonoreerd op 24H2+). Nu: 3 HKCU ops in 1 toggle — Explorer-policy + `Search\BingSearchEnabled=0` + `Search\CortanaConsent=0`. Geen UAC (allemaal HKCU). Aggregate-detectie toont nu Partial als user maar 1 van de 3 keys eerder had gezet → zichtbaar dat de tweak alleen deels gedraaid is

**Diagnostic failure-messages** in `TweakApplyResult` + ResultBar — was: "1 change(s) failed" + "Check tweak state per item" (geen info wat). Nu: `TweakApplyResult.FailureMessages` lijst met per-op `<key>: <reason>` strings; InfoBar toont tot 4 regels met `<tweak.Name> → <key>: <reason>`. Bij meer failures: "...en N meer". Help diagnose wanneer een specifieke key op een edge-case build niet writable is (UCPD / ACL / type-mismatch / etc.)

### v0.9.2 — Taskbar category + Search ComboBox + checkbox-batch UX

**UX herontwerp na user-feedback** (per-toggle apply gaf race-condities + breekbaarheid op shell-cached tweaks zoals TaskbarAl). Nieuwe flow:
- **CheckBoxes ipv ToggleSwitches** voor toggle-tweaks; ComboBoxes blijven voor multi-choice (Search-mode)
- **"Apply (N)" knop top-right** — pending changes worden gestaged, niets schrijft tot user klikt. Disabled wanneer N=0, accent-styled met count erop
- **"Discard" knop top-right** (alleen zichtbaar als N>0) — reset alle pending changes terug naar systeem-state via re-detect
- **Apply All flow**: schrijft alle changes sequentieel (Choice via ApplyChoiceAsync, Toggle via ApplyAsync). Per tweak doet WM_SETTINGCHANGE broadcast + SearchHost-restart (voor taskbar). Aan einde één enkele `RestartExplorerSilent()` als minstens één pending tweak `Restart=ExplorerRestart` had. Daarna re-detect + cards rebuild + pending clear
- **`Process.Start("explorer.exe")` weggehaald uit `RestartExplorerSilent`** — opende een File Explorer venster (Documenten / Home). Windows Shell Watchdog respawnt explorer.exe binnen ~1s zonder window. Geen Process.Start fallback meer — als watchdog ooit faalt, user opent Task Manager → Run new task → explorer
- **Manual "Restart Explorer" knop** blijft als laatste escape

**Taskbar tweaks (7)** in TweakService.BuildAll():
- **Hide buttons**: Search box (multi-choice met 4 modes), Task View, Widgets, Copilot
- **Behavior**: End task in right-click menu (`TaskbarEndTask` onder `TaskbarDeveloperSettings` subkey), Never combine taskbar buttons (`TaskbarGlomLevel=2` + multi-monitor companion `MMTaskbarGlomLevel=2`)
- **Display**: Show seconds in tray clock (`ShowSecondsInSystemClock=1`)
- Verwijderd: Chat/MeetNow (deprecated in 23H2+), Battery % (`EstimatedTimeText` is Win10-only)

**Search-tweak als multi-choice ComboBox** (mirror Windows Settings > Personalization > Taskbar):
- 4 modes: Hide / Search icon only / Search box / Search icon and label
- Schrijft 4 keys per mode (legacy `\Search\SearchboxTaskbarMode` + `Cache` + nieuwe `\Advanced\ShowSearchBox` + `BingSearchEnabled`) zodat zowel oude als nieuwe Win11 taskbar code 't pickt
- Auto-restart SearchHost.exe + StartMenuExperienceHost.exe na de write (functioneel equivalent met wat Settings intern via private `TaskbarSettingsHelper` WinRT API doet — die API is niet redistributable, dus process-restart is de pragmatische aanpak die ook winutil / Winaero gebruiken)

**Multi-choice support in het model**:
- Nieuwe `TweakChoice` + `TweakChoiceValue` records — een tweak heeft óf `Operations` (toggle) óf `Choices` (ComboBox), nooit beide
- Tweak constructor variant voor choices, `IsChoice` discriminator, `SelectedChoiceIndex` (INPC) voor live binding
- `TweakService.ApplyChoiceAsync(tweak, choiceIndex)` parallel met bestaande `ApplyAsync(tweaks, apply)` — zelfde local/elevated split + broadcast
- State-detect zoekt eerste choice waar alle Values matchen — bij geen match SelectedChoiceIndex=-1 (custom state)
- TweaksPage rendert automatisch ComboBox voor `tweak.IsChoice` items, CheckBox voor de rest

**Lag fix (kritiek)**: WM_SETTINGCHANGE broadcast deed 5× synchroon `SendMessageTimeout` met 100ms = tot 500ms UI-thread blokkade per toggle. Nu fire-and-forget via `Task.Run(ShellRefresh.NotifySettingsChanged)` na registry-write + state-redetect

**ShellRefresh helper** (per Windows best-practice research):
- `null` lParam toegevoegd vóór de specifieke categorieën — legacy listeners filteren alleen op NULL
- `ShellState` lParam toegevoegd — voor hidden files / show extensions tweaks die nu correct live-refreshen
- `WindowsSearchSettingChanged` + `SearchSettingsChanged` lParams (nieuw in 24H2/25H2)
- `SMTO_NOTIMEOUTIFNOTHUNG` flag erbij — well-behaved windows krijgen onbeperkte tijd, alleen hung windows worden geskipped
- Nieuwe `NotifyAssociationsChanged()` helper (SHChangeNotify SHCNE_ASSOCCHANGED) — automatisch aangeroepen na tweaks die `\Classes\` paden raken (Classic context menu CLSID)
- `RestartSearchHost()` helper — kill SearchHost.exe + StartMenuExperienceHost.exe voor search + taskbar-rebind
- `RestartExplorerSilent()` helper — alleen kill, geen Process.Start fallback (anders File Explorer venster opent)

**Per-tweak RestartRequirement metadata** (None / ExplorerRestart / SignOut / Reboot) wordt gebruikt om te bepalen of de Apply-batch een single explorer-restart triggert aan het einde (zodat alleen wanneer een shell-cached tweak in de pending zit, de flicker plaatsvindt)

**Research bronnen** (mei 2026):
- Win11 22H2+ search-UI gebruikt nieuwe `\Advanced\ShowSearchBox` naast legacy `\Search\Mode` — beide schrijven nodig
- `SearchboxTaskbarModeCache` companion key — Windows valideert Mode tegen Cache bij login en reset Mode als ze niet matchen
- `BingSearchEnabled` als safety net — op 25H2 wordt SearchHost suspended als Bing uitstaat
- TaskbarAl + andere shell-cached tweaks vereisen op 25H2 een explorer-restart (Settings.exe doet 't via private WinRT API niet beschikbaar voor third-party); SearchHost-restart triggert Start menu re-read maar NIET taskbar re-render
- Microsoft heeft Copilot in 25H2 omgebouwd naar pinned Appx app — `ShowCopilotButton` is daar no-op

### v0.9.1 — Tweaks tab foundation + Explorer category

- **Architectuur**: nieuwe `Models/Tweak.cs` met `TweakCategory` enum (12 buckets — Explorer / Taskbar / StartMenu / AdsBloat / AiCopilot / Privacy / UiTheme / Performance / ContextMenu / NotificationsLock / Updates / Gaming), `TweakOperation` record met `EnabledValue` + `DisabledValue` voor full reversibility, `RestartRequirement` enum (None/ExplorerRestart/SignOut/Reboot), `TweakState` enum (Disabled/Enabled/Partial/Unknown), `Tweak` class met INPC voor state-binding. Multi-op support: één tweak kan meerdere registry-ops bundelen (bv OFGB later met 22 keys). Special `DeleteKeyOnAbsent` flag voor tweaks die een hele key-tree aanmaken bij apply (classic context menu CLSID-subkey)
- **`Services/TweakService.cs`**: data-driven `BuildAll()` registreert alle tweak-definities (geen per-tweak UI code). `DetectStatesAsync` walkt elke op's registry-pad en zet `Tweak.State` op Enabled/Disabled/Partial/Unknown door actual vs EnabledValue/DisabledValue te vergelijken. `ApplyAsync(tweaks, apply: bool)` splitst user-ops (HKCU, in-process via Microsoft.Win32.Registry) van elevated-ops (HKLM, 1 UAC voor de hele subset via PowerShell + reg.exe batch — zelfde patroon als DeepCleanService). `RestartExplorerAsync()` static helper voor de "Restart Explorer?" dialog
- **`TweaksPage` rebuild**: was placeholder InfoBar, nu volledige page met per-tweak Border-cards gegroepeerd per categorie. Per card: naam + status-pill (Active/Default/Partial/Unknown met groen/grijs/geel/grijs achtergrond) + omschrijving + use-case (italic), rechts admin/UAC lock-icoon (alleen bij HKLM-tweaks), uiterst rechts `ToggleSwitch`. Loading-overlay met spinner tijdens initial registry-walk. Toggle-event triggert immediate apply/revert + InfoBar-feedback ("Restart Explorer to see the effect" voor explorer-restart tweaks) + optionele "Restart Explorer?" dialog
- **5 Explorer-tweaks geregistreerd**: show file extensions (`HideFileExt`), show hidden files (`Hidden`), taskbar aligned left (`TaskbarAl`), launch File Explorer to This PC (`LaunchTo`), classic context menu (CLSID `{86ca1aa0-…}\InprocServer32` key-add/delete via DeleteKeyOnAbsent)
- **`App.Tweaks` singleton** toegevoegd
- **Geen restart-icoon per card** — initial draft had Refresh/SignOut/Power glyphs naast elke ToggleSwitch, weggehaald omdat 't verwarrend was. Restart-info komt via de InfoBar-message direct na toggle (contextueel duidelijker). Admin/Lock icoon blijft wel voor HKLM-tweaks omdat dat aankondigt dat UAC gaat triggeren (niet-evident anders)

### v0.8.11 — Diagnostic logs gated + multi-badge + empty-state + auto-refresh + dialog search

- **Diagnostic logs uit voor productie**: nieuwe `Helpers/Diagnostics.cs` met `Enabled` static-readonly bool (false in productie). Alle persistent diagnostic logfiles (`WingetAppDeployer_deepclean.log` / `_leftovers.log` / `_debloat.log` / `_toast.log`) lopen nu via `Diagnostics.Log(fileName, msg)` — no-op wanneer Enabled=false. Geen rommel meer in `%TEMP%` op user-systemen. Voor dev: flip de readonly naar true om de full per-scan trace weer aan te zetten. Load-bearing IPC logs (timestamped per-batch elevated PS-batches voor delete-progress + `_schtasks.log` voor schtasks stderr capture) lopen NIET via deze gate — die hebben hun eigen lifecycle en zijn nodig voor de UI om progress te tonen
- **Multi-badge op gemixte bundles** in `DeepCleanDialog.BuildBundleCard`: voorheen toonde een bundle alleen de category-badge van het eerste item + een generieke "N folders" count-badge. Voor een gemixte bundle (bv. folder + 2× HKCU vendor van dezelfde app) zag user dus alleen "Orphaned folder" als badge, de HKCU-items verborgen in de Expander. Nu: één badge per unieke category in de bundle, met `×N` count-suffix wanneer er meer dan 1 item per category is. Voorbeeld: bundle "Brave" toont nu naast elkaar `Orphaned folder` + `HKCU vendor ×2`. Replacement van de oude dubbele badge (category + folder-count), netto cleaner
- **Empty-state UI op DeepCleanPage**: nieuw `EmptyStatePanel` border met groen `&#xE73E;` checkmark-icon + heading + uitleg. Wordt zichtbaar wanneer een scan 0 items oplevert, i.p.v. de "nothing to clean" InfoBar. Voelt meer als positieve feedback ("looking clean!") dan als een dichtklapbare success-bar
- **Auto-refresh na delete**: nieuwe `isAutoRefresh` parameter op `RunScanAsync`. Na een succesvolle delete-batch (`SuccessCount > 0`) triggert de page automatisch dezelfde scan-kind opnieuw als verify-pass. Drie outcomes: (1) 0 items remaining → empty-state met "cleanup verified" heading, success-InfoBar van de vorige delete blijft staan, (2) items remaining → warning-InfoBar "N items still present after cleanup", geen automatische tweede dialog (anders wordt het opdringerig na een failed delete), (3) auto-refresh roept geen nieuwe auto-refresh aan (geen loop)
- **Search box in DeepCleanDialog**: nieuwe `AutoSuggestBox` bovenaan de dialog (boven de items-lijst). Filtert bundles + items op DisplayName, Path én CategoryLabel — case-insensitive substring + genormaliseerde fallback (alfanum-only) zodat "bravesoftware" ook matcht met `BraveSoftware · Promo` (de `·` separator wegfilteren via Normalize). Bij actieve filter zonder matches toont de lijst "Geen items matchen \"...\"". Selecties op out-of-filter items blijven gerespecteerd — IsSelected zit op het DeepCleanItem model dus persist tussen filter-changes. Use case: user heeft 143 items in de dialog (8+ categorieën, veel orphaned folders), zoekt "brave" → ziet direct alleen de Brave-cluster met folder + 2 HKCU vendor entries gegroepeerd

### v0.8.10 — Orphan services + HKCU vendor-residue

- Twee high-risk leftover-types toegevoegd aan deep clean, beide met strikt filter zodat we geen system-state per ongeluk raken
- **Orphan services**: nieuwe `OrphanedService` `DeepCleanCategory` + `DeepCleanService.ScanOrphanedServicesAsync`. Roept `Get-CimInstance Win32_Service` aan via base64-encoded PS-script, parst pipe-separated `Name|DisplayName|State|StartMode|PathName` lines. Strikt 6-criterium filter: (a) ImagePath dood (na `Environment.ExpandEnvironmentVariables` + `ExtractExePathFromCommandLine`), (b) State=Stopped, (c) StartMode=Manual of Disabled (geen Auto/Boot/System), (d) PathName start niet met svchost.exe (DLL-hosted services), (e) ImagePath niet onder `%SystemRoot%` of `C:\Windows\`, (f) geen overlap met winget/AppX token cross-set. Service-name (NIET DisplayName) opgeslagen in `RegistryValueName` voor sc.exe-delete dispatch. Default unchecked + caution-tier
- **HKCU vendor**: nieuwe `OrphanedHkcuVendor` category + `DeepCleanService.ScanOrphanedHkcuVendorAsync`. Walk `HKCU\Software\<Vendor>\<App>` (top 2 levels). Per app-key zoek pad-values: value-name in vaste set (`InstallPath`, `InstallDir`, `InstallLocation`, `Path`, `Program`, `ProgramPath`, `ExecutablePath`, `Executable`, `Exe`, `ExePath`, `AppPath`, `InstallationDirectory`, `Location`, `AppDir`, `BinPath`, `BinaryPath`, `RootDir`, `WorkingDirectory`) ÓF value-data lijkt op drive-rooted path (`X:\...` of `%EnvVar%\...`). Orphan-flag alleen als ≥1 pad-value gevonden EN alle dood EN geen winget/AppX cross-match. Protected top-level vendor-keys altijd skipped: Microsoft, Classes, Policies, Wow6432Node, RegisteredApplications, Clients, driver-vendors (Intel/AMD/NVIDIA/Realtek), Google, Mozilla (eigen uninstallers). IsSafe=false, default unchecked
- `DeepCleanService.DeleteAsync` uitgebreid: services altijd in elevated batch (sc.exe delete vereist admin), HKCU vendor-keys via in-process `DeleteSubKeyTree` (HKCU = user-context, geen UAC nodig). Bestaande `DeleteRegistryKey` werkt al voor HKCU dus extra category gewoon toegevoegd aan de switch
- `RunElevatedBatchAsync` PS-batch: nieuwe branch voor `OrphanedService` met `& sc.exe delete '<name>' | Out-Null` + `$LASTEXITCODE` check + RESULT-line log
- `DeepCleanPage.RunScanAsync` Leftovers-tak uitgebreid van 8 naar 10 parallel scans (services + HKCU vendor toegevoegd). Per-categorie breakdown counts in `CleanupResultBar` (`{n} services`, `{n} HKCU vendor keys`). XAML-beschrijving op de "Scan leftovers" card vermeldt nu beide types expliciet zodat user weet wat de knop doet
- `DeepCleanDialog.PathLink_Click`: `OrphanedService` → `services.msc` (Services console — niet rechtstreeks navigeerbaar naar één service, alleen open), `OrphanedHkcuVendor` → regedit op de HKCU-key via bestaande `OpenInRegedit` helper
- `DeepCleanService.GetOrphanedScanLocations()` voegt vier soft-categorie labels toe (registry+App Paths+MUIcache+class handlers / shortcuts / scheduled tasks+firewall / services+HKCU) zodat de dialog scan-locations-tekst dekkend is

### v0.1.0 — Sandbox foundation
- WinUI 3 project + solution-koppeling, unpackaged exe (`WindowsPackageType=None`)
- MainWindow met native Fluent TitleBar, content stack, ToggleSwitch, Accent button, SymbolIcon, InfoBar
- DesktopAcrylicController met `DesktopAcrylicKind.Thin` via WindowsSystemDispatcherQueueHelper
- Build + run verified op .NET 10

### v0.2.0 — NavigationView shell
- Sidebar Apps / Tweaks / Debloat / Settings (WinUI 3 Gallery patroon)
- Drie lege Pages als routing targets
- Settings via `FooterMenuItems`
- AppTitleBar drag regions afgestemd

### v0.3.0 — Apps pagina met categorieën
- Categorie-data uit `apps.json` ingelezen
- Settings-style category rows (i.p.v. kleurige tile grid)
- Click → detail page met app list + selectie

### v0.3.1 — MicaBackdrop polish
- MicaBackdrop BaseAlt + simplified backdrop code

### v0.4.0 — Install flow + winget
- WingetService geport
- InstallDialog met Fluent ProgressBar + log
- ScheduleDialog voor auto-update

### v0.4.1 — Install UX polish + installed-state
- InstallDialog redesign (overall progress bar weg, per-app indeterminate bar, live winget stdout)
- DispatcherQueue-marshalling voor `Progress<T>` callbacks
- Expliciete `Visibility`-typed binding properties
- `WingetService.GetInstalledAppIdsAsync()` parseert `winget list`, gecachet
- "Installed" badge in CategoryDetailPage

### v0.4.2 — Stage progress per app
- Pending: kleine ProgressRing (20px, spinning)
- Installing zonder %: indeterminate bar; met %: determinate bar uit "X MB / Y MB" parser
- Card-based layout, theme-aware success/error brushes

### v0.4.3 — Quick uninstall onder Debloat (tijdelijk)
- Lijst van geïnstalleerde catalogus-apps onder Debloat tab
- Per app uninstall-button met bevestigings-dialog
- `WingetService.UninstallAppAsync()` via `winget uninstall --silent`
- Wordt in v0.8.0 vervangen door full Debloat-pagina

### v0.4.4 — Stage ring + Fluent polish
- 4-stage ProgressRing rechts per app: Downloading → Verifying → Installing → Done
- Indeterminate bar altijd zichtbaar tijdens install (= "er gebeurt iets")
- Char-per-char stream reader die ook op `\r` splitst (winget gebruikt carriage returns voor live progress)
- ContentDialog sizing via resource keys + afgeronde hoeken via OverlayCornerRadius

### v0.4.5 — Schedule dialog
- TaskSchedulerService geport (WinUI-specifieke taaknaam)
- `WingetService.UpdateAllAppsAsync()` via `winget upgrade --all --silent`
- `/autoupdate` command-line handler in App.xaml.cs (runt update + Environment.Exit zonder window)
- ScheduleDialog met Daily/Weekly/OnStartup + TimePicker
- SettingsPage status-card met Set up / Change / Disable

### v0.5.1 — Catalog search + globale selectie footer
- AutoSuggestBox in AppsPage (filter categorie-grid) en CategoryDetailPage (filter app-lijst)
- Match op categorie-naam + app-naam + beschrijving + winget ID. Category-match slaat ook aan wanneer een app in die category matcht
- `SelectionHelper` service voor cross-category selectie (count / clear / collect)
- Footer met "X apps selected" + Clear all + Install knop op AppsPage én CategoryDetailPage, beide tonen globale count
- Install pakt alle selected apps over álle categorieën

### v0.5.2 — Winget-repo search + klikbare cards
- `WingetService.SearchWingetRepoAsync` parst `winget search` output, debounced 300ms met epoch-check
- "Results from winget repository" sectie op AppsPage onder catalog-results
- `SelectionHelper.ExtraSelectedApps` voor synthetische winget-search apps (tellen mee in footer + install)
- Search-modus: categorie-grid weg, platte "In your curated list" + winget secties
- Klik op sidebar Apps reset naar root (clear search, leave detail page) via `ItemInvoked`
- Hele app-card klikbaar (`Tag="{x:Bind}"`, CheckBox `IsHitTestVisible=False`)
- Hover-effect via `PointerEntered/Exited` → `CardBackgroundFillColorSecondaryBrush`
- Padding rechts van scrollbar zodat cards niet aan de rail plakken

### v0.5.3 — Fuzzy search
- FuzzySharp 2.0.2 NuGet
- `Helpers/FuzzyMatcher.cs` met `WeightedRatio` (token-set + partial + full combi), threshold 55/100
- Exacte case-insensitive substring shortcut naar score 100
- Resultaten gesort op score DESC, naam als tie-breaker
- Toegepast op AppsPage catalog-results en CategoryDetailPage filter (winget-repo blijft ongefuzzyt)

### v0.5.5 — Local-only apps.json
- Distributie-model: private repo + public GitHub Releases
- `data/apps.json` wordt gebundeld met de exe (csproj `<Content>` + PreserveNewest)
- AppDatabaseService gestript van HttpClient + remote URL — leest alleen bundled file
- Werkt offline, geen auth-tokens, geen GHSAT-tokens die vervallen

### v0.5.6 — Subcategorie grouping
- `SubcategoryGroup` model (UI-only) met Name + Apps + HasName Visibility
- Nested ItemsRepeater (outer = groepen, inner = apps per groep)
- Categorieën zonder subcats krijgen één lege-name groep zonder header
- Search filtert per groep, lege groepen na filter onzichtbaar
- Select all werkt op zichtbare apps in álle visible groepen

### v0.5.7 — INotifyPropertyChanged op App
- INPC op `IsSelected` en `IsInstalled` met `[CallerMemberName]` helper
- Verwijderd alle `ItemsSource = null; ItemsSource = ...` rebind hacks — TwoWay x:Bind reageert nu automatisch
- Sneller (geen full card-rebuild per click), geen valse hover-events op buren

### v0.5.8 — Modern ScrollView + 20ms scroll
- `ScrollView` (modern WinUI 3) i.p.v. `ScrollViewer` (legacy UWP-erfgenaam) → werkt via Parsec en andere remote desktop tools
- `Helpers/ScrollViewSpeedup.cs` zet scroll-animation duration op 20ms (default ~350ms voelt traag)
- Toegepast op CategoryDetailPage, AppsPage, DebloatPage, SettingsPage

### v0.5.10 — Strictere fuzzy search
- `FuzzyMatcher` vervangt `WeightedRatio` door substring → prefix(90) → `PartialRatio` ladder. WeightedRatio's token_set matchte anagrammen ("steam" ↔ "teams" / "signal" / "keepass")
- `MinScore` 55 → 75
- `Description` niet meer mee-gescoord op AppsPage en CategoryDetailPage — alleen naam + winget ID

### v0.5.11 — Filter opties op CategoryDetailPage
- `ComboBox` met All / Popular / Installed naast de SearchBox
- Filter-mode chained mét fuzzy search: eerst mode-filter (`Popular` → `App.Popular`, `Installed` → `App.IsInstalled`), dan optioneel fuzzy zoekquery
- Lege subcat-headers verdwijnen zodra een filter actief is (geen kale section-headers meer)
- Mode-aware "no results" message: "No popular apps in this category" / "No installed apps in this category matching 'X'"
- Installed-filter refresht automatisch wanneer `winget list` async binnenkomt
- `_uiReady` guard voorkomt dat `SelectionChanged` (vuurt al tijdens XAML-parse via `IsSelected="True"`) een redundante render-cycle triggert vóór `OnNavigatedTo` de UI heeft opgezet

### v0.8.6 — Deep clean (los van uninstall)

- Nieuwe `Models/DeepCleanItem` met `DeepCleanCategory` enum (UserTemp / SystemTemp / UpdateCache / Prefetch / RecycleBin / WindowsOld / BrowserCache / OrphanedFolder). Properties: DisplayName, Path, SizeBytes, LastModified (alleen voor orphaned folders), RequiresElevation, IsSafe (true voor Temp / Recycle / Update / Prefetch — default checked, content > 0; false voor caution categorieën — default unchecked). UI helpers voor category-badge, size-label, last-modified-label
- Nieuwe `Services/DeepCleanService` met twee scan-flows: `ScanWindowsCachesAsync` (predefined paden — Temp folders, Update cache, Prefetch, Windows.old, browser caches voor Edge / Chrome / Brave + alle Firefox profiles, Recycle Bin via shell) parallel met size-walk per target, en `ScanOrphanedFoldersAsync` die `InstalledAppsService.DetectAllAsync` gebruikt om een normalized name-set te bouwen en folders in Program Files / Program Files (x86) / %LOCALAPPDATA% / %APPDATA% / %PROGRAMDATA% flag die NIET matchen met enige installed app. Brede protected-list (Microsoft / Windows / WindowsApps / Common Files / Packages / Temp / etc.) voorkomt dat we vendor-of-system folders als orphan voorstellen
- `DeepCleanService.DeleteAsync` splitst in user-context (HKCU AppData, user temp, browser caches) en elevated batch (system temp / Update cache / Windows.old / Program Files / ProgramData / Recycle Bin via `Clear-RecycleBin`). Eén UAC prompt voor de hele admin-required subset. Cache-folders krijgen een "clear contents" delete (folder zelf blijft staan want Windows / browsers maken 'm opnieuw aan), orphaned folders krijgen een volledige `Remove-Item -Recurse -Force` delete. Bytes-freed wordt geretourneerd zodat de UI "X MB freed" kan tonen
- Nieuwe `Dialogs/DeepCleanDialog` mirror van LeftoverCleanupDialog met preview + delete fases, items gegroepeerd op IsSafe-tier (Safe to clean → Caution — review carefully), per item: checkbox + naam + category badge + path + omschrijving + size + admin-marker + last-modified (alleen orphaned). Footer toont totaal-vrij-te-maken-ruimte zodra user iets aanvinkt. Caution-tier items hebben gele rand, safe-tier groene rand
- Sidebar-restructure: NavigationView "Debloat" wordt nu een parent met `SelectsOnInvoked="False"` + `IsExpanded="True"` en twee sub-items: **Apps** (de bestaande MS / OEM / All apps flows op DebloatPage) en **Deep clean** (nieuwe DeepCleanPage). Klik op de parent klapt de groep in/uit zonder zelf te navigeren — sub-items hebben hun eigen Tag → page mapping in MainWindow.NavView_SelectionChanged. Deep clean kreeg een eigen pagina i.p.v. een in-page Expander zodat de pagina-titel + InfoBar + scan-cards eigen ruimte krijgen
- `App.DeepClean` singleton toegevoegd
- Diagnostic log per scan in `%TEMP%\\WingetAppDeployer_deepclean.log` met per-target size-trace + per-folder match-decision

### v0.8.5 — Restant-opruiming direct na uninstall

- Nieuwe `Models/LeftoverItem` met `LeftoverType` enum (`RegistryKey` / `ProgramFilesFolder` / `AppDataFolder`) en `LeftoverConfidence` (`High` / `Medium` / `Low`). Confidence bepaalt of het item default aangevinkt staat in de cleanup-dialog: high = checked (exact-match), medium/low = unchecked. Properties: Path, SourceAppName, SizeBytes (lazy folder-walk), RequiresElevation. UI helpers voor type-badge + size-label
- Nieuwe `Models/UninstalledAppRef` record (DisplayName + Publisher + PackageName + WingetId) als input voor de scanner. Lichtgewicht alternatief voor het meegeven van zware UI-models — scanner heeft genoeg aan deze 4 velden om matches te vinden
- Nieuwe `Services/LeftoverScannerService` scant drie locatie-types parallel: (1) registry uninstall keys (HKLM 64-bit + WOW6432Node + HKCU) — match op DisplayName + Publisher, (2) Program Files / Program Files (x86) folder-namen, (3) `%LOCALAPPDATA%` / `%APPDATA%` / `%PROGRAMDATA%` folders. Match-tier-systeem: exact-na-normalisatie = high, substring-bidirectional = medium (skipt korte namen om vendor-collisions zoals "MS"/"HP" te voorkomen), publisher-only = low. Protected-list voor AppData-folders die we nooit voorstellen (`Microsoft`, `Windows`, `Packages`, `Temp`, `WindowsApps`, `INetCache` etc.) zodat een Microsoft-bloatware uninstall niet de hele `%LOCALAPPDATA%\Microsoft` map suggereert. Diagnostic log per scan in `%TEMP%\\WingetAppDeployer_leftovers.log` met per-item match-trace voor debugging
- `LeftoverScannerService.DeleteAsync` splitst per RequiresElevation: HKCU + AppData (user) gaan in-process via `Registry.DeleteSubKeyTree` / `Directory.Delete`; HKLM + Program Files + ProgramData gaan in één elevated PS-batch met `reg.exe delete /f` of `Remove-Item -Recurse -Force`. Eén UAC prompt voor de hele admin-required subset, log-tail-pattern voor result-parsing zoals BloatwareService / MixedSourceUninstaller
- Nieuwe `Dialogs/LeftoverCleanupDialog` met preview-fase + delete-fase. Preview groepeert items per LeftoverType (Registry → Program Files → AppData), per item: checkbox + path + size + confidence-label + "from <app>" badge + admin-marker. Confidence-tier kleurt de border subtiel (high = success-green, medium/low = neutral). "Select all" toggle + selection-status footer ("X selected · Y need administrator rights"). Delete-fase swap UI naar progress-bar + status-tekst, na voltooiing wordt Primary een Close-knop met summary. **Always preview, never auto-delete** — secondary "Skip" sluit zonder iets te verwijderen
- `SettingsService.ScanLeftoversAfterUninstall` (default true) + nieuwe **"Uninstall"** sectie op SettingsPage met ToggleSwitch. Wanneer false: na uninstall geen scan, geen dialog — user kan handmatig nog v0.8.6 deep-clean draaien
- DebloatPage: `ConfirmAndRemoveBloatwareAsync` (Microsoft + OEM bloatware) en `InstalledUninstallButton_Click` (unified all-apps) triggeren na succesvolle uninstall een `RunLeftoverScanAsync` op de **SuccessfulItems** uit de dialog (nieuwe property op zowel BloatwareUninstallDialog als AllAppsUninstallDialog). Failed/cancelled items hebben hun sporen nog gewoon op disk staan en zijn dus geen leftover — alleen apps die echt weg zijn voeren in de scan. UninstalledAppRef-bouwer voor InstalledAppEntry mapt source-afhankelijk: Store krijgt PackageName uit het eerste segment van PackageFullName, Winget krijgt WingetId, Web heeft alleen DisplayName + Publisher als hint
- `App.LeftoverScanner` singleton toegevoegd

### v0.8.4 — Unified all-installed-apps sectie

- Nieuwe `Models/InstalledAppEntry` met `InstalledSource` enum (`Winget` / `Store` / `Web`). Properties: DisplayName, Identifier (winget ID / PackageFullName / registry key path), Publisher, Version, IsSelected (INPC), `IsSystemComponent` flag, source-aware UI helpers (badge text + brush + tooltip, IconVisibility, GenericIconVisibility, SystemBadgeVisibility, Subtitle). Voor Winget-apps die ook in apps.json staan houden we een referentie naar de App-instance zodat we het bundled icon kunnen tonen; voor Store/Web (en Winget zonder catalog match) tonen we een generieke OEM-icon glyph. Source-namen: `Winget` = winget kan de app managen (Source-kolom uit `winget list`), `Store` = Microsoft Store / AppX, `Web` = vendor-installer download (MSI/EXE niet bekend bij winget of Store)
- Nieuwe `Services/InstalledAppsService` detecteert uit drie bronnen parallel via `Task.WhenAll`: (1) `winget list` (deelt cache met `WingetService` — één gezamenlijke call i.p.v. twee), gefilterd op `Source=winget` óf catalog-match — entries met `Source=msstore` / leeg vallen door naar AppX/Registry detectie, (2) `Get-AppxPackage` voor Microsoft Store / AppX met framework + resource packages eruit gefilterd, system-AppX (`SignatureKind=System`) wordt mét `IsSystemComponent` flag bewaard zodat user ze achter de "Show system components" checkbox kan tonen, (3) registry uninstall keys (`HKLM\\SOFTWARE\\...\\Uninstall` 64-bit + 32-bit `WOW6432Node` + `HKCU` equivalent), filtert Windows Updates / hotfixes / SystemComponent eruit. Cross-source dedup op DisplayName met prioriteit Winget > Store > Web. Diagnostic log per refresh in `%TEMP%\\WingetAppDeployer_debloat.log` met per-bron count + duration zodat detectie-issues debugbaar zijn
- `Services/BloatwareService` omgebouwd naar **detection-driven** i.p.v. hardcoded curated lijst — gebruikt dezelfde `Get-AppxPackage` call en classificeert via vendor-patronen (Microsoft.* + MicrosoftCorporationII.* + MSTeams voor Microsoft, publisher CN= patterns + PFN-prefixes voor HP/Dell/Lenovo/ASUS/Acer/MSI). Curated `BloatwareItem.CuratedMetadata` dict (~50 entries) verrijkt bekende packages met friendly DisplayName + Description; onbekende krijgen de raw package-name. Voorkomt dat we hardcoded bloatware lists moeten onderhouden ("anders kan je aan de gang blijven")
- Nieuwe `Services/MixedSourceUninstaller` runt een gemengde batch met per-source dispatch: Winget-items via bestaande `WingetService.UninstallAppAsync` sequentieel (geen UAC), Store + Web items in één gecombineerde elevated PS-batch zodat user maar één UAC prompt ziet voor alle admin-required items. `MsiExec /X{GUID}` patronen krijgen `/quiet /norestart` toegevoegd zodat MSI-uninstalls silent lopen — non-MSI installers vertrouwen op hun eigen `QuietUninstallString` (anders krijg je gewoon de installer's UI, dezelfde caveat als bij v0.8.1)
- Nieuwe `Dialogs/AllAppsUninstallDialog` consumeert `MixedUninstallProgress` events met source-badge per card, gecombineerde "X of Y done" header, UAC-hint die alleen verschijnt als de batch ook elevated items bevat (pure Winget-batches niet). Cancelled-state bij UAC denial (zelfde patroon als v0.8.2 BloatwareUninstallDialog)
- DebloatPage: v0.8.1 catalog-sectie **vervangen** door unified "All installed apps" sectie. Microsoft + OEM bloatware secties blijven als curated quick-actions. Nieuwe sectie heeft `AutoSuggestBox` voor fuzzy search (op DisplayName + Publisher + winget ID voor Winget-source), `ComboBox` filter (All sources / Winget / Store / Web / **System**) en "Show system components" checkbox. **Sortering**: zonder search-query gegroepeerd op source (Winget eerst → Store → Web) en alfabetisch binnen elke groep — echte managed apps bovenaan vóór de Store/Web bagger. System-filter toont enkel system components ongeacht checkbox. Triple dedup tussen bloatware-secties en unified lijst (PackageFullName + AppX Name uit FullName + DisplayName) zodat een Store-app niet in Microsoft-bloatware én All-apps verschijnt. Counts in header reflecteren huidige zichtbaarheid (system-toggle + filter aware)
- `WingetService` shared cache: één `_appsListCache` + `_installedIdsCache` achter `SemaphoreSlim` zodat de Source-kolom uit `winget list` één keer geparset wordt en zowel `GetInstalledAppIdsAsync` als `GetInstalledAppsListAsync` ervan kunnen lenen. ParseListOutput skipt `ARP\\` / `MSIX\\` prefix entries (laat door naar Registry/AppX detectie), ParseSimpleIds fallback voor non-English Windows headers
- `App.InstalledApps` + `App.MixedUninstaller` singletons toegevoegd. Bloatware-uninstall en unified-uninstall refreshen elkaar's lijsten — Store-apps die in beide voorkomen blijven consistent

### v0.8.3 — OEM bloatware sectie + counts in section-headers

- Nieuwe `BloatwareVendor` enum (`Microsoft` / `Oem`) op `BloatwareItem`. Bestaande Microsoft-items behouden hun gedrag, nieuwe OEM curated lijst (~17 items) toegevoegd voor HP / Dell / Lenovo / ASUS / Acer / MSI bundleware. Multi-package items per vendor (varianten van dezelfde app onder verschillende publisher-prefixes — bv. HP Smart als `AD2F1837.HPSmart` of `HPInc.HPSmart` afhankelijk van Win11-versie) onder één checkbox. Helper `BloatwareItem.CuratedFor(vendor)` filtert de unified curated bron per sectie
- Nieuwe **OEM bloatware** sectie op DebloatPage tussen Microsoft en Catalog. Volledig collapsed wanneer geen OEM-items gedetecteerd — meeste users hebben geen HP/Dell/Lenovo AppX-bloat en zouden anders alleen een lege sectie zien. Detectie loopt in dezelfde `Get-AppxPackage` call als Microsoft (niet 2× duren) — Microsoft-prefixes (`Microsoft.*`) en OEM-prefixes (`HPInc.*` / `DellInc.*` / `LenovoCorporation.*` / `AsusTekComputerInc.*` / `AcerInc.*` / `MSI.*`) zijn disjunct dus geen kruisverontreiniging
- **Counts in section-headers**: alle drie de secties (Microsoft / OEM / Catalog) tonen nu een grijze count naast de titel — `"Microsoft bloatware (5)"` / `"OEM bloatware (2)"` / `"Installed apps from this catalog (12)"`. Lege count = geen tekst zodat het niet als "(0)" een lege sectie suggereert
- Refactor van DebloatPage card-template: Microsoft + OEM secties delen nu dezelfde `BloatwareCardTemplate` resource zodat we 'm niet hoeven te dupliceren. `BloatwareCard_Tapped` is shared en routeert naar de juiste selection-update via `BloatwareItem.Vendor`. `ConfirmAndRemoveBloatwareAsync(items, vendorLabel)` is gedeeld tussen Microsoft + OEM Remove buttons — zelfde confirm dialog + UninstallDialog flow, alleen de title-tekst varieert
- Windows11-Unattended-Debloat integratie (registry-tweaks / scripts) **niet** in v0.8.3 — past logischer bij v0.9.x Tweaks tab en zou hier alleen scope-creep zijn

### v0.8.2 — Microsoft bloatware section op DebloatPage

- Nieuwe `Models/BloatwareItem.cs` met INPC + curated lijst van ~22 Microsoft AppX bloatware items (Solitaire, Xbox suite, Skype, Teams consumer, Mail/Calendar, Bing News/Weather, Cortana, Mixed Reality, 3D Viewer, Paint 3D, Get Help, Tips, Feedback Hub, Office Hub, Maps, OneNote, Groove Music, Movies & TV, Sticky Notes, Phone Link, People). Per item: DisplayName, Description (waarom is dit bloat / wanneer is het misschien wel handig), Category, en lijst PackageNames om tegen `Get-AppxPackage` te matchen. Multi-package items (Xbox = 7 packages) onder één checkbox zodat de hele suite in één klik weg kan
- Nieuwe `Services/BloatwareService.cs`. `DetectInstalledAsync` runt `Get-AppxPackage` (normale user, base64-encoded PS-script om escaping issues te omzeilen) en parst Name + PackageFullName per regel pipe-separated. Vult `BloatwareItem.IsInstalled` + `InstalledPackageFullNames` zodat de UI weet wat te tonen en de uninstall later weet welke FullName te targeten. `UninstallBatchAsync` schrijft een PowerShell-script naar `%TEMP%` dat per item `Remove-AppxPackage` runt en per actie een marker (PROGRESS / RESULT) naar een logfile schrijft. Run via `Process.Start(verb="runas")` zodat één UAC prompt de hele batch dekt; tijdens de run wordt de logfile gepolld zodat de dialog live progress kan tonen ondanks dat verb=runas stdout-redirect uitsluit. Eindresultaat (success/failure per item) wordt na proces-exit uit dezelfde logfile gelezen en als `BloatwareUninstallResult` teruggegeven
- Nieuwe `Dialogs/BloatwareUninstallDialog.xaml` + `.xaml.cs`. Spiegel van UninstallDialog maar consumeert `BloatwareProgress` events i.p.v. `UninstallProgress`. Header toont "X of Y done" tijdens de batch + UAC-hint die verdwijnt zodra de batch klaar is. Per-item: ProgressRing tijdens Pending, indeterminate ProgressBar tijdens Running, checkmark/error op terminal state, label "Removed" / "Failed". Final flush sync-t state nog een keer voor het geval dat een progress-tick miste tussen log-poll cycles
- `DebloatPage` gerefactored: voorheen één lijst, nu twee secties in een ScrollView. Sectie 1 = "Microsoft bloatware" (alleen items die daadwerkelijk geïnstalleerd zijn — anders ruis), Sectie 2 = bestaande catalog uninstall lijst. Elke sectie heeft eigen "Select all" + accent "Remove/Uninstall selected" knop in de header. Beide secties laden parallel via `Task.WhenAll` zodat de pagina sneller responsive is (Get-AppxPackage en winget list zijn beide ~1-2s, sequentieel zou 3-4s zijn). Globale Refresh-knop rechtsboven herlaadt beide
- `App.Bloatware` singleton toegevoegd naast de bestaande `App.Database` / `App.Winget` etc.
- v0.8.4 entry in NEXT-STEPS.md uitgebreid: was eerder alleen "ALLES op de PC search" — nu expliciet beschreven als de unified "alle geïnstalleerde apps van álle bronnen" lijst (registry uninstall keys + Get-AppxPackage + winget list, source-tag per card, filter ComboBox, dispatch per source). User-feedback: deze feature mag niet vergeten worden, behoort uiteindelijk de v0.8.1 + v0.8.2 lijsten te vervangen

### v0.8.1 — Bulk uninstall + UninstallDialog

- Nieuwe `App.IsSelectedForUninstall` (INPC) los van `IsSelected` zodat de Debloat-pagina selectie-state niet kruist met de install-selectie van AppsPage / CategoryDetailPage. Beide pagina's gebruiken dezelfde App-instances dankzij `AppDatabaseService` caching, dus zonder aparte flag zou een Debloat-checkbox de install-footer count vervuilen
- `WingetService.UninstallAppsAsync(IReadOnlyList<App>, IProgress<UninstallProgress>)` — sequential batch met per-app progress events, mirror van `InstallAppsAsync` API. Sequential by design omdat parallel uninstall meer kans geeft op Windows Installer locks (MSI-engine = single-instance) zonder noemenswaardig snelheidsvoordeel — uninstall is sowieso snel. Nieuwe `UninstallProgress` record + `UninstallPhase` enum (Pending / Running / Success / Failed)
- Nieuwe `Dialogs/UninstallDialog.xaml` + `.xaml.cs` als spiegel van `InstallDialog`. Geen 4-stage ring zoals bij install (geen Downloading/Verifying/Installing — uninstall is één action), wel: ProgressRing tijdens Pending, indeterminate ProgressBar tijdens Running, checkmark op Success, error glyph op Failed. Per-app live message, header met "X of Y done" tijdens batch, summary "X uninstalled, Y failed" bij voltooiing. `HadSuccessfulUninstall` property zodat de page kan reageren op een geslaagde batch
- `DebloatPage` herontworpen: card-based lijst (mirror van CategoryDetailPage card layout — icon + naam + winget ID + checkbox), Tapped op de hele card toggelt selectie (CheckBox `IsHitTestVisible=False`), hover-effect via `CardBackgroundFillColorSecondaryBrush`. Footer met selection count + Clear all + Uninstall button, plus Select all toggle in de toolbar. Confirm ContentDialog ("Uninstall N apps?") voor de batch start. `OnNavigatedFrom` cleared `IsSelectedForUninstall` zodat een vergeten selectie niet later terug-popt
- `WingetAppDeployer.WinUI.csproj` krijgt nu wel een `<Version>` / `<AssemblyVersion>` / `<FileVersion>` zodat exe metadata en assembly version mee-bumpen per release. Eerste set op 0.8.1

### v0.7.8 — Toast notificatie fix via Microsoft.Toolkit.Uwp.Notifications
- v0.7.7's `Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register()` faalde silent op unpackaged WinUI 3 apps met `COMException: Class not registered` — vereist een COM activator class die WinAppSDK 1.8 niet auto-registreert
- Switch naar `Microsoft.Toolkit.Uwp.Notifications` 7.x (NuGet `Microsoft.Toolkit.Uwp.Notifications`). `ToastNotificationManagerCompat` doet bij eerste `ToastContentBuilder().Show()` automatisch de AUMID-registratie in HKCU op basis van het exe-pad — geen COM activator class of Start Menu shortcut nodig. Werkt out-of-the-box voor unpackaged Win32/WinUI apps
- `Helpers/ToastHelper.cs` herschreven met `ToastContentBuilder`. Geen `Register()` call meer in App constructor — registratie is implicit bij Show
- Nieuw `/toasttest` debug command-line switch in App.xaml.cs voor snelle dev-verificatie zonder eerst `winget upgrade --all` (~30-60s) te wachten. Toont meteen de success-toast en exit
- Diagnostic logfile in `%TEMP%\WingetAppDeployer_toast.log` met Show()-resultaat (OK / exception). Aangetoond effectief tijdens debug van het v0.7.7 issue
- Bekend issue: `System.Drawing.Common` 4.7.0 transient dep van het toolkit-pakket heeft een NU1904 vulnerability warning. Niet uitbuitbaar via toast-content (we passen geen images aan via System.Drawing). Wordt opgelost zodra toolkit een nieuwere versie release of we naar WinAppSDK's eigen API switchen wanneer die voor unpackaged apps gerepareerd is

### v0.7.7 — Toast notificatie na /autoupdate
- Nieuwe `Helpers/ToastHelper.cs` met `ShowAutoUpdateResult(bool success)`. Gebruikt `Microsoft.Windows.AppNotifications.AppNotificationManager` + `AppNotificationBuilder` uit WinAppSDK 1.4+. Voor unpackaged WinUI 3 vereist `AppNotificationManager.Default.Register()` één keer voor de eerste Show — registreert AUMID + COM activator in HKCU. Best-effort: try/catch op zowel Register als Show zodat een geweigerde notificatie de auto-update flow nooit kan crashen
- `App.xaml.cs` `/autoupdate` handler captured nu de bool result van `UpdateAllAppsAsync()`, post een toast ("All apps have been updated." / "Update finished with errors. Open the app for details.") en wacht 1.5s voor `Environment.Exit(0)` zodat het OS tijd heeft om de toast door te geven voor het proces stopt
- Use case: scheduled task draait stil in achtergrond, user ziet via Action Center (Win+A) dat de update gelopen heeft. Geen window, geen interruptie — pure feedback

### v0.7.6 — First-time prompt voor parallel installs
- Nieuwe `Helpers/ParallelInstallsPrompt.cs` met static `MaybeShowAsync(XamlRoot)`. Toont 1× een ContentDialog "Install apps in parallel?" wanneer user op Install klikt en `ParallelInstallsAsked` nog false is. 2 knoppen: **Yes, install faster** (Primary, accent) → zet `ParallelInstalls = true`, **No, one at a time** (Close, neutral) → zet `ParallelInstalls = false`. Beide zetten `ParallelInstallsAsked = true` zodat de vraag nooit meer terugkomt
- Nieuwe `SettingsService.ParallelInstallsAsked` setting (default false). Bedoeld voor users die niet zelf naar Settings navigeren — bewuste keuze tijdens hun eerste install
- Aangeroepen vanuit `AppsPage.InstallButton_Click` én `CategoryDetailPage.InstallButton_Click` voor de InstallDialog opent. User kan in Settings altijd nog wisselen via de bestaande toggle

### v0.7.5 — Parallel installs + msstore snelheidsfix
- Nieuwe `SettingsService.ParallelInstalls` setting (default `false`) + ToggleSwitch "Run installs in parallel" in de Installation sectie van SettingsPage. Caption waarschuwt: "Install up to 2 apps at the same time. Roughly halves install time, but some MSI installers fail when run concurrently". Bij testing bevestigd: msstore apps in parallel werken vlot, maar twee MSI-based installers (bv. Brave + Chrome) blokkeren elkaar via de Windows Installer single-instance lock — fundamentele platform-beperking, geen bug
- `WingetService.InstallAppsAsync` parallelism via `SemaphoreSlim`. `ConcurrentDictionary` voor results, daarna terug-orderen op input volgorde voor deterministic UI summary. Hard-cap op 4 parallel (`Math.Clamp`) — meer dan 2 sowieso te risky op typische Windows-machines
- `InstallDialog`: header trackt nu `_completedCount` (incremented op Success/Failed phase) i.p.v. flickerende `CurrentIndex` per progress event. Format: `"Installing — 3 of 8 done (2 in parallel)"`. Sequential mode toont dezelfde count-based text zonder de parallel-suffix
- **msstore snelheidsfix**: `winget install --silent --source msstore <id>` ging onder water via een trage COM-pad waardoor msstore-apps (WhatsApp, ChatGPT, etc.) bizar lang duurden. `WingetService.InstallAppAsync` gebruikt nu voor source=msstore een aangepaste command line equivalent met handmatig `winget install <productID> --accept-source-agreements --accept-package-agreements` — geen `--silent`, geen `--source` flag, geen `--exact`. Winget detecteert msstore productIDs (formaat 9XXX / XPXXX) zelf. Resulteert in dezelfde snelle install-ervaring die user in PowerShell ziet
- Eerdere experimentele `ms-windows-store://` Store URI fix (zelfde commit) gerevert — winget zelf is sneller én volautomatisch zodra de juiste flags gebruikt worden

### v0.7.4 — Export / import selectie naar JSON
- Nieuwe `SelectionImportExportService` (singleton via `App.SelectionIO`) — schrijft de huidige selection naar JSON (`my-apps-YYYY-MM-DD.json`) en leest die terug. Format: `{ version, exportedAt, appCount, apps: [wingetId, ...] }`. Alleen WingetIds worden gepersist; bij import wordt elke ID case-insensitive gematcht tegen de huidige catalog en `IsSelected = true` gezet
- Nieuwe `Helpers/FilePickerHelper.cs` met `PickSaveFileAsync` / `PickOpenFileAsync`. Voor unpackaged WinUI 3 zijn FileSavePicker / FileOpenPicker zonder `WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd)` onbruikbaar — pickers eisen een window-handle. HWND uit `App.Window` gehaald
- Nieuwe **"App selection"** sectie op SettingsPage met Export + Import knoppen + InfoBar voor feedback. Export zonder selectie → warning "Niets om te exporteren". Import met onbekende WingetIds → warning "X geselecteerd, Y niet gevonden in catalog" zodat user weet dat een paar IDs niet meer matchen (bv. JSON van oudere catalog-versie). Import doet by default `clearFirst: true` — overschrijft huidige selectie
- Use case: power user maakt op zijn main PC een `my-apps.json`, kopieert die naar een nieuwe machine, importeert via Settings, klikt 1× Install op AppsPage. Of deelt het bestand met een vriend voor dezelfde setup. Profiles (vooraf-gedefinieerd) niet gebouwd — user-gedefinieerde export/import vervangt die behoefte

### v0.7.3 — Post-install schedule prompt + dialog polish
- **Post-install "Schedule auto-updates?" prompt**: nieuwe `Helpers/ScheduleAutoUpdatePrompt.cs` met static `MaybeShowAsync(XamlRoot)`. Triggert na een succesvolle InstallDialog (alleen als `HadSuccessfulInstall == true` én er nog geen scheduled task is én user heeft niet eerder "Don't ask again" geklikt). 3 knoppen: **Schedule** → opent ScheduleDialog, **Don't ask again** → zet `DontAskAboutScheduling = true`, **Not now** → niets. Aangeroepen vanuit zowel `AppsPage.InstallButton_Click` als `CategoryDetailPage.InstallButton_Click`. Nieuwe `InstallDialog.HadSuccessfulInstall` property (set wanneer winget `successCount > 0`)
- **SettingsService** uitgebreid met `DontAskAboutScheduling` (default `false`)
- **`TaskSchedulerService.CreateUpdateTaskAsync`** refactor: schtasks-aanroep gewrapt in `cmd.exe /c "schtasks ... > log 2>&1"` zodat we stdout+stderr kunnen capturen ondanks `UseShellExecute=true` (vereist voor `Verb=runas`). Resolved silent quoting issues — schtasks lijkt een andere quote-parsing te volgen wanneer direct via UseShellExecute aangeroepen vs via cmd. Logfile in `%TEMP%\WingetAppDeployer_schtasks.log`. Return type van `CreateTaskResult` enum naar nieuw `CreateTaskOutcome` record (`Result` + `ErrorOutput`). InfoBar in ScheduleDialog toont nu de echte schtasks output bij `Failed`
- **ScheduleDialog success-feedback**: na `CreateTaskResult.Success` blijft de dialog open, toont `InfoBarSeverity.Success` "Scheduled task created" met de schedule-omschrijving (Daily at HH:MM / Weekly on Monday / On user logon), primary disabled, Close-tekst → "Done"
- **Rounded ContentDialog footer buttons**: WinUI 3 default geeft footer buttons 0 corner radius (snap-fit aan dialog edges). Nieuwe `DialogPrimaryButtonStyle` (BasedOn `AccentButtonStyle`) + `DialogDefaultButtonStyle` (BasedOn `DefaultButtonStyle`) in App.xaml met `CornerRadius="4"`. Toegepast via `PrimaryButtonStyle` / `SecondaryButtonStyle` / `CloseButtonStyle` op ScheduleDialog, InstallDialog, ScheduleAutoUpdatePrompt, en de SettingsPage Disable confirm/result dialogs
- **`DefaultButton = None` fix** voor de Disable-confirm dialog: ContentDialog's `DefaultButton` property forceert AccentButtonStyle op de aangewezen knop en overschrijft custom `CloseButtonStyle`. Was `Close` (Cancel werd dus blauw), nu `None` zodat Disable accent blijft en Cancel neutraal grijs is. Voor destructive actions sowieso veiliger: geen Enter-shortcut

### v0.7.2 — Settings-toggle voor manual download fallback
- Nieuwe `SettingsService` (singleton via `App.Settings`) — JSON-backed store in `%LOCALAPPDATA%\WingetAppDeployer.WinUI\settings.json`. Minimal start: alleen `FallbackToDownloadPage` (default `true` = bestaand v0.7.1 gedrag). Best-effort persist (try/catch op disk IO, in-memory state altijd consistent), camelCase JSON serializer. Wordt in v0.10.0 uitgebreid met de andere settings (`CheckForUpdatesOnStartup`, `ShowWelcomeBanner`, etc.)
- Nieuwe **"Installation"** sectie op SettingsPage met `ToggleSwitch` "Open vendor download pages". Initial sync via `_suppressToggleEvent` guard zodat page-navigatie niet elke keer settings.json terugschrijft
- `InstallDialog` respecteert de toggle: wanneer UIT worden manual-download apps geskipt met nieuwe `InstallItemState.Skipped` ("Skipped" label, secondary text colour) i.p.v. dat de browser geopend wordt. Final summary text combineert nu winget + manual-opened + skipped: "X installed, Y failed, Z manual downloads opened, N skipped"
- Bonus fix: `manualOpenedCount` telt nu alleen state `ManualOpened` (i.p.v. raw count van manual apps), zodat een Failed manual-app niet dubbel in de summary verschijnt

### v0.7.1 — Fallback download URL voor non-winget apps
- `App.DownloadUrl` (nullable string) JSON-veld + `IsManualDownload` + `ManualDownloadVisibility` properties op het model
- Badge **"Manual download"** (caution-orange + Globe glyph E71B) in CategoryDetailPage en AppsPage app-cards naast de andere status badges
- `InstallDialog` splitst geselecteerde apps in winget-installable + manual-download. Voor manual-apps wordt de `downloadUrl` geopend in de default browser via `Process.Start(...UseShellExecute=true)` — geen winget call. Failure (URL invalid) → `InstallItemState.Failed` met "Could not open URL: ..." message
- Nieuwe `InstallItemState.ManualOpened` met label "Browser opened" (caution color) en de message "Opened vendor download page in browser". Final-summary in dialog combineert winget-results + manual-opened count: "X installed, Y failed, Z manual downloads opened"
- 3 voorbeeld-apps toegevoegd aan apps.json: **VMware Workstation Pro** (Development → VMs), **ON1 Photo RAW** (Creative → Graphics), **Nvidia App** (Utilities → System Tools, popular). Alle 3 met icons gefetched
- Roadmap-swap meegenomen: install-features (v0.7.0) zijn nu de core focus i.p.v. Debloat eerst. Volgorde: v0.7=install polish, v0.8=Debloat, v0.9=Tweaks, v0.10=Settings + self-update

### v0.6.3 — AppIcon size + spacing polish
- Cards opgeschaald van 28×20 → 38×26 (~3.5× oppervlakte) zodat icon visueel even groot oogt als andere taskbar-icons (Edge/VSCode/etc.). Cards blijven landscape (1.46:1 ratio)
- Symmetric 5×5 stack-offset i.p.v. 4×4 — duidelijker zichtbare "stap" tussen gestapelde cards (~19% per card-rand zichtbaar i.p.v. 14%) zonder te krap te ogen
- Cards verticaal gecentreerd in canvas (6 padding boven/onder), volledig vullend horizontaal

### v0.6.2 — Custom AppIcon (taskbar + Explorer + alt-tab)
- Vervangt placeholder ICO door custom design dat de "app-installer" metaphor uitstraalt: 3 gestapelde blauwe app-cards (analogous monochrome palette) met Fluent download-icoon (verticale shaft + V-tip + horizontale tray-lijn) gecentreerd in de witte front card. Volgt MS Windows 11 Fluent Design guidelines (single literal metaphor, subtle 120° gradient, layered drop shadows, light from top-left, geen typografie, no background tile)
- `scripts/generate-app-icon.ps1` — programmatische generator via System.Drawing. Bouwt master 256×256 PNG en downscaled naar 16/24/32/48/64/128/256 px PNG-encoded entries in een Vista+ multi-resolution ICO. Reproduceerbaar — gewoon opnieuw runnen om iconen aan te passen
- Cards 36×28 op 48-base grid → vult ~95% van canvas zodat het icoon visueel even groot oogt als andere taskbar-icons
- `<ApplicationIcon>` toegevoegd in csproj zodat MSBuild de ICO als exe-resource embed (Explorer/taskbar/alt-tab pikken het op). `MainWindow` zet runtime window-icon via `AppWindow.SetIcon("Assets/AppIcon.ico")`
- `scripts/generate-icon-variants.ps1` als design-exploratie tool met meerdere alternatives (tilted Photos-style stack, vertical stack, V-chevron+lijn variant) — niet opgenomen in repo final, alleen het script blijft beschikbaar voor toekomstige iteratie. Afgewezen V29 (tilted) PNG ligt in `data/app-icon-backups/`

### v0.6.1 — Icons in AppsPage global search
- CatalogResultsList DataTemplate uitgebreid met 3-column grid (40×40 `Image` / text / checkbox), zelfde patroon als CategoryDetailPage card
- `AppIcon_ImageFailed` handler in AppsPage code-behind verbergt de Image bij missing icon zonder layout-break
- Curated catalog matches in de globale search tonen nu hun icon. Winget-repo search results blijven iconless (dynamisch, niet gebundeld — placeholder is een aparte v0.10.x optie)

### v0.6.0 — Icon system milestone
- `scripts/fetch-icons.ps1`: PowerShell pipeline die per app een 128×128 PNG ophaalt en normaliseert naar transparante canvas (gecentreerd, aspect preserved). Ladder: `iconUrl` override → `iconFile` lokaal → Google favicon API (`sz=128`) → icon.horse fallback (scrapet `apple-touch-icon`, vaak 180-512px). Wikipedia REST API (`/api/rest_v1/page/summary/<title>`) gebruikt om voor probleemgevallen hi-res logo-URLs te vinden — Wikipedia eist gedetailleerde User-Agent met contact-info anders 400
- Curated mix van bronnen: dashboard-icons (Homarr Labs), selfhst/icons, Wikipedia Commons, icons8, Steam GridDB, en `scripts/local-icons/` voor user-supplied PNGs (Everything via Gemini-render)
- Post-processing pipeline op elke icon:
  1. **Auto-crop** (opt-in via `autoCrop = $true` per app) — trimt whitespace borders rond het logo. Default OFF zodat designed icons (Claude, PowerToys, Office, Teams) hun bewuste padding behouden. Aan voor whitespace-heavy sources (Everything, WinRAR icons8 PNG)
  2. **Scale-to-fit** — bewaart aspect ratio, centreert op 128×128 transparante canvas
  3. **White-to-transparent** — BFS flood-fill vanaf canvas-corners met threshold 225 (R,G,B all ≥225 = "near-white"), maakt witte achtergronden transparant zonder logo-witte details kapot te maken
  4. **Squircle rounded corners** (Apple-style, ~22% radius) — alleen toegepast als ALLE 4 hoek-pixels (bijna) volledig opaque zijn (alpha ≥250). Voorkomt clipping van designed icons met breathing room (Claude burst rays, CCleaner broom) en is redundant op al-ronde logos (Discord, Spotify)
- Result: **92/92 icons OK**, 0 failed, 2 LAAG (LibreOffice + Insomnia, acceptabel op 48px UI)
- 3 nieuwe Proton apps toegevoegd aan `apps.json`: Calendar, Wallet, Authenticator. Plus Sheets/Docs/Meet via een tweede ronde
- `data/icons/<wingetId-met-hyphens>.png` gebundeld via `<Content>` + `PreserveNewest`. Filename gebruikt hyphens i.p.v. dots — Windows PRI parser ziet anders bv. `.64-bit.png` als scale qualifier en weigert te resolven
- `App.IconImage` getter returnt `BitmapImage` (lazy + gecached) i.p.v. string-pad — x:Bind doet geen automatische `string` → `ImageSource` conversie (die werkt alleen via XAML markup TypeConverter)
- CategoryDetailPage card layout uitgebreid met 3e column: 40×40 `Image` links, daarna text+badges, dan checkbox. `ImageFailed` event verbergt de Image bij missing icon zonder layout te breken
- WinAppSDK pin: `1.8.*` (was `*` → restored 2.0.1 en crashte met "Required components of the Windows App Runtime are missing — Version 2.x" omdat alleen 1.8 systeembreed is geïnstalleerd)

### v0.5.12 — Self-contained publish configuratie
- `WindowsAppSDKSelfContained=true` in alle drie publish profiles (win-x64, win-arm64, win-x86) — exe heeft de WinAppSDK runtime nu meegebundeld, geen aparte WinAppSDK installer nodig op de doelmachine
- `PublishTrimmed` uit gezet (was conditioneel aan voor non-Debug). Trimming brak `JsonSerializer.Deserialize<AppDatabase>` doordat de reflection-paden van System.Text.Json statisch onbereikbaar lijken — "Could not load categories" bij startup. WinUI 3 unpackaged is sowieso fragiel voor trim (XAML compiler, x:Bind, WinRT bridge leunen op reflection)
- Resultaat: `dotnet publish -c Release -p:PublishProfile=win-x64` levert een drop-and-run folder van ~262 MB op (ZIP'd ~70-80 MB voor distributie)

### v0.5.9 — WPF gearchiveerd
- WPF source (`src/WingetAppDeployer/`) + Launcher (`src/Launcher/`) uit de repo verwijderd
- Code blijft recoverable via git tag `wpf-final-v1.2.1`
- Solution opgeschoond — alleen `WingetAppDeployer.WinUI` blijft over
- README, INTEGRATIE.md, CHANGELOG.md, CLAUDE.md, NEXT-STEPS.md herschreven naar WinUI-only context

### Curated dataset (apps.json v2.0.0)
- Trim van 125 → ~60 apps op basis van wishlist
- Nieuwe top-level "Gaming" categorie (was subcat)
- Nieuwe top-level "App Suites" — Proton (5 apps) + Adobe (Creative Cloud + Acrobat Pro)
- Productivity uitgebreid: AI Assistants subcat (ChatGPT msstore + Claude), Cloud Storage subcat (OneDrive)
- Security met aparte subcats: Password Managers / VPN / Antivirus
- `App.Source` veld + `WingetService` `--source` flag voor msstore-only apps (WhatsApp, Apple Music, ChatGPT)

---

## Open / gepland

### v0.6.x — Icon system polish (lopend)

- Optionele eerste publieke pre-release: GitHub Releases artifact (publish-zip + exe)
- Open issues in icon set: 2 LAAG (LibreOffice, Insomnia) — handmatig vervangen wanneer een schoner logo opduikt
- Icons voor toekomstige Debloat / Tweaks dialogs (per Windows-feature een eigen icon, optioneel)

### v0.7.0 — Install flow UX polish + Launcher port

- ~~WinUI Launcher~~ — geschrapt: Inno Setup installer met `/SILENT` (op v1.0 roadmap) dekt exact dezelfde unattended-install rol én geeft proper Start Menu / uninstaller. Launcher zou dubbele moeite zijn
- ~~Post-install "Schedule auto-updates?" prompt~~ — gedaan in v0.7.3
- ~~Toast notificatie na `/autoupdate`~~ — gedaan in v0.7.7/v0.7.8 (via `Microsoft.Toolkit.Uwp.Notifications`; WinAppSDK's eigen API faalt op unpackaged WinUI 3 met "Class not registered")
- ~~Parallel installaties~~ — gedaan in v0.7.5 + v0.7.6 (MSI-based installers blokkeren elkaar via Windows Installer lock — fundamentele platform-beperking, niet oplosbaar)
- ~~Installation profiles~~ — geschrapt: user-gedefinieerde export/import (v0.7.4) dekt deze behoefte; vooraf-gedefinieerde profiles voegen weinig waarde toe
- ~~Export/Import selectie naar JSON~~ — gedaan in v0.7.4
- ~~Installatie geschiedenis / log~~ — geschrapt: `winget list` is al de source of truth; per-install feedback zit al in InstallDialog. Persistent log is meer noise dan signal
- ~~"Fallback to download page" toggle~~ — gedaan in v0.7.1 (downloadUrl + Manual download badge) en v0.7.2 (Settings toggle)

### v0.8.0 — Debloat tab full (lopend)

Per sub-feature één patch versie. Milestone v0.8.0 = release zodra alle v0.8.x af zijn.

- ~~**v0.8.1** — User-installed apps uninstaller upgraden~~ — gedaan (card-based, multi-select, batch via UninstallDialog)
- ~~**v0.8.2** — Microsoft bloatware removal~~ — gedaan (curated lijst van ~22 Microsoft AppX items, sectie boven catalog-lijst op DebloatPage, batch via één UAC-elevated PowerShell call met live log-tail progress)
- ~~**v0.8.3** — Categorieën-sectie + counts~~ — gedaan (BloatwareVendor enum, OEM curated lijst voor HP/Dell/Lenovo/ASUS/Acer/MSI, counts in alle section-headers, Windows11-Unattended-Debloat integratie geparkeerd voor v0.9.x Tweaks)
- ~~**v0.8.4** — Unified "alle geïnstalleerde apps" lijst~~ — gedaan (InstalledAppEntry + InstalledAppsService voor 3-bron detectie, MixedSourceUninstaller voor per-source dispatch met one-UAC voor de elevated subset, AllAppsUninstallDialog met source-badges en cancellation-state, vervanger voor v0.8.1 catalog-sectie op DebloatPage met fuzzy search + filter ComboBox)
- ~~**v0.8.5** — Restant-opruiming direct na uninstall~~ — gedaan (LeftoverScannerService scant registry uninstall keys + Program Files folders + AppData folders parallel, confidence-tiered matching, LeftoverCleanupDialog met preview-fase en elevated delete-batch, SettingsService.ScanLeftoversAfterUninstall toggle. Scheduled tasks + services + Temp niet meegenomen — te lawaaiig voor app-specifieke leftover scope)
- ~~**v0.8.6** — Deep clean (los van uninstall)~~ — gedaan (DeepCleanService met `ScanWindowsCachesAsync` + `ScanOrphanedFoldersAsync`, DeepCleanDialog met safe/caution-tier groepering, DebloatPage layout-restructure naar outer "Apps debloat" + "Deep clean" expanders. Recycle Bin via `Clear-RecycleBin`, browser caches voor Edge/Chrome/Firefox/Brave, orphaned-folder match via comparison-set uit InstalledAppsService met brede protected-list)
- ~~**v0.8.7** — Orphaned registry + cleaner installed-list~~ — gedaan (nieuwe `OrphanedRegistry` DeepCleanCategory + `ScanOrphanedRegistryAsync` walks HKLM/WOW6432Node/HKCU uninstall keys, `CheckRegistryEntryAlive` filtert op pad-veld bereikbaarheid uit InstallLocation/DisplayIcon/UninstallString/QuietUninstallString/InstallSource. `CollectAliveRegistryIdentifiers` filtert dode Web-source entries uit installed-list zodat folder-scan ze niet als "match" telt — orphan-folder die alleen via leftover registry-entry skipped werd komt nu terug. DeleteAsync: HKCU via in-process `DeleteSubKeyTree`, HKLM via `reg.exe delete /f` in elevated batch. DeepCleanPage tweede knop combineert orphan-folder + orphan-registry parallel scan, bundle-by-name in dialog groept registry+folder van zelfde app automatisch. Bug-fix in oude DeleteAsync local-loop: cache-clear werd ook op OrphanedFolder local items toegepast — nu correct geüpgraded naar full Directory.Delete)
- ~~**v0.8.8** — Bredere deep clean scope (full deep clean)~~ — gedaan (App Paths / MUIcache / Class handlers / Start Menu + Desktop shortcuts allemaal toegevoegd als nieuwe scan-flows in DeepCleanService. Eén "Scan leftovers" knop runt nu 6 scans parallel: orphan-folders + uninstall-registry + App Paths + MUIcache + class handlers + shortcuts. DeepCleanItem extended met optionele `RegistryValueName` voor MUIcache value-deletion. DeleteAsync uitgebreid met DeleteRegistryValue helper voor specifieke MUIcache values + Remove-Item branch voor shortcuts in elevated PS-batch. Alle scans gebruiken dezelfde generieke heuristic — pad-veld resolveert? — geen hardcoded vendor-namen. Bundle-by-name in dialog groept nu folder + uninstall-key + App Paths + MUIcache + shortcut van zelfde app onder één card)
- ~~**v0.8.9** — Orphan scheduled tasks + firewall rules~~ — gedaan (`ScanOrphanedScheduledTasksAsync` via `schtasks /Query /XML ONE` + XML-parse, filtert `\Microsoft\` system-tasks; `ScanOrphanedFirewallRulesAsync` via DIRECTE registry-read van `HKLM\SYSTEM\...\FirewallPolicy\FirewallRules` i.p.v. trage `Get-NetFirewallRule` cmdlet (fractie van seconde i.p.v. 10-30+ sec). Beide scans gaan altijd via elevated batch (admin nodig voor `schtasks /Delete` en `Remove-NetFirewallRule`). Firewall display valt terug op exe-filename als DisplayName cryptisch is (`@`-prefix of GUID). PathLink_Click opent `taskschd.msc` resp. `wf.msc`. Twee nieuwe `OrphanedScheduledTask` + `OrphanedFirewallRule` enum values + UI labels. Scan-leftovers knop runt nu 8 scans parallel)
- ~~**v0.8.9.5** — Post-uninstall extended cleanup~~ — gedaan (LeftoverScannerService uitgebreid van 3 naar 7 scan-types: bestaande registry + Program Files + AppData + nieuwe App Paths + MUIcache + class handlers + Start Menu/Desktop shortcuts, allemaal app-name-filtered zodat alleen leftovers van de zojuist verwijderde app(s) terugkomen. LeftoverType enum + LeftoverItem.RegistryValueName property. TryDeleteLocal + RunElevatedDeleteAsync uitgebreid voor de nieuwe types. Apps Debloat triggert nu de full cleanup ladder i.p.v. alleen folders/registry — geen extra deep clean nodig na een uninstall voor app-specifieke residue)
- ~~**v0.8.10** — Orphan services + HKCU vendor-residue~~ — gedaan (twee high-risk leftover-types met strict filters om system-state niet te raken. **Orphan services**: `Get-CimInstance Win32_Service` via base64-PS, parse pipe-separated `Name|DisplayName|State|StartMode|PathName`. Strikt filter — alleen orphan als (a) ImagePath dood, (b) State=Stopped, (c) StartMode=Manual/Disabled, (d) niet svchost.exe (DLL-hosted), (e) niet onder `%SystemRoot%` / `C:\Windows\`, (f) geen winget/AppX token cross-match. Delete via `sc.exe delete <name>` in elevated batch. Click op path → `services.msc`. **HKCU vendor**: walk `HKCU\Software\<Vendor>\<App>` (top 2 levels), per app-key zoek pad-values (value-name in `[InstallPath, InstallDir, Path, Program, ExecutablePath, ExePath, AppPath, ...]` óf value-data start met drive-letter `X:\`). Als ALLE pad-values dood AND geen winget/AppX cross-match → orphan. Protected vendor-keys (Microsoft / Classes / Policies / Wow6432Node / Google / Mozilla / driver vendors) altijd skipped. Delete via in-process `DeleteSubKeyTree` (HKCU = user-context, geen UAC). Beide types: IsSafe=false, default unchecked, caution-tier. DeepCleanService.GetOrphanedScanLocations + DeepCleanPage scan-leftovers flow uitgebreid naar 10 parallel scans + breakdown-counts in InfoBar)
- ~~**v0.8.11** — Polish (diagnostic logs gated + multi-badge bundles + empty-state UI + auto-refresh)~~ — gedaan (zie Voltooide versies)
- **v0.8.12** — Milestone v0.8.0 release voorbereiding:
  - Versie-bump naar **v0.8.0** als milestone-release zodra dit af is → release op GitHub met exe-artifacts

### v0.9.x — Tweaks tab (uitgebreid uit research mei 2026)

Research mei 2026: ~140 tweaks geïnventariseerd over 14 categorieën (Chris Titus winutil + OFGB + ExplorerPatcher + Winaero + O&O ShutUp10 + community lists + Win11 24H2/25H2 specific). Core feature: **live state-detection** — elke toggle leest huidige registry-waarde bij page-load en reflecteert of de tweak al actief is. Per tweak `EnabledValue` + `DisabledValue` voor full reversibility. Multi-op bundles (bv OFGB = 22 keys onder 1 toggle) krijgen "partial state" indicator. HKLM-ops batchen in 1 elevated PS-call (zelfde patroon als BloatwareService / DeepCleanService). Per toggle een restart-indicator icoon: 🔄 explorer-restart / ⚙️ sign-out / 🔁 reboot / 🔒 admin.

~~**v0.9.1 — Foundation + Explorer (vertical slice)**~~ — gedaan (zie Voltooide versies)

~~**v0.9.2 — Taskbar**~~ — gedaan (zie Voltooide versies)

~~**v0.9.3 — Start Menu**~~ — gedaan (zie Voltooide versies)

~~**v0.9.4 — Ads & Bloat (OFGB-equivalent)**~~ — gedaan (zie Voltooide versies)

~~**v0.9.5 — Backup & Restore infrastructuur**~~ — gedaan (zie Voltooide versies; nieuw item, niet in originele roadmap — daardoor schuift de rest van de v0.9.x nummering 1 op)

~~**v0.9.6 — AI / Copilot (Win11 24H2+)**~~ — gedaan (zie Voltooide versies)

~~**v0.9.7 — Privacy uitbreidingen**~~ — gedaan (zie Voltooide versies)

~~**v0.9.8 — UI / Theme**~~ — gedaan (zie Voltooide versies). Accent-color override + classic Photo Viewer geparkeerd (zie toelichting)

~~**v0.9.9 — Performance**~~ — gedaan (zie Voltooide versies). powercfg-afhankelijke items (Ultimate Performance power plan, hibernation-reclaim) + startup-apps cleanup geparkeerd; placebo-tweaks bewust niet opgenomen

~~**v0.9.10 — Context Menu uitbreidingen**~~ — gedaan (zie Voltooide versies). Clipchamp-removal geskipt (geen betrouwbaar hardcoded CLSID — vereist runtime-discovery)

~~**v0.9.11 — Notifications & Lock Screen**~~ — gedaan (zie Voltooide versies). "Suggest ways to finish setup" niet opgenomen (al in v0.9.4); Action Center + Calendar-systray samengevoegd tot één `DisableNotificationCenter`-tweak

**v0.9.12 — Updates uitbreidingen**
- Pause N dagen, active hours (al gepland in originele scope)
- Defer feature updates (max 365 days) + quality updates (max 30 days)
- Skip driver updates via WU (`ExcludeWUDriversInQualityUpdate=1`)
- Disable auto-restart with logged-on users
- Disable "Get latest as soon as available" continuous-innovation opt-in
- Set Ethernet metered (defers most updates)

**v0.9.13 — Gaming (lagere prio)**
- Disable Game DVR (background recording)
- Disable Game Bar (Xbox overlay), Game Bar capture features
- Disable Xbox services (XblAuthManager / XblGameSave / XboxNetApiSvc / XboxGipSvc)

**v0.9.14 — Presets / Profiles**
- `data/tweaks-presets.json` met preset bundles: "Privacy basics" / "Power user starter" / "Performance focus" / "Minimal UI"
- Eén klik vinkt een set tweaks aan in de Tweaks tab (user kan nog deselecten voor Apply)
- Inspiratie: WinUtil's preset-knoppen

**v0.9.0 — Milestone release**
- Versie-bump naar v0.9.0 → release op GitHub met exe-artifacts (na Inno installer in v1.0)

### Tweaks parking-lot extension — Michael-Matta UWT lijst (mei 2026)

Bron: [Michael-Matta1/windows-utilities-tweaks](https://github.com/Michael-Matta1/windows-utilities-tweaks) — open-source Win-tweaker met ~200 toggles. Hieronder gecategoriseerd naar **onze** category-buckets, met dubbelen tegenover onze huidige roadmap weggefilterd. **Verifieer per item voor implementatie** — UWT is een legacy WinUI tool, sommige tweaks zijn Win7/8/10-era en mogelijk deprecated op Win11 24H2/25H2.

**Caveat**: items die je security verlagen (Disable Defender / Disable UAC / Disable Registry Editor / Disable Task Manager) zijn IT-admin lockdown tooling — out-of-scope voor onze user-quality-of-life focus. Niet implementeren tenzij user expliciet vraagt.

**Explorer** (boven onze huidige 5 tweaks):
- Show Windows version on Desktop (paint.exe-trick of DWM)
- Tweak Drive Letters / Remove duplicate drive letter entry
- Remove shortcut arrows from icons
- Remove "-Shortcut" suffix for new shortcuts
- Enable check boxes to select items
- Enable auto-complete + auto-suggest in address bar
- Show User Folder in nav pane
- Choose which folder File Explorer opens on (Home / This PC / custom) — al gepland v0.9.1 als LaunchTo, mogelijk uitbreiden met custom path
- Disable Aero Peek / Aero Shake / Aero Snap
- Show status bar in File Explorer
- Launch folders in a separate process
- Disable info tips for shortcuts
- Disable full row select items
- Hide preview pane (default off?)
- Always show icons never thumbnails / Display file icons on thumbnails
- Hide protected OS files / Show drive letters
- Show encrypted/compressed NTFS files in color
- Use sharing wizard
- Disable Windows startup sound
- Increase System Restore Points frequency
- Enable automatic registry backups
- Don't show low-disk-space warnings

**Taskbar** (boven onze huidige 9 tweaks):
- Use small taskbar icons (Win10-style — werkt mogelijk niet meer op 11)
- Hide inactive icons from notification area
- Remove individual systray icons: volume / network / action center / clock / battery / notification area
- Customization of taskbar buttons grouping (overlap met onze TaskbarGlomLevel)
- Customization of taskbar thumbnail size + delay
- Make taskbar button switch to last active window
- Show Windows Defender icon in tray
- Customization of taskbar content alignment (= onze TaskbarAl)
- Taskbar size customization

**Start Menu** (boven onze v0.9.3 plan):
- Lock start screen tiles so they can't be rearranged
- Disable taskbar + start jumplists (privacy: app-history wegfilteren)
- Increase jump list items displayed
- Enable accent color for Start menu + taskbar
- Enable / disable Stickers feature
- Disable Bing web search in Start (al gepland)

**Ads & Bloat** (boven onze v0.9.4 OFGB):
- Remove Windows Spotlight "Learn more" desktop icon
- Disable "Look for an app in the store" prompt
- Disable "You have new apps that can open this type of file" notification
- Disable Cortana entirely (al gepland v0.9.3)
- Disable handwriting data sharing
- Disable Wi-Fi Sense

**AI / Copilot** (boven onze v0.9.5):
- (UWT heeft hier niet veel — onze v0.9.5 plan is al uitgebreider)

**Privacy** (boven onze v0.9.6):
- Disable application telemetry
- Disable inventory collector
- Disable steps recorder
- Disable biometrics
- Disable password reveal button
- Disable Windows update sharing (P2P upload)
- Disable Windows feedback requests (al deels in onze plan)
- Disable synchronization of settings (cross-device sync uit)
- Disable App access to: location / calendar / messages / microphone / camera / user account info

**UI / Theme** (boven onze v0.9.7):
- Disable login screen blur (al gepland)
- Disable lock screen / disable changing lock screen image
- Enable + customize lock screen slideshow
- Enable / disable first sign-in animation
- Toggle apps / system light/dark theme (al gepland)
- Increased taskbar transparency
- Change inactive title bar color
- Customize blinking cursor width + time
- Customize scroll bar width
- Disable transparency effects globally (al gepland)
- Disable / enable transparency selectively
- Show / hide power button options (Lock / Sleep / Hibernate / Sign Out)
- Enable verbose logon messages (al gepland)
- Customize logon message (custom-text vóór login screen)

**Performance** (boven onze v0.9.8):
- Waiting times tweakable: kill apps timeout / end services / non-responsive apps
- Auto-end non-responsive programs
- Restart shell automatically after error
- Always unload DLLs to free memory
- Disable automatic folder-view discovery (al gepland)
- Turn off Search Indexer
- Increase IRQ8 priority
- Disable smooth scrolling
- Disable Windows Time service
- Disable Tablet Input service
- Disable Windows Security Center service
- Disable Prefetch / Superfetch service (gevaarlijk op SSD/HDD verschil)
- Disable Printer Spooler (security tweak ook)
- Disable Edge/tab preloading
- Disable all background apps (al gepland)
- Delete pagefile at shutdown

**Context Menu** (boven onze v0.9.9):
- Remove "Open in Windows Terminal" from Desktop context (counterintuitive — meeste users willen 'm juist)
- Add over 15 modern UWP apps to desktop context (Edge / OneNote / Store / Music / Mail)
- Add Take ownership / Copy to / Move to / Open with (al gepland)
- Remove "Cast to Device" / "Edit with Paint 3D" / "Scan with Windows Defender" / "3D Print with 3D Builder"
- Toggle Quick Access menu items
- Add "Phone" / "Gaming Settings" / "Character Map" / "Control Panel" / "Windows Update" to context menu
- Add Windows Defender quick-actions (Open / Quick Scan / Full Scan / Settings / Update) to context

**Notifications & Lock Screen** (boven onze v0.9.10):
- Disable toast notifications globally (al gepland)
- Set notifications display time (Win-specific timeout per toast)
- Disable Action Center quick-action buttons
- Enable / disable Edge "Do you want to close all tabs?" prompt

**Updates** (boven onze v0.9.11):
- Disable Windows Update service entirely (al gepland deels)

**Browsers / Edge**:
- Change Edge default download location
- Adjust tab preview show/hide delay
- Disable Edge tab preview entirely
- Enable Edge "close all tabs?" prompt (= een non-default warning toggle)

**Out-of-scope (security-lockdown — IT-admin tools, niet user QoL)**:
- Disable Registry Editor / Control Panel / Task Manager / CMD / WinKey shortcuts
- Disable folder options menu / display personalization
- Disable shutdown / log off ability
- Disable encrypting file system
- Disable Defender / Windows Store / Mobility Center / Media Center / Update Service
- Disable system restore / MMC snap-ins
- Disable color & appearance settings
- Disable internet communication
- Restrict access to taskbar + start menu properties
- Disable explorer's context menu / taskbar context menu
- Disable changing wallpaper
- Disable user tracking
- Hide entire network / prevent network auto-discovery / disable admin shares
- Disable NTLM 2 / set global network offline
- Disable Anonymous Connections access
- Disable Print Spooler (security-relevant)

**Out-of-scope (UWT-app-specific tweaks die niet ons doel zijn)**:
- Option to add UWT to startup / Integrate UWT with desktop context menu
- Export / import tweaks (we hebben SelectionImportExportService voor app-selecties; tweak-import voor v0.9.13 presets)
- Edit OEM Information (registered owner / organization) — niche
- Customize "New" menu in context — power-user-only

**Mogelijk-interessant maar verifieer-bij-implementatie**:
- Customize UAC settings (security implication — onze v0.9.x parking-lot heeft al "Security caution-tier")
- Enable Admin Approval Mode for built-in Administrator
- Disable switching to Secure Desktop while elevating
- Enable virtualize File and Registry write failures (per-user redirect)
- Display Last Logon Information on logon screen
- Make user enter username while logging on (security)
- Require Ctrl+Alt+Del to logon (security)
- Show Windows Photo Viewer (klassieke viewer terug — al gepland v0.9.7)
- Reset Live Tile cache
- Enable Stickers feature (decoratie op desktop)
- Restore last opened folders at startup

### Tweaks parking-lot (uit research, niet ingepland — voor v1.x feature pack)

Bewust niet meegenomen om v0.9.x scope hanteerbaar te houden. Bij interesse later oppakken:

- **Network tweaks**: DNS swap (Cloudflare/Google/Quad9 per-adapter), Disable IPv6 entirely, Disable Teredo tunneling, Disable NetBIOS-over-TCP/IP, Disable LLMNR — niche, raakt netwerkstack
- **Edge browser debloat** (17 keys onder `HKLM\SOFTWARE\Policies\Microsoft\Edge`): Startup Boost, Background mode, Hubs sidebar, Bing chat, Shopping, Wallet donation, Personalized ads, Address-bar Bing, Search suggestions, Auto-launch on logon
- **Security caution-tier** (achter "I know what I'm doing"-checkbox): Set UAC to Never notify, Set UAC notify-without-dimming, Disable SmartScreen apps, Disable Defender real-time (vereist Tamper Protection off — Safe Mode)
- **ExplorerPatcher integratie** (taskbar grouping labels = naast icon ook app-name) — vereist third-party tool install, scope-creep
- **Remove Edge entirely** — destructive `setup.exe --force-uninstall` flag, kan apps breken
- **Disable SMB1** — security, maar legacy network shares (NAS, oude printers) breken
- **Disable Sticky Keys prompt + Toggle Keys + Filter Keys prompts** — accessibility quality-of-life, lage prio
- **System clock to UTC** (Linux dual-boot users)
- **Disable Razer Synapse auto-install on USB-connect**
- **Disable downloaded-exe security warning** (zone 3 `1806=0`) — security implicatie
- **Always show scrollbars in UWP/Settings apps** (`DynamicScrollbars=0`) — kleine UX-preference
- **Disable folder auto-type discovery** (voorkomt dat folders ineens als "pictures" weergegeven worden)
- **Auto-folder LaunchTo backup** — alternatieve setting voor User Files start-folder

### v0.10.0 — Settings + app self-update

- `SettingsService` — JSON-backed settings file (`%LOCALAPPDATA%\WingetAppDeployer.WinUI\settings.json`):
  - `CheckForUpdatesOnStartup` (default true)
  - `ShowWelcomeBanner` (default true)
  - `AutoUpdateEnabled` + `AutoUpdateSchedule` (mirror van TaskScheduler)
- `GitHubService` — check `api.github.com/repos/.../releases/latest` op startup, vergelijk met assembly version, download + launch nieuwe exe via launcher-pattern
- Welcome banner op AppsPage (dismissible via X en setting)
- Update-beschikbaar InfoBar in MainWindow met "Update now" knop
- Settings UI uitbreiden met ToggleSwitches + "Check for updates now" button

### Latere milestones

**v1.0.0 — eerste stable release**
- Self-update via GitHub (v0.10.0) werkt
- Inno Setup `/SILENT` install dekt de unattended-debloat rol (was eerder gepland als losse v0.7.0 Launcher exe — geschrapt omdat Inno Setup dezelfde rol vervult)
- **Inno Setup installer** met silent-install support (`/SILENT` + `/VERYSILENT` flags). Reden: ZIP+folder-distributie is OK voor early access maar is ruw — installer geeft proper Start Menu entry, uninstaller, en (cruciaal) **scriptable silent install** voor Windows11-Unattended-Debloat integratie. Inno Setup is gratis, geen licentiekosten. Note: sign-cert blijft buiten scope (kosten); SmartScreen reputation bouwt zich vanzelf op naarmate downloads stijgen
- WinUI 3 single-file publish geprobeerd, faalt met `Microsoft.UI.Xaml.dll` 0xc000027b crash door XAML/WinRT activation lookups die filesystem-paden eisen — niet oplosbaar zonder bootstrap-launcher hack ([WinAppSDK #2719](https://github.com/microsoft/WindowsAppSDK/issues/2719)). Daarom installer i.p.v. single-exe
- Geen P0 bugs

**v1.x.x — feature uitbreidingen**
- Multi-language (NL/EN toggle)
- Plugin systeem voor custom app sources
- Cloud sync voor settings + selecties
- Custom app repositories
- CLI interface (`winget-deployer install --profile gaming`)
- Backup / restore functionaliteit

### Out of scope

- Decoratieve themes (Sunset / Aurora / OceanBreeze) — historisch alleen in WPF
- MSIX packaging — voorlopig unpackaged
- Windows-only apps die geen winget hebben — zie v0.9.0 fallback-toggle als alternatief

---

## Development Notes

### Quick commands

```bash
# Build
dotnet build src/WingetAppDeployer.WinUI/WingetAppDeployer.WinUI.csproj -c Debug

# Run
dotnet run --project src/WingetAppDeployer.WinUI/WingetAppDeployer.WinUI.csproj -c Debug

# Self-contained release publish
dotnet publish src/WingetAppDeployer.WinUI/WingetAppDeployer.WinUI.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o ./release

# GitHub release
gh release create v0.X.Y ./release/WingetAppDeployer.WinUI.exe --title "WingetAppDeployer v0.X.Y"
```

### Project structuur

```
src/
└── WingetAppDeployer.WinUI/             # Native Win11 app (WinUI 3 + WinAppSDK 1.8)
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / .xaml.cs        # MicaBackdrop BaseAlt + NavigationView
    ├── Models/AppModels.cs               # App (INPC), Category, SubcategoryGroup
    ├── Pages/                            # AppsPage, CategoryDetailPage, DebloatPage, SettingsPage, TweaksPage
    ├── Dialogs/                          # InstallDialog, ScheduleDialog
    ├── Services/                         # AppDatabaseService, WingetService, TaskSchedulerService, SelectionHelper
    ├── Helpers/                          # FuzzyMatcher, ScrollViewSpeedup
    └── Assets/

data/
└── apps.json                             # Curated app catalog (v2.0.0, ~60 apps, gebundeld met exe)
```
