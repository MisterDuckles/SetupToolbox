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

// Centrale service voor de Tweaks tab. Registreert alle Tweak-definities
// (data-driven, geen per-tweak UI-code), leest live registry-state bij
// page-load, en past tweaks toe via een gemixt local/elevated batch-pad
// (HKCU = in-process, HKLM = 1 UAC voor de gehele admin-subset).
//
// Architectuur is parallel met DeepCleanService:
//   - read = synchronous registry-walk (Microsoft.Win32.Registry)
//   - apply = split user-ops vs elevated-ops, elevated via PowerShell + reg.exe
//   - revert = idem, behalve dat DisabledValue wordt geschreven i.p.v. EnabledValue
public sealed class TweakService
{
    private readonly List<Tweak> _tweaks;

    public TweakService()
    {
        _tweaks = BuildAll();
    }

    public IReadOnlyList<Tweak> All => _tweaks;

    public IEnumerable<Tweak> InCategory(TweakCategory cat) =>
        _tweaks.Where(t => t.Category == cat);

    // ---------------------------------------------------------------
    // LIVE STATE DETECTION
    // ---------------------------------------------------------------

    /// <summary>
    /// Leest voor elke tweak de huidige registry-state. Vult Tweak.State per
    /// item. Async zodat de UI-thread niet blokkeert tijdens een mass-read.
    /// </summary>
    public Task DetectStatesAsync()
    {
        return Task.Run(() =>
        {
            foreach (var tweak in _tweaks)
            {
                try
                {
                    tweak.State = DetectStateInternal(tweak);
                }
                catch
                {
                    tweak.State = TweakState.Unknown;
                }
            }
        });
    }

    private static TweakState DetectStateInternal(Tweak tweak)
    {
        int enabledOps = 0, disabledOps = 0, unknownOps = 0;
        foreach (var op in tweak.Operations)
        {
            var match = MatchOpState(op);
            if (match == TweakState.Enabled) enabledOps++;
            else if (match == TweakState.Disabled) disabledOps++;
            else unknownOps++;
        }
        if (unknownOps > 0 && enabledOps == 0 && disabledOps == 0) return TweakState.Unknown;
        if (enabledOps == tweak.Operations.Count) return TweakState.Enabled;
        if (disabledOps == tweak.Operations.Count) return TweakState.Disabled;
        return TweakState.Partial;
    }

    private static TweakState MatchOpState(TweakOperation op)
    {
        // Read raw value (or detect absent). Return -> compare with Enabled/DisabledValue.
        var actual = TryReadValue(op);
        var actualIsAbsent = !actual.exists;
        var enabledIsAbsent = op.EnabledValue == null;
        var disabledIsAbsent = op.DisabledValue == null;

        if (actualIsAbsent && enabledIsAbsent) return TweakState.Enabled;
        if (actualIsAbsent && disabledIsAbsent) return TweakState.Disabled;
        if (actualIsAbsent) return TweakState.Disabled; // Windows default = key absent in most cases

        if (!enabledIsAbsent && ValuesEqual(actual.value!, op.EnabledValue!)) return TweakState.Enabled;
        if (!disabledIsAbsent && ValuesEqual(actual.value!, op.DisabledValue!)) return TweakState.Disabled;
        // Value bestaat maar matcht geen van beide kanten — gebruiker heeft 'n
        // custom waarde gezet; tellen we als Disabled (= we hebben de tweak
        // niet zelf actief gemaakt) zodat user altijd kan klikken om onze
        // EnabledValue te forceren.
        return TweakState.Disabled;
    }

    private static bool ValuesEqual(object a, object b)
    {
        if (a == null || b == null) return ReferenceEquals(a, b);
        // DWord is int; compare numerically met conversie zodat short/long/uint
        // niet uit elkaar lopen.
        try
        {
            if (a is int || b is int || a is long || b is long || a is uint || b is uint)
            {
                return Convert.ToInt64(a) == Convert.ToInt64(b);
            }
        }
        catch { /* fall through */ }
        // String compare case-sensitive (registry value-data is case-significant).
        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
        return a.Equals(b);
    }

    private static (bool exists, object? value) TryReadValue(TweakOperation op)
    {
        try
        {
            var (hive, view, sub) = ParsePath(op.Path);
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(sub, writable: false);
            if (key == null) return (false, null);
            var values = key.GetValueNames();
            // (Default) value komt terug als empty string in GetValueNames.
            if (!values.Contains(op.ValueName, StringComparer.OrdinalIgnoreCase))
                return (false, null);
            var value = key.GetValue(op.ValueName);
            return (value != null, value);
        }
        catch
        {
            return (false, null);
        }
    }

    private static (RegistryHive hive, RegistryView view, string subPath) ParsePath(string fullPath)
    {
        var firstSep = fullPath.IndexOf('\\');
        if (firstSep <= 0) throw new ArgumentException($"Invalid registry path: {fullPath}");
        var hiveName = fullPath.Substring(0, firstSep).ToUpperInvariant();
        var subPath = fullPath.Substring(firstSep + 1);
        var hive = hiveName switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            _ => throw new ArgumentException($"Unsupported hive: {hiveName}")
        };
        var view = subPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
            ? RegistryView.Registry32
            : RegistryView.Registry64;
        return (hive, view, subPath);
    }

    // ---------------------------------------------------------------
    // APPLY / REVERT
    // ---------------------------------------------------------------

    /// <summary>
    /// Pas een lijst van tweaks toe (apply=true) of revert ze (apply=false).
    /// Splits in user-context ops (HKCU) en elevated ops (HKLM). Elevated krijgt
    /// 1 UAC-prompt voor de hele admin-subset. Returnt per tweak een resultaat
    /// + globale Cancelled-flag bij UAC-denial.
    /// </summary>
    public async Task<TweakApplyResult> ApplyAsync(IReadOnlyList<Tweak> tweaks, bool apply)
    {
        var results = new Dictionary<string, (bool ok, string msg)>();
        var localOps = new List<(Tweak tweak, TweakOperation op)>();
        var elevatedOps = new List<(Tweak tweak, TweakOperation op)>();
        foreach (var t in tweaks)
            foreach (var op in t.Operations)
                (op.RequiresElevation ? elevatedOps : localOps).Add((t, op));

        // 1) Local (HKCU) in-process — geen UAC.
        foreach (var (tweak, op) in localOps)
        {
            try
            {
                ApplyOpLocal(op, apply);
                results[$"{tweak.Id}::{op.Path}::{op.ValueName}"] = (true, apply ? "Applied" : "Reverted");
            }
            catch (Exception ex)
            {
                results[$"{tweak.Id}::{op.Path}::{op.ValueName}"] = (false, ex.Message);
            }
        }

        // 2) Elevated batch — 1 UAC voor de hele subset.
        var cancelled = false;
        if (elevatedOps.Count > 0)
        {
            var (batchResults, wasCancelled) = await RunElevatedBatchAsync(elevatedOps, apply);
            cancelled = wasCancelled;
            foreach (var kv in batchResults) results[kv.Key] = kv.Value;
        }

        // 3) Refresh detected state voor de getoggle-de tweaks.
        foreach (var t in tweaks)
        {
            try { t.State = DetectStateInternal(t); }
            catch { t.State = TweakState.Unknown; }
        }

        var successCount = results.Count(r => r.Value.ok);
        return new TweakApplyResult(successCount, results.Count - successCount, cancelled);
    }

    private static void ApplyOpLocal(TweakOperation op, bool apply)
    {
        var targetValue = apply ? op.EnabledValue : op.DisabledValue;
        var (hive, view, subPath) = ParsePath(op.Path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);

        if (targetValue == null)
        {
            // Naar absent: delete value, of hele key tree als DeleteKeyOnAbsent.
            if (op.DeleteKeyOnAbsent)
            {
                using var probe = baseKey.OpenSubKey(subPath);
                if (probe != null)
                {
                    using var parent = baseKey.OpenSubKey(ParentOf(subPath), writable: true);
                    parent?.DeleteSubKeyTree(LeafOf(subPath), throwOnMissingSubKey: false);
                }
            }
            else
            {
                using var key = baseKey.OpenSubKey(subPath, writable: true);
                key?.DeleteValue(op.ValueName, throwOnMissingValue: false);
            }
            return;
        }

        // Schrijven — CreateSubKey maakt parents automatisch aan.
        using var writeKey = baseKey.CreateSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot create/open key: {op.Path}");
        writeKey.SetValue(op.ValueName, targetValue, op.Kind);
    }

    private static string ParentOf(string subPath)
    {
        var idx = subPath.LastIndexOf('\\');
        return idx < 0 ? string.Empty : subPath.Substring(0, idx);
    }
    private static string LeafOf(string subPath)
    {
        var idx = subPath.LastIndexOf('\\');
        return idx < 0 ? subPath : subPath.Substring(idx + 1);
    }

    private static async Task<(Dictionary<string, (bool ok, string msg)> results, bool cancelled)>
        RunElevatedBatchAsync(IReadOnlyList<(Tweak tweak, TweakOperation op)> items, bool apply)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_tweaks_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{Escape(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START|$(Get-Date -Format o)\"");

        foreach (var (tweak, op) in items)
        {
            var key = $"{tweak.Id}::{op.Path}::{op.ValueName}";
            var targetValue = apply ? op.EnabledValue : op.DisabledValue;
            sb.AppendLine($"Log \"PROGRESS|{Escape(tweak.Name)}\"");
            sb.AppendLine("try {");

            if (targetValue == null)
            {
                // reg.exe delete /va = alle values; we willen alleen specifieke value of hele key
                if (op.DeleteKeyOnAbsent)
                {
                    sb.AppendLine($"    & reg.exe delete '{Escape(op.Path)}' /f | Out-Null");
                }
                else
                {
                    var valueArg = string.IsNullOrEmpty(op.ValueName) ? "/ve" : $"/v '{Escape(op.ValueName)}'";
                    sb.AppendLine($"    & reg.exe delete '{Escape(op.Path)}' {valueArg} /f | Out-Null");
                }
                sb.AppendLine($"    if ($LASTEXITCODE -ne 0) {{ throw \"reg.exe exit $LASTEXITCODE\" }}");
                sb.AppendLine($"    Log \"RESULT|{Escape(key)}|OK|Deleted\"");
            }
            else
            {
                var regType = op.Kind switch
                {
                    RegistryValueKind.DWord => "REG_DWORD",
                    RegistryValueKind.String => "REG_SZ",
                    RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
                    RegistryValueKind.MultiString => "REG_MULTI_SZ",
                    RegistryValueKind.Binary => "REG_BINARY",
                    RegistryValueKind.QWord => "REG_QWORD",
                    _ => "REG_SZ"
                };
                var valueArg = string.IsNullOrEmpty(op.ValueName) ? "/ve" : $"/v '{Escape(op.ValueName)}'";
                var data = FormatValueForReg(targetValue, op.Kind);
                sb.AppendLine($"    & reg.exe add '{Escape(op.Path)}' {valueArg} /t {regType} /d '{Escape(data)}' /f | Out-Null");
                sb.AppendLine($"    if ($LASTEXITCODE -ne 0) {{ throw \"reg.exe exit $LASTEXITCODE\" }}");
                sb.AppendLine($"    Log \"RESULT|{Escape(key)}|OK|Set\"");
            }
            sb.AppendLine("} catch {");
            sb.AppendLine($"    Log \"RESULT|{Escape(key)}|FAIL|$($_.Exception.Message)\"");
            sb.AppendLine("}");
        }
        sb.AppendLine("Log \"END|$(Get-Date -Format o)\"");

        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"WingetAppDeployer_tweaks_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
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
                foreach (var (t, op) in items)
                    results[$"{t.Id}::{op.Path}::{op.ValueName}"] = (false, "Could not start elevated process");
                return (results, false);
            }
            await proc.WaitForExitAsync();
            ParseResults(logPath, results);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            cancelled = true;
            foreach (var (t, op) in items)
            {
                var key = $"{t.Id}::{op.Path}::{op.ValueName}";
                if (!results.ContainsKey(key))
                    results[key] = (false, "Cancelled — UAC prompt declined");
            }
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
        foreach (var (t, op) in items)
        {
            var key = $"{t.Id}::{op.Path}::{op.ValueName}";
            if (!results.ContainsKey(key))
                results[key] = (false, "Did not run (interrupted)");
        }
        return (results, cancelled);
    }

    private static string FormatValueForReg(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord or RegistryValueKind.QWord => Convert.ToInt64(value).ToString(),
        RegistryValueKind.String or RegistryValueKind.ExpandString => value?.ToString() ?? string.Empty,
        _ => value?.ToString() ?? string.Empty
    };

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
            var key = parts[1];
            var ok = parts[2] == "OK";
            var msg = parts.Length >= 4 ? parts[3] : (ok ? "OK" : "Failed");
            results[key] = (ok, msg);
        }
    }

    private static string Escape(string s) => s.Replace("'", "''");

    /// <summary>
    /// Restart Explorer-shell (taskkill + start) zodat tweaks die ExplorerRestart
    /// vereisen direct zichtbaar worden zonder dat user moet sign-out'en.
    /// </summary>
    public static async Task RestartExplorerAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
                // Windows herstart explorer automatisch via shellhost-watchdog, maar in dev/edge cases
                // niet altijd — expliciet starten als safety net.
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Diagnostics.Log("WingetAppDeployer_tweaks.log", $"RestartExplorer failed: {ex.Message}");
            }
        });
    }

    // ---------------------------------------------------------------
    // TWEAK REGISTRY (data-driven definitions)
    // ---------------------------------------------------------------

    private static List<Tweak> BuildAll()
    {
        var list = new List<Tweak>();

        // ── EXPLORER ────────────────────────────────────────────────
        const string explorerAdvanced = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        list.Add(new Tweak(
            id: "Explorer.ShowFileExtensions",
            category: TweakCategory.Explorer,
            name: "Show file extensions",
            description: "Toont .exe / .pdf / .docx achter bestandsnamen.",
            useCase: "Voorkomt verraderlijke double-extension exe's en helpt extensies herkennen.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "HideFileExt",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Explorer.ShowHiddenFiles",
            category: TweakCategory.Explorer,
            name: "Show hidden files",
            description: "Toont AppData, .git folders, en andere verborgen items.",
            useCase: "Onmisbaar voor dev-werk en troubleshooting van app-configs.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "Hidden",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 2
                }
            }));

        list.Add(new Tweak(
            id: "Explorer.TaskbarAlignLeft",
            category: TweakCategory.Explorer,
            name: "Taskbar aligned left (Win10-style)",
            description: "Lijnt taskbar-iconen links uit i.p.v. gecentreerd.",
            useCase: "Muis hoeft niet meer naar het midden te bewegen voor Start.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "TaskbarAl",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Explorer.LaunchToThisPC",
            category: TweakCategory.Explorer,
            name: "Launch File Explorer to This PC",
            description: "Opent File Explorer met de drives in beeld i.p.v. de Home-page.",
            useCase: "Sneller bij dev-werk en algemeen folder-navigeren — Home is vooral promo voor cloud.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "LaunchTo",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 2
                }
            }));

        list.Add(new Tweak(
            id: "Explorer.ClassicContextMenu",
            category: TweakCategory.Explorer,
            name: "Classic context menu (Win10-style)",
            description: "Skipt de 'Show more options'-tussenstap; alle items direct in 1 menu.",
            useCase: "Tweede klik voor 7-Zip / VS Code / Notepad++ entries is irritant.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
                    ValueName = "",  // (Default) value
                    Kind = RegistryValueKind.String,
                    EnabledValue = string.Empty,  // schrijf "" naar (Default) → trigger Win10-mode
                    DisabledValue = null,         // delete value
                    DeleteKeyOnAbsent = true      // bij disable: delete hele InprocServer32 subkey
                }
            }));

        return list;
    }
}

public sealed record TweakApplyResult(int SuccessCount, int FailedCount, bool Cancelled);
