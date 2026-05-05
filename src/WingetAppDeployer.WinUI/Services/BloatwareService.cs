using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Services;

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
    /// Run Get-AppxPackage en match installed packages tegen onze curated list.
    /// Vult IsInstalled + InstalledPackageFullNames per item zodat de UI weet
    /// welke items tonen en de uninstall-call de FullName kan doorgeven aan
    /// Remove-AppxPackage (die heeft FullName nodig, niet Name).
    /// </summary>
    public async Task DetectInstalledAsync(IReadOnlyList<BloatwareItem> items)
    {
        try
        {
            // -AllUsers vereist admin; zonder die flag zien we alleen current-user
            // installs. Voor de meeste bloatware is current-user genoeg en als user
            // het later wil verwijderen klagen we daar pas bij Remove-AppxPackage
            // over de admin-noodzaak.
            //
            // Format: Name + PackageFullName pipe-separated zodat we makkelijk kunnen
            // parsen. ConvertTo-Csv zou robuuster zijn maar deze format is voldoende
            // voor de set tekens die in package names voorkomt.
            var script = "Get-AppxPackage | ForEach-Object { \"$($_.Name)|$($_.PackageFullName)\" }";
            var output = await RunPowerShellAsync(script);

            var installedMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                var parts = trimmed.Split('|');
                if (parts.Length != 2) continue;
                var name = parts[0].Trim();
                var fullName = parts[1].Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(fullName)) continue;

                if (!installedMap.TryGetValue(name, out var list))
                {
                    list = new List<string>();
                    installedMap[name] = list;
                }
                list.Add(fullName);
            }

            // Match curated items tegen detected packages. Een item wordt als installed
            // gemarkeerd zodra ÉÉN van zijn PackageNames matcht — voor multi-package
            // items (Xbox suite) hoeft dus niet de hele suite aanwezig te zijn.
            foreach (var item in items)
            {
                item.InstalledPackageFullNames.Clear();
                foreach (var packageName in item.PackageNames)
                {
                    if (installedMap.TryGetValue(packageName, out var fullNames))
                        item.InstalledPackageFullNames.AddRange(fullNames);
                }
                item.IsInstalled = item.InstalledPackageFullNames.Count > 0;
            }
        }
        catch
        {
            // Bij PowerShell-failure niets markeren als installed → user ziet
            // lege bloatware-lijst i.p.v. een crash. Edge case.
            foreach (var item in items)
            {
                item.IsInstalled = false;
                item.InstalledPackageFullNames.Clear();
            }
        }
    }

    /// <summary>
    /// Uninstall een batch bloatware items via één elevated PowerShell-call. UAC
    /// prompt verschijnt één keer voor de hele batch. Geen live progress (verb=runas
    /// vereist UseShellExecute=true wat stdout-redirect onmogelijk maakt) — we
    /// schrijven log naar %TEMP% en parsen die na afloop voor success/failure.
    /// </summary>
    public async Task<BloatwareUninstallResult> UninstallBatchAsync(
        IReadOnlyList<BloatwareItem> items,
        IProgress<BloatwareProgress>? progress = null)
    {
        if (items.Count == 0)
            return new BloatwareUninstallResult(0, 0, new Dictionary<string, (bool, string)>(), Cancelled: false);

        var logPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_bloatware_{DateTime.Now:yyyyMMdd_HHmmss}.log");

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
            $"WingetAppDeployer_bloatware_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
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
                items.ToDictionary(i => i.DisplayName, i => (false, "Cancelled — UAC prompt declined")),
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
                        progress?.Report(new BloatwareProgress(idx, total, match, BloatwarePhase.Running, $"Removing {displayName}..."));
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
                items.ToDictionary(i => i.DisplayName, i => (false, "No log produced")),
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
                items.ToDictionary(i => i.DisplayName, i => (false, "Could not read log")),
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
                results[item.DisplayName] = (false, "Did not run (interrupted)");
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
