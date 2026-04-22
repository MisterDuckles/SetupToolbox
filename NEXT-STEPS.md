# WingetAppDeployer - Development Roadmap

## Voltooid (v0.5.0 - Alpha)

- [x] GitHub repository opgezet op `MisterDuckles/WinGetAppDeployer`
- [x] Project gebouwd en getest
- [x] GitHub releases aangemaakt
- [x] Project hernoemd van WinAppInstaller naar WingetAppDeployer
- [x] Alle namespaces en referenties bijgewerkt

---

## Voltooid (v1.0.0)

- [x] Category grid navigatie (klik category -> app lijst -> back)
- [x] 5 themes: Google, Windows 11, Sunset, Ocean Breeze, Aurora
- [x] Light + Dark mode voor alle themes (10 theme files)
- [x] Gradient header per theme
- [x] Gekleurde gradient category cards (elke category eigen kleur)
- [x] Elevated cards met zachte shadows + hover animatie
- [x] Smooth pixel-based scrolling
- [x] Theme-aware Settings, Install, en Schedule windows
- [x] Searchbox gele focus bug gefixed (ARGB kleurformaat)
- [x] Dark mode header/footer contrast verbeterd
- [x] Alle hardcoded kleuren vervangen door DynamicResource
- [x] Klik op gehele app card om te selecteren
- [x] App installed status indicator
- [x] Betere error messages bij gefaalde installaties
- [x] Search functionaliteit (beide schermen)
- [x] Loading indicator tijdens database fetch
- [x] ErrorColor resource voor foutmeldingen
- [x] Versie in exe naam (WingetAppDeployer-v1.0.0.exe)
- [x] GitHub Release v1.0.0 gepubliceerd
- [x] App cards redesign: Apple-stijl emoji icons, card highlight selectie met groen vinkje (Optie A gekozen boven iOS toggle)
- [x] Smooth scroll basis (PreviewMouseWheel)
- [x] Gele searchbox/settings knop definitief gefixed (WPF ARGB kleurformaat #AARRGGBB)
- [x] gh CLI geïnstalleerd voor GitHub releases

---

## Bekende Issues (Te Fixen)

### High Priority
- [x] Exe direct downloaden van GitHub opent niet — gefixed: self-contained exe (bevat .NET runtime)
- [x] Auto-update loop: app detecteert steeds "nieuwe versie" en download zichzelf opnieuw (gefixed: versie komt nu dynamisch uit assembly i.p.v. hardcoded)
- [ ] Subcategorie layout onoverzichtelijk — subcats (IDE & Editors, Version Control, etc.) lopen in elkaar over, moet overzichtelijker. Hier moeten we nog over nadenken hoe dit beter kan. **Geldt ook voor WinUI** (CategoryDetailPage flattent subcats nu — zie WinUI v0.5.0)
- [x] Select All moet ook Deselect All zijn (toggle)
- [x] Auto-update schedule werkt niet — gefixed: `UseShellExecute=true` + `Verb="runas"` geeft nu daadwerkelijk UAC-prompt, met onderscheid tussen geannuleerd en mislukt (v1.1.2)
- [x] Smooth scroll verbeteren — werkt maar voelt nog niet 100% vloeiend (165Hz monitor). Uitzoeken of WPF dit beter kan

### Medium Priority
- [ ] Echte app icons — plan uitdenken zodat elke app een eigen icon krijgt. Mogelijkheden: icons hosten op git repo, URL per app in apps.json, of icon pack downloaden. Nu emoji placeholder per category. **Geldt ook voor WinUI** (alleen Segoe icons per categorie nu, zie WinUI v0.5.0)
- [x] Settings window styling kan nog mooier (buttons, combobox, checkbox niet theme-aware)
- [x] WPF default ComboBox/CheckBox/RadioButton styling lekt door in dark mode
- [x] Searchbox placeholder tekst ("Search apps...")

### Low Priority
- [ ] Welcome banner gradient zou theme-kleuren moeten volgen (niet altijd blauw) — **WPF-only** (WinUI gebruikt native Fluent, geen eigen themes)
- [ ] Category card search: matcht nu op naam, zou ook op app-naam moeten filteren — **Geldt ook voor WinUI** (WinUI heeft überhaupt nog geen search, zie WinUI v0.5.0)
- [ ] Integratie documentatie updaten (INTEGRATIE.md) — verwijst nog naar oude namen/structuur — **Gedeeld** (niet WinUI-specifiek)

---

## Geplande Features

### v1.1.0 - Polish & UI Styling
- [x] App cards redesign: Apple-stijl iconen (afgerond vierkant), card highlight selectie, geen checkbox
- [x] Smooth scroll (basis)
- [x] Fix auto-update versie-check loop
- [x] Select All / Deselect All toggle
- [x] Smooth scroll 165Hz verbeteren (CompositionTarget.Rendering + lerp)
- [x] Placeholder tekst in searchbox ("Search apps...")
- [x] Custom WPF styles voor ComboBox, CheckBox, RadioButton (theme-aware in dark mode)

### v1.2.0 - Subcategorie Redesign & Icons
- [x] Exe direct downloaden van GitHub opent niet — self-contained exe
- [ ] Subcategorie layout redesign — overzichtelijker maken (cards? tabs? accordion?)
- [ ] Echte app icons: plan uitdenken + implementeren (icons op git repo, URL in apps.json)
- [x] Windows theme met echte Mica backdrop, WinUI 3 color tokens, WindowChrome (native Win11 look)
- [x] Google theme verwijderd (Windows theme is nu de default)
- [x] v1.2.1: WPF-UI NuGet integratie + Fluent sandbox PoC (verworpen als UI-pad — zie losse WinUI 3 track hieronder). Sandbox-files staan nog in `Views/Fluent/` voor referentie; WPF-UI package blijft in csproj.

### v1.3.0 - Enhanced UX
- [ ] Auto-update toast notificatie — na een `/autoupdate` run een **native Windows toast** in Action Center tonen ("Alle apps succesvol geüpdatet" / "X apps geüpdatet, Y gefaald"). Nu draait de update volledig stil, user krijgt geen feedback dat de scheduled task heeft gelopen. Implementatie: `CommunityToolkit.WinUI.Notifications` NuGet package (opvolger van `Microsoft.Toolkit.Uwp.Notifications`) — werkt vanuit WPF zonder UWP-packaging, landt in Action Center. **Geldt ook voor WinUI** (zie WinUI v0.9.0)
- [ ] **Global search field in MainWindow** — groot, prominent zoekveld bovenaan de MainWindow (boven de category cards) waarmee je direct op app-naam kan zoeken, ook als je niet weet in welke categorie de app staat. Moet *twee* bronnen doorzoeken:
  1. **Lokale apps.json** — alle apps uit onze gecureerde categorieen (snel, instant results)
  2. **Winget repository** — alle beschikbare apps via `winget search <query>` zodat je ook apps kan vinden en installeren die *niet* in onze categorieen staan. Resultaten moeten duidelijk gelabeld worden (bijv. "In je lijst" vs "Via winget") en via dezelfde install-flow kunnen worden geïnstalleerd.

  Styling: moderne "gave" search box met glassmorphism/frosted glass effect, gradient glow erachter (zie reference image), search icon links, clear icon rechts. Moet theme-aware zijn (werkt met alle 4 themes in light + dark mode). Debounced input (300ms) zodat we niet bij elke toetsaanslag `winget search` aanroepen. **Geldt ook voor WinUI** (zonder glassmorphism — WinUI gebruikt native `AutoSuggestBox`, zie WinUI v0.5.0)
- [ ] App deinstallatie — geinstalleerde apps kunnen verwijderen vanuit de app. Uitzoeken: kan `winget list` detecteren welke apps al geinstalleerd zijn? Zo ja: installed status tonen (checkmark) + uninstall optie aanbieden — **Al gedaan in WinUI** (v0.4.1 installed-badge + v0.4.3 Debloat quick uninstall + v0.6.0 full Debloat)
- [ ] Installation profiles (Gaming, Developer, Office, etc.) — **Geldt ook voor WinUI** (zie WinUI v0.9.0)
- [ ] Parallel installaties (meerdere apps tegelijk) — **Geldt ook voor WinUI** (zie WinUI v0.9.0)
- [ ] Progress bar per app tijdens installatie — **Al gedaan in WinUI** (v0.4.2 determinate bar + v0.4.4 stage ring)
- [ ] Export/Import selectie naar JSON — **Geldt ook voor WinUI** (zie WinUI v0.9.0)
- [ ] Filter opties (alleen popular, al geinstalleerd, etc.) — **Geldt ook voor WinUI** (zie WinUI v0.5.0)
- [ ] Installatie geschiedenis/logs — **Geldt ook voor WinUI** (zie WinUI v0.9.0)
- [ ] Category card search: ook filteren op app-naam binnen categories — **Geldt ook voor WinUI** (onderdeel van WinUI v0.5.0 search werk)

### v1.4.0 - Advanced Features
- [ ] Multi-language support (NL/EN toggle) — **Geldt ook voor WinUI**
- [ ] Fuzzy search in app lijst — **Geldt ook voor WinUI** (onderdeel van search werk)
- [ ] Update checker voor geinstalleerde apps — **Geldt ook voor WinUI** (aparte dashboard-view naast auto-update scheduler)
- [ ] Notifications bij voltooide installaties — **Geldt ook voor WinUI** (overlap met toast notificaties in v1.3.0)
- [ ] Welcome banner styling volgt theme kleuren — **WPF-only** (WinUI heeft native Fluent)

### v1.5.0 - Integratie & Deployment
- [ ] Integratie met Windows11-Unattended-Debloat updaten en testen — **Geldt ook voor WinUI** (past bij WinUI v0.6.0 full Debloat)
- [ ] deploy.ps1 script updaten voor nieuwe exe naam — **WPF-only** (WinUI heeft eigen publish flow, zie WinUI v0.5.0)
- [ ] INTEGRATIE.md bijwerken met nieuwe instructies — **Gedeeld** (documenteert beide apps)

### v2.0.0 - Major Update
- [ ] Plugin systeem voor custom app sources — **Geldt ook voor WinUI**
- [ ] Cloud sync voor settings en app selecties — **Geldt ook voor WinUI**
- [ ] Custom app repositories toevoegen — **Geldt ook voor WinUI**
- [ ] Portable mode (geen installatie nodig) — **Al standaard in WinUI** (unpackaged exe)
- [ ] CLI interface (`winget-deployer install --profile gaming`) — **Geldt ook voor WinUI**
- [ ] Backup/restore functionaliteit — **Geldt ook voor WinUI**

---

## WingetAppDeployer.WinUI — Native Windows 11 App (Parallelle Track)

Apart product, apart project (`src/WingetAppDeployer.WinUI/`), aparte exe, eigen versie-lijn. Echte **WinUI 3 / Windows App SDK** stack — geen WPF, geen lepoco workarounds. Dit wordt de "Windows native" versie van de app. De bestaande WPF app blijft parallel bestaan voor de decoratieve themes (Sunset/OceanBreeze/Aurora) en voor gebruikers die die stijl willen.

**Stack:** .NET 10 + Windows App SDK 1.8 + WinUI 3 + unpackaged exe. DesktopAcrylicController voor backdrop, echte `Microsoft.UI.Xaml` controls (TitleBar, ToggleSwitch, InfoBar, SymbolIcon, NavigationView etc.).

### Feature-parity met WPF app — gap-overzicht

Status per april 2026. Items met ✅ zitten al in WinUI, met ⏳ staan ingepland in de versie, met ❌ nog niet gepland / WPF-only.

| Feature / behavior | WPF | WinUI | Waar in WinUI roadmap |
|---|---|---|---|
| Category grid + navigatie | ✅ | ✅ | v0.3.0 |
| App-selectie + install flow | ✅ | ✅ | v0.4.0 |
| Install progress per app | ✅ | ✅ (stage ring) | v0.4.2 / v0.4.4 |
| Installed-state detectie | ✅ | ✅ | v0.4.1 |
| Schedule dialog + auto-update | ✅ | ✅ | v0.4.5 |
| App uninstall | ✅ (via winget) | ✅ (Debloat quick) | v0.4.3 / v0.6.0 full |
| Search over catalogus (apps.json) | ✅ | ✅ | v0.5.1 |
| Subcategorie grouping | ✅ | ⏳ (nu flattened) | **v0.5.0** |
| Filter opties (popular/installed/all) | ❌ geplaand | ⏳ | **v0.5.0** |
| Echte app icons (per app) | ❌ geplaand | ⏳ (nu alleen Segoe cat-icons) | **v0.5.0** (shared plan) |
| Settings persistence (JSON file) | ✅ | ⏳ | **v0.8.0** |
| App self-update check (GitHub) | ✅ | ⏳ | **v0.8.0** |
| Welcome banner (dismissible) | ✅ | ⏳ | **v0.8.0** |
| Theme selector (Sunset/Aurora/...) | ✅ | ❌ **WPF-only** | niet porten — WinUI gebruikt native Fluent + systeem-theme |
| Post-install "schedule?" prompt | ✅ | ⏳ | **v0.9.0** |
| Toast notificatie bij silent update | ❌ geplaand | ⏳ | **v0.9.0** |
| Parallel installaties | ❌ geplaand | ⏳ | **v0.9.0** |
| Installation profiles (Gaming/...) | ❌ geplaand | ⏳ | **v0.9.0** |
| Export/Import selectie | ❌ geplaand | ⏳ | **v0.9.0** |
| Install history / log | ❌ geplaand | ⏳ | **v0.9.0** |
| Full Debloat (Windows bloatware) | ❌ | ⏳ | **v0.6.0** |
| Tweaks tab (registry toggles) | ❌ | ⏳ | **v0.7.0** |
| Smooth scroll custom physics | ✅ (165Hz lerp) | ❌ | niet porten — WinUI ScrollViewer is al native vloeiend |
| Multi-language (NL/EN) | ❌ geplaand | ⏳ | v1.4.0 shared |
| Winget-repo search (buiten catalogus) | ❌ geplaand | ⏳ | **v0.5.0** |
| Fuzzy search (typo/afkortingen) | ❌ geplaand | ⏳ | **v0.5.0** |
| `/fluentsandbox` arg | ✅ (WPF legacy) | ❌ **WPF-only** | niet porten — sandbox was WPF-UI experiment, WinUI ís het native pad |

### v0.1.0 - Sandbox foundation (huidig)
- [x] Nieuw WinUI 3 project aangemaakt + toegevoegd aan solution
- [x] Unpackaged exe config (`WindowsPackageType=None`)
- [x] MainWindow met native Fluent TitleBar, content stack, ToggleSwitch, Accent button, Default button, SymbolIcon, InfoBar
- [x] DesktopAcrylicController met `DesktopAcrylicKind.Thin` (via officiële Microsoft snippet + WindowsSystemDispatcherQueueHelper)
- [x] Build + run verified op .NET 10

### v0.2.0 - NavigationView shell
- [x] NavigationView sidebar: Apps / Tweaks / Debloat / Settings (identiek aan WinUI 3 Gallery patroon)
- [x] Drie lege Pages als routing targets
- [x] Settings item via `FooterMenuItems` + custom Settings page
- [x] AppTitleBar drag regions goed afgestemd

### v0.3.0 - Apps pagina met categorieën
- [x] Port van de categorie-data uit `apps.json` (via gedeelde Models — eventueel via nieuw `WingetAppDeployer.Core` class library of initieel duplicate)
- [x] Settings-style category rows (`SettingsCard` / CardExpander) ipv kleurige tile grid
- [x] Click → detail page met app list + selectie

### v0.3.1 - MicaBackdrop polish
- [x] MicaBackdrop BaseAlt + simplified backdrop code

### v0.4.0 - Install flow + winget integratie
- [x] WingetService porten / hergebruiken
- [x] Install dialog met echte Fluent `ProgressBar` + log
- [x] Schedule dialog voor auto-update

### v0.4.1 - Install UX polish + installed-state detectie
- [x] InstallDialog redesign — overall progress bar weg, per-app indeterminate bar, live winget stdout line ("97.3 MB / 154 MB")
- [x] DispatcherQueue-marshalling voor `Progress<T>` callbacks (WinUI 3 Desktop heeft niet altijd een SyncContext op de UI-thread)
- [x] Expliciete `Visibility`-typed binding properties (bool->Visibility implicit via x:Bind kan flaky zijn)
- [x] `WingetService.GetInstalledAppIdsAsync()` — parseert `winget list`, gecachet, `forceRefresh` na install batch
- [x] "Installed" badge in CategoryDetailPage met groene achtergrond + checkmark glyph
- [x] Auto-refresh installed state na install-batch

### v0.4.2 - Install dialog 3-state progress per app
- [x] Pending apps tonen kleine `ProgressRing` (20px, spinning) — visuele "wachten in queue" indicator
- [x] Installing zonder percentage: indeterminate `ProgressBar`
- [x] Installing met percentage: determinate `ProgressBar` met `Value` uit geparste "X MB / Y MB" winget output
- [x] Percentage parser: regex met unit normalization (B/KB/MB/GB), clamp op [0,1]
- [x] `HasProgress` flag — zodra eerste percentage binnenkomt flipt de bar van indeterminate naar determinate, blijft daarna determinate (install phase na download emit geen percentages)
- [x] Install dialog card-based layout — elke app in een Border met CardBackground, bredere bar (Stretch), 15px app-naam, message alleen tijdens Installing (verdwijnt bij Success/Failed)
- [x] Split Visibility props (RingVisibility / CheckVisibility / ErrorVisibility / Indeterminate / Determinate) zodat x:Bind OneWay altijd correct re-evalueert
- [x] Theme-aware success/error brushes (SystemFillColorSuccessBrush / SystemFillColorCriticalBrush) i.p.v. hardcoded RGB

### v0.4.3 - Quick uninstall onder Debloat (tijdelijk)
- [x] Simpele lijst van geïnstalleerde apps uit onze `apps.json` catalogus onder Debloat tab
- [x] Per app een Uninstall button met bevestigings-ContentDialog
- [x] `WingetService.UninstallAppAsync()` via `winget uninstall --id <id> --silent --accept-source-agreements`
- [x] Refresh installed-apps lijst na elke uninstall
- **Note:** Dit is een tijdelijke minimal-viable versie zodat user apps kan verwijderen tijdens testen. Wordt later vervangen door de full Debloat-pagina (zie v0.6.0 hieronder).

### v0.4.4 - Install dialog stage ring + Fluent polish
- [x] 4-stage ProgressRing (60×60) rechts per app: 1/4 Downloading → 2/4 Verifying → 3/4 Installing → 4/4 Done
- [x] Indeterminate ProgressBar altijd zichtbaar tijdens install (= "er gebeurt iets", geen vals 100%-gevoel)
- [x] Stage-parsing uit winget stdout ("installer hash" → 2, "Starting package install" → 3)
- [x] Char-per-char stream reader in `WingetService.RunWingetCommandAsync` die ook op `\r` splitst — winget overschrijft z'n download-progressbar met carriage returns, default `OutputDataReceived` miste daardoor alle live updates
- [x] ContentDialog sizing via `ContentDialogMinWidth`/`ContentDialogMaxWidth` resource keys (attributen op element werken niet)
- [x] Afgeronde hoeken via `CornerRadius="8"` attribuut op de ContentDialog
- [x] Vast 120px kolom voor de stage ring zodat die niet van scherm afvalt

### v0.4.5 - Schedule dialog voor auto-update
- [x] `TaskSchedulerService` geport (WinUI-specifieke taaknaam `WingetAppDeployer_WinUI_AutoUpdate` zodat 'ie niet botst met de WPF-taak)
- [x] `WingetService.UpdateAllAppsAsync()` via `winget upgrade --all --silent`
- [x] `/autoupdate` command-line handler in `App.xaml.cs` — runt update, `Environment.Exit(0)` zonder window te openen
- [x] `ScheduleDialog` met Daily/Weekly/OnStartup radio's + TimePicker (verbergt bij OnStartup)
- [x] Fout-afhandeling via `InfoBar` in de dialog — UserCancelled (UAC geweigerd) en Failed krijgen elk hun eigen bericht
- [x] SettingsPage met status-card: toont of taak actief is + Set up / Change / Disable knoppen
- [x] `App.TaskScheduler` singleton alongside `App.Database` / `App.Winget`

### v0.5.0 - Polish + release (feature parity met WPF)
**Doel:** dichten van de grootste gaten t.o.v. de WPF app, dan de eerste public release.

- [x] ~~Segoe Fluent Icons per categorie~~ — **verworpen.** Kort geprobeerd (Globe/Code/Shield/…) maar emoji's ogen beter: kleurrijk, herkenbaarder op het eerste oog, en Windows 11 Settings/Start/File Explorer gebruikt zelf óók colorful emoji voor content-categorisatie (Fluent glyphs zijn meer voor UI chrome). apps.json blijft leidend, emoji rendert als Segoe UI Emoji
- [ ] **Echte app icons per app** — shared met WPF v1.2.0. Plan uitwerken: icons op git repo / URL-veld in apps.json / icon pack download. Fallback op categorie-glyph als geen icon beschikbaar
- [x] **AutoSuggestBox search over de catalogus** in AppsPage (bovenaan, filter de categorie-grid) en in CategoryDetailPage (filter de app-lijst). Matcht op categorie-naam + app-naam + beschrijving + winget ID uit onze eigen `apps.json`. Category-match slaat ook aan wanneer een app in die categorie matcht (search "chrome" → Browsers card blijft)
- [x] **Search-uitbreiding naar de volledige winget repository** — resultaten die niet in onze `apps.json` staan verschijnen onder een aparte "Results from winget repository" sectie op AppsPage. Gebruikt `winget search <query> --source winget`, char-per-char stream reader voor live progress, debounced 300ms met epoch-check zodat oudere calls niet overschrijven wat de newer call oplevert. Duplicaten met de catalog worden gefilterd. Selectie integreert via `SelectionHelper.ExtraSelectedApps` (synthetische App-objecten naast de catalog) — telt mee in de globale footer en gaat mee met "Install selected apps"
- [x] **Fuzzy search algoritme** — `FuzzySharp` NuGet (2.0.2), `WeightedRatio` (combineert token-set + partial + full). `Helpers/FuzzyMatcher.cs` scoort query tegen Name + Description + WingetId, neemt het maximum, threshold 55/100. Exacte case-insensitive substring match shortcut naar score 100. Results gesort op score DESC, tie-breaker op naam. Toegepast op AppsPage catalog-results en CategoryDetailPage filter. Winget-repo resultaten niet gefuzzyt (winget CLI doet z'n eigen matching al).

### v0.5.2 - Search UX + klikbare cards (gedaan)
- [x] Search-modus: categorie-grid verdwijnt, platte "In your curated list" + "Results from winget repository" secties tonen matchende apps direct zonder categorie-drill
- [x] Klik op een sidebar-item "Apps" (via `NavigationView.ItemInvoked`, fired ook op re-click van het geselecteerde item): reset naar rootbeeld (clear search) of navigeer terug uit CategoryDetailPage
- [x] Hele app-card is klikbaar om te (de)selecteren (niet alleen de CheckBox-hitbox). CheckBox op `IsHitTestVisible="False"`, Grid heeft `Tapped` handler. `Tag="{x:Bind}"` in de DataTemplate zodat het App-object betrouwbaar beschikbaar is (DataContext is onder ItemsRepeater + x:DataType niet altijd gezet)
- [x] Subtiele hover-kleur op cards via `PointerEntered`/`Exited` → `CardBackgroundFillColorSecondaryBrush`
- [x] Padding rechts van scrollbar (14px) zodat cards niet tegen de scrollbar aan plakken
- [x] **Globale selectie footer** (bug-fix): selectie wordt nu cross-category geteld en geïnstalleerd. `SelectionHelper` service centraliseert tellen/ophalen/clearen over de hele AppDatabase. Footer met "X apps selected" + "Clear all" + "Install selected apps" staat nu op AppsPage EN CategoryDetailPage, beide tonen dezelfde globale count. Install installeert alle selected apps over álle categorieën, niet alleen de huidige. Select all respecteert de actieve search-filter (lokaal), de footer telt globaal
- [ ] **Subcategorie grouping in CategoryDetailPage** — CategoryDetailPage flattent nu de subcategorieën (zie comment "v0.3.0" in `CategoryDetailPage.xaml.cs` rond regel 34-35). Port de subcat-layout: `Expander` of headered section per subcat ("IDE & Editors", "Version Control") met de apps eronder. Hangt samen met het "Subcategorie layout onoverzichtelijk" issue in de WPF track
- [ ] **Filter opties** (popular / installed / all) boven de app-lijst op CategoryDetailPage — shared met WPF v1.3.0
- [ ] Self-contained publish configuratie (`dotnet publish -r win-x64 --self-contained`)
- [ ] Eigen GitHub release artifact (aparte release-lijn naast de WPF releases, bijv `WingetAppDeployer.WinUI-v0.5.0.exe`)
- [ ] Eventueel `WingetAppDeployer.Core/` extractie van Models+Services zodat WPF en WinUI shared code gebruiken

### v0.6.0 - Debloat tab (full implementation)
- [ ] **Windows bloatware removal** — lijst van Microsoft "standaard" bloat apps die in Win11 meekomen (Xbox, Teams consumer, Solitaire, Weather, etc.) met checkboxes en "Remove selected" batch-actie. Gebruikt `Get-AppxPackage | Remove-AppxPackage` (PowerShell) of `winget uninstall`. Moet runnen als admin.
- [ ] **User-installed apps uninstaller** — vervanger voor de tijdelijke v0.4.3 lijst. Toont alle geïnstalleerde apps uit onze `apps.json` catalogus in een nette card-based layout met multi-select + batch uninstall, zelfde stijl als de install flow. Progress per app zoals bij install.
- [ ] **Categorieën in Debloat** — Microsoft apps / OEM bloat / User installed, met counts per categorie
- [ ] Integratie met bestaande Windows11-Unattended-Debloat logica (scripts hergebruiken of naar C# porten)
- [ ] **"ALLES op de PC" search** — niet alleen winget-geïnstalleerde apps, maar écht alle geïnstalleerde programma's op het systeem. Bronnen combineren:
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` + `HKCU\...\Uninstall` + `HKLM\...\Wow6432Node\...\Uninstall` (klassieke Win32 apps, met `DisplayName`, `UninstallString`, `InstallLocation`, `Publisher`)
  - `Get-AppxPackage` (UWP/Store apps, incl. bloat)
  - `winget list` (voor winget-beheerde apps zodat we kunnen onderscheiden welke via winget te verwijderen zijn)
  - Debounced search-box bovenaan die over alle bronnen matcht (naam, publisher, install location)
  - Tag per resultaat waar het vandaan komt ("Winget", "UWP", "Win32 registry") zodat de juiste uninstall-methode gekozen wordt
- [ ] **Restant-opruiming bij uninstall** — na een `winget uninstall` / `Remove-AppxPackage` / registry-uninstaller laten uitvoeren, scan het systeem op achtergebleven sporen en bied aan ze op te ruimen:
  - Registry keys: `HKLM\SOFTWARE\<Publisher>\<AppName>`, `HKCU\SOFTWARE\<Publisher>\<AppName>`, installer cache in `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Products`, `RunOnce` / startup-entries van de app
  - Bestanden: `%ProgramFiles%\<Publisher>\<AppName>`, `%ProgramFiles(x86)%\<Publisher>\<AppName>`, `%LocalAppData%\<AppName>`, `%AppData%\<AppName>`, `%ProgramData%\<AppName>`, `%Temp%\<AppName>*`
  - Scheduled tasks in `\Microsoft\<Publisher>\...` of met de app-naam in de path
  - Services die nog geregistreerd staan (`sc queryex state=all` filteren op `DisplayName`/`PathName` die naar de install-locatie wijst)
  - UI: na een succesvolle uninstall een ContentDialog "We found leftover traces" met per item een checkbox (registry key X, folder Y, service Z) + "Clean up" knop. Altijd admin-prompt, altijd preview voordat er gewist wordt, nooit auto-delete. Loggen in een install-history file wat er weg is gehaald

### v0.7.0 - Tweaks tab
- [ ] **Windows tweaks UI** — toggles en knoppen voor veelgebruikte Windows customizations:
  - Explorer: show hidden files, show file extensions, classic context menu, taskbar align left
  - Privacy: telemetry uit, ad ID uit, location tracking uit
  - Performance: visual effects (best performance / balanced), disable startup apps
  - UI: dark mode systeem-wide, accent kleur, transparency effects
  - Updates: pause updates voor N dagen, active hours
- [ ] **Registry-backed** — elke tweak is een `HKCU` of `HKLM` registry edit; groepeer per categorie in `SettingsCard`-stijl rows met ToggleSwitch
- [ ] **Apply/revert** — tweaks onthouden wat de originele waarde was zodat user kan terugdraaien
- [ ] **Preset profiles** — "Privacy-focused", "Performance", "Minimal UI" als één-klik batches

### v0.8.0 - Settings + app self-update
**Gap fix:** WPF heeft al persistente app-settings en een update-checker voor de app zelf. WinUI heeft alleen de scheduled-task card in de Settings tab en geen update-check.

- [ ] **`SettingsService` port** — JSON-backed settings file (bijv. `%LOCALAPPDATA%\WingetAppDeployer.WinUI\settings.json`). Velden die *wél* relevant zijn voor WinUI:
  - `CheckForUpdatesOnStartup` (bool, default true)
  - `ShowWelcomeBanner` (bool, default true)
  - `AutoUpdateEnabled` + `AutoUpdateSchedule` (kan nu al uit TaskScheduler gelezen worden, maar persist ook hier voor snelle check zonder schtasks-call)
  - **Niet porten:** `Theme` / `DarkMode` (WinUI volgt het Windows systeem-theme native via Mica)
- [ ] **`GitHubService` port** — check `api.github.com/repos/MisterDuckles/WinGetAppDeployer/releases/latest` op startup, vergelijk met `Assembly.GetExecutingAssembly().GetName().Version`. Download + launch nieuwe exe via launcher-pattern (zoals WPF doet)
- [ ] **Welcome banner** op AppsPage (bovenaan, dismissible via X en via `ShowWelcomeBanner` setting). Korte uitleg wat de app doet + link naar repo
- [ ] **Update-beschikbaar InfoBar** in MainWindow (boven de NavigationView) wanneer GitHubService nieuwe release vindt — met "Update now" knop
- [ ] **Settings UI uitbreiden** met toggles voor:
  - ToggleSwitch "Check for updates on startup"
  - ToggleSwitch "Show welcome banner on Apps page"
  - Button "Check for updates now"

### v0.9.0 - Install flow UX polish
**Gap fix:** overgebleven UX-items uit de WPF v1.3.0/v1.4.0 roadmap die ook voor WinUI gelden.

- [ ] **Post-install "Schedule auto-updates?" prompt** — na een succesvolle InstallDialog-run (als er nog geen scheduled task is) een ContentDialog tonen met "Wil je voortaan automatisch updates laten draaien?" → opent de ScheduleDialog. WPF heeft dit (InstallWindow.xaml.cs rond regel 607-618)
- [ ] **Toast notificatie** na `/autoupdate` — `CommunityToolkit.WinUI.Notifications` NuGet package. Toont in Action Center: "WingetAppDeployer: X apps geüpdatet" (bij success) of "Y gefaald" (bij failure). Nu draait de silent update 100% stil
- [ ] **Parallel installaties** (optioneel, met warning) — nu draait `InstallAppsAsync` strict sequentieel. Overweeg een `MaxParallelism=2` optie in settings zodat 2 apps tegelijk geïnstalleerd kunnen worden. Complexiteit: winget lockt per package, maar globale concurrent runs zijn meestal safe
- [ ] **Installation profiles** (Gaming / Developer / Office / Productivity) — preset-selecties. Ofwel hardcoded in apps.json (extra `profiles` section), ofwel als aparte `profiles.json`. Eén klik = alle apps in die profile geselecteerd
- [ ] **Export/Import selectie naar JSON** — user kan huidige selectie opslaan als `my-apps.json` en later importeren, bijv. voor verse installs op nieuwe machines
- [ ] **Installatie geschiedenis/log** — append-log file (`%LOCALAPPDATA%\WingetAppDeployer.WinUI\install-history.log`) met timestamp + app + outcome. Settings pagina heeft een "View install history" button die de log in-app toont
- [ ] **Notificaties bij voltooide install** — overlap met toast item hierboven; kan ook gewoon de native `AppNotificationBuilder` zijn na elke InstallDialog-sessie

### Out of scope voor dit track
- Decoratieve themes (Sunset/Aurora/OceanBreeze) — exclusief in de WPF app, nooit in WinUI 3
- MSIX packaging — mogelijk later, voor nu unpackaged
- Integratie in de bestaande `Launcher/` — voor nu los te downloaden

---

## Development Notes

### Quick Commands

```bash
# Build solution
dotnet build WingetAppDeployer.sln -c Debug

# Publish executables
dotnet publish src/WingetAppDeployer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./release
dotnet publish src/Launcher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./release

# Create GitHub release
gh release create v1.x.x ./release/WingetAppDeployer-v1.x.x.exe ./release/Launcher.exe --title "WingetAppDeployer v1.x.x"
```

### Project Structure

```
src/
├── WingetAppDeployer/           # Main WPF application (decorative themes)
│   ├── Models/                  # Data models (App, Category, Settings)
│   ├── Services/                # Business logic (Winget, GitHub, Settings, TaskScheduler)
│   ├── Views/                   # XAML windows (Install, Settings, Schedule)
│   │   └── Fluent/              # Legacy WPF-UI sandbox (reference only, approach dropped)
│   ├── Themes/                  # Theme files (light/dark for each)
│   │   ├── WindowsLight.xaml / WindowsDark.xaml
│   │   ├── SunsetLight.xaml / SunsetDark.xaml
│   │   ├── OceanBreezeLight.xaml / OceanBreezeDark.xaml
│   │   └── AuroraLight.xaml / AuroraDark.xaml
│   ├── Helpers/MicaHelper.cs    # P/Invoke Mica backdrop for Windows theme
│   └── MainWindow.xaml          # Main UI (category grid + app list)
├── WingetAppDeployer.WinUI/     # NEW: native Windows 11 app (WinUI 3 + WinAppSDK 1.8)
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / .xaml.cs         # DesktopAcrylicController (Thin)
│   ├── WindowsSystemDispatcherQueueHelper.cs
│   ├── app.manifest + Package.appxmanifest
│   └── Assets/                  # Store icons + AppIcon.ico
└── Launcher/                    # Bootstrap launcher (WPF app only)
    └── Program.cs               # Downloads & launches main app
```

### Theme Resource Keys

Alle UI-elementen gebruiken DynamicResource. Beschikbare keys:
- `BackgroundColor`, `SurfaceColor`, `CardHoverColor`, `FooterBackgroundColor`
- `PrimaryColor`, `PrimaryDarkColor`, `PrimaryLightColor`, `AccentColor`
- `TextPrimaryColor`, `TextSecondaryColor`
- `BorderColor`, `ErrorColor`
- `HeaderBackground` (LinearGradientBrush), `HeaderTextColor`
- `SearchBoxBackgroundColor`, `SearchBoxBorderColor`
- `CategoryCardBg`, `CategoryCardBorder`
- `CardCornerRadius`, `CardShadowDepth`, `CardShadowBlur`, `CardShadowOpacity`, `CategoryCornerRadius`
- Styles: `MaterialDesignFlatButton`, `MaterialDesignRaisedButton`, `SelectAllButtonStyle`, `BackButtonStyle`, `MaterialDesignCircularProgressBar`

---

## Project Stats

- **5 themes** met light + dark mode (10 theme files)
- **8 categorieen** met 200+ apps
- **~3500 regels code**
- **~35 bestanden**
