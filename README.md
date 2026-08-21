# SetupToolbox 🪟

Native Windows 11 app voor het in batch installeren van Windows-applicaties via `winget`. Bedoeld voor fresh-install setups en debloat-flows.

> **Status:** v0.5.x (pre-release). Code en releases komen via deze repo. Eerdere WPF-implementatie is per `wpf-final-v1.2.1` git tag gearchiveerd.

## ✨ Features

- 🎨 **Native Win11 UI** — WinUI 3 + Windows App SDK met Mica backdrop, geen third-party UI libs
- 📦 **~60 gecureerde apps** verdeeld over 10 categorieën (Browsers, Development, Security, Productivity, Communication, Media, Gaming, Utilities, Creative, App Suites)
- 🔍 **Fuzzy search** in catalog + uitbreiding naar de volledige winget repository
- ✅ **Bulk install** met live winget output, 4-stage progress per app (Downloading → Verifying → Installing → Done)
- 🗑️ **Quick uninstall** onder Debloat tab voor catalog-apps
- ⏰ **Auto-update scheduled task** — `winget upgrade --all` op Daily / Weekly / OnStartup trigger
- 📦 **App Suites** — Proton + Adobe ecosystems met een klik
- 🏪 **Microsoft Store apps** — WhatsApp, Apple Music, ChatGPT via `--source msstore`

## 🏗️ Project Structuur

```
SetupToolbox/
├── src/
│   └── SetupToolbox/      # Native Win11 app (WinUI 3 + WinAppSDK 1.8)
│       ├── App.xaml / .xaml.cs
│       ├── MainWindow.xaml / .xaml.cs    # NavigationView shell + Mica backdrop
│       ├── Models/AppModels.cs            # App (INPC), Category, SubcategoryGroup
│       ├── Pages/                         # Apps, CategoryDetail, Debloat, Tweaks, Settings
│       ├── Dialogs/                       # InstallDialog, ScheduleDialog
│       ├── Services/                      # AppDatabase, Winget, TaskScheduler, SelectionHelper
│       └── Helpers/                       # FuzzyMatcher, ScrollViewSpeedup
├── data/
│   └── apps.json                          # Curated app catalog (gebundeld met exe)
├── README.md
└── NEXT-STEPS.md                          # Roadmap + decisions log
```

## 🚀 Quick Start

### Voor gebruikers

Download de nieuwste `.exe` van de [Releases pagina](https://github.com/MisterDuckles/SetupToolbox/releases) en run.

### Voor developers

**Requirements**
- .NET 10 SDK
- Windows 11 (build 26100 of nieuwer)
- Visual Studio 2026 Community (of Rider) — niet strict nodig, `dotnet` CLI volstaat

```powershell
git clone https://github.com/MisterDuckles/SetupToolbox.git
cd SetupToolbox

# Build + run
dotnet run --project src/SetupToolbox/SetupToolbox.csproj -c Debug

# Self-contained release publish
dotnet publish src/SetupToolbox/SetupToolbox.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o ./release
```

## 📝 Apps toevoegen / aanpassen

`data/apps.json` is de source of truth. Voeg een app toe als JSON-object onder de juiste categorie of subcategorie:

```json
{
  "name": "Nieuwe App",
  "wingetId": "Publisher.AppName",
  "popular": false
}
```

**Microsoft Store apps** (zoals WhatsApp, Apple Music, ChatGPT):

```json
{
  "name": "WhatsApp Desktop",
  "wingetId": "9NKSQGP7F2NH",
  "source": "msstore"
}
```

De **omschrijving** staat sinds v1.2.6 in de vertaaltabellen en niet meer in
`apps.json`, met de `wingetId` als sleutel — zo loopt alle vertaalbare tekst in
de app via één mechanisme. Voeg 'm toe aan `data/strings.en.json` (brontaal,
verplicht) én `data/strings.nl.json`:

```json
"catalogApp.Publisher.AppName.desc": "Korte beschrijving"
```

Hetzelfde geldt voor categorie- en subcategorie-namen (`appCategory.<id>.name`,
`appCategory.<id>.desc`, `appSubcategory.<id>.name`). App-**namen** blijven wél
gewoon in `apps.json`: dat zijn eigennamen die in beide talen hetzelfde zijn.

Controleer met `py scripts/check-catalog-keys.py` dat elke catalogus-tekst in
beide talen bestaat — dat script kijkt naar de hele catalogus, niet alleen naar
wat je toevallig op het scherm krijgt.

**Winget ID vinden:**

```powershell
winget search "App Name"
winget search "App Name" --source msstore
```

`apps.json` wordt gebundeld met de exe (csproj `<Content>` + PreserveNewest), dus updates komen via een nieuwe release — er wordt nu niet live van GitHub gefetcht.

## ⏰ Auto-update setup

Open **Settings** → **Scheduled auto-updates** → **Set up**:

- **Daily** / **Weekly** / **On user logon**
- Tijd kiezen (behalve OnStartup)
- UAC-prompt voor admin rechten (taak draait met highest privileges)

Achter de schermen maakt dit een Windows scheduled task `SetupToolbox_AutoUpdate` die de exe runt met `/autoupdate` argument. App detecteert dat, runt `winget upgrade --all --silent`, en exit zonder window.

## 🛠️ Tech stack

- **.NET 10** + **Windows App SDK 1.8** + **WinUI 3** (unpackaged)
- **FuzzySharp** — fuzzy search met Levenshtein distance
- **MicaBackdrop BaseAlt** — native Win11 transparency
- **winget.exe CLI** — install / uninstall / upgrade / list / search

Distributie: private repo + public GitHub Releases. App.json wordt gebundeld in de exe-output dus geen netwerk dependency tijdens runtime.

## 🗺️ Roadmap

Zie [NEXT-STEPS.md](NEXT-STEPS.md) voor de volledige feature roadmap. Highlights die nog komen:

- v0.5.0 — Filter opties, echte app-icons per app, eerste public release
- v0.6.0 — Full Debloat tab (Windows bloatware removal + restant-cleanup)
- v0.7.0 — Tweaks tab (registry toggles voor Explorer / Privacy / Performance)
- v0.8.0 — Settings persistence + app self-update via GitHub
- v0.9.0 — Install profiles, parallel installs, toast notificaties, install history

## 🐛 Troubleshooting

**"Winget not found"** — installeer **App Installer** via Microsoft Store (zit standaard in Win11).

**Microsoft Store app installeert niet** — check of je ingelogd bent in de Store. Store apps vereisen een Microsoft account voor sommige licentiechecks.

**Scheduled task failt** — taak vereist admin rights. Re-run **Set up** met UAC-acceptatie.

## 📜 License

Source-available onder een propriëtaire licentie — zie `LICENSE`. Dat is uitdrukkelijk **geen** open-source-licentie: de broncode is gepubliceerd om gelezen te worden en om bijdragen mogelijk te maken, niet om hergebruikt te worden. De gecompileerde app zelf is gratis te gebruiken, privé en zakelijk.

---

**Built for fast Windows setups.**
