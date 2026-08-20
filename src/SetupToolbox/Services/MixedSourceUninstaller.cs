using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SetupToolbox.Models;

namespace SetupToolbox.Services;

// Uninstall-coordinator voor de unified DebloatPage. Splitst de input-lijst per
// InstalledSource en routeert elk per-source naar het juiste mechanisme:
//   Winget -> WingetService.UninstallAppAsync (sequential, no UAC)
//   Store  -> Remove-AppxPackage (elevated PS batch)
//   Web    -> registry UninstallString (elevated PS batch)
//
// Store + Web gaan in één gecombineerde elevated PS-call zodat user maar één
// UAC prompt ziet voor de hele "admin-required" subset. Winget-items lopen
// daarvoor — geen UAC nodig dus user merkt geen onderbreking.
public sealed class MixedSourceUninstaller
{
    /// <summary>
    /// Run de uninstall over alle items. Phase events voeden de UI dialog. Cancelled-
    /// flag op het result is true wanneer user UAC declined voor de elevated batch
    /// — catalog-items kunnen alsnog gelopen hebben in dat geval.
    /// </summary>
    public async Task<MixedUninstallResult> UninstallBatchAsync(
        IReadOnlyList<InstalledAppEntry> items,
        IProgress<MixedUninstallProgress>? progress = null)
    {
        var results = new ConcurrentDictionary<string, (bool, string)>();
        if (items.Count == 0)
            return new MixedUninstallResult(0, 0, results, Cancelled: false);

        var byWinget = items.Where(i => i.Source == InstalledSource.Winget).ToList();
        var byStore = items.Where(i => i.Source == InstalledSource.Store).ToList();
        var byWeb = items.Where(i => i.Source == InstalledSource.Web).ToList();

        // 1) Winget: sequential winget uninstall. Snel + geen UAC.
        foreach (var entry in byWinget)
        {
            progress?.Report(new MixedUninstallProgress(entry, MixedUninstallPhase.Running,
                App.Loc.S("progress.uninstalling", entry.DisplayName)));
            var (ok, msg) = await App.Winget.UninstallAppAsync(entry.Identifier);
            results[entry.Identifier] = (ok, msg);
            progress?.Report(new MixedUninstallProgress(
                entry,
                ok ? MixedUninstallPhase.Success : MixedUninstallPhase.Failed,
                msg));
        }

        // 2) Store + Web: één elevated PS-batch.
        var elevatedItems = byStore.Concat(byWeb).ToList();
        var cancelled = false;
        if (elevatedItems.Count > 0)
        {
            cancelled = await RunElevatedBatchAsync(elevatedItems, results, progress);
        }

        var success = results.Count(kv => kv.Value.Item1);
        var failed = results.Count - success;
        return new MixedUninstallResult(success, failed, results, cancelled);
    }

    private static async Task<bool> RunElevatedBatchAsync(
        IReadOnlyList<InstalledAppEntry> items,
        ConcurrentDictionary<string, (bool, string)> results,
        IProgress<MixedUninstallProgress>? progress)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"SetupToolbox_mixed_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // Bouw PS-script. Per item: PROGRESS-marker → uninstall-actie → RESULT-marker.
        // PROGRESS marker formaat:  PROGRESS|<identifier>|<displayName>
        // RESULT marker formaat:    RESULT|<identifier>|OK or FAIL|<message>
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{EscapePath(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START|$(Get-Date -Format o)\"");

        foreach (var entry in items)
        {
            var displayName = EscapeForPs(entry.DisplayName);
            var identifier = EscapeForPs(entry.Identifier);
            sb.AppendLine($"Log \"PROGRESS|{identifier}|{displayName}\"");

            if (entry.Source == InstalledSource.Store)
            {
                // PackageFullName komt rechtstreeks uit Get-AppxPackage output, geen
                // quotes/specials te verwachten. Defensief in single-quoted string
                // wrapper voor robuustheid.
                sb.AppendLine("try {");
                sb.AppendLine($"    Remove-AppxPackage -Package '{EscapeForPs(entry.Identifier)}' -ErrorAction Stop");
                sb.AppendLine($"    Log \"RESULT|{identifier}|OK|Removed AppX package\"");
                sb.AppendLine("} catch {");
                sb.AppendLine($"    Log \"RESULT|{identifier}|FAIL|$($_.Exception.Message)\"");
                sb.AppendLine("}");
            }
            else if (entry.Source == InstalledSource.Web && !string.IsNullOrWhiteSpace(entry.UninstallCommand))
            {
                var cmd = NormalizeUninstallCommand(entry.UninstallCommand!);
                // cmd.exe /c <command> — laat Windows zelf de exe-aanroep parsen
                // (handles spaces in paths, quotes etc.). Output redirect naar
                // null zodat het PS-script niet hangt op interactive prompts.
                var escapedCmd = cmd.Replace("\"", "\\\"");
                sb.AppendLine("try {");
                sb.AppendLine($"    $proc = Start-Process -FilePath cmd.exe -ArgumentList '/c \"{escapedCmd}\"' -Wait -PassThru -WindowStyle Hidden -ErrorAction Stop");
                sb.AppendLine("    if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {");
                sb.AppendLine($"        Log \"RESULT|{identifier}|OK|Uninstalled (exit $($proc.ExitCode))\"");
                sb.AppendLine("    } else {");
                sb.AppendLine($"        Log \"RESULT|{identifier}|FAIL|Uninstaller returned exit $($proc.ExitCode)\"");
                sb.AppendLine("    }");
                sb.AppendLine("} catch {");
                sb.AppendLine($"    Log \"RESULT|{identifier}|FAIL|$($_.Exception.Message)\"");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine($"Log \"RESULT|{identifier}|FAIL|No uninstall command available\"");
            }
        }

        sb.AppendLine("Log \"END|$(Get-Date -Format o)\"");

        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"SetupToolbox_mixed_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
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

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                MarkAllFailed(items, results, App.Loc.S("elevated.couldNotStart"));
                ReportAllFailed(items, results, progress);
                return false;
            }

            var lastLineCount = 0;
            while (!proc.HasExited)
            {
                await Task.Delay(200);
                lastLineCount = ReadProgressFromLog(logPath, lastLineCount, items, results, progress);
            }
            await Task.Delay(100);
            ReadProgressFromLog(logPath, lastLineCount, items, results, progress);

            // Items zonder RESULT-line in de log → markeer als failed (PS gecrashd
            // halverwege, kan in theorie gebeuren). Cancelled=false hier; UAC
            // denial wordt door de catch hieronder afgehandeld.
            FinalizePending(items, results, App.Loc.S("elevated.didNotRun"));
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User klikte "No" op UAC prompt → Win32Exception 1223. Mark alle
            // items als cancelled-met-message zodat dialog dit als 'Cancelled'
            // kan tonen i.p.v. 'Failed'.
            foreach (var entry in items)
            {
                if (!results.ContainsKey(entry.Identifier))
                    results[entry.Identifier] = (false, App.Loc.S("elevated.uacDeclined"));
            }
            ReportAllFailed(items, results, progress);
            return true;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    /// <summary>
    /// MSI-installs hebben een UninstallString in de vorm "MsiExec.exe /X{GUID}" of
    /// "MsiExec.exe /I{GUID}". Append /quiet /norestart als die nog niet aanwezig zijn
    /// zodat de uninstall geen UI toont. Voor non-MSI strings doen we niets — die
    /// vertrouwen op QuietUninstallString of installer-specifieke silent flags die
    /// we niet kunnen voorspellen.
    /// </summary>
    private static string NormalizeUninstallCommand(string raw)
    {
        var cmd = raw.Trim();
        if (Regex.IsMatch(cmd, @"^\s*""?msiexec(\.exe)?""?\s*/[xi]", RegexOptions.IgnoreCase))
        {
            if (!cmd.Contains("/quiet", StringComparison.OrdinalIgnoreCase) &&
                !cmd.Contains("/q", StringComparison.OrdinalIgnoreCase))
                cmd += " /quiet";
            if (!cmd.Contains("/norestart", StringComparison.OrdinalIgnoreCase))
                cmd += " /norestart";
        }
        return cmd;
    }

    private static int ReadProgressFromLog(
        string logPath,
        int alreadyRead,
        IReadOnlyList<InstalledAppEntry> items,
        ConcurrentDictionary<string, (bool, string)> results,
        IProgress<MixedUninstallProgress>? progress)
    {
        if (!File.Exists(logPath)) return alreadyRead;

        string[] lines;
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { return alreadyRead; }

        for (int i = alreadyRead; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.StartsWith("PROGRESS|"))
            {
                var parts = line.Split('|', 3);
                if (parts.Length < 3) continue;
                var entry = items.FirstOrDefault(it => it.Identifier == parts[1]);
                if (entry != null)
                    progress?.Report(new MixedUninstallProgress(entry, MixedUninstallPhase.Running,
                        App.Loc.S("progress.removing", entry.DisplayName)));
            }
            else if (line.StartsWith("RESULT|"))
            {
                var parts = line.Split('|', 4);
                if (parts.Length < 3) continue;
                var entry = items.FirstOrDefault(it => it.Identifier == parts[1]);
                if (entry == null) continue;
                var ok = parts[2] == "OK";
                var msg = parts.Length >= 4 ? parts[3] : (ok ? "Removed" : "Failed");
                results[entry.Identifier] = (ok, msg);
                progress?.Report(new MixedUninstallProgress(
                    entry,
                    ok ? MixedUninstallPhase.Success : MixedUninstallPhase.Failed,
                    msg));
            }
        }
        return lines.Length;
    }

    private static void FinalizePending(
        IReadOnlyList<InstalledAppEntry> items,
        ConcurrentDictionary<string, (bool, string)> results,
        string message)
    {
        foreach (var entry in items)
            if (!results.ContainsKey(entry.Identifier))
                results[entry.Identifier] = (false, message);
    }

    private static void MarkAllFailed(
        IReadOnlyList<InstalledAppEntry> items,
        ConcurrentDictionary<string, (bool, string)> results,
        string message)
    {
        foreach (var entry in items)
            if (!results.ContainsKey(entry.Identifier))
                results[entry.Identifier] = (false, message);
    }

    private static void ReportAllFailed(
        IReadOnlyList<InstalledAppEntry> items,
        ConcurrentDictionary<string, (bool, string)> results,
        IProgress<MixedUninstallProgress>? progress)
    {
        foreach (var entry in items)
        {
            if (results.TryGetValue(entry.Identifier, out var r))
                progress?.Report(new MixedUninstallProgress(entry, MixedUninstallPhase.Failed, r.Item2));
        }
    }

    private static string EscapeForPs(string s) => s.Replace("'", "''");
    private static string EscapePath(string path) => path.Replace("'", "''");
}

public enum MixedUninstallPhase { Pending, Running, Success, Failed }

public readonly record struct MixedUninstallProgress(
    InstalledAppEntry Entry,
    MixedUninstallPhase Phase,
    string Message);

public sealed record MixedUninstallResult(
    int SuccessCount,
    int FailedCount,
    System.Collections.Generic.IReadOnlyDictionary<string, (bool success, string message)> ResultsByIdentifier,
    bool Cancelled);
