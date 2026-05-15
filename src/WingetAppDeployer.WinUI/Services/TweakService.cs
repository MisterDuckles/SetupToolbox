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
        // Choice-tweak: zoek de eerste choice waarvan ALLE Values matchen de
        // huidige registry-state. Geen match → SelectedChoiceIndex = -1 (custom).
        if (tweak.IsChoice)
        {
            tweak.SelectedChoiceIndex = FindMatchingChoiceIndex(tweak);
            // State-label voor choice-tweaks: Enabled wanneer een choice matcht,
            // anders Unknown (user heeft een waarde die niet bij onze opties hoort).
            return tweak.SelectedChoiceIndex >= 0 ? TweakState.Enabled : TweakState.Unknown;
        }

        // Toggle-tweak: bestaande aggregate-logica over alle Operations.
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

    /// <summary>
    /// Voor een choice-tweak: walk alle Choices en return de index waar ALLE
    /// Values matchen de actuele registry-state. -1 als geen choice 100% matcht.
    /// </summary>
    private static int FindMatchingChoiceIndex(Tweak tweak)
    {
        if (tweak.Choices == null) return -1;
        for (int i = 0; i < tweak.Choices.Count; i++)
        {
            var choice = tweak.Choices[i];
            bool allMatch = true;
            foreach (var v in choice.Values)
            {
                var actual = TryReadValue(new TweakOperation
                {
                    Path = v.Path,
                    ValueName = v.ValueName,
                    Kind = v.Kind
                });
                var actualIsAbsent = !actual.exists;
                var expectedIsAbsent = v.Value == null;

                if (expectedIsAbsent)
                {
                    if (!actualIsAbsent) { allMatch = false; break; }
                }
                else
                {
                    if (actualIsAbsent) { allMatch = false; break; }
                    if (!ValuesEqual(actual.value!, v.Value!)) { allMatch = false; break; }
                }
            }
            if (allMatch) return i;
        }
        return -1;
    }

    private static TweakState MatchOpState(TweakOperation op)
    {
        // 1) Eerst alle AlternateEnabledPaths probereren — als ÉÉN alternate
        // matcht, is de tweak effectief actief (via een ander mechanisme dan
        // wat wij schrijven). Voorbeelden: Win11 Home gebruikers die
        // Start_IrisRecommendations=0 zetten via Settings i.p.v. de
        // HideRecommendedSection policy; HKLM-policy die HKCU overruled.
        // Alternates kunnen alleen VOOR Enabled stemmen, nooit voor Disabled —
        // ze zijn advisory reads, niet authoritative writes.
        foreach (var alt in op.AlternateEnabledPaths)
        {
            var altActual = TryReadValue(new TweakOperation
            {
                Path = alt.Path,
                ValueName = alt.ValueName,
                Kind = alt.Kind
            });
            var altIsAbsent = !altActual.exists;
            var altExpectedAbsent = alt.EnabledValue == null;
            if (altExpectedAbsent && altIsAbsent) return TweakState.Enabled;
            if (!altExpectedAbsent && !altIsAbsent && ValuesEqual(altActual.value!, alt.EnabledValue!))
                return TweakState.Enabled;
        }

        // 2) Primary-path read — onze eigen schrijf-target.
        var actual = TryReadValue(op);
        var actualIsAbsent = !actual.exists;
        var enabledIsAbsent = op.EnabledValue == null;
        var disabledIsAbsent = op.DisabledValue == null;

        if (actualIsAbsent && enabledIsAbsent) return TweakState.Enabled;
        if (actualIsAbsent && disabledIsAbsent) return TweakState.Disabled;
        if (actualIsAbsent) return op.AbsenceMeans;

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

        // 3) Refresh detected state voor de getoggle-de tweaks (fast — registry reads).
        // Doe dit VOOR de broadcast zodat de UI direct na await terug kan vallen
        // op de nieuwe state. State-read leest gewoon wat we net schreven.
        foreach (var t in tweaks)
        {
            try { t.State = DetectStateInternal(t); }
            catch { t.State = TweakState.Unknown; }
        }

        // 4) Broadcast WM_SETTINGCHANGE async / fire-and-forget zodat de UI niet
        // blokkeert op de SendMessageTimeout calls. De toggle update immediate;
        // de shell pickt de nieuwe waardes op in de achtergrond. Plus
        // SHChangeNotify wanneer een tweak file-associations / CLSID-handlers
        // raakt (zoals ClassicContextMenu) — dat is een aparte refresh-flow.
        var touchedClassesOrAssoc = tweaks
            .SelectMany(t => t.Operations)
            .Any(o => o.Path.Contains(@"\Classes\", StringComparison.OrdinalIgnoreCase));
        // SearchHost-restart triggert een full taskbar + Start-rebind: de Win11
        // 25H2 XAML-taskbar reageert lui op WM_SETTINGCHANGE voor TaskbarAl /
        // TaskbarDa / Start_Layout / etc., maar wanneer SearchHost en
        // StartMenuExperienceHost respawnen herlezen ze de hele config. We doen
        // dit voor elke Taskbar- of StartMenu-categorie tweak én voor tweaks die
        // het \Search\ of \SearchSettings\ pad raken. Lichter dan explorer-
        // restart (~50MB host die in <1s respawnt, geen visible flicker).
        var needsShellHostRestart = tweaks.Any(t =>
            t.Category == TweakCategory.Taskbar ||
            t.Category == TweakCategory.StartMenu ||
            t.Operations.Any(o =>
                o.Path.Contains(@"\CurrentVersion\Search", StringComparison.OrdinalIgnoreCase) ||
                o.Path.Contains(@"\Policies\Microsoft\Windows\Explorer", StringComparison.OrdinalIgnoreCase)));
        _ = Task.Run(() =>
        {
            ShellRefresh.NotifySettingsChanged();
            if (touchedClassesOrAssoc) ShellRefresh.NotifyAssociationsChanged();
            if (needsShellHostRestart) ShellRefresh.RestartSearchHost();
        });

        var successCount = results.Count(r => r.Value.ok);
        var failures = results.Where(r => !r.Value.ok)
            .Select(r => $"{ShortenKey(r.Key)}: {r.Value.msg}")
            .ToList();
        return new TweakApplyResult(successCount, results.Count - successCount, cancelled, failures);
    }

    /// <summary>
    /// Verkort de result-key (tweakId::path::valueName) naar iets dat in de UI
    /// past — gebruikt alleen het laatste segment van Path + valueName.
    /// </summary>
    private static string ShortenKey(string key)
    {
        var parts = key.Split("::", 3);
        if (parts.Length < 3) return key;
        var path = parts[1];
        var valueName = parts[2];
        var lastSlash = path.LastIndexOf('\\');
        var pathTail = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        return string.IsNullOrEmpty(valueName) ? pathTail : $"{pathTail}\\{valueName}";
    }

    /// <summary>
    /// Apply één gekozen optie van een multi-state choice-tweak. Schrijft alle
    /// Values uit de geselecteerde TweakChoice naar registry. Volgt dezelfde
    /// local/elevated split + WM_SETTINGCHANGE broadcast als ApplyAsync.
    /// </summary>
    public async Task<TweakApplyResult> ApplyChoiceAsync(Tweak tweak, int choiceIndex)
    {
        if (tweak.Choices == null || choiceIndex < 0 || choiceIndex >= tweak.Choices.Count)
            return new TweakApplyResult(0, 1, false, new[] { "Invalid choice index" });

        var choice = tweak.Choices[choiceIndex];
        var results = new Dictionary<string, (bool ok, string msg)>();
        var localValues = new List<TweakChoiceValue>();
        var elevatedValues = new List<TweakChoiceValue>();
        foreach (var v in choice.Values)
            (v.RequiresElevation ? elevatedValues : localValues).Add(v);

        // 1) Local writes — geen UAC.
        foreach (var v in localValues)
        {
            try
            {
                ApplyChoiceValueLocal(v);
                results[$"{tweak.Id}::{v.Path}::{v.ValueName}"] = (true, "Set");
            }
            catch (Exception ex)
            {
                results[$"{tweak.Id}::{v.Path}::{v.ValueName}"] = (false, ex.Message);
            }
        }

        // 2) Elevated batch — 1 UAC voor de admin-subset. Translate naar
        // TweakOperation (waarbij EnabledValue = de gekozen Value) zodat we
        // dezelfde RunElevatedBatchAsync kunnen hergebruiken.
        var cancelled = false;
        if (elevatedValues.Count > 0)
        {
            var asOps = elevatedValues.Select(v => (
                tweak,
                new TweakOperation
                {
                    Path = v.Path,
                    ValueName = v.ValueName,
                    Kind = v.Kind,
                    EnabledValue = v.Value,
                    DisabledValue = v.Value,
                    RequiresElevation = true
                }
            )).ToList();
            var (batchResults, wasCancelled) = await RunElevatedBatchAsync(asOps, apply: true);
            cancelled = wasCancelled;
            foreach (var kv in batchResults) results[kv.Key] = kv.Value;
        }

        // 3) Re-detect state — vult Tweak.SelectedChoiceIndex met de geactiveerde keuze.
        try { tweak.State = DetectStateInternal(tweak); }
        catch { tweak.State = TweakState.Unknown; }

        // 4) Broadcast + side-effects in achtergrond (zelfde patroon als ApplyAsync).
        var touchedClassesOrAssoc = choice.Values.Any(v => v.Path.Contains(@"\Classes\", StringComparison.OrdinalIgnoreCase));
        // SearchHost-restart triggert een full taskbar + Start-rebind (zie
        // ApplyAsync-comment voor toelichting). Doe het voor Taskbar- en
        // StartMenu-categorie tweaks én voor tweaks die het \Search\ pad of
        // de Explorer-policies raken.
        var needsShellHostRestart = tweak.Category == TweakCategory.Taskbar ||
            tweak.Category == TweakCategory.StartMenu ||
            choice.Values.Any(v =>
                v.Path.Contains(@"\CurrentVersion\Search", StringComparison.OrdinalIgnoreCase) ||
                v.Path.Contains(@"\Policies\Microsoft\Windows\Explorer", StringComparison.OrdinalIgnoreCase));
        _ = Task.Run(() =>
        {
            ShellRefresh.NotifySettingsChanged();
            if (touchedClassesOrAssoc) ShellRefresh.NotifyAssociationsChanged();
            if (needsShellHostRestart) ShellRefresh.RestartSearchHost();
        });

        var successCount = results.Count(r => r.Value.ok);
        var failures = results.Where(r => !r.Value.ok)
            .Select(r => $"{ShortenKey(r.Key)}: {r.Value.msg}")
            .ToList();
        return new TweakApplyResult(successCount, results.Count - successCount, cancelled, failures);
    }

    private static void ApplyChoiceValueLocal(TweakChoiceValue v)
    {
        var (hive, view, subPath) = ParsePath(v.Path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        if (v.Value == null)
        {
            using var key = baseKey.OpenSubKey(subPath, writable: true);
            key?.DeleteValue(v.ValueName, throwOnMissingValue: false);
            return;
        }
        using var writeKey = baseKey.CreateSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot create/open key: {v.Path}");
        writeKey.SetValue(v.ValueName, v.Value, v.Kind);
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
            description: "Lijnt taskbar-iconen links uit i.p.v. gecentreerd. Op Win11 25H2 shell-cached — klik 'Restart Explorer' top-right om live te zien.",
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

        // ── TASKBAR ─────────────────────────────────────────────────
        // Alle taskbar-tweaks zitten onder dezelfde Explorer\Advanced key + zijn
        // HKCU (geen UAC). Geen Explorer-restart geforceerd — na elke apply
        // broadcast TweakService een WM_SETTINGCHANGE, wat voor de meeste
        // taskbar-keys live picked-up wordt (TaskView / Widgets / Copilot etc.).
        // Voor tweaks die het niet live kunnen (Show seconds / Never combine /
        // Classic context menu) is er een manual "Restart Explorer" knop top-right
        // op de TweaksPage als escape hatch.

        // Search-tweak als MULTI-CHOICE i.p.v. toggle — mirror van Windows
        // Settings > Personalization > Taskbar. 4 modes (Hide / Icon only /
        // Search box / Icon and label). Elke mode schrijft 4 keys (legacy
        // \Search\Mode + Cache + nieuwe \Advanced\ShowSearchBox + BingSearchEnabled)
        // zodat Win11 22H2+ taskbar de change live picked-up + SearchHost.exe
        // auto-respawned wordt.
        const string searchPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
        const string advancedPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        TweakChoiceValue[] SearchValuesForMode(int mode) => new TweakChoiceValue[]
        {
            new() { Path = searchPath, ValueName = "SearchboxTaskbarMode", Kind = RegistryValueKind.DWord, Value = mode },
            new() { Path = searchPath, ValueName = "SearchboxTaskbarModeCache", Kind = RegistryValueKind.DWord, Value = mode },
            new() { Path = advancedPath, ValueName = "ShowSearchBox", Kind = RegistryValueKind.DWord, Value = mode == 0 ? 0 : 1 },
            new() { Path = searchPath, ValueName = "BingSearchEnabled", Kind = RegistryValueKind.DWord, Value = mode == 0 ? 0 : 1 },
        };

        list.Add(new Tweak(
            id: "Taskbar.SearchMode",
            category: TweakCategory.Taskbar,
            name: "Taskbar search",
            description: "Hoe Windows-zoeken op de taskbar verschijnt — mirror van Settings > Personalization > Taskbar.",
            useCase: "Kies tussen volledige zoekbalk, alleen icoon, of helemaal verbergen — al naar gelang hoeveel ruimte je op de taskbar wilt geven aan search.",
            restart: RestartRequirement.None,
            choices: new[]
            {
                new TweakChoice("Hide",                  SearchValuesForMode(0)),
                new TweakChoice("Search icon only",      SearchValuesForMode(1)),
                new TweakChoice("Search box",            SearchValuesForMode(2)),
                new TweakChoice("Search icon and label", SearchValuesForMode(3)),
            }));

        list.Add(new Tweak(
            id: "Taskbar.HideTaskView",
            category: TweakCategory.Taskbar,
            name: "Hide Task View button",
            description: "Verbergt het multi-desktop icoon op de taskbar.",
            useCase: "Win+Tab werkt nog steeds — alleen de button verdwijnt.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "ShowTaskViewButton",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.HideWidgets",
            category: TweakCategory.Taskbar,
            name: "Hide Widgets button",
            description: "Verwijdert het Widgets-paneel (weer / nieuws) van de taskbar. Werkt op Win11 23H2-; op 24H2+ blokkeert de nieuwe UCPD service deze HKCU-key silent (Group Policy workaround volgt in latere versie).",
            useCase: "Voorkomt accidentele hovers + reclame in het paneel.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "TaskbarDa",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1,
                    // HKLM AllowNewsAndInterests=0 (Dsh policy of MDM PolicyManager)
                    // verbergt het Widgets-paneel system-wide; user heeft 'm dan
                    // al uit ongeacht TaskbarDa. Detection-only — we schrijven
                    // alleen TaskbarDa zelf.
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh", ValueName = "AllowNewsAndInterests", EnabledValue = 0 },
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Microsoft\PolicyManager\default\NewsAndInterests\AllowNewsAndInterests", ValueName = "value", EnabledValue = 0 }
                    }
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.HideCopilot",
            category: TweakCategory.Taskbar,
            name: "Hide Copilot button (24H2 only)",
            description: "Verbergt het Copilot-AI icoon op de taskbar. Werkt op Win11 24H2; op 25H2+ heeft Microsoft de button vervangen door de pinned Copilot-app.",
            useCase: "Tweak voor users die de Copilot-feature niet gebruiken.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "ShowCopilotButton",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1,
                    // TurnOffWindowsCopilot policy verbergt Copilot system-wide
                    // (vooral Enterprise/Education honored op 24H2+, soms ook
                    // Pro). User heeft 'm dan al uit ongeacht ShowCopilotButton.
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = @"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", ValueName = "TurnOffWindowsCopilot", EnabledValue = 1 },
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", ValueName = "TurnOffWindowsCopilot", EnabledValue = 1 }
                    }
                }
            }));

        // TaskbarMn / Chat-button tweak verwijderd — Microsoft heeft Chat
        // weggehaald uit Win11 23H2+ in de meeste regio's, dus die key bestaat
        // niet meer op moderne installs. Toggle had geen effect.

        list.Add(new Tweak(
            id: "Taskbar.EndTaskRightClick",
            category: TweakCategory.Taskbar,
            name: "Show 'End task' in right-click menu",
            description: "Voegt 'End task' toe aan het taskbar right-click menu zodat je apps direct kunt killen zonder Task Manager.",
            useCase: "Hangende apps razendsnel afsluiten — geen Ctrl+Shift+Esc nodig.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings",
                    ValueName = "TaskbarEndTask",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.NeverCombineButtons",
            category: TweakCategory.Taskbar,
            name: "Never combine taskbar buttons (Win10-style)",
            description: "Toont aparte button per window i.p.v. gegroepeerd per app.",
            useCase: "Snel switchen tussen meerdere browsers / explorer-vensters zonder hover-popup.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "TaskbarGlomLevel",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 2,  // 2 = never combine
                    DisabledValue = 0  // 0 = always combine (Win11 default)
                },
                // Multi-monitor companion key — zonder MMTaskbarGlomLevel
                // werkt de tweak op de primary monitor maar niet op secundaire
                // taskbars. v0.9.2 fix: beide schrijven voor consistente UX.
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "MMTaskbarGlomLevel",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 2,
                    DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.ShowSecondsInClock",
            category: TweakCategory.Taskbar,
            name: "Show seconds in tray clock",
            description: "Toont HH:MM:SS in plaats van HH:MM op de taskbar.",
            useCase: "Handig voor timing, screen-recording, of als je gewoon de seconden wilt zien tikken.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "ShowSecondsInSystemClock",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0
                }
            }));

        // Battery % tweak verwijderd — EstimatedTimeText key werkt niet
        // betrouwbaar op Win11 24H2/25H2. Komt terug zodra we de juiste key
        // hebben gevonden.

        // ── START MENU ──────────────────────────────────────────────
        // Alle Start menu tweaks zitten in HKCU (geen UAC) behalve AllowCortana
        // (HKLM policy). Layout / TrackProgs / TrackDocs delen dezelfde
        // Explorer\Advanced key als Explorer + Taskbar. HideRecommendedSection
        // en DisableSearchBoxSuggestions zijn group-policy keys onder
        // \Software\Policies\Microsoft\Windows\Explorer — werken op Win11 22H2+
        // ook voor Home (de policy-engine is decoupled van de Pro-only UI).
        const string explorerPolicies = @"HKCU\Software\Policies\Microsoft\Windows\Explorer";
        const string searchSettings = @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings";

        // Layout-tweak als MULTI-CHOICE — mirror van Settings > Personalization
        // > Start > Layout. Start_Layout DWORD: 0=Default, 1=More pins,
        // 2=More recommendations. Picked up door StartMenuExperienceHost na
        // SearchHost-restart (zie ApplyAsync onderaan).
        list.Add(new Tweak(
            id: "StartMenu.Layout",
            category: TweakCategory.StartMenu,
            name: "Start menu layout",
            description: "Verdeling tussen pinned apps en aanbevolen items — mirror van Settings > Personalization > Start > Layout.",
            useCase: "Meer pins als je veel apps vastgepind hebt, of meer recommendations als je MRU-files in beeld wil.",
            restart: RestartRequirement.None,
            choices: new[]
            {
                new TweakChoice("Default", new TweakChoiceValue[]
                {
                    new() { Path = explorerAdvanced, ValueName = "Start_Layout", Kind = RegistryValueKind.DWord, Value = 0 }
                }),
                new TweakChoice("More pins", new TweakChoiceValue[]
                {
                    new() { Path = explorerAdvanced, ValueName = "Start_Layout", Kind = RegistryValueKind.DWord, Value = 1 }
                }),
                new TweakChoice("More recommendations", new TweakChoiceValue[]
                {
                    new() { Path = explorerAdvanced, ValueName = "Start_Layout", Kind = RegistryValueKind.DWord, Value = 2 }
                }),
            }));

        list.Add(new Tweak(
            id: "StartMenu.HideRecommendedSection",
            category: TweakCategory.StartMenu,
            name: "Hide Recommended section",
            description: "Verbergt de 'Recommended' sectie onderaan Start. Policy-key — werkt op Win11 22H2+ ook op Home. Detectie pikt ook de HKLM-policy + de Win11 Home Settings-toggle Start_IrisRecommendations + MDM PolicyManager op.",
            useCase: "Strakker Start menu zonder MRU-files en cloud-tips.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerPolicies,
                    ValueName = "HideRecommendedSection",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    // Detectie voor alternatieve mechanismen:
                    //  - HKLM policy (gpedit Pro/Enterprise schrijft hier)
                    //  - MDM/Intune via PolicyManager
                    //  - Start_IrisRecommendations: de Win11 Home 24H2+ Settings-
                    //    toggle "Show recommendations for tips, shortcuts, new
                    //    apps" — als die UIT staat heeft user al een groot deel
                    //    van Recommended onzichtbaar gemaakt zonder onze policy
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer", ValueName = "HideRecommendedSection", EnabledValue = 1 },
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Start", ValueName = "HideRecommendedSection", EnabledValue = 1 },
                        new TweakAlternateSignal { Path = explorerAdvanced, ValueName = "Start_IrisRecommendations", EnabledValue = 0 }
                    }
                }
            }));

        list.Add(new Tweak(
            id: "StartMenu.HideMostUsedApps",
            category: TweakCategory.StartMenu,
            name: "Hide most-used apps",
            description: "Zet 'Show most used apps' uit — Start toont alleen pinned items, geen automatische top-rij. Detectie pikt ook de ShowOrHideMostUsedApps key + de NoInstrumentation policy op.",
            useCase: "Voorkomt dat je Start-layout verspringt op basis van wat je vandaag toevallig opende.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "Start_TrackProgs",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1,
                    // Sommige third-party tweakers schrijven ShowOrHideMostUsedApps=2
                    // i.p.v. Start_TrackProgs=0. NoInstrumentation is de gpedit-
                    // policy die het Start-menu instrumentation volledig uitzet.
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = explorerAdvanced, ValueName = "ShowOrHideMostUsedApps", EnabledValue = 2 },
                        new TweakAlternateSignal { Path = explorerPolicies, ValueName = "NoInstrumentation", EnabledValue = 1 }
                    }
                }
            }));

        list.Add(new Tweak(
            id: "StartMenu.HideRecentFiles",
            category: TweakCategory.StartMenu,
            name: "Hide recently opened items",
            description: "Verbergt MRU-files in de Recommended sectie EN in taskbar/Start jump-lists.",
            useCase: "Privacy: collega's of demos zien geen lijst van bestanden die je net open had.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "Start_TrackDocs",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "StartMenu.DisableBingSearch",
            category: TweakCategory.StartMenu,
            name: "Disable web search in Start",
            description: "Schakelt Bing-resultaten en web-suggesties in het Start zoek-veld uit. Schrijft 3 keys (Explorer-policy + Bing toggle + Cortana consent) voor consistent gedrag op Win11 22H2 t/m 25H2 — verschillende builds honoreren verschillende keys.",
            useCase: "Geen browser-opens meer als je per ongeluk een typo maakt in Start — lokale resultaten only.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerPolicies,
                    ValueName = "DisableSearchBoxSuggestions",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    // Alternates: HKLM-policy van gpedit Pro/Ent + ConnectedSearchUseWeb
                    // (de "Don't search the web or display web results" policy)
                    // + Win11 25H2 nieuwere variant. Detection-only — we schrijven
                    // alleen onze eigen 3 HKCU keys.
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer", ValueName = "DisableSearchBoxSuggestions", EnabledValue = 1 },
                        new TweakAlternateSignal { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search", ValueName = "ConnectedSearchUseWeb", EnabledValue = 0 },
                        new TweakAlternateSignal { Path = @"HKCU\Software\Policies\Microsoft\Windows\Windows Search", ValueName = "ConnectedSearchUseWeb", EnabledValue = 0 }
                    }
                },
                // BingSearchEnabled=0 is een legacy key die op 22H2/23H2 wel
                // honored wordt; op 24H2+ heeft Microsoft 'm grotendeels
                // genegeerd, maar 't schrijven schaadt niet en pikt sub-builds
                // op die 'm nog wel checken.
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search",
                    ValueName = "BingSearchEnabled",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                },
                // CortanaConsent=0 was Win10-era companion. Niet meer
                // dwingend nodig op moderne Win11, maar blokkeert eventuele
                // resterende Cortana-search code paths.
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search",
                    ValueName = "CortanaConsent",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "StartMenu.DisableSearchHighlights",
            category: TweakCategory.StartMenu,
            name: "Disable Search Highlights",
            description: "Verwijdert de roterende 'today in history' / Bing trending content uit het zoekveld. Schrijft 2 keys (Feeds\\DSB legacy + SearchSettings modern).",
            useCase: "Zoekveld wordt sneller en minder vol — geen marketing-prompts in je workflow.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Feeds\DSB",
                    ValueName = "ShowDynamicContent",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = searchSettings,
                    ValueName = "IsDynamicSearchBoxEnabled",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "StartMenu.DisableCortana",
            category: TweakCategory.StartMenu,
            name: "Disable Cortana",
            description: "Disable Cortana via group policy (HKLM). Op Win11 24H2+ is Cortana al naar een aparte Appx geknipt; deze policy zorgt dat de search-hook 'm niet meer triggert.",
            useCase: "Voor users die Cortana volledig willen blokkeren, niet alleen de UI verbergen.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                    ValueName = "AllowCortana",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        return list;
    }
}

public sealed record TweakApplyResult(
    int SuccessCount,
    int FailedCount,
    bool Cancelled,
    IReadOnlyList<string> FailureMessages);
