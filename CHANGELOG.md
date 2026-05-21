# Changelog

All notable changes to SetupToolbox (WinUI). Vorige WPF-historie staat
gearchiveerd onder de git tag `wpf-final-v1.2.1`.

## [Unreleased] — v0.5.x

### v0.5.12 — Self-contained publish configuratie (2026-04-30)

- `WindowsAppSDKSelfContained=true` in alle drie publish profiles —
  exe bundelt nu de WinAppSDK runtime, geen aparte installer nodig op
  doelmachine
- `PublishTrimmed` uit: trimming brak `JsonSerializer.Deserialize` voor
  `apps.json` ("Could not load categories"). WinUI 3 unpackaged + trim is
  sowieso fragiel
- Publish: ~262 MB folder, ~70-80 MB ge-ZIP'd voor distributie

### v0.5.11 — Filter opties op CategoryDetailPage (2026-04-30)

- ComboBox met All / Popular / Installed naast de SearchBox
- Filter-mode + fuzzy search chained; lege subcat-headers verdwijnen tijdens
  filter
- Installed-filter herrendert wanneer `winget list` async binnenkomt
- `_uiReady` guard tegen redundante render-cycle bij vroege ComboBox
  SelectionChanged firing tijdens XAML-parse

### v0.5.10 — Strictere fuzzy search (2026-04-29)

- `WeightedRatio` vervangen door substring → prefix → `PartialRatio` ladder.
  WeightedRatio's token_set matchte anagram-achtige namen zoals "steam" ↔
  "teams" / "signal" / "keepass"
- `MinScore` 55 → 75 voor minder ruis
- `Description` niet langer mee-gescoord — alleen naam + winget ID

### v0.5.9 — WPF archived (2026-04-28)

- WPF app + Launcher uit de repo verwijderd, code blijft recoverable via tag
  `wpf-final-v1.2.1`
- Solution opgeschoond — alleen `SetupToolbox` blijft over
- README, INTEGRATIE.md en NEXT-STEPS.md herschreven naar WinUI-only context
- CLAUDE.md bijgewerkt: WPF-specifieke regels (DynamicResource, ARGB hex) weg

### v0.5.8 — Modern ScrollView + 20ms scroll
- `ScrollView` (modern WinUI 3) i.p.v. `ScrollViewer` (legacy) voor Parsec /
  remote desktop compatibiliteit
- `ScrollViewSpeedup` helper zet scroll-animation duration op 20ms
- Toegepast op alle pagina's

### v0.5.7 — INotifyPropertyChanged op App
- INPC op `IsSelected` en `IsInstalled` — geen `ItemsSource = null; reassign`
  rebind hacks meer
- Snellere UX, geen valse hover-events op buren

### v0.5.6 — Subcategorie grouping
- `SubcategoryGroup` model + nested ItemsRepeater voor section-headers per
  subcat in CategoryDetailPage

### v0.5.5 — Local-only apps.json
- `apps.json` gebundeld met de exe i.p.v. live van GitHub fetchen — werkt met
  private repo + public Releases distribution model

### v0.5.3 — Fuzzy search
- FuzzySharp NuGet, `WeightedRatio` met threshold 55/100, exact-substring
  shortcut naar 100, sort op score

### v0.5.2 — Winget-repo search + klikbare cards
- `winget search` integratie met "Results from winget repository" sectie
- Hele app-card klikbaar (Tag binding, hover effect, padding rechts van
  scrollbar)
- Sidebar Apps reset naar root via `ItemInvoked`

### v0.5.1 — Catalog search + globale selectie footer
- AutoSuggestBox in AppsPage en CategoryDetailPage
- `SelectionHelper` voor cross-category selectie

### Curated dataset (apps.json v2.0.0)
- 125 → ~60 apps op basis van wishlist
- Nieuwe top-level Gaming + App Suites (Proton, Adobe)
- Productivity uitgebreid met AI Assistants (ChatGPT msstore + Claude) en
  Cloud Storage
- Security met aparte subcats (Password Managers / VPN / Antivirus)
- `App.Source` veld + `--source msstore` flag voor Microsoft Store apps

## [v0.4.x] — Install flow

- v0.4.5: Schedule dialog + scheduled task voor auto-update
- v0.4.4: 4-stage progress ring (Downloading → Verifying → Installing → Done)
- v0.4.3: Quick uninstall onder Debloat (tijdelijk)
- v0.4.2: Per-app indeterminate / determinate progress bar
- v0.4.1: InstallDialog redesign + installed-state detectie
- v0.4.0: Eerste install flow met Fluent ProgressBar

## [v0.3.x] — Apps pagina

- v0.3.1: MicaBackdrop BaseAlt
- v0.3.0: Categorie-data uit apps.json, click → detail page

## [v0.2.0] — NavigationView shell

- Sidebar Apps / Tweaks / Debloat / Settings (WinUI 3 Gallery patroon)

## [v0.1.0] — Sandbox foundation

- WinUI 3 project + DesktopAcrylicController + native Fluent controls

---

## Pre-WinUI history

Voor WPF-changelog (v0.5.0 alpha t/m v1.2.1) zie de git history van tag
`wpf-final-v1.2.1`:

```bash
git log wpf-final-v1.2.1
```
