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

// Scant het systeem na een succesvolle uninstall naar overgebleven sporen van
// de zojuist-verwijderde app(s). Doel: gebruiker een preview tonen van mogelijke
// leftovers (registry-keys, Program Files-mappen, AppData-mappen) zodat hij/zij
// expliciet kan kiezen wat opgeruimd wordt. Nooit auto-delete — alle deletes
// gaan door de elevated-batch en pas na expliciete UI-bevestiging.
//
// Match-strategie per app:
//   - DisplayName (case-insensitive, met simpele normalisatie)
//   - Publisher (uit `Publisher` veld)
//   - WingetId / PackageName als fallback
// We mikken op een gezonde mix tussen "vinden wat er is" en "geen false positives
// die belangrijke andere apps voorstellen". Daarom confidence-tiers: hoge match =
// default checked, medium/lage match = default unchecked.
public sealed class LeftoverScannerService
{
    /// <summary>
    /// Scan de drie locatie-types parallel. Resultaat is een platte lijst items
    /// gegroepeerd per type → de UI dialog regelt de visuele groepering.
    /// </summary>
    public async Task<List<LeftoverItem>> ScanAsync(IReadOnlyList<UninstalledAppRef> apps)
    {
        if (apps.Count == 0) return new List<LeftoverItem>();

        // Diagnostic log per scan — overschreven per run zodat we per item
        // kunnen terugzien waarom het wel/niet matchte.
        var logPath = Path.Combine(Path.GetTempPath(), "WingetAppDeployer_leftovers.log");
        Action<string> log = msg =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        };
        try
        {
            File.WriteAllText(logPath,
                $"=== LeftoverScanner run {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}" +
                $"Apps: {string.Join(", ", apps.Select(a => a.DisplayName))}{Environment.NewLine}");
        }
        catch { }

        var sw = Stopwatch.StartNew();

        // 7 scans parallel. Registry-walks zijn snel (sync), folder-scans
        // bevatten size-berekening die per app traag kan zijn → Task.Run.
        // App Paths / MUIcache / class handlers / shortcuts toegevoegd in
        // v0.8.9.5 zodat post-uninstall cleanup op niveau van full deep clean
        // ligt voor app-specifieke leftovers.
        var registryTask = Task.Run(() => ScanRegistry(apps, log));
        var programFilesTask = Task.Run(() => ScanProgramFilesFolders(apps, log));
        var appDataTask = Task.Run(() => ScanAppDataFolders(apps, log));
        var appPathsTask = Task.Run(() => ScanAppPaths(apps, log));
        var muiCacheTask = Task.Run(() => ScanMuiCache(apps, log));
        var classHandlersTask = Task.Run(() => ScanClassHandlers(apps, log));
        var shortcutsTask = Task.Run(() => ScanShortcuts(apps, log));

        await Task.WhenAll(registryTask, programFilesTask, appDataTask,
                           appPathsTask, muiCacheTask, classHandlersTask, shortcutsTask);

        var results = new List<LeftoverItem>();
        results.AddRange(await registryTask);
        results.AddRange(await programFilesTask);
        results.AddRange(await appDataTask);
        results.AddRange(await appPathsTask);
        results.AddRange(await muiCacheTask);
        results.AddRange(await classHandlersTask);
        results.AddRange(await shortcutsTask);

        log($"Scan complete in {sw.ElapsedMilliseconds}ms — {results.Count} leftovers (" +
            $"reg:{results.Count(r => r.Type == LeftoverType.RegistryKey)} " +
            $"pf:{results.Count(r => r.Type == LeftoverType.ProgramFilesFolder)} " +
            $"ad:{results.Count(r => r.Type == LeftoverType.AppDataFolder)} " +
            $"ap:{results.Count(r => r.Type == LeftoverType.AppPath)} " +
            $"mui:{results.Count(r => r.Type == LeftoverType.MuiCache)} " +
            $"cls:{results.Count(r => r.Type == LeftoverType.ClassHandler)} " +
            $"sc:{results.Count(r => r.Type == LeftoverType.Shortcut)})");

        // Sorteer op (Type, Confidence, Path) zodat de UI ze in een logische
        // volgorde toont — registry eerst (snel weg te klikken), folders laatst.
        return results
            .OrderBy(r => (int)r.Type)
            .ThenBy(r => (int)r.Confidence)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Registry scan ─────────────────────────────────────────────

    private static List<LeftoverItem> ScanRegistry(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
        var sources = new (RegistryHive Hive, string Path, RegistryView View, bool RequiresElevation)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry64, true),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Registry32, true),
            (RegistryHive.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryView.Default, false),
        };

        foreach (var (hive, path, view, requiresElev) in sources)
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

                    var display = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display)) continue;

                    var publisher = sub.GetValue("Publisher") as string ?? string.Empty;

                    foreach (var app in apps)
                    {
                        var confidence = MatchAgainstApp(display, publisher, app);
                        if (confidence == null) continue;

                        var fullKey = $"{HiveName(hive)}\\{path}\\{subName}";
                        results.Add(new LeftoverItem(
                            path: fullKey,
                            type: LeftoverType.RegistryKey,
                            confidence: confidence.Value,
                            sourceAppName: app.DisplayName,
                            sizeBytes: 0,
                            requiresElevation: requiresElev));
                        log($"REG match {confidence}: '{display}' ↔ {app.DisplayName} → {fullKey}");
                        break;  // één app-match is genoeg, niet dubbel toevoegen
                    }
                }
            }
            catch (Exception ex)
            {
                log($"REG scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return results;
    }

    private static string HiveName(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        _ => hive.ToString()
    };

    // ── Program Files scan ────────────────────────────────────────

    private static List<LeftoverItem> ScanProgramFilesFolders(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Distinct().Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)).ToList();

        foreach (var root in roots)
        {
            DirectoryInfo[] subdirs;
            try { subdirs = new DirectoryInfo(root).GetDirectories(); }
            catch (Exception ex)
            {
                log($"PF scan error reading {root}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var dir in subdirs)
            {
                foreach (var app in apps)
                {
                    var confidence = MatchFolderName(dir.Name, app);
                    if (confidence == null) continue;

                    var size = TrySizeOf(dir);
                    results.Add(new LeftoverItem(
                        path: dir.FullName,
                        type: LeftoverType.ProgramFilesFolder,
                        confidence: confidence.Value,
                        sourceAppName: app.DisplayName,
                        sizeBytes: size,
                        requiresElevation: true));
                    log($"PF match {confidence}: '{dir.FullName}' ↔ {app.DisplayName} ({size}B)");
                    break;
                }
            }
        }
        return results;
    }

    // ── AppData scan ──────────────────────────────────────────────

    private static List<LeftoverItem> ScanAppDataFolders(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
        var roots = new[]
        {
            (Path: Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Elevated: false),
            (Path: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),     Elevated: false),
            (Path: Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Elevated: true)
        }.Where(r => !string.IsNullOrEmpty(r.Path) && Directory.Exists(r.Path)).ToList();

        foreach (var (rootPath, elevated) in roots)
        {
            DirectoryInfo[] subdirs;
            try { subdirs = new DirectoryInfo(rootPath).GetDirectories(); }
            catch (Exception ex)
            {
                log($"AD scan error reading {rootPath}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach (var dir in subdirs)
            {
                // Microsoft- en Windows-folders skippen — die delen we niet uit
                // ook al matched een Microsoft-app op naam (bv. een Microsoft.X
                // bloatware uninstall mag niet "Microsoft" folder voorstellen).
                if (IsProtectedAppDataFolder(dir.Name)) continue;

                foreach (var app in apps)
                {
                    var confidence = MatchFolderName(dir.Name, app);
                    if (confidence == null) continue;

                    var size = TrySizeOf(dir);
                    results.Add(new LeftoverItem(
                        path: dir.FullName,
                        type: LeftoverType.AppDataFolder,
                        confidence: confidence.Value,
                        sourceAppName: app.DisplayName,
                        sizeBytes: size,
                        requiresElevation: elevated));
                    log($"AD match {confidence}: '{dir.FullName}' ↔ {app.DisplayName} ({size}B)");
                    break;
                }
            }
        }
        return results;
    }

    private static bool IsProtectedAppDataFolder(string folderName)
    {
        // Folders die we nooit als leftover voorstellen — system / shell / browser
        // basisfolders die meerdere apps gebruiken of cruciaal zijn voor Windows.
        var protectedNames = new[]
        {
            "Microsoft", "Windows", "Packages", "Temp", "TileDataLayer", "ConnectedDevicesPlatform",
            "WindowsApps", "WinRT", "VirtualStore", "PackageStaging", "Publishers",
            "Programs", "ElevatedDiagnostics", "D3DSCache", "Comms", "CrashDumps",
            "WebCache", "INetCache", "INetCookies", "History", "ActiveSync"
        };
        return protectedNames.Any(p => string.Equals(p, folderName, StringComparison.OrdinalIgnoreCase));
    }

    // ── Match logic ───────────────────────────────────────────────

    /// <summary>
    /// Match een registry-entry's DisplayName (en publisher) tegen de zojuist
    /// verwijderde app. Returnt null als er geen plausible match is.
    /// </summary>
    private static LeftoverConfidence? MatchAgainstApp(string display, string publisher, UninstalledAppRef app)
    {
        var normDisplay = Normalize(display);
        var normApp = Normalize(app.DisplayName);

        // Exact match (na normalisatie) → high.
        if (normDisplay == normApp) return LeftoverConfidence.High;

        // App-naam zit volledig in display name (of andersom) → medium.
        // Korte namen ("HP", "MS") skippen — te veel false positives.
        if (normApp.Length >= 4)
        {
            if (normDisplay.Contains(normApp) || normApp.Contains(normDisplay))
                return LeftoverConfidence.Medium;
        }

        // Publisher-match alleen → low (vendor folder, geen app-specifieke match).
        if (!string.IsNullOrEmpty(app.Publisher) && !string.IsNullOrEmpty(publisher))
        {
            if (Normalize(publisher).Contains(Normalize(app.Publisher)))
                return LeftoverConfidence.Low;
        }

        return null;
    }

    /// <summary>
    /// Match een folder-naam (in Program Files of AppData) tegen de app.
    /// Folders zijn vaak korter dan registry DisplayNames — en folder-namen zijn
    /// soms gewoon de publisher ("Microsoft", "Adobe"). Daarom dezelfde tier-logica
    /// als registry, maar de protected-list voorkomt dat we vendor-roots voorstellen.
    /// </summary>
    private static LeftoverConfidence? MatchFolderName(string folderName, UninstalledAppRef app)
    {
        var normFolder = Normalize(folderName);
        var normApp = Normalize(app.DisplayName);

        if (normFolder == normApp) return LeftoverConfidence.High;

        // PackageName is bv. "Microsoft.MicrosoftSolitaireCollection" — folder-match
        // op de naam-na-de-laatste-dot is ook high confidence.
        if (!string.IsNullOrEmpty(app.PackageName))
        {
            var lastSegment = app.PackageName.Split('.').LastOrDefault() ?? string.Empty;
            if (lastSegment.Length >= 4 && Normalize(lastSegment) == normFolder)
                return LeftoverConfidence.High;
        }

        if (normApp.Length >= 4 && (normFolder.Contains(normApp) || normApp.Contains(normFolder)))
            return LeftoverConfidence.Medium;

        // Publisher-folder match (bv. een leeggebleven "JetBrains" map na het
        // verwijderen van PyCharm). Lage confidence want een andere JetBrains-app
        // zou nog kunnen wonen daar.
        if (!string.IsNullOrEmpty(app.Publisher))
        {
            var pubNorm = Normalize(app.Publisher);
            if (pubNorm.Length >= 4 && (normFolder == pubNorm || normFolder.Contains(pubNorm)))
                return LeftoverConfidence.Low;
        }

        return null;
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ── App Paths scan (v0.8.9.5) ─────────────────────────────────

    /// <summary>
    /// Walkt HKLM/HKCU `\Software\Microsoft\Windows\CurrentVersion\App Paths\<exe>`
    /// keys en flag entries waar de exe-naam matcht met een uninstalled app.
    /// App Paths is Windows' canonical "where is this exe" register; na
    /// uninstall blijven entries soms achter.
    /// </summary>
    private static List<LeftoverItem> ScanAppPaths(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
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

                    var defaultExe = (sub.GetValue(null) as string)?.Trim().Trim('"');
                    var exePath = defaultExe ?? string.Empty;

                    foreach (var app in apps)
                    {
                        // Match: exe-name (bv. "vmware.exe") tegen app-naam tokens, OF
                        // het exe-pad bevat de app-naam.
                        var exeName = subName;  // subkey name = exe filename
                        var conf = MatchExeNameAgainstApp(exeName, exePath, app);
                        if (conf == null) continue;

                        var fullKey = $"{displayPrefix}\\{subName}";
                        results.Add(new LeftoverItem(
                            path: fullKey,
                            type: LeftoverType.AppPath,
                            confidence: conf.Value,
                            sourceAppName: app.DisplayName,
                            sizeBytes: 0,
                            requiresElevation: requiresElev));
                        log($"AP match {conf}: '{exeName}' ↔ {app.DisplayName} → {fullKey}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                log($"AppPaths scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return results;
    }

    // ── MUIcache scan (v0.8.9.5) ──────────────────────────────────

    /// <summary>
    /// Walkt HKCU MUIcache values en flag values waarvan de exe-pad in de
    /// value-name matcht met een uninstalled app. MUIcache slaat per exe-pad
    /// een friendly-name op; na uninstall blijven die entries achter.
    /// </summary>
    private static List<LeftoverItem> ScanMuiCache(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
        const string muiPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        const string displayPrefix = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(muiPath);
            if (key == null) return results;

            foreach (var valueName in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(valueName)) continue;
                // Strip MUIcache suffix (.FriendlyAppName / .ApplicationCompany)
                var exePath = StripMuiCacheSuffix(valueName);
                if (string.IsNullOrEmpty(exePath)) continue;

                foreach (var app in apps)
                {
                    var conf = MatchPathContainsApp(exePath, app);
                    if (conf == null) continue;

                    results.Add(new LeftoverItem(
                        path: $"{displayPrefix} → {valueName}",
                        type: LeftoverType.MuiCache,
                        confidence: conf.Value,
                        sourceAppName: app.DisplayName,
                        sizeBytes: 0,
                        requiresElevation: false,
                        registryValueName: valueName));
                    log($"MUI match {conf}: '{exePath}' ↔ {app.DisplayName}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log($"MUIcache scan error: {ex.GetType().Name}: {ex.Message}");
        }
        return results;
    }

    private static string StripMuiCacheSuffix(string raw)
    {
        var suffixes = new[] { ".FriendlyAppName", ".ApplicationCompany" };
        foreach (var suffix in suffixes)
        {
            if (raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return raw.Substring(0, raw.Length - suffix.Length);
        }
        if (raw.Length >= 3 && raw[1] == ':' && raw[2] == '\\') return raw;
        return string.Empty;
    }

    // ── Class handlers scan (v0.8.9.5) ────────────────────────────

    /// <summary>
    /// Walkt `HKLM/HKCU\Software\Classes\Applications\<exe>` keys en flag
    /// entries die met de uninstalled app matchen.
    /// </summary>
    private static List<LeftoverItem> ScanClassHandlers(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
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
                    foreach (var app in apps)
                    {
                        var conf = MatchExeNameAgainstApp(exeName, string.Empty, app);
                        if (conf == null) continue;

                        var keyPath = $"{displayPrefix}\\{exeName}";
                        results.Add(new LeftoverItem(
                            path: keyPath,
                            type: LeftoverType.ClassHandler,
                            confidence: conf.Value,
                            sourceAppName: app.DisplayName,
                            sizeBytes: 0,
                            requiresElevation: requiresElev));
                        log($"CLS match {conf}: '{exeName}' ↔ {app.DisplayName}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                log($"ClassHandlers scan error in {hive}\\{path}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return results;
    }

    // ── Shortcuts scan (v0.8.9.5) ─────────────────────────────────

    /// <summary>
    /// Walkt Start Menu en Desktop folders voor `.lnk` files waarvan het
    /// target naar de uninstalled app's exe verwijst.
    /// </summary>
    private static List<LeftoverItem> ScanShortcuts(IReadOnlyList<UninstalledAppRef> apps, Action<string> log)
    {
        var results = new List<LeftoverItem>();
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
                string target;
                try { target = ResolveShortcutTarget(lnk); }
                catch { continue; }
                if (string.IsNullOrEmpty(target)) continue;

                foreach (var app in apps)
                {
                    var conf = MatchPathContainsApp(target, app);
                    if (conf == null) continue;

                    results.Add(new LeftoverItem(
                        path: lnk,
                        type: LeftoverType.Shortcut,
                        confidence: conf.Value,
                        sourceAppName: app.DisplayName,
                        sizeBytes: SafeFileSize(lnk),
                        requiresElevation: requiresElev));
                    log($"SC match {conf}: '{lnk}' → '{target}' ↔ {app.DisplayName}");
                    break;
                }
            }
        }
        return results;
    }

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
        catch { return string.Empty; }
    }

    private static long SafeFileSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    // ── Match helpers (extra voor v0.8.9.5 types) ────────────────

    /// <summary>
    /// Match een exe-name (`foo.exe`) of optioneel exe-pad tegen een app.
    /// Voor App Paths en class handlers waar subkey = exe filename.
    /// </summary>
    private static LeftoverConfidence? MatchExeNameAgainstApp(string exeName, string exePath, UninstalledAppRef app)
    {
        var normExe = Normalize(System.IO.Path.GetFileNameWithoutExtension(exeName));
        var normApp = Normalize(app.DisplayName);
        if (normExe.Length < 3) return null;

        if (normExe == normApp) return LeftoverConfidence.High;
        if (normApp.Length >= 4 && (normExe.Contains(normApp) || normApp.Contains(normExe)))
            return LeftoverConfidence.High;

        // Fallback: exe-pad bevat de app-naam (vaak voor App Paths met expliciet pad).
        if (!string.IsNullOrEmpty(exePath))
        {
            var normPath = Normalize(exePath);
            if (normApp.Length >= 4 && normPath.Contains(normApp))
                return LeftoverConfidence.Medium;
        }

        if (!string.IsNullOrEmpty(app.Publisher))
        {
            var normPub = Normalize(app.Publisher);
            if (normPub.Length >= 4 && normExe.Contains(normPub))
                return LeftoverConfidence.Low;
        }
        return null;
    }

    /// <summary>
    /// Match een path-string (bv. exe-pad uit MUIcache value-name of shortcut
    /// target) tegen een app door te checken of de app-naam ergens in het pad
    /// voorkomt.
    /// </summary>
    private static LeftoverConfidence? MatchPathContainsApp(string fullPath, UninstalledAppRef app)
    {
        var normPath = Normalize(fullPath);
        var normApp = Normalize(app.DisplayName);
        if (normApp.Length < 4) return null;

        if (normPath.Contains(normApp)) return LeftoverConfidence.High;

        // PackageName voor Store-apps (bv. "Microsoft.X" → "X")
        if (!string.IsNullOrEmpty(app.PackageName))
        {
            var lastSegment = app.PackageName.Split('.').LastOrDefault() ?? string.Empty;
            if (lastSegment.Length >= 4 && normPath.Contains(Normalize(lastSegment)))
                return LeftoverConfidence.High;
        }

        if (!string.IsNullOrEmpty(app.Publisher))
        {
            var normPub = Normalize(app.Publisher);
            if (normPub.Length >= 4 && normPath.Contains(normPub))
                return LeftoverConfidence.Low;
        }
        return null;
    }

    private static long TrySizeOf(DirectoryInfo dir)
    {
        try
        {
            long total = 0;
            foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { total += fi.Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    // ── Delete batch ──────────────────────────────────────────────

    /// <summary>
    /// Verwijdert de geselecteerde items. Splitst in elevated (HKLM, Program Files,
    /// CommonApplicationData) en non-elevated (HKCU, Local/RoamingAppData). Elevated
    /// items gaan in één UAC-prompt batch via PowerShell. Non-elevated in-process
    /// met `Directory.Delete` / `Registry.DeleteSubKeyTree`.
    /// </summary>
    public async Task<LeftoverDeleteResult> DeleteAsync(IReadOnlyList<LeftoverItem> items)
    {
        if (items.Count == 0)
            return new LeftoverDeleteResult(0, 0, new Dictionary<string, (bool, string)>(), Cancelled: false);

        var results = new Dictionary<string, (bool, string)>();
        var elevated = items.Where(i => i.RequiresElevation).ToList();
        var local = items.Where(i => !i.RequiresElevation).ToList();

        // 1) Local deletes — geen UAC nodig.
        foreach (var item in local)
        {
            var (ok, msg) = TryDeleteLocal(item);
            results[item.Path] = (ok, msg);
        }

        // 2) Elevated batch — één UAC prompt.
        var cancelled = false;
        if (elevated.Count > 0)
        {
            var elevatedResult = await RunElevatedDeleteAsync(elevated);
            cancelled = elevatedResult.Cancelled;
            foreach (var kv in elevatedResult.ResultsByPath)
                results[kv.Key] = kv.Value;
        }

        var successCount = results.Count(kv => kv.Value.Item1);
        var failedCount = results.Count - successCount;
        return new LeftoverDeleteResult(successCount, failedCount, results, cancelled);
    }

    private static (bool ok, string msg) TryDeleteLocal(LeftoverItem item)
    {
        try
        {
            switch (item.Type)
            {
                case LeftoverType.RegistryKey:
                case LeftoverType.AppPath:
                case LeftoverType.ClassHandler:
                    DeleteRegistryKey(item.Path);
                    return (true, "Removed registry key");
                case LeftoverType.MuiCache:
                    // MUIcache: hardcoded key, value-name in RegistryValueName.
                    const string muiKey = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
                    DeleteRegistryValue(muiKey, item.RegistryValueName ?? string.Empty);
                    return (true, "Removed MUIcache value");
                case LeftoverType.Shortcut:
                    File.Delete(item.Path);
                    return (true, "Deleted shortcut");
                case LeftoverType.AppDataFolder:
                case LeftoverType.ProgramFilesFolder:
                    Directory.Delete(item.Path, recursive: true);
                    return (true, "Deleted folder");
                default:
                    return (false, "Unknown type");
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

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

    private static void DeleteRegistryKey(string fullPath)
    {
        // Format: "HKLM\\SOFTWARE\\...\\Uninstall\\<sub>" of HKCU equivalent.
        var firstSep = fullPath.IndexOf('\\');
        if (firstSep <= 0) throw new ArgumentException($"Invalid registry path: {fullPath}");
        var hiveName = fullPath.Substring(0, firstSep);
        var subPath = fullPath.Substring(firstSep + 1);

        var (hive, view) = hiveName.ToUpperInvariant() switch
        {
            "HKLM" => (RegistryHive.LocalMachine, RegistryView.Registry64),
            "HKCU" => (RegistryHive.CurrentUser, RegistryView.Default),
            _ => throw new ArgumentException($"Unsupported hive: {hiveName}")
        };

        // Voor WOW6432Node-paden gebruiken we Registry32 view zodat we de juiste
        // 32-bit ruimte raken (DeleteSubKeyTree op Registry64 zou 'm niet vinden).
        if (subPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            view = RegistryView.Registry32;

        // Splits in parent-pad + subkey-naam (DeleteSubKeyTree wil de parent open).
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
    /// Bouwt een PS-script dat alle elevated items verwijdert in één UAC-prompt.
    /// Per item: Remove-Item / reg.exe delete met try/catch zodat één failure de
    /// rest niet blokkeert. Logfile heeft RESULT-markers per item.
    /// </summary>
    private static async Task<ElevatedDeleteResult> RunElevatedDeleteAsync(IReadOnlyList<LeftoverItem> items)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_leftoverdelete_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{Escape(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START|$(Get-Date -Format o)\"");

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var path = Escape(item.Path);
            sb.AppendLine($"Log \"PROGRESS|{i + 1}|{items.Count}|{path}\"");
            sb.AppendLine("try {");
            if (item.Type == LeftoverType.RegistryKey ||
                item.Type == LeftoverType.AppPath ||
                item.Type == LeftoverType.ClassHandler)
            {
                // reg.exe accepts forward HKLM\\... paths cleanly. /f = force,
                // geen confirmatie-prompt.
                sb.AppendLine($"    & reg.exe delete '{path}' /f | Out-Null");
                sb.AppendLine("    if ($LASTEXITCODE -ne 0) { throw \"reg.exe exit $LASTEXITCODE\" }");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Removed registry key\"");
            }
            else if (item.Type == LeftoverType.Shortcut)
            {
                // .lnk file delete (CommonStartMenu paths require admin)
                sb.AppendLine($"    Remove-Item -LiteralPath '{path}' -Force -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Deleted shortcut\"");
            }
            else
            {
                // Recursive force-delete. Long-path proof via -LiteralPath.
                sb.AppendLine($"    Remove-Item -LiteralPath '{path}' -Recurse -Force -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{path}|OK|Deleted folder\"");
            }
            sb.AppendLine("} catch {");
            sb.AppendLine($"    Log \"RESULT|{path}|FAIL|$($_.Exception.Message)\"");
            sb.AppendLine("}");
        }
        sb.AppendLine("Log \"END|$(Get-Date -Format o)\"");

        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_leftoverdelete_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
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
            ParseResults(logPath, items, results);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            cancelled = true;
            foreach (var item in items)
                if (!results.ContainsKey(item.Path)) results[item.Path] = (false, "Cancelled — UAC prompt declined");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }

        // Items zonder RESULT-line → markeren als failed.
        foreach (var item in items)
            if (!results.ContainsKey(item.Path))
                results[item.Path] = (false, "Did not run (interrupted)");

        return new ElevatedDeleteResult(results, cancelled);
    }

    private static void ParseResults(string logPath, IReadOnlyList<LeftoverItem> items, Dictionary<string, (bool, string)> results)
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
            var msg = parts.Length >= 4 ? parts[3] : (ok ? "Removed" : "Failed");
            results[path] = (ok, msg);
        }
    }

    private static string Escape(string s) => s.Replace("'", "''");

    private sealed record ElevatedDeleteResult(
        IReadOnlyDictionary<string, (bool ok, string msg)> ResultsByPath,
        bool Cancelled);
}

public sealed record LeftoverDeleteResult(
    int SuccessCount,
    int FailedCount,
    IReadOnlyDictionary<string, (bool success, string message)> ResultsByPath,
    bool Cancelled);
