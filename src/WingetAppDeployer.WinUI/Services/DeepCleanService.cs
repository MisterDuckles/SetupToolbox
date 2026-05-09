using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Services;

// System-wide cleanup-scanner los van een specifieke uninstall (de v0.8.5
// LeftoverScannerService doet dat juist wel). Twee bron-types:
//
//   1. Windows caches — vaste paden waar Windows / browsers caches en tijdelijke
//      files dumpen. Geen detection nodig, paden zijn altijd dezelfde. We
//      checken bestaan + size en bouwen een DeepCleanItem als er content is.
//   2. Orphaned folders — folders in Program Files / AppData die NIET matchen
//      met enige geïnstalleerde app (winget / AppX / registry). Gebruikt
//      InstalledAppsService voor de comparison set.
//
// Deletes worden gesplitst in user-context (HKCU AppData, user temp, browser
// caches) en elevated (system temp, Windows.old, Update cache, Program Files,
// CommonApplicationData). Eén UAC prompt voor de elevated subset.
public sealed class DeepCleanService
{
    /// <summary>
    /// Returnt de paden die ScanOrphanedFoldersAsync afgaat. Gebruikt door de
    /// dialog header zodat user weet wat er onderzocht is — anders is het een
    /// black box.
    /// </summary>
    public static IReadOnlyList<string> GetOrphanedScanLocations()
    {
        var paths = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };
        return paths.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
    }

    /// <summary>
    /// Returnt de cache-locatie patronen die ScanWindowsCachesAsync probeert.
    /// Gebruikt door de dialog header.
    /// </summary>
    public static IReadOnlyList<string> GetCacheScanLocations()
    {
        return new List<string>
        {
            "%TEMP% (user temp)",
            "%WINDIR%\\Temp",
            "%WINDIR%\\SoftwareDistribution\\Download",
            "%WINDIR%\\Prefetch",
            "%WINDIR%.old (Windows.old)",
            "Edge / Chrome / Brave / Firefox caches",
            "Recycle Bin"
        };
    }

    /// <summary>
    /// Scant alle Windows-cache locaties die we kennen. Returnt items voor
    /// paden die bestaan (anders heeft het geen zin om ze als optie te tonen).
    /// Size wordt synchroon berekend per item — Windows caches zijn typisch
    /// niet enorm (paar GB max) dus dat blijft snel.
    /// </summary>
    public async Task<List<DeepCleanItem>> ScanWindowsCachesAsync()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "WingetAppDeployer_deepclean.log");
        Action<string> log = msg =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        };
        try
        {
            File.WriteAllText(logPath, $"=== DeepClean caches scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch { }

        var sw = Stopwatch.StartNew();
        var results = new List<DeepCleanItem>();

        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Targets parallel scannen — size-walk voor sommige caches kan seconden
        // duren (Edge/Chrome cache kan GB's bevatten).
        var tasks = new List<Task<DeepCleanItem?>>
        {
            ScanCacheTargetAsync(
                "User Temp folder",
                Path.GetTempPath(),
                DeepCleanCategory.UserTemp,
                requiresElevation: false,
                isSafe: true,
                description: "Files in %TEMP% van je user account. Kan vrij weg zonder problemen — Windows en apps maken ze opnieuw aan.",
                log),
            ScanCacheTargetAsync(
                "System Temp folder",
                Path.Combine(winDir, "Temp"),
                DeepCleanCategory.SystemTemp,
                requiresElevation: true,
                isSafe: true,
                description: "Tijdelijke files van system services en installers. Veilig om weg te gooien.",
                log),
            ScanCacheTargetAsync(
                "Windows Update cache",
                Path.Combine(winDir, "SoftwareDistribution", "Download"),
                DeepCleanCategory.UpdateCache,
                requiresElevation: true,
                isSafe: true,
                description: "Gedownloade Windows Update payloads die al geïnstalleerd zijn. Windows downloadt opnieuw als er een nieuwe update is.",
                log),
            ScanCacheTargetAsync(
                "Prefetch",
                Path.Combine(winDir, "Prefetch"),
                DeepCleanCategory.Prefetch,
                requiresElevation: true,
                isSafe: true,
                description: "Cache van app-startup metadata. Wordt opnieuw opgebouwd na cleanup — apps starten een paar keer iets trager tot 'ie weer gevuld is.",
                log),
            ScanCacheTargetAsync(
                "Windows.old",
                Path.Combine(Path.GetPathRoot(winDir) ?? "C:\\", "Windows.old"),
                DeepCleanCategory.WindowsOld,
                requiresElevation: true,
                isSafe: false,
                description: "Backup van je vorige Windows-install na een upgrade. Verwijderen = geen rollback meer mogelijk naar de oudere versie.",
                log)
        };

        // Browser caches — alleen toevoegen als de browser geïnstalleerd lijkt
        // (default-pad bestaat). Voorkomt dat we een Firefox-entry tonen aan
        // een user die alleen Edge gebruikt.
        var browserTargets = new (string Name, string Path, string Description)[]
        {
            ("Edge cache",
             Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"),
             "Browser cache van Microsoft Edge. Sites moeten resources opnieuw laden, login-state blijft typisch behouden."),
            ("Chrome cache",
             Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"),
             "Browser cache van Google Chrome. Sites moeten resources opnieuw laden."),
            ("Brave cache",
             Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
             "Browser cache van Brave. Sites moeten resources opnieuw laden.")
        };
        foreach (var (name, path, desc) in browserTargets)
        {
            tasks.Add(ScanCacheTargetAsync(
                name, path, DeepCleanCategory.BrowserCache,
                requiresElevation: false, isSafe: false, description: desc, log));
        }

        // Firefox heeft per-profile cache directories. Alle profiles meenemen.
        var firefoxRoot = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxRoot))
        {
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(firefoxRoot))
                {
                    var cachePath = Path.Combine(profile, "cache2");
                    if (!Directory.Exists(cachePath)) continue;
                    var profileName = new DirectoryInfo(profile).Name;
                    tasks.Add(ScanCacheTargetAsync(
                        $"Firefox cache ({profileName})",
                        cachePath,
                        DeepCleanCategory.BrowserCache,
                        requiresElevation: false,
                        isSafe: false,
                        description: "Browser cache van Firefox-profiel. Sites moeten resources opnieuw laden.",
                        log));
                }
            }
            catch (Exception ex)
            {
                log($"Firefox profiles enum failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Recycle Bin — speciaal: niet via Directory.Delete maar via shell API.
        // We scannen 'm via hidden $Recycle.Bin folders op alle drives en
        // sommeren de sizes. Delete gaat via PowerShell Clear-RecycleBin.
        var recycleSize = ComputeRecycleBinSize(log);
        if (recycleSize > 0)
        {
            results.Add(new DeepCleanItem(
                displayName: "Recycle Bin",
                path: "shell:RecycleBinFolder",
                category: DeepCleanCategory.RecycleBin,
                sizeBytes: recycleSize,
                requiresElevation: false,
                isSafe: true,
                description: "Bestanden die je naar de Prullenbak hebt gestuurd. Verwijderen = definitief weg, geen ongedaan-maken meer."));
        }

        var scanned = await Task.WhenAll(tasks);
        results.AddRange(scanned.Where(r => r != null)!);

        log($"Caches scan complete in {sw.ElapsedMilliseconds}ms — {results.Count} item(s)");
        return results.OrderBy(r => (int)r.Category).ToList();
    }

    /// <summary>
    /// Scant Program Files / Program Files (x86) / %LOCALAPPDATA% / %APPDATA% /
    /// %PROGRAMDATA% en flag folders die NIET matchen met enige installed app.
    /// Loopt InstalledAppsService.DetectAllAsync om de comparison set op te bouwen.
    /// Conservatief: protected-list voor system folders die we nooit als orphan
    /// voorstellen (Microsoft / Windows / WindowsApps / etc.).
    /// </summary>
    public async Task<List<DeepCleanItem>> ScanOrphanedFoldersAsync()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "WingetAppDeployer_deepclean.log");
        Action<string> log = msg =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        };
        log($"=== Orphaned folders scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        var sw = Stopwatch.StartNew();

        // Comparison set — niet alleen volledige DisplayNames, maar ook losse
        // tokens (woorden) zodat een folder "VMware" matcht met installed app
        // "VMware Workstation Pro". Zonder tokenize zou alleen substring werken,
        // wat soms faalt door publisher-encoding of locale-verschillen.
        var installed = await App.InstalledApps.DetectAllAsync();
        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in installed)
        {
            AddWithTokens(nameSet, entry.DisplayName);
            if (!string.IsNullOrEmpty(entry.Publisher)) AddWithTokens(nameSet, entry.Publisher);
            // Voor Store entries: PackageFullName eerste segment + last segment
            if (entry.Source == InstalledSource.Store)
            {
                var parts = entry.Identifier.Split('_')[0].Split('.');
                foreach (var p in parts)
                    if (p.Length >= 4) nameSet.Add(Normalize(p));
            }
            // Voor Winget: Publisher.AppName splitten, beide halves toevoegen
            if (entry.Source == InstalledSource.Winget)
            {
                foreach (var p in entry.Identifier.Split('.'))
                    if (p.Length >= 4) nameSet.Add(Normalize(p));
            }
        }
        log($"Comparison set: {nameSet.Count} normalized name(s) from {installed.Count} installed apps");
        log($"  nameSet sample: {string.Join(", ", nameSet.OrderBy(s => s).Take(60))}{(nameSet.Count > 60 ? " ..." : "")}");

        // Owned-paths set: registry InstallLocation values geven ons exacte
        // paden waar Windows weet dat een app fysiek staat. Folders die zelf
        // óf parent zijn van een install-pad zijn dus eigenlijk "van een
        // geïnstalleerde app" — geen orphan ook al matcht de naam niet.
        // Zonder dit krijgt "C:\Program Files\VMware" een orphan-flag terwijl
        // de app fysiek zit in "C:\Program Files\VMware\VMware Workstation Pro".
        var ownedPaths = CollectInstallLocationsFromRegistry(log);
        log($"Owned-paths set: {ownedPaths.Count} install-location(s) (incl. parents)");

        var results = new List<DeepCleanItem>();
        var roots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), true),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), true),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), true),
        }.Where(r => !string.IsNullOrEmpty(r.Item1) && Directory.Exists(r.Item1)).ToList();

        var folderTasks = new List<Task<DeepCleanItem?>>();
        foreach (var (rootPath, requiresElev) in roots)
        {
            DirectoryInfo[] subdirs;
            try { subdirs = new DirectoryInfo(rootPath).GetDirectories(); }
            catch (Exception ex)
            {
                log($"Root scan error {rootPath}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var dir in subdirs)
            {
                if (IsProtectedFolder(dir.Name)) continue;

                // Owned-path check: als deze folder zelf een install-location
                // is óf parent van een install-location, hoort 'ie bij een
                // geïnstalleerde app — geen orphan. Dit vangt de top-level
                // vendor-folder (bv. "C:\Program Files\VMware") die een
                // installed app als subfolder heeft.
                if (PathIsOwned(dir.FullName, ownedPaths))
                {
                    log($"SKIP {dir.FullName}: owned by registry InstallLocation");
                    continue;
                }

                // Naam-match check (volledige normalized + tokens + bidirectional
                // substring). Vangt apps zonder InstallLocation in registry.
                if (HasInstalledMatch(dir.Name, nameSet))
                {
                    log($"SKIP {dir.FullName}: name match in installed-set");
                    continue;
                }

                // Subfolder-peek: kijk één level diep. Als een direct child folder
                // matcht met een installed app → parent is impliciet "van die
                // vendor". Voorbeeld: "C:\Program Files\Anthropic\Claude\" — als
                // Claude installed is matcht subfolder, dus parent niet orphan.
                if (HasInstalledMatchInChildren(dir, nameSet, log))
                {
                    log($"SKIP {dir.FullName}: child folder matches installed app");
                    continue;
                }

                // GEEN exe-contains check meer: deep clean is juist bedoeld om
                // verlaten .exe-garbage te vinden van apps die NIET in de
                // installed-list staan. Source of truth = InstalledAppsService.
                // Zit een folder daar niet in, dan IS het orphan, ook als er
                // een unins000.exe of vendor-installer.exe in zit.
                folderTasks.Add(BuildOrphanedItemAsync(dir, requiresElev, log));
            }
        }

        var items = await Task.WhenAll(folderTasks);
        results.AddRange(items.Where(i => i != null)!);

        log($"Orphaned folders scan complete in {sw.ElapsedMilliseconds}ms — {results.Count} candidate(s)");
        // Sort: groot-naar-klein (meeste ruimte vrij) zodat de high-impact
        // items bovenaan staan. Tie-break op pad voor determinisme.
        return results
            .OrderByDescending(r => r.SizeBytes)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Voegt zowel de volledige genormaliseerde naam als losse tokens (woorden)
    /// toe aan de match-set. "VMware Workstation Pro" → adds "vmwareworkstationpro"
    /// PLUS "vmware", "workstation". Generieke tokens (Pro / App / Tool / for /
    /// version-numbers) worden geskipt om false negatives te voorkomen — een
    /// folder "Pro" zou anders altijd matchen.
    /// </summary>
    private static void AddWithTokens(HashSet<string> nameSet, string raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        nameSet.Add(Normalize(raw));
        var separators = new[] { ' ', '-', '_', ',', '.', '(', ')', '[', ']', '/', '\\', '+', '&' };
        foreach (var token in raw.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var norm = Normalize(token);
            if (norm.Length < 4) continue;        // te korte tokens (HP / MS) overslaan
            if (IsGenericToken(norm)) continue;
            nameSet.Add(norm);
        }
    }

    private static bool IsGenericToken(string normToken)
    {
        // Woorden die in heel veel app-namen zitten — als nameSet deze bevat
        // zou bijna elke folder als "match" gelden. Filter ze eruit.
        return normToken switch
        {
            "pro" or "plus" or "lite" or "free" or "premium" or "basic" or "standard" or "enterprise" => true,
            "edition" or "version" or "build" => true,
            "app" or "apps" or "tool" or "tools" or "suite" or "manager" or "studio" or "center" => true,
            "for" or "and" or "the" or "with" => true,
            "x86" or "x64" or "win32" or "win64" or "amd64" => true,
            "microsoft" or "windows" => true,    // te breed; specifieke productnamen vangen we via volle DisplayName
            _ => false
        };
    }

    /// <summary>
    /// Walkt registry uninstall keys (HKLM 64-bit + WOW6432Node + HKCU) en
    /// extraheert install-paden uit MEERDERE velden — niet alleen
    /// InstallLocation. Veel apps (VMware, Razer, WinRAR, etc.) schrijven
    /// geen InstallLocation maar wel een DisplayIcon ("C:\path\app.exe,0")
    /// of UninstallString ("C:\path\unins000.exe /S") waaruit we de install-
    /// folder kunnen afleiden. Plus: HKLM\Software\Microsoft\Windows\
    /// CurrentVersion\App Paths — Windows' canonical "where is this exe"
    /// register, gevuld door bijna elke installer.
    ///
    /// Voor elk gevonden pad voegen we het zelf én alle parent-directories
    /// toe t/m een stop-folder (Program Files / AppData) zodat een vendor-
    /// folder ("C:\Program Files\VMware") die een installed app als child
    /// heeft niet als orphan flagged wordt.
    /// </summary>
    private static HashSet<string> CollectInstallLocationsFromRegistry(Action<string> log)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new (RegistryHive Hive, string Path, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry64),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry32),
            (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Default),
        };

        // Stop-lijst voor parent-walking: deze paden mogen NIET als owned
        // gemarkeerd worden (anders zou bv. een "C:\Program Files\VMware"
        // install-location de hele "Program Files" beschermen, en zou geen
        // enkele folder in Program Files ooit als orphan flaggen).
        var stopAt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sf in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        })
        {
            if (!string.IsNullOrEmpty(sf)) stopAt.Add(NormalizePath(sf));
        }

        foreach (var (hive, path, view) in sources)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(path);
                if (uninstallKey == null) continue;
                foreach (var subName in uninstallKey.GetSubKeyNames())
                {
                    using var sub = uninstallKey.OpenSubKey(subName);
                    if (sub == null) continue;

                    // Verzamel alle paden uit deze entry. Volgorde maakt niet uit —
                    // duplicates worden door de HashSet weggefilterd.
                    foreach (var dir in ExtractPathsFromUninstallEntry(sub))
                        AddPathAndParents(dir, result, stopAt);
                }
            }
            catch (Exception ex)
            {
                log($"InstallLocation scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // App Paths — Windows' canonical "exe → install folder" register.
        // Bijna elke installer schrijft hier een entry voor de hoofd-exe zodat
        // Windows die kan resolven via "start <exe>". Vangt apps die geen
        // klassieke uninstall-entry hebben.
        var appPathSources = new (RegistryHive Hive, string Path, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", RegistryView.Registry64),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths", RegistryView.Registry32),
            (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", RegistryView.Default),
        };
        foreach (var (hive, path, view) in appPathSources)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPathsKey = baseKey.OpenSubKey(path);
                if (appPathsKey == null) continue;
                foreach (var subName in appPathsKey.GetSubKeyNames())
                {
                    using var sub = appPathsKey.OpenSubKey(subName);
                    if (sub == null) continue;
                    // Default value is de exe-pad; "Path" value is soms de install-folder.
                    var defaultExe = sub.GetValue(null) as string;
                    var explicitPath = sub.GetValue("Path") as string;

                    var dir1 = ResolveToDirectory(defaultExe);
                    if (dir1 != null) AddPathAndParents(dir1, result, stopAt);
                    var dir2 = ResolveToDirectory(explicitPath);
                    if (dir2 != null) AddPathAndParents(dir2, result, stopAt);
                }
            }
            catch (Exception ex)
            {
                log($"App Paths scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Trekt alle paden die er enigszins op lijken uit een uninstall registry-
    /// entry. Probeert verschillende velden — apps zijn inconsistent over wat
    /// ze schrijven. Yields directory-paden (file paths worden geconverteerd
    /// naar hun parent directory).
    /// </summary>
    private static IEnumerable<string> ExtractPathsFromUninstallEntry(RegistryKey entry)
    {
        // 1. InstallLocation — meest betrouwbaar wanneer aanwezig (= directe
        //    install-folder, niet een file).
        var dir = ResolveToDirectory(entry.GetValue("InstallLocation") as string);
        if (dir != null) yield return dir;

        // 2. DisplayIcon — wijst naar de hoofd-exe (of ico). Format:
        //    "C:\path\to\app.exe,0" of "C:\path\to\app.exe". Strip de ,N
        //    icon-index, dan parent dir.
        dir = ResolveToDirectory(StripIconIndex(entry.GetValue("DisplayIcon") as string));
        if (dir != null) yield return dir;

        // 3. UninstallString / QuietUninstallString — command line voor
        //    uninstaller. Format kan zijn:
        //      "C:\path\unins000.exe" /S
        //      C:\path\unins000.exe
        //      MsiExec.exe /X{GUID}    ← geen pad om uit te halen
        //      cmd.exe /c "..."         ← idem
        dir = ResolveToDirectory(ExtractExePathFromCommandLine(entry.GetValue("UninstallString") as string));
        if (dir != null) yield return dir;
        dir = ResolveToDirectory(ExtractExePathFromCommandLine(entry.GetValue("QuietUninstallString") as string));
        if (dir != null) yield return dir;

        // 4. InstallSource — soms hetzelfde als InstallLocation, soms een
        //    download-folder (geen install). Best-effort.
        dir = ResolveToDirectory(entry.GetValue("InstallSource") as string);
        if (dir != null) yield return dir;
    }

    private static void AddPathAndParents(string fullPath, HashSet<string> result, HashSet<string> stopAt)
    {
        var normalized = NormalizePath(fullPath);
        if (string.IsNullOrEmpty(normalized)) return;
        if (stopAt.Contains(normalized)) return;  // niet de root toevoegen
        result.Add(normalized);

        var parent = System.IO.Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(parent) && !stopAt.Contains(parent))
        {
            result.Add(parent);
            parent = System.IO.Path.GetDirectoryName(parent);
        }
    }

    private static string? StripIconIndex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("\""))
        {
            // Quoted: "C:\path\file.exe",0 of "C:\path\file.exe"
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 0) return trimmed.Substring(1, endQuote - 1);
        }
        // Onquoted: kijk of er een ,N suffix is. Comma kan in pad voorkomen,
        // maar de meeste DisplayIcons doen "path,0" als index. Heuristic:
        // laatste comma + alleen digits erna = icon-index.
        var lastComma = trimmed.LastIndexOf(',');
        if (lastComma > 0 && lastComma < trimmed.Length - 1)
        {
            var suffix = trimmed.Substring(lastComma + 1);
            if (int.TryParse(suffix, out _))
                return trimmed.Substring(0, lastComma);
        }
        return trimmed;
    }

    private static string? ExtractExePathFromCommandLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        // MsiExec.exe of cmd.exe — geen relevant pad om te extraheren.
        if (trimmed.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.StartsWith("cmd.exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.StartsWith("rundll32", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)) return null;

        if (trimmed.StartsWith("\""))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 0) return trimmed.Substring(1, endQuote - 1);
        }
        // Onquoted command — pak alles tot eerste spatie als pad. Werkt alleen
        // als pad geen spatie bevat (rare, maar mogelijk voor unins000.exe-style
        // entries onder C:\Program Files\<NoSpaceVendor>\).
        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0) return trimmed.Substring(0, spaceIdx);
        return trimmed;
    }

    private static string? ResolveToDirectory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().Trim('"');
        try
        {
            if (Directory.Exists(trimmed)) return trimmed;
            if (File.Exists(trimmed)) return System.IO.Path.GetDirectoryName(trimmed);
        }
        catch { }
        return null;
    }

    private static string NormalizePath(string p)
    {
        try { return Path.GetFullPath(p).TrimEnd('\\', '/'); }
        catch { return p.TrimEnd('\\', '/'); }
    }

    private static bool PathIsOwned(string folderPath, HashSet<string> ownedPaths)
    {
        if (ownedPaths.Count == 0) return false;
        return ownedPaths.Contains(NormalizePath(folderPath));
    }

    /// <summary>
    /// Peek één level diep: als een direct child folder matcht met een
    /// installed app, hoort de parent impliciet bij die vendor. Voorbeeld:
    /// folder "Anthropic" met child "Claude" → "Claude" is installed app,
    /// dus parent "Anthropic" niet orphan.
    /// </summary>
    private static bool HasInstalledMatchInChildren(DirectoryInfo dir, HashSet<string> nameSet, Action<string> log)
    {
        try
        {
            foreach (var child in dir.EnumerateDirectories())
            {
                if (HasInstalledMatch(child.Name, nameSet))
                    return true;
            }
        }
        catch (Exception ex)
        {
            // ACL of permission failure — beter false-negative dan exception
            // die de hele scan opblaast. We willen liever een orphan-flag op
            // een folder die we niet konden inspecteren dan dat de scan crasht.
            log($"Child peek error on {dir.FullName}: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    private static bool HasInstalledMatch(string folderName, HashSet<string> nameSet)
    {
        var normFolder = Normalize(folderName);
        if (normFolder.Length < 3) return true;  // zeer korte namen niet als orphan flaggen
        if (nameSet.Contains(normFolder)) return true;
        // Substring-match: folder bevat een installed naam, of installed naam
        // bevat folder. Vermijd false positives door minimum-lengte check.
        foreach (var name in nameSet)
        {
            if (name.Length < 4) continue;
            if (normFolder.Contains(name) || name.Contains(normFolder))
                return true;
        }
        return false;
    }

    private static bool IsProtectedFolder(string folderName)
    {
        // Folders die we nooit als orphan voorstellen — system / shell /
        // platform infrastructure die meerdere apps gebruiken of cruciaal zijn
        // voor Windows. Ruime lijst zodat we false positives minimaliseren.
        var protectedNames = new[]
        {
            // Program Files system
            "Common Files", "Internet Explorer", "Windows Defender",
            "Windows Mail", "Windows Media Player", "Windows NT",
            "Windows Photo Viewer", "Windows Portable Devices",
            "Windows Sidebar", "WindowsApps", "WindowsPowerShell",
            "ModifiableWindowsApps", "Microsoft Update Health Tools",
            "Microsoft", "Application Verifier", "dotnet",
            // AppData system
            "Windows", "Packages", "Temp", "TileDataLayer",
            "ConnectedDevicesPlatform", "WinRT", "VirtualStore",
            "PackageStaging", "Publishers", "Programs",
            "ElevatedDiagnostics", "D3DSCache", "Comms", "CrashDumps",
            "WebCache", "INetCache", "INetCookies", "History", "ActiveSync",
            "PeerNetworking", "Network Shortcuts", "Recent",
            "SendTo", "Templates", "Start Menu", "Application Data",
            "Local Settings", "NetHood", "PrintHood", "Cookies",
            // Common Files / app frameworks
            "DTS", "Adobe AIR", "Java",
            "Common Files", "WindowsApps"
        };
        foreach (var p in protectedNames)
            if (string.Equals(p, folderName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static async Task<DeepCleanItem?> ScanCacheTargetAsync(
        string displayName, string path, DeepCleanCategory category,
        bool requiresElevation, bool isSafe, string description,
        Action<string> log)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                log($"SKIP {displayName}: path doesn't exist ({path})");
                return null;
            }
            var size = await Task.Run(() => SafeFolderSize(path));
            if (size == 0)
            {
                log($"SKIP {displayName}: empty");
                return null;
            }
            log($"FOUND {displayName}: {size}B at {path}");
            return new DeepCleanItem(
                displayName: displayName,
                path: path,
                category: category,
                sizeBytes: size,
                requiresElevation: requiresElevation,
                isSafe: isSafe,
                description: description);
        }
        catch (Exception ex)
        {
            log($"ERROR scanning {displayName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<DeepCleanItem?> BuildOrphanedItemAsync(
        DirectoryInfo dir, bool requiresElevation, Action<string> log)
    {
        try
        {
            var size = await Task.Run(() => SafeFolderSize(dir.FullName));
            DateTime? lastWrite = null;
            try { lastWrite = dir.LastWriteTime; } catch { }
            log($"ORPHAN candidate: {dir.FullName} ({size}B, modified {lastWrite:yyyy-MM-dd})");
            return new DeepCleanItem(
                displayName: dir.Name,
                path: dir.FullName,
                category: DeepCleanCategory.OrphanedFolder,
                sizeBytes: size,
                requiresElevation: requiresElevation,
                isSafe: false,
                lastModified: lastWrite,
                description: "Geen geïnstalleerde app gevonden die bij deze folder hoort. Kan een leftover zijn van een eerder verwijderde app, een portable app, of een vendor-folder die meerdere subapps deelt.");
        }
        catch (Exception ex)
        {
            log($"ORPHAN error for {dir.FullName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static long ComputeRecycleBinSize(Action<string> log)
    {
        long total = 0;
        // Per-drive $Recycle.Bin\<SID> — we gebruiken Local Application Data's
        // root drive als startpunt (waar user typisch z'n Recycle Bin zit) en
        // alle fixed drives erbij om volledig te zijn.
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName);
        foreach (var drive in drives)
        {
            var binRoot = Path.Combine(drive, "$Recycle.Bin");
            if (!Directory.Exists(binRoot)) continue;
            try
            {
                foreach (var sidDir in Directory.EnumerateDirectories(binRoot))
                {
                    total += SafeFolderSize(sidDir);
                }
            }
            catch (Exception ex)
            {
                log($"RecycleBin size scan error on {binRoot}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return total;
    }

    private static long SafeFolderSize(string path)
    {
        try
        {
            long total = 0;
            foreach (var fi in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { total += fi.Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ── Delete batch ──────────────────────────────────────────────

    /// <summary>
    /// Delete-batch met split user/elevated. Recycle Bin gaat speciaal via
    /// PowerShell Clear-RecycleBin (kan vanuit user-context als de bin van de
    /// huidige user is, maar we doen 'm vanuit de elevated batch zodat alle
    /// drives + all-users meegenomen worden).
    /// </summary>
    public async Task<DeepCleanDeleteResult> DeleteAsync(IReadOnlyList<DeepCleanItem> items)
    {
        if (items.Count == 0)
            return new DeepCleanDeleteResult(0, 0, 0, new Dictionary<string, (bool, string)>(), Cancelled: false);

        var results = new Dictionary<string, (bool, string)>();
        var elevated = items.Where(i => i.RequiresElevation || i.Category == DeepCleanCategory.RecycleBin).ToList();
        var local = items.Where(i => !i.RequiresElevation && i.Category != DeepCleanCategory.RecycleBin).ToList();
        long bytesFreed = 0;

        // 1) Local deletes — geen UAC.
        foreach (var item in local)
        {
            try
            {
                ClearFolderContents(item.Path);
                results[item.Path] = (true, "Cleared");
                bytesFreed += item.SizeBytes;
            }
            catch (Exception ex)
            {
                results[item.Path] = (false, ex.Message);
            }
        }

        // 2) Elevated batch.
        var cancelled = false;
        if (elevated.Count > 0)
        {
            var elevatedResult = await RunElevatedBatchAsync(elevated);
            cancelled = elevatedResult.Cancelled;
            foreach (var kv in elevatedResult.ResultsByPath)
            {
                results[kv.Key] = kv.Value;
                if (kv.Value.ok)
                {
                    var match = elevated.FirstOrDefault(e => e.Path == kv.Key);
                    if (match != null) bytesFreed += match.SizeBytes;
                }
            }
        }

        var success = results.Count(kv => kv.Value.Item1);
        var failed = results.Count - success;
        return new DeepCleanDeleteResult(success, failed, bytesFreed, results, cancelled);
    }

    /// <summary>
    /// Verwijdert alle inhoud van een cache-folder maar laat de folder zelf
    /// staan — Windows / browser maken die opnieuw aan en sommige system tools
    /// raken in de war als de folder zelf weg is (bv. Prefetch). Voor orphaned
    /// folders zou je wél de folder zelf willen weghalen — die handelen we
    /// daarom apart af.
    /// </summary>
    private static void ClearFolderContents(string path)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return;
        foreach (var file in dir.EnumerateFiles())
        {
            try { file.IsReadOnly = false; file.Delete(); } catch { /* in-use → skip */ }
        }
        foreach (var sub in dir.EnumerateDirectories())
        {
            try { sub.Delete(recursive: true); } catch { /* in-use → skip */ }
        }
    }

    private static async Task<ElevatedDeleteResult> RunElevatedBatchAsync(IReadOnlyList<DeepCleanItem> items)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_deepclean_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{Escape(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START|$(Get-Date -Format o)\"");

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var path = Escape(item.Path);
            sb.AppendLine($"Log \"PROGRESS|{i + 1}|{items.Count}|{Escape(item.DisplayName)}\"");
            sb.AppendLine("try {");

            if (item.Category == DeepCleanCategory.RecycleBin)
            {
                // Clear-RecycleBin pakt alle drives + force = geen confirm.
                sb.AppendLine("    Clear-RecycleBin -Force -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Recycle Bin emptied\"");
            }
            else if (item.Category == DeepCleanCategory.OrphanedFolder)
            {
                // Volledige folder delete (NIET alleen contents) want het is
                // een orphan, geen cache die zichzelf hervult.
                sb.AppendLine($"    Remove-Item -LiteralPath '{path}' -Recurse -Force -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Deleted orphaned folder\"");
            }
            else
            {
                // Cache-folders: alleen content weg, folder zelf laten staan.
                // PS-equivalent van ClearFolderContents.
                sb.AppendLine($"    Get-ChildItem -LiteralPath '{path}' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Cache cleared\"");
            }
            sb.AppendLine("} catch {");
            sb.AppendLine($"    Log \"RESULT|{path}|FAIL|$($_.Exception.Message)\"");
            sb.AppendLine("}");
        }
        sb.AppendLine("Log \"END|$(Get-Date -Format o)\"");

        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_deepclean_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
        await File.WriteAllTextAsync(scriptPath, sb.ToString(), Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var results = new Dictionary<string, (bool, string)>();
        var cancelled = false;
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                foreach (var item in items) results[item.Path] = (false, "Could not start elevated process");
                return new ElevatedDeleteResult(results, false);
            }
            await proc.WaitForExitAsync();
            ParseResults(logPath, results);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            cancelled = true;
            foreach (var item in items)
                if (!results.ContainsKey(item.Path))
                    results[item.Path] = (false, "Cancelled — UAC prompt declined");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }

        // Items zonder RESULT-line markeren als failed.
        foreach (var item in items)
            if (!results.ContainsKey(item.Path))
                results[item.Path] = (false, "Did not run (interrupted)");

        return new ElevatedDeleteResult(results, cancelled);
    }

    private static void ParseResults(string logPath, Dictionary<string, (bool, string)> results)
    {
        if (!File.Exists(logPath)) return;
        string[] lines;
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { return; }

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("RESULT|")) continue;
            var parts = trimmed.Split('|', 4);
            if (parts.Length < 3) continue;
            var path = parts[1];
            var ok = parts[2] == "OK";
            var msg = parts.Length >= 4 ? parts[3] : (ok ? "OK" : "Failed");
            results[path] = (ok, msg);
        }
    }

    private static string Escape(string s) => s.Replace("'", "''");

    private sealed record ElevatedDeleteResult(
        IReadOnlyDictionary<string, (bool ok, string msg)> ResultsByPath,
        bool Cancelled);
}

public sealed record DeepCleanDeleteResult(
    int SuccessCount,
    int FailedCount,
    long BytesFreed,
    IReadOnlyDictionary<string, (bool success, string message)> ResultsByPath,
    bool Cancelled);
