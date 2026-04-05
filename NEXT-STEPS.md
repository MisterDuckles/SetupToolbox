# WingetAppDeployer - Development Roadmap

## ✅ Voltooid (v0.5.0 - Alpha)

- [x] GitHub repository opgezet op `MisterDuckles/WinGetAppDeployer`
- [x] Project gebouwd en getest
- [x] GitHub releases aangemaakt
- [x] Installatie getest en geverifieerd
- [x] Project hernoemd van WinAppInstaller naar WingetAppDeployer
- [x] Alle namespaces en referenties bijgewerkt

---

## ✅ Voltooid (v1.0.0)

- [x] Fix scrolling in MainWindow
- [x] Klik op gehele card om app te selecteren (niet alleen checkbox)
- [x] Redesign Minimal theme (animatie, hover-effecten, shadows)
- [x] Fix theme switching functionaliteit
- [x] Fluent (Windows 11) en Material Design themes toegevoegd
- [x] Settings icoon cutting off gefixed
- [x] App installed status indicator (groen vinkje)
- [x] Betere error messages bij gefaalde installaties
- [x] Loading indicator tijdens app database fetch
- [x] Search functionaliteit in app lijst

---

## 🐛 Bekende Issues (Te Fixen)

### Medium Priority
- [ ] Add icons for apps

### Low Priority
- [ ]

---

## 🚀 Geplande Features

### v1.1.0 - Enhanced UX
- [ ] Dark mode implementatie
- [ ] App icons tonen (fetch van GitHub/Winget)
- [ ] Installation profiles (Gaming, Developer, Office, etc.)
- [ ] Parallel installaties (meerdere apps tegelijk)
- [ ] Progress bar per app tijdens installatie
- [ ] Export/Import selectie naar JSON

### v1.2.0 - Advanced Features
- [ ] Multi-language support (NL/EN toggle)
- [ ] Fuzzy search in app lijst
- [ ] Filter opties (alleen popular, al geïnstalleerd, etc.)
- [ ] Update checker voor geïnstalleerde apps
- [ ] Installatie geschiedenis/logs
- [ ] Notifications bij voltooide installaties

### v2.0.0 - Major Update
- [ ] Plugin systeem voor custom app sources
- [ ] Cloud sync voor settings en app selecties
- [ ] Custom app repositories toevoegen
- [ ] Portable mode (geen installatie nodig)
- [ ] CLI interface (`winget-deployer install --profile gaming`)
- [ ] Backup/restore functionaliteit

---

## 💡 Feature Ideas (Nog te beoordelen)

Voeg hier nieuwe ideeën toe:

- [ ]
- [ ]
- [ ]

---

## 📝 Development Notes

### Quick Commands

```bash
# Build solution
dotnet build WingetAppDeployer.sln -c Release

# Publish executables
dotnet publish src/Launcher -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
dotnet publish src/WingetAppDeployer -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# Run tests (wanneer toegevoegd)
dotnet test

# Clean build
dotnet clean && dotnet build
```

### Project Structure

```
src/
├── WingetAppDeployer/           # Main WPF application
│   ├── Models/                  # Data models (App, Category, Settings)
│   ├── Services/                # Business logic
│   │   ├── WingetService.cs    # Winget CLI wrapper
│   │   ├── GitHubService.cs    # GitHub API for updates & apps.json
│   │   ├── SettingsService.cs  # Settings persistence
│   │   └── TaskSchedulerService.cs # Scheduled auto-updates
│   ├── Views/                   # XAML windows (Install, Settings, Schedule)
│   ├── Themes/                  # UI themes (Minimal, Fluent, Material)
│   └── MainWindow.xaml          # Main UI with app list
└── Launcher/                    # Bootstrap launcher (~5KB)
    └── Program.cs               # Downloads & launches main app
```

### Testing Checklist

Voor elke release:
- [ ] Build succesvol (geen errors)
- [ ] Launcher werkt (download + launch)
- [ ] App database laadt correct
- [ ] Minimaal 3 apps succesvol geïnstalleerd
- [ ] Settings opslaan/laden werkt
- [ ] Auto-update functionaliteit werkt
- [ ] Theme switching werkt correct (alle 3 themes)

---

## 🔧 Troubleshooting

### Build Issues

**MaterialDesignThemes niet gevonden:**
```bash
dotnet restore src/WingetAppDeployer/WingetAppDeployer.csproj
```

**Verkeerde .NET versie:**
- Project gebruikt .NET 10.0
- Download: https://dotnet.microsoft.com/download/dotnet/10.0

### Runtime Issues

**"Winget not found":**
- Installeer App Installer via Microsoft Store
- Of download: https://github.com/microsoft/winget-cli/releases

**"Failed to download app database":**
- Check GitHub repository is public
- Verifieer `apps.json` in `main` branch staat
- Test internet connectie

**Scrolling werkt niet:**
- Gefixed in v1.0.0

---

## 📊 Project Stats

- **8 categorieën** met apps
- **15 subcategorieën**
- **200+ apps** in database
- **~3000 regels code**
- **~25 bestanden**

---

## 📞 Contact & Support

- GitHub Issues: https://github.com/MisterDuckles/WinGetAppDeployer/issues
- Discussions: https://github.com/MisterDuckles/WinGetAppDeployer/discussions

**Happy coding! 🚀**
