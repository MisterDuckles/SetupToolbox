# Bijdragen aan Setup Toolbox

Bedankt voor je interesse. Dit document beschrijft hoe je apps aan de catalogus
toevoegt en hoe je code bijdraagt.

> **Let op — licentie.** Dit is geen open-source project. De broncode is
> *source-available* onder een propriëtaire licentie: je mag 'm lezen en er
> verbeteringen voor voorstellen, maar niet hergebruiken. Lees `LICENSE`
> voordat je begint; sectie 4 beschrijft wat er met een bijdrage gebeurt.
> Onderaan dit document staat een samenvatting.

## Apps toevoegen

De makkelijkste manier om bij te dragen is een nieuwe app in `data/apps.json`.

### Stap 1: vind de winget-ID

```powershell
winget search "App Name"
```

Bijvoorbeeld:

```powershell
> winget search "Visual Studio Code"

Name                     Id                           Version    Source
-------------------------------------------------------------------------------
Visual Studio Code       Microsoft.VisualStudioCode   1.85.2     winget
```

De **Id**-kolom bevat de winget-ID: `Microsoft.VisualStudioCode`

Voor Microsoft Store-apps: `winget search "App Name" --source msstore`. De ID is
dan een Store-productcode (bijvoorbeeld `9NKSQGP7F2NH`).

### Stap 2: voeg de app toe aan apps.json

Zoek de juiste categorie in `data/apps.json` en voeg je app toe:

```json
{
  "name": "Visual Studio Code",
  "wingetId": "Microsoft.VisualStudioCode",
  "popular": false
}
```

**Velden:**

- `name` — weergavenaam. Blijft onvertaald: het zijn eigennamen
- `wingetId` — de winget-package-ID
- `popular` — `true` voor veelgebruikte apps (max 3 per categorie)
- `source` — alleen nodig als het géén winget-package is (bijvoorbeeld `msstore`)

Alle overige tekst staat in de vertaaltabellen, niet in `apps.json`. Zie stap 3.

### Stap 3: voeg de omschrijving toe aan de vertaaltabellen

De omschrijving staat sinds v1.2.6 **niet meer in `apps.json`** maar in de twee
stringtabellen, zodat de app één vertaalmechanisme heeft in plaats van twee. De
sleutel is `catalogApp.<wingetId>.desc`.

In `data/strings.en.json` (de brontaal — deze regel is verplicht):

```json
"catalogApp.Microsoft.VisualStudioCode.desc": "Lightweight but powerful code editor"
```

En in `data/strings.nl.json`:

```json
"catalogApp.Microsoft.VisualStudioCode.desc": "Lichte maar krachtige code-editor"
```

Houd de omschrijving kort (max ~60 tekens). Spreek je geen Nederlands? Zet dan
dezelfde Engelse zin in beide bestanden en vermeld het in je PR — dan vertalen
wij 'm.

> **Geen `//`-commentaar in de stringtabellen.** `System.Text.Json` wijst het af
> en `LocalizationService.Load` vangt de exception op met een **lege** tabel —
> de app draait dan zonder één vertaling. Er is een `_comment`-**key** voor
> notities.

Controleer je toevoeging met:

```powershell
py scripts/check-catalog-keys.py
```

Dat meldt ontbrekende of lege vertalingen, sleutels die na het verwijderen van
een app zijn blijven hangen, en sleutels die nergens in de code aangeroepen
worden — dat laatste betekent bijna altijd dat de aanroepplek nog een hardcoded
tekst heeft staan.

### Stap 4: test de app

Voordat je een PR maakt, test of de app daadwerkelijk installeert:

```powershell
winget install --id <wingetId> --exact --silent
```

Werkt dat, dan is de app geschikt.

### Stap 5: open een pull request

1. Fork de repository
2. Maak een branch: `git checkout -b add-app-name`
3. Commit je wijziging: `git commit -m "Add [App Name] to [Category]"`
4. Push naar je fork: `git push origin add-app-name`
5. Open een pull request

## Nieuwe categorie toevoegen

```json
{
  "id": "category-id",
  "icon": "🎯",
  "apps": [
    {
      "name": "App Name",
      "wingetId": "Publisher.AppName",
      "popular": false
    }
  ]
}
```

Of met subcategorieën:

```json
{
  "id": "category-id",
  "icon": "🎯",
  "subcategories": [
    {
      "id": "subcat-id",
      "apps": []
    }
  ]
}
```

Het `icon` is een emoji en blijft in `apps.json` staan: taalonafhankelijk. Naam
en omschrijving horen — net als bij een app — in de twee stringtabellen, met de
`id` als sleutel:

```json
"appCategory.category-id.name": "Category Name",
"appCategory.category-id.desc": "Short description",
"appSubcategory.subcat-id.name": "Subcategory Name"
```

Een subcategorie heeft **geen** eigen omschrijving: alleen de naam wordt
gerenderd, als groepsheader op de categoriepagina.

Draai daarna `py scripts/check-catalog-keys.py`. Het script leidt de verwachte
sleutelset af uit `apps.json`, dus het meldt precies wat je vergeten bent.

## Code bijdragen

### Vereisten

- .NET 10 SDK
- Windows 11 (build 26100 of nieuwer)
- Visual Studio 2022 of nieuwer, of Rider — optioneel, de `dotnet` CLI volstaat
- Python 3 voor de twee controlescripts (aanroep: `py`)
- Inno Setup 6, alleen als je de installer wilt bouwen

### Ontwikkelomgeving opzetten

```powershell
git clone https://github.com/MisterDuckles/SetupToolbox.git
cd SetupToolbox

# Build
dotnet build src/SetupToolbox/SetupToolbox.csproj -c Debug

# Build + run
dotnet run --project src/SetupToolbox/SetupToolbox.csproj -c Debug
```

De installer bouwen:

```powershell
pwsh scripts/build-installer.ps1
```

### Projectstructuur

```
SetupToolbox/
├── src/
│   └── SetupToolbox/          # De app — WinUI 3 op Windows App SDK, unpackaged
│       ├── App.xaml(.cs)      # Startup, dienst-singletons, command-line-dispatch
│       ├── MainWindow.xaml    # NavigationView-shell + Mica-backdrop
│       ├── Models/            # App, Category, Tweak, BloatwareItem, DeepCleanItem, …
│       ├── Pages/             # Apps, CategoryDetail, Debloat, DeepClean, Tweaks, Settings
│       ├── Dialogs/           # Install, DeepClean, LeftoverCleanup, ConfigImport/Export, …
│       ├── Services/          # Winget, AppDatabase, Tweak, Snapshot, Localization, …
│       ├── Helpers/           # FuzzyMatcher, DialogService, Localize, Diagnostics, …
│       └── Assets/            # App-icoon en de MSIX-logo's
├── data/
│   ├── apps.json              # Curated catalogus (naast de exe gebundeld)
│   ├── strings.en.json        # Brontaal — elke key MOET hier bestaan
│   ├── strings.nl.json        # Nederlandse vertaling
│   └── icons/                 # App-iconen, PNG
├── scripts/                   # Build-, installer- en controlescripts
├── installer/                 # Inno Setup-definitie
├── website/                   # Projectsite (Vite + React)
├── CONTRIBUTING.md
├── LICENSE
├── README.md
└── NEXT-STEPS.md              # Roadmap + beslissingenlogboek
```

Eén csproj, geen aparte launcher, geen MVVM-laag: de pagina's en dialogen zijn
code-behind met `x:Bind` op de modellen.

### Richtlijnen

- **C#-stijl** — volg de Microsoft-conventies; `Nullable` staat aan
- **Commentaar in het Nederlands**, en leg het *waarom* vast, niet het *wat*.
  De bestaande comments beschrijven vaak welke aanpak eerder is geprobeerd en
  waarom die niet werkte — houd dat zo
- **Geen hardcoded kleuren in XAML.** Gebruik `ThemeResource`-keys uit het
  Fluent-designsysteem (`CardBackgroundFillColorDefaultBrush`,
  `TextFillColorSecondaryBrush`, `AccentFillColorDefaultBrush`, …) zodat licht
  en donker allebei kloppen
- **Geen gebruiker-zichtbare tekst in de code.** Alles loopt via de twee
  stringtabellen — in C# `App.Loc.S(key)`, `App.Loc.S(key, args…)`,
  `App.Loc.Plural(keyBase, count)`, en in XAML `{loc:Localize Key=…}`. Voeg een
  nieuwe key aan **beide** tabellen toe; ze moeten dezelfde sleutelset houden.
  Wijzigt alleen de tekst, hernoem de key dan **niet** — dan wordt de oude een
  wees en valt de checker om
- **Foutafhandeling** — vang uitzonderingen af en toon een begrijpelijke
  melding; laat een dialog nooit op een stacktrace eindigen
- **Async/await** voor alle I/O

### Twee controles moeten groen blijven

```powershell
py scripts/scan-untranslated.py
py scripts/check-catalog-keys.py
```

De eerste zoekt gebruiker-zichtbare tekst die nog een letterlijke string is, in
vijf passes: XAML-tekstattributen, inline XAML-inhoud
(`<TextBlock>tekst</TextBlock>`, `<Run>`), toewijzingen aan `.Text` / `.Content`
/ `.Title` / `.Message`, switch-armen (`=> "Recycle Bin"`) en zin-achtige
literals in `Dialogs/`, `Pages/`, `Helpers/` en `Services/`. De tweede bewijst
dat elke catalogustekst in beide talen bestaat en dat er geen ongebruikte of
wees-sleutels achterblijven. Beide geven exit-code 1 zodra er iets mis is.

Blijft een literal bewust hardcoded — de naam van een Windows-artefact, een
merknaam, een registerwaarde — zet 'm dan in de `ALLOW`-lijst bovenin
`scan-untranslated.py`, **met de reden erbij**. Een uitzondering zonder
verantwoording is een dempknop.

### Pull requests

1. **Eén onderwerp per PR** — klein en gefocust
2. **Draai de build en beide scripts** voordat je submit
3. **Beschrijf wat en waarom**, niet alleen wat er veranderd is
4. **Werk `README.md` bij** als het gedrag zichtbaar verandert

## Waar we hulp bij kunnen gebruiken

- **Nieuwe apps in de catalogus** — verreweg de nuttigste bijdrage
- **Vertaalcorrecties** in `data/strings.nl.json` en `data/strings.en.json`
- **Toegankelijkheid** — UI-Automation-namen kloppen niet overal na een
  taalwissel
- **Custom app-bronnen** — `AppDatabaseService` leest precies één gebundelde
  `apps.json`; er is nog geen abstractie voor een extra bron
- **CLI-interface** (`install --profile gaming`) — `App.OnLaunched` heeft al een
  dispatch-patroon voor command-line-argumenten om op voort te bouwen

Zie `NEXT-STEPS.md` voor de actuele roadmap en de gemaakte beslissingen. Pak je
iets groters op, open dan eerst een issue — dan voorkomen we dubbel werk.

## Bugs melden

[Open een issue](https://github.com/MisterDuckles/SetupToolbox/issues/new).

**Vermeld:**

- Windows-versie (11 of 10, en het buildnummer)
- App-versie (Instellingen → onderaan)
- Winget-versie (`winget --version`)
- Stappen om het te reproduceren
- Foutmeldingen en logs — de app schrijft naar
  `%LocalAppData%\SetupToolbox\logs`, te openen via *Instellingen → Open logmap*

## Ideeën indienen

[Open een feature request](https://github.com/MisterDuckles/SetupToolbox/issues/new)
en beschrijf:

- **Wat** je wil toevoegen
- **Waarom** het nuttig is
- **Hoe** het zou kunnen werken

## Vragen?

Weet je niet waar te beginnen? Open een issue met je vraag.

## Licentie

Setup Toolbox staat onder een **source-available, propriëtaire licentie** — zie
`LICENSE`. Dat is uitdrukkelijk geen open-source-licentie: de broncode is
gepubliceerd om gelezen te worden en om bijdragen mogelijk te maken, niet om
hergebruikt te worden.

Voor bijdragers zijn twee punten van belang:

- **Je mag de code niet hergebruiken** in een ander project, er geen afgeleide
  werken van maken en 'm niet herdistribueren, behalve wat nodig is om een pull
  request voor te bereiden (`LICENSE` sectie 3).
- **Door een bijdrage in te dienen** geef je de auteur een eeuwigdurende,
  wereldwijde, royaltyvrije en onherroepelijke licentie om jouw bijdrage te
  gebruiken, aan te passen, te sublicentiëren en op te nemen in Setup Toolbox
  (`LICENSE` sectie 4). De auteur is niet verplicht een bijdrage te beoordelen
  of over te nemen.

De gecompileerde applicatie zelf is gratis te gebruiken, privé en zakelijk.

---

**Bedankt voor je bijdrage.**
