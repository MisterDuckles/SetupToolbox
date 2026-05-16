using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using WingetAppDeployer_WinUI.Helpers;

namespace WingetAppDeployer_WinUI.Services;

// System Restore Point service. Wrapt Checkpoint-Computer + Get-ComputerRestorePoint
// in elevated PowerShell — Checkpoint-Computer vereist Administrator.
//
// Windows-defaults om in 't achterhoofd te houden:
//  - System Protection kan disabled zijn voor de C: drive (door user of
//    OEM-image). Checkpoint-Computer faalt dan met een specifieke error.
//    IsSystemProtectionEnabledAsync() checkt dit zodat de UI duidelijk kan
//    melden dat de optie niet bruikbaar is.
//  - Default rate-limit: max 1 restore point per 24h. Kan overschreven worden
//    via HKLM\Software\Microsoft\Windows NT\CurrentVersion\SystemRestore\
//    SystemRestorePointCreationFrequency=0, maar dat is een system-level
//    tweak die we niet stiekem willen doen. CanCreateNowAsync() checkt of
//    de laatste >24u geleden is.
//
// Returnt geen exceptions naar UI; resultaat-objecten met success/reason.
public sealed class RestorePointService
{
    private const string SystemRestoreKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    /// <summary>
    /// Maakt een restore point met de gegeven description. Vereist UAC.
    /// Returnt success=false met een reden als 't faalt (System Protection
    /// uit, rate-limited, UAC geweigerd, etc.).
    /// </summary>
    public async Task<RestorePointResult> CreateAsync(string description)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_restorepoint_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        // RestorePointType MODIFY_SETTINGS (12) is de geschikte categorie voor
        // app-changes die settings/files raken. APPLICATION_INSTALL (0) zou
        // ook werken maar is semantisch voor installers.
        var script = $@"
$ErrorActionPreference = 'Stop'
$logPath = '{Escape(logPath)}'
function Log($msg) {{ Add-Content -Path $logPath -Value $msg }}
try {{
    Checkpoint-Computer -Description '{Escape(description)}' -RestorePointType MODIFY_SETTINGS
    Log 'RESULT|OK'
}} catch {{
    Log ""RESULT|FAIL|$($_.Exception.Message)""
}}
";

        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_restorepoint_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);

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
            if (proc == null) return new RestorePointResult(false, "Kon elevated process niet starten.");
            await proc.WaitForExitAsync();

            if (File.Exists(logPath))
            {
                var content = await File.ReadAllTextAsync(logPath);
                if (content.Contains("RESULT|OK")) return new RestorePointResult(true, "Restore point aangemaakt.");
                var failIdx = content.IndexOf("RESULT|FAIL|", StringComparison.Ordinal);
                if (failIdx >= 0)
                {
                    var reason = content.Substring(failIdx + "RESULT|FAIL|".Length).Split('\n')[0].Trim();
                    return new RestorePointResult(false, reason);
                }
            }
            return new RestorePointResult(false, "Geen output van Checkpoint-Computer.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new RestorePointResult(false, "UAC geweigerd.");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("WingetAppDeployer_restorepoint.log", $"CreateAsync failed: {ex.Message}");
            return new RestorePointResult(false, ex.Message);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
            try { File.Delete(logPath); } catch { }
        }
    }

    /// <summary>
    /// Wanneer was 't laatste restore point gemaakt? Null wanneer er geen is
    /// of als de query faalt. Lichtgewicht — gebruikt Get-ComputerRestorePoint
    /// (read-only WMI, geen UAC nodig).
    /// </summary>
    public Task<DateTime?> GetLastRestorePointAsync()
    {
        return Task.Run<DateTime?>(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                        "\"Get-ComputerRestorePoint | Select-Object -Last 1 | ForEach-Object { $_.CreationTime.ToString('o') }\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(10000);
                // Output is een WMI-datetime string (yyyyMMddHHmmss.ffffff+TZ) als
                // CreationTime nog niet geconverteerd is, of een ISO 8601 string
                // als de ForEach-Object 't goed parsed. Probeer beide.
                if (string.IsNullOrEmpty(output)) return null;
                if (DateTime.TryParse(output, out var iso)) return iso;
                if (output.Length >= 14 && long.TryParse(output.Substring(0, 14), out _))
                {
                    // yyyyMMddHHmmss
                    if (DateTime.TryParseExact(output.Substring(0, 14), "yyyyMMddHHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeLocal, out var dt))
                        return dt;
                }
                return null;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// True als System Protection op de system-drive is ingeschakeld. Zo nee,
    /// faalt Checkpoint-Computer met "Cannot create a restore point".
    /// </summary>
    public Task<bool> IsSystemProtectionEnabledAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                // SystemRestore disabled wanneer DisableSR=1 in HKLM. Lichtgewicht
                // signal — niet perfect (per-drive config zit elders), maar dekt
                // de gangbare disable-case.
                var (hive, view, sub) = ParsePath(SystemRestoreKey);
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(sub, writable: false);
                if (key == null) return true;  // key niet aanwezig = default = aan
                var disableSR = key.GetValue("DisableSR");
                if (disableSR is int v && v == 1) return false;
                return true;
            }
            catch
            {
                return true;  // bij twijfel: aanmaken proberen, fail message vangt 't af
            }
        });
    }

    /// <summary>
    /// Combineert IsSystemProtectionEnabled + GetLastRestorePoint om te
    /// adviseren of een nieuwe checkpoint zinvol is. Returnt (canCreate,
    /// hoursSinceLastPoint, reasonIfNot).
    /// </summary>
    public async Task<RestorePointStatus> GetStatusAsync()
    {
        var enabled = await IsSystemProtectionEnabledAsync();
        if (!enabled) return new RestorePointStatus(false, null, "System Protection is uit op deze PC.");

        var last = await GetLastRestorePointAsync();
        if (last == null) return new RestorePointStatus(true, null, null);

        var sinceLast = DateTime.Now - last.Value;
        if (sinceLast < TimeSpan.FromHours(24))
        {
            var hoursAgo = sinceLast.TotalHours;
            return new RestorePointStatus(false, hoursAgo,
                $"Laatste restore point was {FormatAgo(sinceLast)} geleden — Windows skipt nieuwe punten binnen 24u.");
        }
        return new RestorePointStatus(true, sinceLast.TotalHours, null);
    }

    /// <summary>
    /// Snippet voor inline opname in een andere elevated PS-batch (bv. de
    /// DeepClean delete script). Zo krijgt user maar 1 UAC-prompt voor
    /// checkpoint + delete samen.
    /// </summary>
    public static string BuildInlineCheckpointScript(string description)
    {
        return $@"
# WingetAppDeployer auto-checkpoint — voor de delete-batch eronder.
try {{
    Checkpoint-Computer -Description '{Escape(description)}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
}} catch {{
    # Niet fataal: delete gaat door. Restore point ontbreken is niet onze blokkade.
}}
";
    }

    public static string FormatAgo(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return "minder dan een minuut";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} uur";
        var days = (int)span.TotalDays;
        return days == 1 ? "1 dag" : $"{days} dagen";
    }

    private static (RegistryHive hive, RegistryView view, string subPath) ParsePath(string fullPath)
    {
        var firstSep = fullPath.IndexOf('\\');
        var hiveName = fullPath.Substring(0, firstSep).ToUpperInvariant();
        var subPath = fullPath.Substring(firstSep + 1);
        var hive = hiveName switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException($"Unsupported hive: {hiveName}")
        };
        return (hive, RegistryView.Registry64, subPath);
    }

    private static string Escape(string s) => s.Replace("'", "''");
}

public sealed record RestorePointResult(bool Success, string Message);

// canCreate=false betekent: rate-limited of System Protection uit. Hours
// is null als er nog nooit een restore point is geweest.
public sealed record RestorePointStatus(bool CanCreate, double? HoursSinceLast, string? BlockedReason);
