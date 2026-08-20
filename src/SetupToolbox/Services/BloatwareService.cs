using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SetupToolbox.Models;

namespace SetupToolbox.Services;

// Detect en uninstall van Microsoft AppX-bloatware via PowerShell. Geen winget hier
// — `winget uninstall` werkt niet (of slecht) op standaard Microsoft Store apps zoals
// Solitaire / Xbox / Skype omdat die als "system component" geïnstalleerd staan en
// alleen via Remove-AppxPackage te verwijderen zijn.
//
// Detectie draait als normale user (Get-AppxPackage). Uninstall vereist admin
// (Remove-AppxPackage van een system-installed package geeft anders Access Denied).
// We elevate alleen de uninstall-call zelf via UAC — de rest van de app blijft als
// normale user draaien zodat user niet met admin-prompt wordt overvallen bij elke
// app-start.
public sealed class BloatwareService
{
    /// <summary>
    /// Run Get-AppxPackage en construct een BloatwareItem per gedetecteerd package
    /// dat onder Microsoft- of OEM-vendor valt. Detection-driven — geen hardcoded
    /// lijst van wat-is-bloat. Curated metadata uit BloatwareItem.LookupMetadata
    /// wordt opgevraagd voor friendly display + description; onbekende packages
    /// krijgen de raw package-name als display met lege description.
    ///
    /// Filters:
    ///   - IsFramework / IsResourcePackage: geen UI-apps, skippen.
    ///   - SignatureKind = System: Windows-eigen system components (AAD.BrokerPlugin,
    ///     BioEnrollment, etc.) — niet veilig om te uninstallen, skippen.
    ///   - Microsoft.* prefix EN niet-system → Microsoft bloat (Solitaire, Xbox, etc.)
    ///   - Publisher contains HP/Dell/Lenovo/AsusTek/Acer/Micro-Star (MSI) → OEM bloat
    /// </summary>
    public async Task<List<BloatwareItem>> DetectAllAsync()
    {
        var items = new List<BloatwareItem>();
        try
        {
            // Format: Name|PackageFullName|Publisher pipe-separated. Eén PS-call.
            var script = @"Get-AppxPackage |
                Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and $_.SignatureKind -ne 'System' } |
                ForEach-Object { ""$($_.Name)|$($_.PackageFullName)|$($_.Publisher)"" }";

            var output = await RunPowerShellAsync(script);

            // Eén regel per package. Group by Name zodat multi-version installs
            // (twee Solitaire entries voor x86+x64 etc.) onder één item komen
            // met meerdere FullNames.
            var byName = new Dictionary<string, (string publisher, List<string> fullNames)>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.TrimEnd('\r').Split('|');
                if (parts.Length < 3) continue;
                var name = parts[0].Trim();
                var fullName = parts[1].Trim();
                var publisher = parts[2].Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullName)) continue;

                if (!byName.TryGetValue(name, out var entry))
                {
                    entry = (publisher, new List<string>());
                    byName[name] = entry;
                }
                entry.fullNames.Add(fullName);
            }

            foreach (var (name, entry) in byName)
            {
                var vendor = ClassifyVendor(name, entry.publisher);
                if (vendor == null) continue;  // niet Microsoft, niet OEM → niet voor deze sectie

                // Naam / omschrijving / categorie zijn vertaald (v1.2.5): we geven de
                // stabiele keys door, BloatwareItem doet de lookup. Een onbekend package
                // heeft geen vertaling en valt terug op de opgeschoonde package-naam.
                var metadata = BloatwareItem.LookupMetadata(name);
                var categoryKey = metadata?.CategoryKey ?? CategoryKeyFromVendor(vendor.Value);

                var item = new BloatwareItem(metadata?.Key, StripCommonPrefix(name),
                                             categoryKey, vendor.Value, name);
                item.InstalledPackageFullNames.AddRange(entry.fullNames);
                item.IsInstalled = true;
                items.Add(item);
            }
        }
        catch
        {
            // PS-failure → lege lijst, andere bronnen blijven werken.
        }

        // Sort: bekende (gecureerde) items eerst (alfabetisch op DisplayName),
        // daarna onbekende — zo zien power-users de zooi waarvan we weten "ja dit
        // is bloat" eerst.
        return items
            .OrderByDescending(i => i.IsCurated)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BloatwareVendor? ClassifyVendor(string packageName, string publisher)
    {
        // Microsoft: name starts with "Microsoft." OR publisher contains Microsoft.
        // Sluit ook MicrosoftCorporationII (used by Quick Assist etc.) in.
        if (packageName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("MicrosoftCorporation", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("MicrosoftTeams", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("MSTeams", StringComparison.OrdinalIgnoreCase) ||
            packageName.Equals("microsoft.windowscommunicationsapps", StringComparison.OrdinalIgnoreCase) ||
            publisher.Contains("CN=Microsoft", StringComparison.OrdinalIgnoreCase))
            return BloatwareVendor.Microsoft;

        // OEM: zowel Name-prefix als Publisher CN= patterns zodat we whitelabeled
        // OEM-builds catchen (sommige OEM-AppX gebruiken een anonieme app-id zoals
        // "AD2F1837.HPSmart" — naam-prefix vangt die niet, publisher wel).
        var p = publisher;
        if (p.Contains("CN=HP Inc", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("CN=Hewlett-Packard", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("HP.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("HPInc.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("AD2F1837.", StringComparison.Ordinal))   // Microsoft's HP container PFN
            return BloatwareVendor.Oem;

        if (p.Contains("CN=Dell Inc", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("DellInc.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("Dell.", StringComparison.OrdinalIgnoreCase))
            return BloatwareVendor.Oem;

        if (p.Contains("CN=Lenovo", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("Lenovo", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("E0469640.", StringComparison.Ordinal))   // Lenovo's MS PFN
            return BloatwareVendor.Oem;

        if (p.Contains("CN=AsusTek", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("CN=ASUSTek Computer", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("AsusTek", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("Asus.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("B9ECED6F.", StringComparison.Ordinal))   // ASUS MS PFN
            return BloatwareVendor.Oem;

        if (p.Contains("CN=Acer", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("AcerInc.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("Acer.", StringComparison.OrdinalIgnoreCase))
            return BloatwareVendor.Oem;

        if (p.Contains("CN=Micro-Star", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("CN=MSI", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("MSI.", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("9099B36F.", StringComparison.Ordinal))   // MSI MS PFN
            return BloatwareVendor.Oem;

        return null;
    }

    private static string StripCommonPrefix(string packageName)
    {
        // Friendlier display voor unknown packages: "Microsoft.SomeNewApp" → "SomeNewApp"
        var prefixes = new[] { "Microsoft.", "MicrosoftCorporationII.", "HPInc.", "HP.", "DellInc.", "LenovoCorporation.", "AsusTekComputerInc.", "AsusTek.", "AcerInc.", "MSI." };
        foreach (var prefix in prefixes)
        {
            if (packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return packageName.Substring(prefix.Length);
        }
        return packageName;
    }

    // Categorie-key voor een package dat niet in de curated lijst staat. Key, niet
    // label: de weergave komt uit de vertaaltabel (bloatware.category.*).
    private static string CategoryKeyFromVendor(BloatwareVendor vendor) =>
        vendor == BloatwareVendor.Microsoft ? "microsoft" : "oem";

    /// <summary>
    /// Uninstall een batch bloatware items via één elevated PowerShell-call. UAC
    /// prompt verschijnt één keer voor de hele batch. Geen live progress (verb=runas
    /// vereist UseShellExecute=true wat stdout-redirect onmogelijk maakt) — we
    /// schrijven log naar %TEMP% en parsen die na afloop voor success/failure.
    /// </summary>
    public async Task<BloatwareUninstallResult> UninstallBatchAsync(
        IReadOnlyList<BloatwareItem> items,
        IProgress<BloatwareProgress>? progress = null,
        string? restorePointDescription = null)
    {
        if (items.Count == 0)
            return new BloatwareUninstallResult(0, 0, new Dictionary<string, (bool, string)>(), Cancelled: false);

        var logPath = Path.Combine(Path.GetTempPath(),
            $"SetupToolbox_bloatware_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // Bouw een PS-script dat per item Remove-AppxPackage runt en per actie een
        // marker naar de log schrijft. Marker-format: "RESULT|<displayName>|OK|<msg>"
        // of "RESULT|<displayName>|FAIL|<msg>" zodat de parser na afloop kan zien
        // welke items lukten/faalden.
        //
        // We gebruiken Remove-AppxPackage -AllUsers waar mogelijk (niet alle systemen
        // staan dat toe, vandaar try/catch fallback naar zonder -AllUsers).
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{EscapePath(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START|$(Get-Date -Format o)\"");

        // Optionele System Restore Point vóór de Remove-AppxPackage batch.
        // Bij 24h rate-limit silent skip — exception gevangen zodat de
        // uninstall-batch hoe dan ook doorgaat.
        if (!string.IsNullOrEmpty(restorePointDescription))
        {
            var escapedDesc = restorePointDescription.Replace("'", "''");
            sb.AppendLine("Log \"CHECKPOINT|START\"");
            sb.AppendLine("try {");
            sb.AppendLine($"    Checkpoint-Computer -Description '{escapedDesc}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop");
            sb.AppendLine("    Log \"CHECKPOINT|OK\"");
            sb.AppendLine("} catch {");
            sb.AppendLine("    Log \"CHECKPOINT|SKIP|$($_.Exception.Message)\"");
            sb.AppendLine("}");
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // Display name op single line, geen quotes erin (curated list heeft geen
            // single-quotes maar toch escapen voor zekerheid).
            var displayName = item.DisplayName.Replace("'", "''");
            sb.AppendLine($"Log \"PROGRESS|{i + 1}|{items.Count}|{EscapeForPs(displayName)}\"");

            sb.AppendLine($"$itemSuccess = $true");
            sb.AppendLine($"$itemMessages = @()");

            foreach (var fullName in item.InstalledPackageFullNames)
            {
                // PackageFullName komt rechtstreeks uit Get-AppxPackage output, geen
                // quotes/specials te verwachten — toch via single-quoted string wrapper
                // zodat een unverwacht karakter niet de PS-parser breekt.
                sb.AppendLine("try {");
                sb.AppendLine($"    Remove-AppxPackage -Package '{fullName}' -ErrorAction Stop");
                sb.AppendLine($"    $itemMessages += 'Removed {fullName}'");
                sb.AppendLine("} catch {");
                sb.AppendLine("    $itemSuccess = $false");
                sb.AppendLine($"    $itemMessages += \"Failed {fullName}: $($_.Exception.Message)\"");
                sb.AppendLine("}");
            }

            sb.AppendLine("$result = if ($itemSuccess) { 'OK' } else { 'FAIL' }");
            sb.AppendLine($"$msg = $itemMessages -join ' / '");
            sb.AppendLine($"Log \"RESULT|{EscapeForPs(displayName)}|$result|$msg\"");
        }

        sb.AppendLine("Log \"END|$(Get-Date -Format o)\"");

        // Schrijf script naar tmp file zodat we 'm via -File kunnen aanroepen — args
        // op de command-line worden anders te lang en complex met escaping.
        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"SetupToolbox_bloatware_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
        await File.WriteAllTextAsync(scriptPath, sb.ToString(), Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,   // verplicht voor Verb=runas
            Verb = "runas",            // UAC elevation prompt
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                return new BloatwareUninstallResult(0, items.Count, new Dictionary<string, (bool, string)>(), Cancelled: false);

            // Tail de logfile asynchroon zodat we live progress kunnen reporten.
            // Polling i.p.v. FileSystemWatcher omdat PowerShell de file write/close
            // tussen elke Add-Content cyclet en watcher-events soms verloren raken.
            var lastLineCount = 0;
            while (!proc.HasExited)
            {
                await Task.Delay(200);
                lastLineCount = ReadProgressFromLog(logPath, lastLineCount, items, progress);
            }
            // Final flush — eventuele laatste regels die binnen de 200ms-window
            // werden geschreven nog parsen.
            await Task.Delay(100);
            ReadProgressFromLog(logPath, lastLineCount, items, progress);

            return ParseFinalResults(logPath, items);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User klikte "No" op UAC prompt → ProcessStart gooit Win32Exception 1223.
            // Cancelled-flag staat aan zodat de dialog dit visueel anders kan laten
            // zien dan een echte failure (geen rode error glyph, neutrale tekst).
            return new BloatwareUninstallResult(0, items.Count,
                items.ToDictionary(i => i.DisplayName, i => (false, App.Loc.S("elevated.uacDeclined"))),
                Cancelled: true);
        }
        finally
        {
            // Best-effort cleanup van tmp script. Log laten staan voor debugging als
            // er iets mis ging.
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static int ReadProgressFromLog(
        string logPath,
        int alreadyRead,
        IReadOnlyList<BloatwareItem> items,
        IProgress<BloatwareProgress>? progress)
    {
        if (!File.Exists(logPath)) return alreadyRead;

        string[] lines;
        try
        {
            // ReadLines met FileShare.ReadWrite zodat de PS-process tegelijk kan
            // schrijven zonder dat we een lock-conflict krijgen.
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd();
            lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { return alreadyRead; }

        for (int i = alreadyRead; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.StartsWith("PROGRESS|"))
            {
                var parts = line.Split('|');
                if (parts.Length >= 4 && int.TryParse(parts[1], out var idx) && int.TryParse(parts[2], out var total))
                {
                    var displayName = parts[3];
                    var match = items.FirstOrDefault(it => it.DisplayName == displayName);
                    if (match != null)
                        progress?.Report(new BloatwareProgress(idx, total, match, BloatwarePhase.Running,
                            App.Loc.S("progress.removing", displayName)));
                }
            }
            else if (line.StartsWith("RESULT|"))
            {
                var parts = line.Split('|');
                if (parts.Length >= 4)
                {
                    var displayName = parts[1];
                    var ok = parts[2] == "OK";
                    var msg = parts.Length >= 5 ? parts[4] : (ok ? "Removed" : "Failed");
                    var match = items.FirstOrDefault(it => it.DisplayName == displayName);
                    if (match != null)
                    {
                        var idx = items.ToList().IndexOf(match) + 1;
                        progress?.Report(new BloatwareProgress(
                            idx, items.Count, match,
                            ok ? BloatwarePhase.Success : BloatwarePhase.Failed,
                            ok ? "Removed" : msg));
                    }
                }
            }
        }
        return lines.Length;
    }

    private static BloatwareUninstallResult ParseFinalResults(string logPath, IReadOnlyList<BloatwareItem> items)
    {
        var results = new Dictionary<string, (bool, string)>();
        if (!File.Exists(logPath))
        {
            // Geen log = niets gestart (bv. UAC denied voor de log werd gecreated).
            return new BloatwareUninstallResult(0, items.Count,
                items.ToDictionary(i => i.DisplayName, i => (false, App.Loc.S("elevated.noLog"))),
                Cancelled: false);
        }

        string[] lines;
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd();
            lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return new BloatwareUninstallResult(0, items.Count,
                items.ToDictionary(i => i.DisplayName, i => (false, App.Loc.S("elevated.logUnreadable"))),
                Cancelled: false);
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (!trimmed.StartsWith("RESULT|")) continue;
            var parts = trimmed.Split('|');
            if (parts.Length < 4) continue;

            var displayName = parts[1];
            var ok = parts[2] == "OK";
            var msg = parts.Length >= 5 ? parts[4] : (ok ? "Removed" : "Failed");
            results[displayName] = (ok, msg);
        }

        // Items die geen RESULT-line kregen (bv. PS crashte halverwege) → gemarkeerd
        // als failed met een neutrale boodschap. Anders zou de UI ze in Pending laten
        // hangen.
        foreach (var item in items)
        {
            if (!results.ContainsKey(item.DisplayName))
                results[item.DisplayName] = (false, App.Loc.S("elevated.didNotRun"));
        }

        var success = results.Count(kv => kv.Value.Item1);
        var failed = results.Count - success;
        return new BloatwareUninstallResult(success, failed, results, Cancelled: false);
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

    // Voor inline gebruik in een PS-scriptbody. Single quotes zijn de string-delimiter
    // dus die moeten verdubbeld; pipe-tekens niet, die zijn alleen aan de PS-parser
    // betekenisvol buiten een string-context.
    private static string EscapeForPs(string s) => s.Replace("'", "''");

    // Voor file-paden in PS single-quoted strings.
    private static string EscapePath(string path) => path.Replace("'", "''");
}

public enum BloatwarePhase { Pending, Running, Success, Failed }

public readonly record struct BloatwareProgress(
    int CurrentIndex,
    int Total,
    BloatwareItem Item,
    BloatwarePhase Phase,
    string Message);

public sealed record BloatwareUninstallResult(
    int SuccessCount,
    int FailedCount,
    IReadOnlyDictionary<string, (bool success, string message)> ResultsByDisplayName,
    bool Cancelled);
