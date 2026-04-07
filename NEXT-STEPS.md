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
- [x] Auto-update loop: app detecteert steeds "nieuwe versie" en download zichzelf opnieuw (gefixed: versie komt nu dynamisch uit assembly i.p.v. hardcoded)
- [ ] Subcategorie layout onoverzichtelijk — subcats (IDE & Editors, Version Control, etc.) lopen in elkaar over, moet overzichtelijker. Hier moeten we nog over nadenken hoe dit beter kan
- [x] Select All moet ook Deselect All zijn (toggle)
- [ ] Auto-update schedule werkt niet — bij aanmaken task admin elevation nodig (UAC prompt)
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

### v1.1.0 - Polish & Subcategorie Redesign
- [x] App cards redesign: Apple-stijl iconen (afgerond vierkant), card highlight selectie, geen checkbox
- [x] Smooth scroll (basis)
- [x] Fix auto-update versie-check loop
- [x] Select All / Deselect All toggle
- [ ] Subcategorie layout redesign — overzichtelijker maken (cards? tabs? accordion?)
- [x] Smooth scroll 165Hz verbeteren (CompositionTarget.Rendering + lerp)
- [ ] Echte app icons: plan uitdenken + implementeren (icons op git repo, URL in apps.json)
- [x] Placeholder tekst in searchbox ("Search apps...")
- [x] Custom WPF styles voor ComboBox, CheckBox, RadioButton (theme-aware in dark mode)
- [ ] Windows theme meer laten lijken op Windows 11 Settings (referentie: Win11Debloat)

### v1.2.0 - Enhanced UX
- [ ] App deinstallatie — geinstalleerde apps kunnen verwijderen vanuit de app. Uitzoeken: kan `winget list` detecteren welke apps al geinstalleerd zijn? Zo ja: installed status tonen (checkmark) + uninstall optie aanbieden
- [ ] Installation profiles (Gaming, Developer, Office, etc.)
- [ ] Parallel installaties (meerdere apps tegelijk)
- [ ] Progress bar per app tijdens installatie
- [ ] Export/Import selectie naar JSON
- [ ] Filter opties (alleen popular, al geinstalleerd, etc.)
- [ ] Installatie geschiedenis/logs
- [ ] Category card search: ook filteren op app-naam binnen categories

### v1.3.0 - Advanced Features
- [ ] Multi-language support (NL/EN toggle)
- [ ] Fuzzy search in app lijst
- [ ] Update checker voor geinstalleerde apps
- [ ] Notifications bij voltooide installaties
- [ ] Welcome banner styling volgt theme kleuren

### v1.4.0 - Integratie & Deployment
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

## Development Notes

### Quick Commands

```bash
# Build solution
dotnet build WingetAppDeployer.sln -c Debug

# Publish executables
dotnet publish src/WingetAppDeployer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./release
dotnet publish src/Launcher -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./release

# Create GitHub release
gh release create v1.x.x ./release/WingetAppDeployer-v1.x.x.exe ./release/Launcher.exe --title "WingetAppDeployer v1.x.x"
```

### Project Structure

```
src/
├── WingetAppDeployer/           # Main WPF application
│   ├── Models/                  # Data models (App, Category, Settings)
│   ├── Services/                # Business logic (Winget, GitHub, Settings, TaskScheduler)
│   ├── Views/                   # XAML windows (Install, Settings, Schedule)
│   ├── Themes/                  # 10 theme files (5 themes x light/dark)
│   │   ├── GoogleLight.xaml
│   │   ├── GoogleDark.xaml
│   │   ├── WindowsLight.xaml
│   │   ├── WindowsDark.xaml
│   │   ├── SunsetLight.xaml
│   │   ├── SunsetDark.xaml
│   │   ├── OceanBreezeLight.xaml
│   │   ├── OceanBreezeDark.xaml
│   │   ├── AuroraLight.xaml
│   │   └── AuroraDark.xaml
│   └── MainWindow.xaml          # Main UI (category grid + app list)
└── Launcher/                    # Bootstrap launcher
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
