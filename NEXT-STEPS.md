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
- Wordt in v0.7.0 vervangen door full Debloat-pagina

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

### v0.7.0 — Debloat tab full

- Windows bloatware removal — Microsoft "standaard" bloat (Xbox, Teams consumer, Solitaire, etc.) met checkboxes + batch-actie via `Get-AppxPackage | Remove-AppxPackage` of `winget uninstall`. Vereist admin
- User-installed apps uninstaller — vervanger voor v0.4.3 lijst, card-based met multi-select + batch + per-app progress
- Categorieën in Debloat: Microsoft apps / OEM bloat / User installed met counts
- Integratie met Windows11-Unattended-Debloat logica (scripts hergebruiken of porten)
- "ALLES op de PC" search — combineert registry uninstall keys + `Get-AppxPackage` + `winget list` met source-tag per resultaat
- Restant-opruiming bij uninstall — scan registry / Program Files / AppData / Temp / scheduled tasks / services voor leftover sporen, ContentDialog met checkboxes per item, altijd preview, nooit auto-delete

### v0.8.0 — Tweaks tab

- Windows tweaks UI met toggles per categorie:
  - Explorer: hidden files, file extensions, classic context menu, taskbar align left
  - Privacy: telemetry, ad ID, location tracking
  - Performance: visual effects, startup apps
  - UI: dark mode systeem-wide, accent kleur, transparency
  - Updates: pause N dagen, active hours
- Registry-backed (HKCU / HKLM) met SettingsCard + ToggleSwitch
- Apply / revert (originele waardes onthouden)
- Preset profiles ("Privacy-focused", "Performance", "Minimal UI") als één-klik batches

### v0.9.0 — Settings + app self-update

- `SettingsService` — JSON-backed settings file (`%LOCALAPPDATA%\WingetAppDeployer.WinUI\settings.json`):
  - `CheckForUpdatesOnStartup` (default true)
  - `ShowWelcomeBanner` (default true)
  - `AutoUpdateEnabled` + `AutoUpdateSchedule` (mirror van TaskScheduler)
- `GitHubService` — check `api.github.com/repos/.../releases/latest` op startup, vergelijk met assembly version, download + launch nieuwe exe via launcher-pattern
- Welcome banner op AppsPage (dismissible via X en setting)
- Update-beschikbaar InfoBar in MainWindow met "Update now" knop
- Settings UI uitbreiden met ToggleSwitches + "Check for updates now" button

### v0.10.0 — Install flow UX polish + Launcher port

- WinUI Launcher: kleine bootstrap exe (~5KB) die de full app downloadt naar `%ProgramFiles%`. Nodig voor Windows11-Unattended-Debloat integratie waarin we niet de hele 80MB app via firstlogon willen pushen
- Post-install "Schedule auto-updates?" prompt — ContentDialog na succesvolle InstallDialog als er nog geen task is
- Toast notificatie na `/autoupdate` via `CommunityToolkit.WinUI.Notifications` — landt in Action Center
- Parallel installaties (optioneel) — `MaxParallelism=2` in settings, 2 apps tegelijk
- Installation profiles (Gaming / Developer / Office / Productivity) — preset-selecties via extra section in apps.json of aparte profiles.json
- Export/Import selectie naar JSON — `my-apps.json` voor verse installs op nieuwe machines
- Installatie geschiedenis / log — append-log in `%LOCALAPPDATA%`, "View install history" in Settings
- "Fallback to download page" toggle — apps die niet op winget staan (VMware Workstation Pro, ON1 Photo RAW, Nvidia App, etc.) krijgen een `downloadUrl` veld; install-knop opent vendor-pagina met "Manual download" badge

### Latere milestones

**v1.0.0 — eerste stable release**
- Self-update via GitHub (v0.9.0) werkt
- Launcher (v0.10.0) werkt voor unattended-debloat integratie
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
