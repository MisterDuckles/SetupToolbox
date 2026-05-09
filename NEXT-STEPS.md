# WingetAppDeployer — Roadmap

Native Windows 11 app voor het bulk-installeren van apps via `winget`. Pre-release (v0.5.x), op weg naar v1.0.

> WPF-historie tot en met v1.2.1 is gearchiveerd onder git tag `wpf-final-v1.2.1`. Repo is sinds v0.5.9 WinUI-only.

**Stack:** .NET 10 + Windows App SDK 1.8 + WinUI 3 + unpackaged exe. Mica backdrop, native `Microsoft.UI.Xaml` controls. Distributie via private repo + public GitHub Releases. `apps.json` is gebundeld met de exe (geen live fetch).

---

## Voltooide versies

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
- ~~**v0.8.9** — Orphan scheduled tasks + firewall rules~~ — gedaan (`ScanOrphanedScheduledTasksAsync` via `schtasks /Query /XML ONE` + XML-parse, filtert `\Microsoft\` system-tasks; `ScanOrphanedFirewallRulesAsync` via PowerShell `Get-NetFirewallApplicationFilter` op rules met dode Program-paths. Beide scans gaan altijd via elevated batch (admin nodig voor `schtasks /Delete` en `Remove-NetFirewallRule`). PathLink_Click opent `taskschd.msc` resp. `wf.msc` — Windows MMC consoles ondersteunen geen deeplink-args dus alleen het console openen, geen specifiek item. Twee nieuwe `OrphanedScheduledTask` + `OrphanedFirewallRule` enum values + UI labels. Scan-leftovers knop runt nu 8 scans parallel)
- **v0.8.9.5** — Hoog-risico leftover types die we apart bouwen voor extra zorgvuldigheid:
  - **Orphaned services** — enumerate via `Get-CimInstance Win32_Service`, check ImagePath bestaat. Strikter filter: alleen orphan als (a) exe-pad dood, (b) service is `Stopped`, (c) StartMode = Manual/Disabled (niet Auto), (d) PathName niet svchost.exe (DLL services), (e) niet in `C:\Windows\` of `C:\Windows\System32\` (system services), (f) geen winget/AppX cross-match. Default unchecked, prominente caution-warning. Click → `services.msc`. Delete via `sc.exe delete <name>` (admin)
  - **HKCU\Software\\<Vendor>\\<App> vendor-residue** — walk HKCU\Software top 2 levels. Per leaf-key: detecteer string-values met path-achtige content (begint met `C:\`, `D:\` of value-name in `["InstallPath", "InstallDir", "Path", "Program", "ExecutablePath"]`). Als ALLE pad-values dead AND geen winget/AppX match → orphan. Skip protected paths (`Software\Microsoft`, `Software\Classes`, `Software\Policies`). Default unchecked. Click → regedit.exe

### v0.9.0 — Tweaks tab

- Windows tweaks UI met toggles per categorie:
  - Explorer: hidden files, file extensions, classic context menu, taskbar align left
  - Privacy: telemetry, ad ID, location tracking
  - Performance: visual effects, startup apps
  - UI: dark mode systeem-wide, accent kleur, transparency
  - Updates: pause N dagen, active hours
- Registry-backed (HKCU / HKLM) met SettingsCard + ToggleSwitch
- Apply / revert (originele waardes onthouden)
- Preset profiles ("Privacy-focused", "Performance", "Minimal UI") als één-klik batches

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
