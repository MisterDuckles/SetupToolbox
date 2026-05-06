using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Services;

// Detecteert ALLE geïnstalleerde apps op het systeem uit drie bronnen, dedup-t
// resultaten, en tagt elk met een Source. De unified lijst voedt de Debloat-pagina.
//
// Bronnen:
//   1. Winget — álles in `winget list` (zowel apps uit onze catalogus als andere
//      die handmatig via winget zijn geïnstalleerd). Apps die ook in apps.json
//      staan krijgen een referentie naar het App-object zodat we het bundled
//      icon kunnen tonen. Voorrang boven AppX/Registry.
//   2. AppX/Store — Get-AppxPackage. Microsoft Store, AppX bundles, system bloat.
//   3. Web — registry uninstall keys (HKLM/HKCU/WOW6432Node) MINUS de items die
//      al via winget tracked zijn. Deze blijven over voor vendor-installer
//      downloads van het web (Chrome buiten winget, oude MSI's, dev tools).
//
// Dedup-strategie: case-insensitive DisplayName comparison. Bij collision wint de
// hogere-prioriteit source (Winget > Store > Web). Een app uit Registry die qua
// DisplayName matcht met een Winget-entry of Store-entry wordt geskipt — populair
// bij apps zoals Discord die zowel als AppX als als MSI/EXE geregistreerd kunnen
// staan.
public sealed class InstalledAppsService
{
    public async Task<List<InstalledAppEntry>> DetectAllAsync()
    {
        // Diagnostic log — overschreven per detectie-run zodat we bij issues kunnen
        // zien WELKE source faalde en met welke exception.
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WingetAppDeployer_debloat.log");
        Action<string> log = msg =>
        {
            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        };
        try { System.IO.File.WriteAllText(logPath, $"=== DetectAllAsync run {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"); } catch { }
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Catalog dictionary — GroupBy+First voorkomt ArgumentException op duplicate
        // WingetIds (een app kan onder meerdere categorieën staan in apps.json).
        var db = await App.Database.GetAppDatabaseAsync();
        var catalogApps = new List<Models.App>();
        if (db != null)
        {
            foreach (var cat in db.Categories)
            {
                if (cat.Apps != null) catalogApps.AddRange(cat.Apps);
                if (cat.Subcategories != null)
                    foreach (var sub in cat.Subcategories)
                        catalogApps.AddRange(sub.Apps);
            }
        }
        var catalogByWingetId = catalogApps
            .GroupBy(a => a.WingetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        log($"Catalog apps loaded: {catalogApps.Count} ({catalogByWingetId.Count} unique winget IDs)");

        // Drie detection sources parallel — winget (slow), AppX/PowerShell (slow),
        // Registry (fast). Voorheen sequential = som van alle 3 (~10-15s); parallel =
        // duur van de langste (~5-7s).
        var wingetTask = SafeDetectWingetAsync(catalogByWingetId, log);
        var appxTask = SafeDetectAppxAsync(log);
        var registryTask = Task.Run(() => SafeDetectRegistry(log));
        await Task.WhenAll(wingetTask, appxTask, registryTask);

        var wingetEntries = await wingetTask;
        var appxEntries = await appxTask;
        var registryEntries = await registryTask;

        log($"Parallel detection completed in {sw.ElapsedMilliseconds}ms — winget:{wingetEntries.Count} appx:{appxEntries.Count} registry:{registryEntries.Count}");

        // Merge met priority dedup: Winget > Store > Web. DisplayName comparison
        // case-insensitive zodat "Discord" uit winget de "Discord" uit registry
        // overschrijft.
        var entries = new List<InstalledAppEntry>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in wingetEntries) { if (seenNames.Add(e.DisplayName)) entries.Add(e); }
        foreach (var e in appxEntries)   { if (seenNames.Add(e.DisplayName)) entries.Add(e); }
        foreach (var e in registryEntries) { if (seenNames.Add(e.DisplayName)) entries.Add(e); }

        log($"Total entries returned (after dedup): {entries.Count}");
        return entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<InstalledAppEntry>> SafeDetectWingetAsync(
        Dictionary<string, Models.App> catalogByWingetId,
        Action<string> log)
    {
        try
        {
            var richList = await App.Winget.GetInstalledAppsListAsync();
            log($"Winget rich list: {richList.Count} entries");

            // Filter op Source kolom: alleen Source="winget" rechtvaardigt de Winget
            // badge in onze UI. Source="msstore" laten we via Get-AppxPackage / Store
            // detectie binnenkomen, en blanke Source via registry / Web. Catalog-apps
            // (in apps.json) zijn een uitzondering — die forceren we als Winget zelfs
            // bij blanke Source, want we weten dat ze winget-installable zijn.
            //
            // Voor de Dutch-Windows fallback (ParseSimpleIds) is Source altijd leeg;
            // daar gebruiken we catalogByWingetId match als enige Winget-trigger
            // zodat we geen valse Winget badges geven aan registry-only entries.
            var entries = new List<InstalledAppEntry>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rich in richList)
            {
                if (!seenIds.Add(rich.Id)) continue;

                catalogByWingetId.TryGetValue(rich.Id, out var catalogApp);
                var isWingetSource = string.Equals(rich.Source, "winget", StringComparison.OrdinalIgnoreCase);

                // Skip wanneer geen Source=winget EN niet in onze catalog. Die entries
                // worden door Get-AppxPackage of registry detection opgepakt met de
                // juiste tag (Store/Web).
                if (!isWingetSource && catalogApp == null) continue;

                var displayName = catalogApp?.Name
                                  ?? (string.IsNullOrEmpty(rich.Name) || rich.Name == rich.Id ? rich.Id : rich.Name);
                var version = rich.Version ?? string.Empty;

                entries.Add(new InstalledAppEntry(
                    displayName: displayName,
                    source: InstalledSource.Winget,
                    identifier: rich.Id,
                    publisher: ExtractPublisherFromWingetId(rich.Id),
                    version: version,
                    iconImage: catalogApp?.IconImage,
                    catalogApp: catalogApp));
            }
            log($"Winget entries (Source=winget OR catalog-match): {entries.Count}");
            return entries;
        }
        catch (Exception ex)
        {
            log($"WINGET DETECTION FAILED: {ex.GetType().Name}: {ex.Message}");
            return new List<InstalledAppEntry>();
        }
    }

    private async Task<List<InstalledAppEntry>> SafeDetectAppxAsync(Action<string> log)
    {
        try
        {
            var appx = await DetectAppxAsync();
            log($"AppX entries detected: {appx.Count}");
            return appx;
        }
        catch (Exception ex)
        {
            log($"APPX DETECTION FAILED: {ex.GetType().Name}: {ex.Message}");
            return new List<InstalledAppEntry>();
        }
    }

    private List<InstalledAppEntry> SafeDetectRegistry(Action<string> log)
    {
        try
        {
            var reg = DetectRegistry();
            log($"Registry entries detected: {reg.Count}");
            return reg;
        }
        catch (Exception ex)
        {
            log($"REGISTRY DETECTION FAILED: {ex.GetType().Name}: {ex.Message}");
            return new List<InstalledAppEntry>();
        }
    }

    private static string ExtractPublisherFromWingetId(string wingetId)
    {
        // Winget IDs hebben formaat "Publisher.AppName" — splitsen op de eerste dot.
        var idx = wingetId.IndexOf('.');
        return idx > 0 ? wingetId[..idx] : string.Empty;
    }

    private static async Task<List<InstalledAppEntry>> DetectAppxAsync()
    {
        var results = new List<InstalledAppEntry>();
        try
        {
            // Filter:
            //   - IsFramework: dependency-libs (Microsoft.NET.Native, VCLibs, WindowsAppRuntime etc.)
            //     hebben geen UI entry-point en zijn niet zinnig vanuit user-perspectief.
            //   - IsResourcePackage: language packs en resource bundles, ook geen losstaande apps.
            //
            // SignatureKind=System (AAD.BrokerPlugin, AccountsControl, BioEnrollment, etc.) blijft
            // WEL in de output — `IsSystemComponent` flag wordt op de InstalledAppEntry gezet zodat
            // de UI ze default achter een "Show system components" checkbox kan verbergen. Dat is
            // veiliger dan ze hardcoded weggooien (user kan ze nog inspecteren als gewenst) en past
            // bij user-feedback dat het soms handig is om systeem-AppX te kunnen zien.
            //
            // Format: Name|PackageFullName|Publisher|Version|SignatureKind pipe-separated zodat we
            // 1 PS-call doen. Single-quote in fields zou parsing breken — onwaarschijnlijk bij
            // Microsoft package names maar we trimmen defensief.
            var script = @"Get-AppxPackage |
                Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage } |
                ForEach-Object { ""$($_.Name)|$($_.PackageFullName)|$($_.Publisher)|$($_.Version)|$($_.SignatureKind)"" }";

            var output = await RunPowerShellAsync(script);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.TrimEnd('\r').Split('|');
                if (parts.Length < 5) continue;
                var name = parts[0].Trim();
                var fullName = parts[1].Trim();
                var publisher = parts[2].Trim();
                var version = parts[3].Trim();
                var signatureKind = parts[4].Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullName)) continue;

                // Friendlier display: "Microsoft.MicrosoftSolitaireCollection" →
                // "MicrosoftSolitaireCollection". Geen perfecte cleanup maar beter
                // dan rauwe Name. We tonen Publisher apart als subtekst.
                var displayName = StripPublisherPrefix(name);

                results.Add(new InstalledAppEntry(
                    displayName: displayName,
                    source: InstalledSource.Store,
                    identifier: fullName,
                    publisher: ExtractFriendlyPublisher(publisher),
                    version: version,
                    isSystemComponent: string.Equals(signatureKind, "System", StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch
        {
            // Geen Get-AppxPackage / failure → lege lijst, andere bronnen vullen aan.
        }
        return results;
    }

    private static string StripPublisherPrefix(string name)
    {
        // Microsoft.* prefix strippen voor friendlier display. Andere "Foo." prefixes
        // laten we staan — vaak informatief (bv. "Adobe.Reader").
        if (name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            return name.Substring("Microsoft.".Length);
        return name;
    }

    private static string ExtractFriendlyPublisher(string raw)
    {
        // PowerShell publishers hebben formaat "CN=Foo Bar, O=..." — eerste CN-piece
        // is wat user wil zien.
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var cn = raw.Split(',').FirstOrDefault(p => p.TrimStart().StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
        if (cn == null) return raw;
        var idx = cn.IndexOf('=');
        return idx >= 0 ? cn[(idx + 1)..].Trim() : raw;
    }

    private static List<InstalledAppEntry> DetectRegistry()
    {
        var results = new List<InstalledAppEntry>();
        // 64-bit, 32-bit-on-64, en current-user uninstall keys. Microsoft documenteert
        // deze drie als de canonical lookup-paden voor "Programs and Features".
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

                    var display = sub.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display)) continue;

                    // Filter Windows Updates / patches (KB-nummers in DisplayName, of
                    // SystemComponent=1, of ParentKeyName != null). Die zien we niet
                    // graag in een debloat-lijst.
                    if (sub.GetValue("SystemComponent") is int sysCmp && sysCmp == 1) continue;
                    if (sub.GetValue("ParentKeyName") is string parent && !string.IsNullOrEmpty(parent)) continue;
                    if (display.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase)) continue;
                    if (display.StartsWith("Security Update for ", StringComparison.OrdinalIgnoreCase)) continue;
                    if (display.StartsWith("Hotfix for ", StringComparison.OrdinalIgnoreCase)) continue;

                    var quietUninstall = sub.GetValue("QuietUninstallString") as string;
                    var uninstall = sub.GetValue("UninstallString") as string;
                    var command = quietUninstall ?? uninstall;
                    // Geen UninstallString = niet uit te schakelen vanuit hier
                    // (typisch system-installed components). Skippen.
                    if (string.IsNullOrWhiteSpace(command)) continue;

                    var publisher = sub.GetValue("Publisher") as string ?? string.Empty;
                    var version = sub.GetValue("DisplayVersion") as string ?? string.Empty;

                    results.Add(new InstalledAppEntry(
                        displayName: display,
                        source: InstalledSource.Web,
                        identifier: $"{hive}\\{path}\\{subName}",
                        publisher: publisher,
                        version: version,
                        uninstallCommand: command));
                }
            }
            catch
            {
                // Toegang geweigerd / pad bestaat niet → skip deze hive.
            }
        }

        // Dedup binnen registry-results op DisplayName (zelfde app kan zowel in
        // HKLM 64-bit als WOW6432Node verschijnen — pak de eerste). Cross-source
        // dedup gebeurt in DetectAllAsync hierboven.
        return results
            .GroupBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<string> RunPowerShellAsync(string script)
    {
        // Encode het script in base64 zodat we ons geen zorgen hoeven te maken over
        // command-line escaping. PowerShell -EncodedCommand verwacht UTF-16LE base64.
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
}
