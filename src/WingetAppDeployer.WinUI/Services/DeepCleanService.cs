using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using WingetAppDeployer_WinUI.Helpers;
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
        var result = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        // Ook de "soft" categorie-locations meenemen zodat de scan-summary
        // tekst dekkend is — anders denkt user dat we alleen folders scanden.
        result.Add("Uninstall registry keys + App Paths + MUIcache + class handlers");
        result.Add("Start Menu / Desktop shortcuts");
        result.Add("Scheduled tasks + Firewall rules");
        result.Add("Windows services + HKCU\\Software vendor keys");
        return result;
    }

    /// <summary>
    /// Returnt de registry-paden die ScanOrphanedRegistryAsync afloopt.
    /// </summary>
    public static IReadOnlyList<string> GetOrphanedRegistryLocations()
    {
        return new List<string>
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        };
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
        Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
        log($"=== DeepClean caches scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

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
    /// Scant uninstall registry keys (HKLM 64-bit + WOW6432Node + HKCU) en
    /// flag entries waarvan ALLE pad-velden (InstallLocation / DisplayIcon /
    /// UninstallString) niet meer naar bestaande paden wijzen — registry-
    /// leftovers van apps die ooit verwijderd zijn waarvan Windows de uninstall
    /// key niet opruimde.
    /// Levert geen entries die we ook niet als installed-app zouden tellen
    /// (SystemComponent / ParentKeyName / KB-updates), zodat we geen system-
    /// patches als "orphan" voorstellen.
    /// </summary>
    public async Task<List<DeepCleanItem>> ScanOrphanedRegistryAsync()
    {
        Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
        log($"=== Orphaned registry scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

        // Build cross-check token-set uit winget + AppX (non-registry sources).
        // Een minimale registry-entry zonder werkende paden is dan nog steeds
        // alive als winget of AppX 'm tracked.
        var rawInstalled = await App.InstalledApps.DetectAllAsync();
        var crossCheckTokens = BuildNonRegistryInstalledTokens(rawInstalled);
        log($"Cross-check tokens: {crossCheckTokens.Count} from winget+AppX entries (registry-source excluded)");

        return await Task.Run(() =>
        {
            var results = new List<DeepCleanItem>();
            int checkedAlive = 0;
            int skippedSystem = 0;
            var sources = new (RegistryHive Hive, string Path, RegistryView View, bool RequiresElevation, string DisplayPrefix)[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry64, true,  @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry32, true, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Default, false,    @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            };

            foreach (var (hive, path, view, requiresElev, displayPrefix) in sources)
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

                        // Filter out entries die we ook niet als installed-app
                        // tellen — deze zijn geen "leftover" maar bewuste
                        // system/patch entries.
                        if (sub.GetValue("SystemComponent") is int sysCmp && sysCmp == 1) { skippedSystem++; continue; }
                        if (sub.GetValue("ParentKeyName") is string parent && !string.IsNullOrEmpty(parent)) { skippedSystem++; continue; }
                        var display = sub.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(display)) { skippedSystem++; continue; }
                        if (display.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase)) { skippedSystem++; continue; }
                        if (display.StartsWith("Security Update for ", StringComparison.OrdinalIgnoreCase)) { skippedSystem++; continue; }
                        if (display.StartsWith("Hotfix for ", StringComparison.OrdinalIgnoreCase)) { skippedSystem++; continue; }

                        // Check de gezondheid van de entry: minstens één pad-veld
                        // moet resolven, OF DisplayName moet matchen met een
                        // winget/AppX-tracked install (cross-check).
                        var aliveResult = CheckRegistryEntryAlive(sub, crossCheckTokens);
                        var registryKeyPath = $"{displayPrefix}\\{subName}";
                        var displayNameFinal = string.IsNullOrWhiteSpace(display) ? subName : display;

                        if (aliveResult.IsAlive)
                        {
                            // Valid install — niet als orphan flaggen. Wel logging
                            // zodat user kan verifieren waarom z'n entry niet als
                            // orphan gemarkeerd is (bv. unins000.exe nog op disk).
                            checkedAlive++;
                            log($"  ALIVE : {displayNameFinal} → {aliveResult.Reason}");
                            continue;
                        }

                        var publisher = sub.GetValue("Publisher") as string ?? string.Empty;

                        log($"  ORPHAN: {displayNameFinal} → {aliveResult.Reason}");

                        results.Add(new DeepCleanItem(
                            displayName: displayNameFinal,
                            path: registryKeyPath,
                            category: DeepCleanCategory.OrphanedRegistry,
                            sizeBytes: 0,
                            requiresElevation: requiresElev,
                            isSafe: false,
                            description: $"Uninstall registry-entry zonder werkende paden. {aliveResult.Reason} — Windows zou deze key normaal opruimen tijdens een uninstall, maar dat is hier niet gebeurd. Veilig om te verwijderen, ruimt alleen registry op."));
                    }
                }
                catch (Exception ex)
                {
                    log($"Registry orphan scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            log($"Orphaned registry scan complete — checked: {results.Count + checkedAlive} entries " +
                $"(orphan: {results.Count}, alive: {checkedAlive}, skipped system/patches: {skippedSystem})");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Walkt dezelfde uninstall registry keys als ScanOrphanedRegistryAsync en
    /// returnt de identifiers (in InstalledAppsService Web-source format
    /// "{hive}\\{path}\\{subName}") van entries die alive zijn. Gebruikt door
    /// ScanOrphanedFoldersAsync om dode registry-entries uit de installed-list
    /// te filteren — anders zou een folder die alleen via zo'n dode entry
    /// "matched" niet als orphan terugkomen.
    /// </summary>
    private static HashSet<string> CollectAliveRegistryIdentifiers(HashSet<string> crossCheckTokens)
    {
        var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new (RegistryHive Hive, string Path, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry64),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry32),
            (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Default),
        };
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
                    var (isAlive, _) = CheckRegistryEntryAlive(sub, crossCheckTokens);
                    if (isAlive)
                    {
                        // Match het exacte format dat InstalledAppsService.DetectRegistry
                        // gebruikt voor InstalledAppEntry.Identifier: "{hive}\\{path}\\{subName}"
                        // — daar zit GEEN spatie tussen "Local" en "Machine" omdat de enum-name
                        // gewoon "LocalMachine" is.
                        alive.Add($"{hive}\\{path}\\{subName}");
                    }
                }
            }
            catch
            {
                // Permissions / IO error — entries in die hive worden niet geverifieerd.
                // Conservatief: niet leeg laten zodat we niet alle web-entries filteren.
            }
        }
        return alive;
    }

    /// <summary>
    /// Checkt of een uninstall registry-entry "alive" is. Twee signal-bronnen:
    ///   1. Pad-velden — InstallLocation / DisplayIcon / UninstallString / QuietUninstallString
    ///      Bewust GEEN InstallSource — dat is een download/staging-pad
    ///      (typisch %TEMP%\WinGet\...) dat Windows opruimt na install.
    ///   2. Cross-check tegen winget+AppX — een minimale registry-entry zonder
    ///      werkende paden kan tóch een echte install zijn als winget of AppX
    ///      'm tracked (winget-installed apps schrijven vaak een sparse uninstall
    ///      key). Omgekeerd: een leftover registry-entry zonder paden EN zonder
    ///      winget/AppX-tegenhanger is een echte orphan.
    /// </summary>
    private static (bool IsAlive, string Reason) CheckRegistryEntryAlive(
        RegistryKey entry, HashSet<string> nonRegistryInstalledTokens)
    {
        var fields = new (string Name, string? Raw)[]
        {
            ("InstallLocation",       entry.GetValue("InstallLocation") as string),
            ("DisplayIcon",           StripIconIndex(entry.GetValue("DisplayIcon") as string)),
            ("UninstallString",       ExtractExePathFromCommandLine(entry.GetValue("UninstallString") as string)),
            ("QuietUninstallString",  ExtractExePathFromCommandLine(entry.GetValue("QuietUninstallString") as string)),
        };

        var checkedFields = 0;
        var deadFields = new List<string>();
        foreach (var (name, raw) in fields)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            checkedFields++;
            var resolved = ResolveToDirectory(raw);
            if (resolved != null)
                return (true, $"{name}='{raw}' → {resolved}");
            deadFields.Add($"{name}='{raw}'");
        }

        // Path-check faalde of niet mogelijk. Cross-check met winget+AppX:
        // tokenize de DisplayName en kijk of er overlap is met de set van
        // tokens uit non-registry installed apps. Voorbeeld:
        //   Entry "GitHub CLI" → tokens [github] (cli te kort)
        //     winget tracked "GitHub CLI" → set bevat [github]
        //     overlap → alive ✓
        //   Entry "Claude" → tokens [claude]
        //     winget/AppX heeft geen Claude → set bevat geen [claude]
        //     no overlap → orphan ✓
        var displayName = entry.GetValue("DisplayName") as string;
        if (!string.IsNullOrWhiteSpace(displayName) && nonRegistryInstalledTokens.Count > 0)
        {
            var entryTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddWithTokens(entryTokens, displayName);
            if (entryTokens.Overlaps(nonRegistryInstalledTokens))
                return (true, $"DisplayName='{displayName}' matched by winget/AppX tracked install");
        }

        if (checkedFields == 0)
        {
            // Geen pad-velden EN geen winget/AppX cross-match. Dit is bijna
            // zeker een leftover registry-entry zonder enige verifieerbare
            // install-bewijs. Mark als orphan.
            return (false, "no path fields and no winget/AppX cross-match");
        }

        return (false, $"all {checkedFields} path field(s) dead and no winget/AppX cross-match [{string.Join(" | ", deadFields)}]");
    }

    /// <summary>
    /// Bouwt een token-set uit InstalledAppsService entries die NIET uit registry
    /// komen — dus winget + AppX. Gebruikt door CheckRegistryEntryAlive om
    /// minimale registry-entries (zonder werkende paden) te valideren tegen een
    /// onafhankelijke detectiebron. Als winget of AppX een app tracked, is die
    /// genuinely installed regardless of registry-state.
    /// </summary>
    private static HashSet<string> BuildNonRegistryInstalledTokens(IEnumerable<InstalledAppEntry> rawInstalled)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rawInstalled)
        {
            // Skip Web-source — dat zíjn de registry-entries die we evalueren.
            // Cross-check moet onafhankelijk zijn van registry zelf.
            if (entry.Source == InstalledSource.Web) continue;
            AddWithTokens(tokens, entry.DisplayName);
            if (!string.IsNullOrEmpty(entry.Publisher)) AddWithTokens(tokens, entry.Publisher);
            // Voor Winget: ID-segments (Publisher.AppName)
            if (entry.Source == InstalledSource.Winget)
            {
                foreach (var p in entry.Identifier.Split('.'))
                    if (p.Length >= 4) tokens.Add(NormalizeForTokens(p));
            }
            // Voor Store: PackageFullName-segments
            if (entry.Source == InstalledSource.Store)
            {
                foreach (var p in entry.Identifier.Split('_')[0].Split('.'))
                    if (p.Length >= 4) tokens.Add(NormalizeForTokens(p));
            }
        }
        return tokens;
    }

    private static string NormalizeForTokens(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
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
        Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
        log($"=== Orphaned folders scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        var sw = Stopwatch.StartNew();

        // Comparison set — niet alleen volledige DisplayNames, maar ook losse
        // tokens (woorden) zodat een folder "VMware" matcht met installed app
        // "VMware Workstation Pro". Zonder tokenize zou alleen substring werken,
        // wat soms faalt door publisher-encoding of locale-verschillen.
        //
        // Filter eerst dode registry-entries uit de installed-list zodat een
        // folder die alleen via een leftover registry-entry "matched" wordt
        // niet ten onrechte als geldig wordt geskipt. CollectAliveRegistryIds
        // walkt de uninstall keys parallel met onze ScanOrphanedRegistryAsync
        // en bouwt de set van entries die nog werkende paden hebben — alleen
        // die tellen als "echte installed app" voor folder-matching doeleinden.
        var rawInstalled = await App.InstalledApps.DetectAllAsync();
        // Cross-check tokens uit winget+AppX (non-registry) — gebruikt door
        // CheckRegistryEntryAlive om minimale registry-entries te valideren
        // tegen onafhankelijke detectie. Voorkomt dat "GitHub CLI" type winget-
        // entries (sparse uninstall key) als orphan worden gemarkeerd.
        var crossCheckTokens = BuildNonRegistryInstalledTokens(rawInstalled);
        var aliveRegistryIds = CollectAliveRegistryIdentifiers(crossCheckTokens);
        var installed = rawInstalled
            .Where(e => e.Source != InstalledSource.Web || aliveRegistryIds.Contains(e.Identifier))
            .ToList();
        log($"Installed-list filtered: {installed.Count} of {rawInstalled.Count} kept ({rawInstalled.Count - installed.Count} dead-registry web-entries removed)");

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
    /// Scant App Paths registry (HKLM/WOW6432Node/HKCU) — Windows' "where is
    /// this exe" register. Elke entry heeft typisch een (Default) value met
    /// het volledige pad naar de exe. Als die exe niet bestaat = orphan.
    /// Dit is bredere registry-scope dan alleen uninstall keys; vangt residue
    /// van apps wiens uninstall-key wel werd opgeruimd maar App Paths niet.
    /// </summary>
    public Task<List<DeepCleanItem>> ScanOrphanedAppPathsAsync()
    {
        return Task.Run(() =>
        {
            Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
            log($"=== Orphaned App Paths scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            var results = new List<DeepCleanItem>();
            var sources = new (RegistryHive Hive, string Path, RegistryView View, bool RequiresElevation, string DisplayPrefix)[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", RegistryView.Registry64, true, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths", RegistryView.Registry32, true, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
                (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", RegistryView.Default, false, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            };

            foreach (var (hive, path, view, requiresElev, displayPrefix) in sources)
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

                        var defaultExe = sub.GetValue(null) as string;
                        var pathValue = sub.GetValue("Path") as string;

                        // Beide kunnen aanwezig zijn. Als minstens één resolveert
                        // naar een bestaand bestand of directory → alive.
                        var exeResolved = ResolveToDirectory(defaultExe?.Trim().Trim('"'));
                        var dirResolved = ResolveToDirectory(pathValue);
                        if (exeResolved != null || dirResolved != null) continue;

                        // Geen velden gevuld? Dan kunnen we niet zeggen of orphan.
                        // Conservatief: skip.
                        if (string.IsNullOrWhiteSpace(defaultExe) && string.IsNullOrWhiteSpace(pathValue)) continue;

                        var keyPath = $"{displayPrefix}\\{subName}";
                        log($"ORPHAN App Paths: {keyPath} (Default='{defaultExe}', Path='{pathValue}')");
                        results.Add(new DeepCleanItem(
                            displayName: subName,  // exe filename = de subkey-naam
                            path: keyPath,
                            category: DeepCleanCategory.OrphanedAppPath,
                            sizeBytes: 0,
                            requiresElevation: requiresElev,
                            isSafe: false,
                            description: $"App Paths registry-entry voor '{subName}' wijst naar een exe die niet meer bestaat. Windows gebruikt deze entries om 'Start > Run > {subName}' te resolven; bij een dood pad faalt dat."));
                    }
                }
                catch (Exception ex)
                {
                    log($"App Paths scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            log($"Orphaned App Paths scan complete — {results.Count} broken entries");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Scant MUIcache (`HKCU\Software\Classes\Local Settings\Software\Microsoft\
    /// Windows\Shell\MuiCache`) — Windows shell cache voor recently-launched
    /// programma's. Elke value name = exe-pad, value data = friendly name.
    /// Value names die naar dode exes wijzen = leftover MUIcache entries van
    /// apps die ooit gestart zijn en nu weg zijn. CCleaner-style.
    /// </summary>
    public Task<List<DeepCleanItem>> ScanOrphanedMuiCacheAsync()
    {
        return Task.Run(() =>
        {
            Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
            log($"=== Orphaned MUIcache scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            var results = new List<DeepCleanItem>();
            const string muiPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
            const string displayPrefix = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(muiPath);
                if (key == null)
                {
                    log("MUIcache key not found — skipping");
                    return results;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    // MUIcache value names hebben formaat:
                    //   "C:\Program Files\App\app.exe.FriendlyAppName"
                    //   "C:\path\file.exe.ApplicationCompany"
                    // Strip de ".FriendlyAppName" / ".ApplicationCompany" suffix
                    // om het pure exe-pad te krijgen.
                    if (string.IsNullOrEmpty(valueName)) continue;
                    var exePath = StripMuiCacheSuffix(valueName);
                    if (string.IsNullOrEmpty(exePath)) continue;

                    // Skip if path resolves
                    try { if (File.Exists(exePath) || Directory.Exists(exePath)) continue; } catch { continue; }

                    var friendlyName = key.GetValue(valueName) as string ?? Path.GetFileName(exePath);
                    log($"ORPHAN MUIcache: '{friendlyName}' → '{exePath}' (gone)");
                    // Path moet uniek zijn per item (results-dict in DeleteAsync
                    // keys op Path). Daarom de value-name erbij. Format laat user
                    // zien WAT er leeft in MUIcache key en de exe waar het naar
                    // wijst.
                    results.Add(new DeepCleanItem(
                        displayName: friendlyName,
                        path: $"{displayPrefix} → {valueName}",
                        category: DeepCleanCategory.OrphanedMuiCache,
                        sizeBytes: 0,
                        requiresElevation: false,
                        isSafe: false,
                        description: $"MUIcache entry voor '{friendlyName}' wijst naar exe '{exePath}' die niet meer bestaat. Windows shell onthoudt zo het laatst-gebruikte programma; bij dode entries is het pure ruis.",
                        registryValueName: valueName));
                }
            }
            catch (Exception ex)
            {
                log($"MUIcache scan error: {ex.GetType().Name}: {ex.Message}");
            }

            log($"Orphaned MUIcache scan complete — {results.Count} broken entries");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Suffix-stripper voor MUIcache value names. Format:
    ///   "<exe-pad>.FriendlyAppName"  → exe-pad
    ///   "<exe-pad>.ApplicationCompany" → exe-pad
    ///   Special values als "LangID" zonder pad → null
    /// </summary>
    private static string StripMuiCacheSuffix(string raw)
    {
        // De suffix is altijd één van bekende reserved namen. We weten 'm
        // niet exact; pak alles tot de laatste punt VOOR de suffix.
        var suffixes = new[] { ".FriendlyAppName", ".ApplicationCompany" };
        foreach (var suffix in suffixes)
        {
            if (raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return raw.Substring(0, raw.Length - suffix.Length);
        }
        // Als geen bekende suffix maar het lijkt op een exe-pad (bevat ":\"), pak het zo
        if (raw.Length >= 3 && raw[1] == ':' && raw[2] == '\\') return raw;
        return string.Empty;  // skip — meta values zoals LangID
    }

    /// <summary>
    /// Scant `HKLM\Software\Classes\Applications\<exe>\shell\open\command` en
    /// HKCU equivalent — file-extension class handlers waarvan de exe weg is.
    /// Voorbeeld: na uninstall blijft soms een `Applications\app.exe` entry
    /// staan met `\shell\open\command` = `"C:\old\app.exe" "%1"`. Als
    /// `C:\old\app.exe` niet bestaat = orphan.
    /// </summary>
    public Task<List<DeepCleanItem>> ScanOrphanedClassHandlersAsync()
    {
        return Task.Run(() =>
        {
            Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
            log($"=== Orphaned class handlers scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            var results = new List<DeepCleanItem>();
            var sources = new (RegistryHive Hive, string Path, RegistryView View, bool RequiresElevation, string DisplayPrefix)[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\Classes\Applications", RegistryView.Registry64, true, @"HKLM\SOFTWARE\Classes\Applications"),
                (RegistryHive.CurrentUser,  @"SOFTWARE\Classes\Applications", RegistryView.Default, false, @"HKCU\SOFTWARE\Classes\Applications"),
            };

            foreach (var (hive, path, view, requiresElev, displayPrefix) in sources)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appsKey = baseKey.OpenSubKey(path);
                    if (appsKey == null) continue;
                    foreach (var exeName in appsKey.GetSubKeyNames())
                    {
                        using var exeKey = appsKey.OpenSubKey(exeName);
                        if (exeKey == null) continue;
                        using var cmdKey = exeKey.OpenSubKey(@"shell\open\command");
                        var commandRaw = cmdKey?.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(commandRaw)) continue;

                        // Extract exe-pad uit command line. Format meestal:
                        //   "C:\path\app.exe" "%1"
                        //   C:\path\app.exe %1
                        var exePath = ExtractExePathFromCommandLine(commandRaw);
                        if (string.IsNullOrEmpty(exePath)) continue;
                        try { if (File.Exists(exePath)) continue; } catch { continue; }

                        var keyPath = $"{displayPrefix}\\{exeName}";
                        log($"ORPHAN class handler: {keyPath} → '{exePath}' (gone)");
                        results.Add(new DeepCleanItem(
                            displayName: exeName,
                            path: keyPath,
                            category: DeepCleanCategory.OrphanedClassHandler,
                            sizeBytes: 0,
                            requiresElevation: requiresElev,
                            isSafe: false,
                            description: $"File-association registry-entry voor '{exeName}' wijst naar exe '{exePath}' die niet meer bestaat. Veroorzaakt failed Open With dialogs en lege right-click menu's."));
                    }
                }
                catch (Exception ex)
                {
                    log($"Class handlers scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            log($"Orphaned class handlers scan complete — {results.Count} broken entries");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Scant Start Menu folders voor `.lnk` shortcuts waarvan het target-pad
    /// niet meer bestaat. Walk both per-user en all-users Start Menu.
    /// </summary>
    public Task<List<DeepCleanItem>> ScanOrphanedShortcutsAsync()
    {
        return Task.Run(() =>
        {
            Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
            log($"=== Orphaned shortcuts scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            var results = new List<DeepCleanItem>();
            var roots = new (string Path, bool RequiresElevation)[]
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), false),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), true),
                (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false),
                (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), true),
            };

            foreach (var (rootPath, requiresElev) in roots.Where(r => !string.IsNullOrEmpty(r.Path) && Directory.Exists(r.Path)))
            {
                IEnumerable<string> lnkFiles;
                try { lnkFiles = Directory.EnumerateFiles(rootPath, "*.lnk", SearchOption.AllDirectories); }
                catch (Exception ex)
                {
                    log($"Shortcut enum error in {rootPath}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (var lnk in lnkFiles)
                {
                    try
                    {
                        var target = ResolveShortcutTarget(lnk);
                        if (string.IsNullOrEmpty(target)) continue;
                        // Target check — File of Directory.
                        if (File.Exists(target) || Directory.Exists(target)) continue;

                        var displayName = Path.GetFileNameWithoutExtension(lnk);
                        var size = SafeFileSize(lnk);
                        log($"ORPHAN shortcut: '{lnk}' → '{target}' (gone)");
                        results.Add(new DeepCleanItem(
                            displayName: displayName,
                            path: lnk,
                            category: DeepCleanCategory.OrphanedShortcut,
                            sizeBytes: size,
                            requiresElevation: requiresElev,
                            isSafe: false,
                            description: $"Shortcut wijst naar target '{target}' die niet meer bestaat. Klikken doet niets — kan veilig weg."));
                    }
                    catch (Exception ex)
                    {
                        log($"Shortcut parse error for {lnk}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            log($"Orphaned shortcuts scan complete — {results.Count} broken shortcuts");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Scant scheduled tasks via `schtasks /Query /XML ONE` + XML-parse. Per
    /// task: extract `<Actions><Exec><Command>` path, check of exe bestaat. Als
    /// exe weg is = orphan task. Filtert `\Microsoft\` system-tasks weg
    /// (Windows Update, Defender, etc. — daar willen we vanaf blijven).
    /// </summary>
    public Task<List<DeepCleanItem>> ScanOrphanedScheduledTasksAsync()
    {
        return Task.Run(async () =>
        {
            Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
            log($"=== Orphaned scheduled tasks scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            var sw = Stopwatch.StartNew();

            var results = new List<DeepCleanItem>();
            string xmlOutput;
            try
            {
                // schtasks /XML ONE dumpt alle tasks in één XML — sneller dan
                // per-task PowerShell. Locale-onafhankelijk (XML format = stabiel).
                xmlOutput = await RunCommandAsync("schtasks.exe", "/Query /XML ONE", log);
                log($"schtasks.exe completed in {sw.ElapsedMilliseconds}ms ({xmlOutput.Length} bytes)");
            }
            catch (Exception ex)
            {
                log($"schtasks.exe failed: {ex.GetType().Name}: {ex.Message}");
                return results;
            }

            // schtasks /XML ONE output is een concatenatie van XML-documents
            // gescheiden door `<?xml` declarations. Plus elk task-block heeft
            // een TaskPath header line ("Folder: \..." of "TaskPath: ...").
            // Robust parsing: split op `<?xml` en parse elk fragment apart.
            var fragments = xmlOutput.Split(new[] { "<?xml" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var frag in fragments)
            {
                var fullXml = "<?xml" + frag;
                try
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(fullXml);
                    var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");

                    // RegistrationInfo/URI = volledige task-path inclusief naam
                    var uri = doc.SelectSingleNode("//t:RegistrationInfo/t:URI", ns)?.InnerText
                              ?? doc.SelectSingleNode("//RegistrationInfo/URI")?.InnerText;
                    if (string.IsNullOrWhiteSpace(uri)) continue;

                    // Skip Microsoft system-tasks — niet onze verantwoordelijkheid
                    // en delete kan boot/update kapot maken.
                    if (uri.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;

                    // Pak alle Exec/Command paden — een task kan multiple actions hebben.
                    var commandNodes = doc.SelectNodes("//t:Actions/t:Exec/t:Command", ns);
                    if (commandNodes == null || commandNodes.Count == 0)
                    {
                        // Geen Exec actions (alleen ComHandler / SendEmail / ShowMessage) — niet checkable
                        continue;
                    }

                    var deadCommands = new List<string>();
                    var hasAliveCommand = false;
                    foreach (System.Xml.XmlNode? cmdNode in commandNodes)
                    {
                        if (cmdNode == null) continue;
                        var cmdRaw = cmdNode.InnerText?.Trim();
                        if (string.IsNullOrWhiteSpace(cmdRaw)) continue;

                        // Resolve env vars (%SystemRoot%, etc.)
                        var resolved = Environment.ExpandEnvironmentVariables(cmdRaw.Trim('"'));
                        if (File.Exists(resolved) || Directory.Exists(resolved))
                        {
                            hasAliveCommand = true;
                            break;
                        }
                        deadCommands.Add(cmdRaw);
                    }
                    if (hasAliveCommand) continue;
                    if (deadCommands.Count == 0) continue;

                    // Task-naam = deel na laatste `\`
                    var taskName = uri.Substring(uri.LastIndexOf('\\') + 1);
                    var deadList = string.Join(", ", deadCommands);
                    log($"ORPHAN task: {uri} → dead command(s): {deadList}");

                    // System-tasks (root-level paths zonder \Users\) typisch admin
                    // om te deleten. User-tasks onder \Users\<user>\ kunnen zonder UAC.
                    var requiresElev = !uri.StartsWith(@"\Users\", StringComparison.OrdinalIgnoreCase);

                    results.Add(new DeepCleanItem(
                        displayName: taskName,
                        path: uri,
                        category: DeepCleanCategory.OrphanedScheduledTask,
                        sizeBytes: 0,
                        requiresElevation: requiresElev,
                        isSafe: false,
                        description: $"Scheduled task '{taskName}' verwijst naar een exe die niet meer bestaat ({deadList}). Veilig om te deleten — Windows Task Scheduler probeert anders periodiek een dood programma te starten."));
                }
                catch (Exception ex)
                {
                    log($"Task XML parse error: {ex.GetType().Name}: {ex.Message}");
                }
            }

            log($"Orphaned scheduled tasks scan complete in {sw.ElapsedMilliseconds}ms — {results.Count} orphan task(s)");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Scant Windows Defender Firewall rules via DIRECTE registry-read i.p.v.
    /// `Get-NetFirewallRule` PowerShell cmdlet — die laatste is berucht traag
    /// (10-30+ seconden op een gemiddeld systeem) door COM-overhead. Rules
    /// zitten in `HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\
    /// Parameters\FirewallPolicy\FirewallRules` als value-pairs:
    ///   value-name = rule-id (gebruikt door Remove-NetFirewallRule -Name)
    ///   value-data = pipe-separated config: "v2.31|Action=Allow|App=C:\..|Name=...|..."
    /// Direct lezen = fractie van een seconde i.p.v. tientallen seconden.
    /// </summary>
    public Task<List<DeepCleanItem>> ScanOrphanedFirewallRulesAsync()
    {
        return Task.Run(() =>
        {
            Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
            var sw = Stopwatch.StartNew();
            log($"=== Orphaned firewall rules scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            var results = new List<DeepCleanItem>();
            const string fwPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var fwKey = baseKey.OpenSubKey(fwPath);
                if (fwKey == null)
                {
                    log("Firewall rules registry key not found");
                    return results;
                }

                int total = 0;
                foreach (var ruleName in fwKey.GetValueNames())
                {
                    total++;
                    if (string.IsNullOrEmpty(ruleName)) continue;
                    var raw = fwKey.GetValue(ruleName) as string;
                    if (string.IsNullOrEmpty(raw)) continue;

                    // Parse pipe-separated key=value format. App= heeft het
                    // program path; Name= heeft de display-name (kan @-prefix
                    // hebben voor indirect string, dan val terug op rule-id).
                    string? appPath = null;
                    string? displayName = null;
                    foreach (var segment in raw.Split('|'))
                    {
                        var eq = segment.IndexOf('=');
                        if (eq <= 0) continue;
                        var k = segment.Substring(0, eq);
                        var v = segment.Substring(eq + 1);
                        if (k.Equals("App", StringComparison.OrdinalIgnoreCase)) appPath = v;
                        else if (k.Equals("Name", StringComparison.OrdinalIgnoreCase)) displayName = v;
                    }

                    if (string.IsNullOrEmpty(appPath)) continue;  // port-only rule, geen program-pad
                    if (appPath.Equals("Any", StringComparison.OrdinalIgnoreCase)) continue;
                    if (appPath.Equals("System", StringComparison.OrdinalIgnoreCase)) continue;

                    var resolved = Environment.ExpandEnvironmentVariables(appPath);

                    // Microsoft system-rules: skip om geen Windows-component te raken.
                    if (resolved.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (resolved.StartsWith(@"%SystemRoot%", StringComparison.OrdinalIgnoreCase)) continue;

                    if (File.Exists(resolved)) continue;  // alive

                    // DisplayName kan een indirect string zijn (`@C:\path\res.dll,-123`)
                    // of een GUID-rule-id. Voor user-friendly weergave: val
                    // terug op de exe-filename (zonder extensie) — die zegt
                    // user direct welke app de rule was. "vmware.exe" → "vmware".
                    var exeName = Path.GetFileNameWithoutExtension(resolved);
                    var friendlyName =
                        !string.IsNullOrEmpty(displayName) && !displayName.StartsWith("@") && !LooksLikeGuid(displayName)
                            ? displayName
                            : (!string.IsNullOrEmpty(exeName) ? exeName : ruleName);

                    log($"ORPHAN firewall: '{friendlyName}' [{ruleName}] → '{appPath}' (gone)");
                    results.Add(new DeepCleanItem(
                        displayName: friendlyName,
                        path: $"{ruleName} → {appPath}",
                        category: DeepCleanCategory.OrphanedFirewallRule,
                        sizeBytes: 0,
                        requiresElevation: true,
                        isSafe: false,
                        description: $"Firewall rule '{friendlyName}' verwijst naar program '{appPath}' die niet meer bestaat. De rule heeft geen effect meer — verwijderen is veilig.",
                        registryValueName: ruleName));
                }

                log($"Firewall scan checked {total} rule(s) in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                log($"Firewall registry read error: {ex.GetType().Name}: {ex.Message}");
            }

            log($"Orphaned firewall rules scan complete — {results.Count} orphan rule(s)");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// Scant Windows services via `Get-CimInstance Win32_Service`. Flag een
    /// service als orphan ALLEEN als ALLE strikte criteria kloppen:
    ///   (a) ImagePath wijst naar een exe die niet meer bestaat,
    ///   (b) service is Stopped (niet Running),
    ///   (c) StartMode is Manual of Disabled (geen Auto / Boot / System —
    ///       die zijn cruciaal voor boot/login en raken we niet aan),
    ///   (d) PathName start niet met svchost.exe (DLL-hosted Windows services
    ///       die hun host-dll elders hebben),
    ///   (e) ImagePath niet in C:\Windows\ of System32 (system services),
    ///   (f) geen overlap met winget/AppX-tracked install tokens.
    /// Strict filter zodat we GEEN system-services per ongeluk weghalen.
    /// Default unchecked + caution-tier — user moet expliciet kiezen.
    /// </summary>
    public async Task<List<DeepCleanItem>> ScanOrphanedServicesAsync()
    {
        Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
        log($"=== Orphaned services scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        var sw = Stopwatch.StartNew();

        // Cross-check tokens uit winget+AppX — service met dezelfde tokens als
        // een tracked install is GEEN orphan, ook niet als z'n exe-pad faalt te
        // resolven (bv. lege string, "%SystemRoot%\sysnative\..." etc.).
        var rawInstalled = await App.InstalledApps.DetectAllAsync();
        var crossCheckTokens = BuildNonRegistryInstalledTokens(rawInstalled);
        log($"Cross-check tokens: {crossCheckTokens.Count} from winget+AppX");

        var results = new List<DeepCleanItem>();
        string psOutput;
        try
        {
            // Pipe-separated dump: Name|DisplayName|State|StartMode|PathName
            // -split '\|',5 garandeert dat PathName intact blijft als 'ie pipes
            // zou bevatten (rare maar mogelijk).
            const string script =
                "Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | " +
                "ForEach-Object { \"$($_.Name)|$($_.DisplayName)|$($_.State)|$($_.StartMode)|$($_.PathName)\" }";
            psOutput = await RunPowerShellAsync(script);
            log($"Get-CimInstance Win32_Service completed in {sw.ElapsedMilliseconds}ms ({psOutput.Length} bytes)");
        }
        catch (Exception ex)
        {
            log($"Win32_Service query failed: {ex.GetType().Name}: {ex.Message}");
            return results;
        }

        var lines = psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int totalChecked = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split('|', 5);
            if (parts.Length < 5) continue;
            totalChecked++;

            var name = parts[0].Trim();
            var displayName = parts[1].Trim();
            var state = parts[2].Trim();
            var startMode = parts[3].Trim();
            var pathName = parts[4].Trim();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pathName)) continue;

            // (b) State moet Stopped zijn — Running services zijn ofwel actief
            //     of net gecrasht; in beide gevallen niet onze taak om weg te halen.
            if (!state.Equals("Stopped", StringComparison.OrdinalIgnoreCase)) continue;

            // (c) StartMode strict op Manual/Disabled. Auto = wordt bij boot
            //     gestart, Boot/System = cruciaal voor Windows zelf.
            if (!startMode.Equals("Manual", StringComparison.OrdinalIgnoreCase) &&
                !startMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                continue;

            // (d) svchost.exe is een shared DLL-host. Kan niet zomaar de
            //     service-key weghalen want de DLL-implementatie zit elders
            //     (HKLM\SYSTEM\CurrentControlSet\Services\<name>\Parameters\ServiceDll).
            //     Out-of-scope voor onze orphan-scope.
            var pathTrimmed = pathName.Trim('"').TrimStart();
            if (pathTrimmed.StartsWith("svchost", StringComparison.OrdinalIgnoreCase) ||
                pathTrimmed.IndexOf("\\svchost.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            // Extract exe-pad uit PathName. Format kan zijn:
            //   "C:\path\service.exe" -arg
            //   C:\path\service.exe
            //   %SystemRoot%\system32\foo.exe
            var exePath = ExtractExePathFromCommandLine(pathName);
            if (string.IsNullOrEmpty(exePath)) continue;
            var resolvedExe = Environment.ExpandEnvironmentVariables(exePath);

            // (e) Skip alles onder C:\Windows\ — system services. Boot drive
            //     kan op een andere letter staan, dus check %SystemRoot% ook.
            var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(sysRoot) &&
                resolvedExe.StartsWith(sysRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (resolvedExe.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase)) continue;

            // (a) Exe moet daadwerkelijk dood zijn.
            try { if (File.Exists(resolvedExe)) continue; } catch { continue; }

            // (f) Cross-check: tokens uit Name+DisplayName matchen met winget/AppX?
            //     Skip — dit is een echte managed install, alleen de service entry
            //     is mogelijk gestale (bv. tijdens auto-update).
            var entryTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddWithTokens(entryTokens, name);
            if (!string.IsNullOrEmpty(displayName)) AddWithTokens(entryTokens, displayName);
            if (crossCheckTokens.Count > 0 && entryTokens.Overlaps(crossCheckTokens))
            {
                log($"  ALIVE service: {name} ({displayName}) — token cross-match with winget/AppX");
                continue;
            }

            var friendlyName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
            log($"  ORPHAN service: '{friendlyName}' [{name}] state={state} startMode={startMode} → '{resolvedExe}' (gone)");
            results.Add(new DeepCleanItem(
                displayName: friendlyName,
                path: $"{name} → {pathName}",
                category: DeepCleanCategory.OrphanedService,
                sizeBytes: 0,
                requiresElevation: true,
                isSafe: false,
                description: $"Windows service '{friendlyName}' verwijst naar exe '{resolvedExe}' die niet meer bestaat (Stopped, StartMode={startMode}). Veilig om te deleten via sc.exe — de service kan nooit meer starten.",
                registryValueName: name));
        }

        log($"Orphaned services scan complete in {sw.ElapsedMilliseconds}ms — checked {totalChecked}, found {results.Count} orphan(s)");
        return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Walkt `HKCU\Software\<Vendor>\<App>` (top 2 levels) en flag <App>-keys
    /// waarvan ALLE pad-values dood zijn — vendor-residue dat na uninstall
    /// blijft hangen omdat de uninstaller HKCU niet ruimt.
    ///
    /// Path-value detectie:
    ///   - Value-name in {InstallPath, InstallDir, InstallLocation, Path,
    ///     Program, ExecutablePath, ExePath, AppPath, Location, ...}
    ///   - Of value-data start met een drive-letter pattern (`X:\`)
    ///
    /// Voorwaarden voor orphan-flag:
    ///   - Minstens 1 pad-value gevonden in de key (anders niets te checken)
    ///   - ALLE pad-values resolven niet (file/dir bestaat niet)
    ///   - Geen overlap met winget/AppX tokens
    ///
    /// Skip protected top-level keys: Microsoft, Classes, Policies, Wow6432Node,
    /// RegisteredApplications — die zijn van Windows / system shells.
    /// </summary>
    public async Task<List<DeepCleanItem>> ScanOrphanedHkcuVendorAsync()
    {
        Action<string> log = msg => Diagnostics.Log("WingetAppDeployer_deepclean.log", msg);
        log($"=== Orphaned HKCU vendor scan {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

        var rawInstalled = await App.InstalledApps.DetectAllAsync();
        var crossCheckTokens = BuildNonRegistryInstalledTokens(rawInstalled);
        log($"Cross-check tokens: {crossCheckTokens.Count} from winget+AppX");

        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var results = new List<DeepCleanItem>();

            // Protected top-level subkeys onder HKCU\Software die we nooit
            // aanraken. "Microsoft" is gigantisch en heeft tienduizenden
            // sub-keys voor system-componenten; "Classes" is shell-binding;
            // "Policies" is group policy state.
            var protectedTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft", "Classes", "Policies", "Wow6432Node",
                "RegisteredApplications", "Clients", "Khronos",
                "Intel", "AMD", "NVIDIA", "Realtek",  // driver vendors — skip om GPU/audio state niet te raken
                "Google",    // Chrome + andere Google apps onder Google\<App>; Chrome heeft eigen uninstall
                "Mozilla"    // Firefox + thunderbird onder Mozilla\<App>; eigen uninstall
            };

            // Common-app names binnen vendor-keys die we niet als "key" willen
            // weergeven want ze zijn op zichzelf niet de vendor-naam. Niet
            // strict needed maar helpt logs cleaner houden.
            var pathValueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "InstallPath", "InstallDir", "InstallLocation", "Install Path",
                "Path", "Program", "ProgramPath", "ExecutablePath", "Executable",
                "Exe", "ExePath", "AppPath", "InstallationDirectory", "Location",
                "AppDir", "BinPath", "BinaryPath", "RootDir", "WorkingDirectory"
            };

            try
            {
                using var softwareKey = Registry.CurrentUser.OpenSubKey("Software");
                if (softwareKey == null)
                {
                    log("HKCU\\Software niet leesbaar — skipping");
                    return results;
                }

                int vendorsChecked = 0;
                int appsChecked = 0;
                foreach (var vendorName in softwareKey.GetSubKeyNames())
                {
                    if (protectedTop.Contains(vendorName)) continue;
                    vendorsChecked++;
                    using var vendorKey = softwareKey.OpenSubKey(vendorName);
                    if (vendorKey == null) continue;

                    foreach (var appName in vendorKey.GetSubKeyNames())
                    {
                        appsChecked++;
                        using var appKey = vendorKey.OpenSubKey(appName);
                        if (appKey == null) continue;

                        // Verzamel pad-values uit deze key. We kijken alleen
                        // naar top-level VALUES (geen recursive subkey-walk) —
                        // diepere keys kunnen we niet zonder false-positives
                        // verifieren.
                        var pathValues = new List<(string Name, string Resolved)>();
                        foreach (var valName in appKey.GetValueNames())
                        {
                            if (appKey.GetValueKind(valName) is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
                                continue;
                            var raw = appKey.GetValue(valName) as string;
                            if (string.IsNullOrWhiteSpace(raw)) continue;

                            var looksLikeKnownPathName = pathValueNames.Contains(valName);
                            var looksLikeDrivePath = LooksLikeDriveRootedPath(raw);
                            if (!looksLikeKnownPathName && !looksLikeDrivePath) continue;

                            var resolved = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                            pathValues.Add((valName, resolved));
                        }

                        if (pathValues.Count == 0) continue;

                        // Alle pad-values moeten dood zijn om als orphan te
                        // tellen. Eén leeft → dit is een legit app-config.
                        var allDead = true;
                        foreach (var (_, resolved) in pathValues)
                        {
                            try
                            {
                                if (File.Exists(resolved) || Directory.Exists(resolved)) { allDead = false; break; }
                            }
                            catch { /* ACL / IO — wees conservatief, beschouw als levend */ allDead = false; break; }
                        }
                        if (!allDead) continue;

                        // Cross-check tegen winget/AppX op zowel vendor- als app-tokens.
                        var entryTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        AddWithTokens(entryTokens, vendorName);
                        AddWithTokens(entryTokens, appName);
                        if (crossCheckTokens.Count > 0 && entryTokens.Overlaps(crossCheckTokens))
                        {
                            log($"  ALIVE HKCU: {vendorName}\\{appName} — token cross-match with winget/AppX");
                            continue;
                        }

                        var keyPath = $@"HKCU\Software\{vendorName}\{appName}";
                        var deadPathsList = string.Join(", ", pathValues.Take(3).Select(p => $"{p.Name}='{p.Resolved}'"));
                        log($"  ORPHAN HKCU: {keyPath} → dead: {deadPathsList}");
                        results.Add(new DeepCleanItem(
                            displayName: $"{vendorName} · {appName}",
                            path: keyPath,
                            category: DeepCleanCategory.OrphanedHkcuVendor,
                            sizeBytes: 0,
                            requiresElevation: false,
                            isSafe: false,
                            description: $"HKCU registry-key voor '{appName}' (van {vendorName}) heeft {pathValues.Count} pad-value(s) die allemaal dood zijn. Vendor-residue uit een uninstall die HKCU niet ruimde — kan weg."));
                    }
                }

                log($"HKCU vendor scan checked {vendorsChecked} vendors / {appsChecked} apps in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                log($"HKCU vendor scan error: {ex.GetType().Name}: {ex.Message}");
            }

            log($"Orphaned HKCU vendor scan complete — {results.Count} orphan key(s)");
            return results.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>
    /// True als de string lijkt op een absoluut drive-rooted pad — bv.
    /// "C:\Program Files\...", "D:\Tools\app.exe". Bewust ruim: geen file-
    /// bestaanscheck hier, alleen de heuristic voor value-data classificatie.
    /// </summary>
    private static bool LooksLikeDriveRootedPath(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return false;
        var t = raw.Trim().Trim('"');
        if (t.Length < 3) return false;
        if (char.IsLetter(t[0]) && t[1] == ':' && (t[2] == '\\' || t[2] == '/')) return true;
        // %SystemRoot%\... / %ProgramFiles%\... / %LocalAppData%\... als path-like
        if (t.StartsWith("%") && t.IndexOf('%', 1) > 1 && t.Contains('\\')) return true;
        return false;
    }

    /// <summary>
    /// Heuristiek: ziet een string eruit als een GUID? Firewall rules in
    /// registry hebben soms `{8A547BE2-...}` als DisplayName voor system-rules.
    /// Geen guarantee, gewoon "begint met `{` en bevat een hex-pattern".
    /// </summary>
    private static bool LooksLikeGuid(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!s.StartsWith("{") || !s.EndsWith("}")) return false;
        // Niet exact-parsen, gewoon: heeft het >=20 chars en bevat het dashes?
        return s.Length >= 30 && s.Count(c => c == '-') >= 4;
    }

    /// <summary>
    /// Generieke command-runner met stdout-capture (synchroon). Voor schtasks.exe
    /// die we niet via base64-encoded PS willen wrappen — schtasks output is
    /// XML en daar willen we direct toegang tot.
    /// </summary>
    private static async Task<string> RunCommandAsync(string fileName, string args, Action<string> log)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (!string.IsNullOrEmpty(stderr))
            log($"{fileName} stderr: {stderr.Substring(0, Math.Min(stderr.Length, 200))}");
        return stdout;
    }

    /// <summary>
    /// Encoded PS script runner (UTF-16 LE base64), zelfde patroon als
    /// InstalledAppsService gebruikt. Voor multi-line scripts met quote-escaping
    /// die anders een hoofdpijn zou zijn.
    /// </summary>
    private static async Task<string> RunPowerShellAsync(string script)
    {
        var bytes = Encoding.Unicode.GetBytes(script);
        var encoded = Convert.ToBase64String(bytes);
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return stdout;
    }

    /// <summary>
    /// Resolveert een .lnk file naar z'n target via WSH Shell COM-interface.
    /// Returnt empty string bij failure of als target niet leesbaar is.
    /// </summary>
    private static string ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return string.Empty;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return string.Empty;
            try
            {
                dynamic shortcut = shell.CreateShortcut(lnkPath);
                string target = shortcut.TargetPath ?? string.Empty;
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                return target;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
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
    public async Task<DeepCleanDeleteResult> DeleteAsync(IReadOnlyList<DeepCleanItem> items, string? restorePointDescription = null)
    {
        if (items.Count == 0)
            return new DeepCleanDeleteResult(0, 0, 0, new Dictionary<string, (bool, string)>(), Cancelled: false);

        var results = new Dictionary<string, (bool, string)>();
        // ScheduledTask + FirewallRule + Service altijd via elevated batch —
        // schtasks /Delete, Remove-NetFirewallRule en sc.exe delete hebben anders
        // timing-gevoelige permission checks. Eén UAC voor het hele zooitje is
        // voorspelbaarder.
        var elevated = items.Where(i =>
            i.RequiresElevation
            || i.Category == DeepCleanCategory.RecycleBin
            || i.Category == DeepCleanCategory.OrphanedScheduledTask
            || i.Category == DeepCleanCategory.OrphanedFirewallRule
            || i.Category == DeepCleanCategory.OrphanedService).ToList();
        var local = items.Where(i =>
            !i.RequiresElevation
            && i.Category != DeepCleanCategory.RecycleBin
            && i.Category != DeepCleanCategory.OrphanedScheduledTask
            && i.Category != DeepCleanCategory.OrphanedFirewallRule
            && i.Category != DeepCleanCategory.OrphanedService).ToList();
        long bytesFreed = 0;

        // 1) Local deletes — geen UAC. Per categorie verschillende strategie:
        //   - OrphanedRegistry / AppPath / ClassHandler → DeleteSubKeyTree op HKCU
        //   - OrphanedMuiCache → specifieke value (niet hele key) deleten
        //   - OrphanedShortcut → File.Delete op de .lnk
        //   - OrphanedFolder   → Directory.Delete(recursive)
        //   - Cache (rest)     → ClearFolderContents (folder zelf laten staan)
        foreach (var item in local)
        {
            try
            {
                if (item.Category == DeepCleanCategory.OrphanedRegistry ||
                    item.Category == DeepCleanCategory.OrphanedAppPath ||
                    item.Category == DeepCleanCategory.OrphanedClassHandler ||
                    item.Category == DeepCleanCategory.OrphanedHkcuVendor)
                {
                    DeleteRegistryKey(item.Path);
                    results[item.Path] = (true, "Removed registry key");
                }
                else if (item.Category == DeepCleanCategory.OrphanedMuiCache)
                {
                    // Hardcoded MUIcache key — Path bevat ook value-name voor
                    // uniqueness in dialog/dict, maar de echte key is constant.
                    const string muiKey = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
                    DeleteRegistryValue(muiKey, item.RegistryValueName ?? string.Empty);
                    results[item.Path] = (true, "Removed MUIcache value");
                }
                else if (item.Category == DeepCleanCategory.OrphanedShortcut)
                {
                    File.Delete(item.Path);
                    results[item.Path] = (true, "Deleted shortcut");
                    bytesFreed += item.SizeBytes;
                }
                else if (item.Category == DeepCleanCategory.OrphanedFolder)
                {
                    Directory.Delete(item.Path, recursive: true);
                    results[item.Path] = (true, "Deleted folder");
                    bytesFreed += item.SizeBytes;
                }
                else
                {
                    ClearFolderContents(item.Path);
                    results[item.Path] = (true, "Cleared");
                    bytesFreed += item.SizeBytes;
                }
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
            var elevatedResult = await RunElevatedBatchAsync(elevated, restorePointDescription);
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
    /// <summary>
    /// Verwijdert een registry-uninstall-key. Path format = "HKCU\..." of
    /// "HKLM\..." zoals door ScanOrphanedRegistryAsync gegenereerd. Voor HKLM
    /// hoort dit via de elevated PS-batch te lopen — DeleteRegistryKey gaat
    /// hier alleen voor HKCU (user-context), maar checkt voor de zekerheid.
    /// </summary>
    private static void DeleteRegistryKey(string fullPath)
    {
        var firstSep = fullPath.IndexOf('\\');
        if (firstSep <= 0) throw new ArgumentException($"Invalid registry path: {fullPath}");
        var hiveName = fullPath.Substring(0, firstSep).ToUpperInvariant();
        var subPath = fullPath.Substring(firstSep + 1);

        var (hive, view) = hiveName switch
        {
            "HKCU" => (RegistryHive.CurrentUser, RegistryView.Default),
            "HKLM" => (RegistryHive.LocalMachine, RegistryView.Registry64),
            _ => throw new ArgumentException($"Unsupported hive: {hiveName}")
        };
        // WOW6432Node-paden gaan via 32-bit view zodat we de juiste registry-
        // ruimte raken (Registry64 zou 'm niet vinden door reflection-mapping).
        if (subPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            view = RegistryView.Registry32;

        var lastSep = subPath.LastIndexOf('\\');
        if (lastSep <= 0) throw new ArgumentException($"Invalid sub path: {subPath}");
        var parentPath = subPath.Substring(0, lastSep);
        var keyName = subPath.Substring(lastSep + 1);

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var parent = baseKey.OpenSubKey(parentPath, writable: true)
                           ?? throw new InvalidOperationException($"Parent key not found: {parentPath}");
        parent.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Verwijdert een specifieke value uit een registry key (zonder de key
    /// zelf te raken). Voor MUIcache: we willen alleen de leftover-value
    /// weghalen, andere apps' values in dezelfde MuiCache key blijven staan.
    /// </summary>
    private static void DeleteRegistryValue(string fullKeyPath, string valueName)
    {
        if (string.IsNullOrEmpty(valueName))
            throw new ArgumentException("Value name required for MUIcache delete");

        var firstSep = fullKeyPath.IndexOf('\\');
        if (firstSep <= 0) throw new ArgumentException($"Invalid registry path: {fullKeyPath}");
        var hiveName = fullKeyPath.Substring(0, firstSep).ToUpperInvariant();
        var subPath = fullKeyPath.Substring(firstSep + 1);

        var (hive, view) = hiveName switch
        {
            "HKCU" => (RegistryHive.CurrentUser, RegistryView.Default),
            "HKLM" => (RegistryHive.LocalMachine, RegistryView.Registry64),
            _ => throw new ArgumentException($"Unsupported hive: {hiveName}")
        };

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subPath, writable: true)
                        ?? throw new InvalidOperationException($"Key not found: {subPath}");
        key.DeleteValue(valueName, throwOnMissingValue: false);
    }

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

    private static async Task<ElevatedDeleteResult> RunElevatedBatchAsync(IReadOnlyList<DeepCleanItem> items, string? restorePointDescription = null)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_deepclean_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{Escape(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START|$(Get-Date -Format o)\"");

        // Optionele Windows System Restore Point vóór de delete-batch. Wordt
        // alleen meegestuurd als de Settings-toggle aan staat en CanCreate.
        // Bij 24h rate-limit binnen 24u skipt Windows silent — exception
        // gevangen zodat de delete-batch hoe dan ook doorgaat.
        if (!string.IsNullOrEmpty(restorePointDescription))
        {
            sb.AppendLine("Log \"CHECKPOINT|START\"");
            sb.AppendLine("try {");
            sb.AppendLine($"    Checkpoint-Computer -Description '{Escape(restorePointDescription)}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop");
            sb.AppendLine("    Log \"CHECKPOINT|OK\"");
            sb.AppendLine("} catch {");
            sb.AppendLine("    Log \"CHECKPOINT|SKIP|$($_.Exception.Message)\"");
            sb.AppendLine("}");
        }

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
            else if (item.Category == DeepCleanCategory.OrphanedRegistry ||
                     item.Category == DeepCleanCategory.OrphanedAppPath ||
                     item.Category == DeepCleanCategory.OrphanedClassHandler)
            {
                // reg.exe accepteert HKLM\... / HKCU\... paden direct. /f =
                // force, geen confirm. Output naar null zodat de logfile niet
                // door reg.exe stdout vergiftigd wordt.
                sb.AppendLine($"    & reg.exe delete '{path}' /f | Out-Null");
                sb.AppendLine($"    if ($LASTEXITCODE -ne 0) {{ throw \"reg.exe exit $LASTEXITCODE\" }}");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Removed registry key\"");
            }
            else if (item.Category == DeepCleanCategory.OrphanedShortcut)
            {
                // Shortcut file delete via PowerShell (elevated voor common Start Menu).
                sb.AppendLine($"    Remove-Item -LiteralPath '{path}' -Force -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Deleted shortcut\"");
            }
            else if (item.Category == DeepCleanCategory.OrphanedScheduledTask)
            {
                // schtasks /Delete /TN "<path>" /F. Path bevat de full task URI
                // (bv. "\Vendor\Update Task"). /F = no confirm prompt.
                sb.AppendLine($"    & schtasks.exe /Delete /TN '{path}' /F | Out-Null");
                sb.AppendLine($"    if ($LASTEXITCODE -ne 0) {{ throw \"schtasks exit $LASTEXITCODE\" }}");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Deleted scheduled task\"");
            }
            else if (item.Category == DeepCleanCategory.OrphanedFirewallRule)
            {
                // Remove-NetFirewallRule -Name <ruleName>. Het ruleName zit in
                // RegistryValueName-veld (hergebruikt voor unique identifier).
                var ruleName = Escape(item.RegistryValueName ?? string.Empty);
                sb.AppendLine($"    Remove-NetFirewallRule -Name '{ruleName}' -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Removed firewall rule\"");
            }
            else if (item.Category == DeepCleanCategory.OrphanedService)
            {
                // sc.exe delete <ServiceName>. Service-name (NIET DisplayName)
                // zit in RegistryValueName. Service moet vooraf Stopped zijn,
                // wat onze scan-filter al garandeert. /quiet niet nodig — sc.exe
                // is silent by default als de service exists.
                var serviceName = Escape(item.RegistryValueName ?? string.Empty);
                sb.AppendLine($"    & sc.exe delete '{serviceName}' | Out-Null");
                sb.AppendLine($"    if ($LASTEXITCODE -ne 0) {{ throw \"sc.exe exit $LASTEXITCODE\" }}");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Removed service\"");
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
