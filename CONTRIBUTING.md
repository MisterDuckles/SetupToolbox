# Contributing to SetupToolbox

Bedankt voor je interesse om bij te dragen! 🎉

## Apps Toevoegen

De makkelijkste manier om bij te dragen is door nieuwe apps toe te voegen aan `data/apps.json`.

### Stap 1: Vind de Winget ID

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

De **Id** kolom bevat de winget ID: `Microsoft.VisualStudioCode`

### Stap 2: Voeg de App toe aan apps.json

Zoek de juiste categorie in `data/apps.json` en voeg je app toe:

```json
{
  "name": "Visual Studio Code",
  "wingetId": "Microsoft.VisualStudioCode",
  "popular": false
}
```

**Velden:**
- `name`: Display naam van de app. Blijft onvertaald — het zijn eigennamen
- `wingetId`: De winget package ID
- `popular`: `true` voor veel gebruikte apps (max 3 per categorie)

### Stap 3: Voeg de omschrijving toe aan de vertaaltabellen

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

Houd de omschrijving kort (max ~60 karakters). Spreek je geen Nederlands? Zet
dan dezelfde Engelse zin in beide bestanden en vermeld het in je PR — dan
vertalen wij 'm.

Controleer je toevoeging met:

```powershell
py scripts/check-catalog-keys.py
```

Dat meldt ontbrekende of lege vertalingen, en sleutels die na het verwijderen
van een app zijn blijven hangen.

### Stap 4: Test de App

Voordat je een PR maakt, test of de app correct installeert:

```powershell
winget install --id <wingetId> --exact --silent
```

Als dit werkt, is de app geschikt!

### Stap 5: Submit Pull Request

1. Fork de repository
2. Maak een nieuwe branch: `git checkout -b add-app-name`
3. Commit je wijzigingen: `git commit -m "Add [App Name] to [Category]"`
4. Push naar je fork: `git push origin add-app-name`
5. Open een Pull Request

## Nieuwe Categorie Toevoegen

Als je een hele nieuwe categorie wil toevoegen:

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
      "apps": [
        // apps here
      ]
    }
  ]
}
```

De naam en omschrijving van de categorie horen — net als bij een app — in de
twee stringtabellen, met de `id` als sleutel:

```json
"appCategory.category-id.name": "Category Name",
"appCategory.category-id.desc": "Short description",
"appSubcategory.subcat-id.name": "Subcategory Name"
```

Een subcategorie heeft geen eigen omschrijving nodig: alleen de naam wordt
getoond, als sectie-header op de categoriepagina. Draai daarna
`py scripts/check-catalog-keys.py` om te controleren dat je niets vergeten bent.

## Code Contributions

### Prerequisites

- .NET 8 SDK
- Visual Studio 2022, Rider, of VS Code
- Git

### Setup Development Environment

```bash
# Clone repo
git clone https://github.com/YOUR_USERNAME/SetupToolbox.git
cd SetupToolbox

# Restore packages
dotnet restore

# Build
dotnet build

# Run
cd src/SetupToolbox
dotnet run
```

### Project Structure

```
src/
├── SetupToolbox/          # Main WPF application
│   ├── Models/               # Data models
│   ├── Services/             # Business logic
│   │   ├── WingetService     # Winget operations
│   │   ├── GitHubService     # GitHub API & updates
│   │   └── TaskSchedulerService # Scheduled tasks
│   ├── Views/                # XAML windows
│   ├── ViewModels/           # View models (MVVM)
│   └── Themes/               # UI themes
└── Launcher/                 # Bootstrap launcher
```

### Coding Guidelines

- **C# Style**: Follow Microsoft C# conventions
- **XAML**: Gebruik Material Design components waar mogelijk
- **Comments**: Engels voor code, Nederlands voor user-facing text OK
- **Error Handling**: Altijd try/catch met user-friendly messages
- **Async/Await**: Gebruik async voor I/O operaties

### Pull Request Guidelines

1. **Een feature per PR** - Maak kleine, focused PRs
2. **Test je code** - Zorg dat alles werkt voordat je submit
3. **Beschrijf je changes** - Leg uit wat en waarom
4. **Update README** als nodig

### Features die we zoeken

- [ ] Dark mode implementatie
- [ ] App installatie status indicator (check of app al geïnstalleerd is)
- [ ] Parallel app installatie
- [ ] App icons fetchen en tonen
- [ ] Export/Import geselecteerde apps
- [ ] Installatie profielen (Gaming, Developer, Office, etc.)
- [ ] Multi-language support (NL/EN)
- [ ] Search improvements (fuzzy search)
- [ ] App ratings/reviews tonen

## Bug Reports

Found a bug? [Open een issue](https://github.com/YOUR_USERNAME/SetupToolbox/issues/new)!

**Include:**
- OS version (Windows 10/11)
- App version
- Winget version (`winget --version`)
- Steps to reproduce
- Error messages/logs

## Feature Requests

Heb je een idee? [Open een feature request](https://github.com/YOUR_USERNAME/SetupToolbox/issues/new)!

Beschrijf:
- **Wat** je wil toevoegen
- **Waarom** het nuttig zou zijn
- **Hoe** het zou kunnen werken

## Questions?

Niet zeker waar te beginnen? Open een issue met je vraag of start een discussion!

## License

Door bij te dragen ga je akkoord dat je code onder de MIT License valt.

---

**Bedankt voor je bijdrage! 🚀**
