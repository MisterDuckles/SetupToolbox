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
- [ ] Subcategorie layout onoverzichtelijk — subcats (IDE & Editors, Version Control, etc.) lopen in elkaar over, moet overzichtelijker. Hier moeten we nog over nadenken hoe dit beter kan
- [x] Select All moet ook Deselect All zijn (toggle)
- [x] Auto-update schedule werkt niet — gefixed: `UseShellExecute=true` + `Verb="runas"` geeft nu daadwerkelijk UAC-prompt, met onderscheid tussen geannuleerd en mislukt (v1.1.2)
- [x] Smooth scroll verbeteren — werkt maar voelt nog niet 100% vloeiend (165Hz monitor). Uitzoeken of WPF dit beter kan

### Medium Priority
- [ ] Echte app icons — plan uitdenken zodat elke app een eigen icon krijgt. Mogelijkheden: icons hosten op git repo, URL per app in apps.json, of icon pack downloaden. Nu emoji placeholder per category
- [x] Settings window styling kan nog mooier (buttons, combobox, checkbox niet theme-aware)
- [x] WPF default ComboBox/CheckBox/RadioButton styling lekt door in dark mode
- [x] Searchbox placeholder tekst ("Search apps...")

### Low Priority
- [ ] Welcome banner gradient zou theme-kleuren moeten volgen (niet altijd blauw)
- [ ] Category card search: matcht nu op naam, zou ook op app-naam moeten filteren
- [ ] Integratie documentatie updaten (INTEGRATIE.md) — verwijst nog naar oude namen/structuur

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
- [ ] Auto-update toast notificatie — na een `/autoupdate` run een **native Windows toast** in Action Center tonen ("Alle apps succesvol geüpdatet" / "X apps geüpdatet, Y gefaald"). Nu draait de update volledig stil, user krijgt geen feedback dat de scheduled task heeft gelopen. Implementatie: `CommunityToolkit.WinUI.Notifications` NuGet package (opvolger van `Microsoft.Toolkit.Uwp.Notifications`) — werkt vanuit WPF zonder UWP-packaging, landt in Action Center
- [ ] **Global search field in MainWindow** — groot, prominent zoekveld bovenaan de MainWindow (boven de category cards) waarmee je direct op app-naam kan zoeken, ook als je niet weet in welke categorie de app staat. Moet *twee* bronnen doorzoeken:
  1. **Lokale apps.json** — alle apps uit onze gecureerde categorieen (snel, instant results)
  2. **Winget repository** — alle beschikbare apps via `winget search <query>` zodat je ook apps kan vinden en installeren die *niet* in onze categorieen staan. Resultaten moeten duidelijk gelabeld worden (bijv. "In je lijst" vs "Via winget") en via dezelfde install-flow kunnen worden geïnstalleerd.

  Styling: moderne "gave" search box met glassmorphism/frosted glass effect, gradient glow erachter (zie reference image), search icon links, clear icon rechts. Moet theme-aware zijn (werkt met alle 4 themes in light + dark mode). Debounced input (300ms) zodat we niet bij elke toetsaanslag `winget search` aanroepen.
- [ ] App deinstallatie — geinstalleerde apps kunnen verwijderen vanuit de app. Uitzoeken: kan `winget list` detecteren welke apps al geinstalleerd zijn? Zo ja: installed status tonen (checkmark) + uninstall optie aanbieden
- [ ] Installation profiles (Gaming, Developer, Office, etc.)
- [ ] Parallel installaties (meerdere apps tegelijk)
- [ ] Progress bar per app tijdens installatie
- [ ] Export/Import selectie naar JSON
- [ ] Filter opties (alleen popular, al geinstalleerd, etc.)
- [ ] Installatie geschiedenis/logs
- [ ] Category card search: ook filteren op app-naam binnen categories

### v1.4.0 - Advanced Features
- [ ] Multi-language support (NL/EN toggle)
- [ ] Fuzzy search in app lijst
- [ ] Update checker voor geinstalleerde apps
- [ ] Notifications bij voltooide installaties
- [ ] Welcome banner styling volgt theme kleuren

### v1.5.0 - Integratie & Deployment
- [ ] Integratie met Windows11-Unattended-Debloat updaten en testen
- [ ] deploy.ps1 script updaten voor nieuwe exe naam
- [ ] INTEGRATIE.md bijwerken met nieuwe instructies

### v2.0.0 - Major Update
- [ ] Plugin systeem voor custom app sources
- [ ] Cloud sync voor settings en app selecties
- [ ] Custom app repositories toevoegen
- [ ] Portable mode (geen installatie nodig)
- [ ] CLI interface (`winget-deployer install --profile gaming`)
- [ ] Backup/restore functionaliteit

---

## WingetAppDeployer.WinUI — Native Windows 11 App (Parallelle Track)

Apart product, apart project (`src/WingetAppDeployer.WinUI/`), aparte exe, eigen versie-lijn. Echte **WinUI 3 / Windows App SDK** stack — geen WPF, geen lepoco workarounds. Dit wordt de "Windows native" versie van de app. De bestaande WPF app blijft parallel bestaan voor de decoratieve themes (Sunset/OceanBreeze/Aurora) en voor gebruikers die die stijl willen.

**Stack:** .NET 10 + Windows App SDK 1.8 + WinUI 3 + unpackaged exe. DesktopAcrylicController voor backdrop, echte `Microsoft.UI.Xaml` controls (TitleBar, ToggleSwitch, InfoBar, SymbolIcon, NavigationView etc.).

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
- [ ] Schedule dialog voor auto-update

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

### v0.5.0 - Polish + release
- [ ] Segoe Fluent Icons per categorie
- [ ] Self-contained publish configuratie (`dotnet publish -r win-x64 --self-contained`)
- [ ] Eigen GitHub release artifact
- [ ] Eventueel `WingetAppDeployer.Core/` extractie van Models+Services

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
