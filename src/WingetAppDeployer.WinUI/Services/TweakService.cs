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
            // HKU\.DEFAULT = het profiel dat het login-scherm gebruikt vóór er
            // iemand inlogt. Nodig voor o.a. NumLock-at-boot. Writes vereisen
            // admin (.DEFAULT is SYSTEM-owned) — markeer die ops RequiresElevation.
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
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
        // Failure-messages krijgen de tweak-naam erbij zodat een gebatchte
        // ApplyAsync-call (meerdere tweaks tegelijk) de failures nog correct
        // attribueert in de UI. Id→Name map uit de meegegeven tweaks-lijst.
        var idToName = tweaks.ToDictionary(t => t.Id, t => t.Name);
        var failures = results.Where(r => !r.Value.ok)
            .Select(r => $"{FailureLabel(r.Key, idToName)}: {r.Value.msg}")
            .ToList();
        return new TweakApplyResult(successCount, results.Count - successCount, cancelled, failures);
    }

    /// <summary>
    /// Bouwt een leesbaar failure-label uit de result-key (tweakId::path::
    /// valueName): "&lt;TweakNaam&gt; → &lt;keyTail&gt;".
    /// </summary>
    private static string FailureLabel(string key, IReadOnlyDictionary<string, string> idToName)
    {
        var parts = key.Split("::", 3);
        if (parts.Length < 1) return key;
        var tweakName = idToName.TryGetValue(parts[0], out var n) ? n : parts[0];
        return $"{tweakName} → {KeyTail(key)}";
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
        // ApplyChoiceAsync verwerkt 1 tweak — TweaksPage prefixt zelf de
        // tweak-naam, dus hier alleen de key-tail.
        var failures = results.Where(r => !r.Value.ok)
            .Select(r => $"{KeyTail(r.Key)}: {r.Value.msg}")
            .ToList();
        return new TweakApplyResult(successCount, results.Count - successCount, cancelled, failures);
    }

    // keyTail uit een result-key (tweakId::path::valueName) — laatste pad-
    // segment + value-name, zonder tweak-naam.
    private static string KeyTail(string key)
    {
        var parts = key.Split("::", 3);
        if (parts.Length < 3) return key;
        var lastSlash = parts[1].LastIndexOf('\\');
        var pathTail = lastSlash >= 0 ? parts[1].Substring(lastSlash + 1) : parts[1];
        return string.IsNullOrEmpty(parts[2]) ? pathTail : $"{pathTail}\\{parts[2]}";
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

        list.Add(new Tweak(
            id: "Explorer.CompactMode",
            category: TweakCategory.Explorer,
            name: "Compact view in File Explorer",
            description: "Dichtere rij-spacing in File Explorer (UseCompactMode=1) — meer items zichtbaar zonder scrollen, zoals Win10.",
            useCase: "Win11's ruime touch-spacing is voor muis-gebruik vaak te luchtig.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "UseCompactMode",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "Explorer.FullPathInTitleBar",
            category: TweakCategory.Explorer,
            name: "Show full path in File Explorer",
            description: "Toont het volledige pad in de adresbalk en titelbalk van File Explorer i.p.v. alleen de mapnaam (CabinetState\\FullPath=1).",
            useCase: "Directer zien waar je zit in de mappenstructuur — handig bij diep geneste paden.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState",
                    ValueName = "FullPath",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
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
            description: "Verbergt de 'Recommended' sectie onderaan Start. Policy-key — werkt op Win11 22H2+ ook op Home. Vereist UAC: Microsoft heeft de Policies-subtree op 24H2+ gehard zodat alleen Administrators kunnen schrijven. Detectie pikt ook HKLM-policy + Start_IrisRecommendations + MDM PolicyManager op.",
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
                    // Win11 24H2+ ACL: HKCU\Software\Policies\Microsoft\Windows\Explorer
                    // is alleen writable door BUILTIN\Administrators. In-process
                    // user-token heeft ReadKey → access denied. Forceer via
                    // elevated reg.exe batch zodat admin-token de ACL passeert.
                    RequiresElevation = true,
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
            description: "Schakelt Bing-resultaten en web-suggesties in het Start zoek-veld uit. Schrijft 3 keys (Explorer-policy + Bing toggle + Cortana consent) voor consistent gedrag op Win11 22H2 t/m 25H2 — verschillende builds honoreren verschillende keys. Vereist UAC voor de Policies-key write (Win11 24H2+ ACL-hardening).",
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
                    // Win11 24H2+ ACL: HKCU\Software\Policies\Microsoft\Windows\Explorer
                    // is alleen writable door BUILTIN\Administrators. Forceer
                    // elevated batch (zie HideRecommendedSection toelichting).
                    RequiresElevation = true,
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
                // op die 'm nog wel checken. AbsenceMeans bewust Disabled
                // gelaten zodat een fresh Win11 install (alles absent) niet
                // Partial toont — als user nog niets gedaan heeft moet de pill
                // Disabled zijn ("nog niet getweakt"), niet Partial.
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search",
                    ValueName = "BingSearchEnabled",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                },
                // CortanaConsent=0 was Win10-era companion. Niet meer
                // dwingend nodig op moderne Win11 (Cortana removed in 24H2+),
                // maar blokkeert eventuele resterende Cortana-search code
                // paths op legacy installs.
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

        // ── ADS & BLOAT (OFGB-equivalent) ───────────────────────────
        // Inspired by xM4ddy/OFGB ("Oh Frick Go Back") en ChrisTitusTech winutil.
        // Alle HKCU — werkt op Home/Pro/Enterprise zonder UAC. HKLM CloudContent
        // policies (Pro/Edu only, vereisen admin) zijn voor latere iteratie.
        const string contentDeliveryManager = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string userProfileEngagement = @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement";
        const string privacyPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy";
        const string advertisingInfo = @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";

        // Mega-bundle: 18 keys die samen de complete OFGB-set vormen. ÉÉN toggle
        // schakelt alles. State-detect: Partial wanneer user al een subset
        // handmatig had gezet — visueel zien ze dat in de status-pill.
        list.Add(new Tweak(
            id: "Ads.DisableAllSuggestedContent",
            category: TweakCategory.AdsBloat,
            name: "Disable all suggested & sponsored content (OFGB bundle)",
            description: "Schakelt 18 HKCU registry-keys uit voor: lock-screen tips, Start menu suggesties, Settings-ads (3 keys), Welcome experience na updates, 'Finish setting up' popup, auto-install OEM/Store apps, Tailored Experiences, en notification suggestions. Geen UAC.",
            useCase: "Schone Windows-ervaring zonder Microsoft marketing-touchpoints. Mirror van wat xM4ddy/OFGB tool doet maar als 1 toggle.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                // Lock screen
                new TweakOperation { Path = contentDeliveryManager, ValueName = "RotatingLockScreenOverlayEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-338387Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Start menu suggestions
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-338388Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Notification suggestions
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-338389Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Settings app ads (3 known IDs)
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-338393Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-353694Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-353696Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Welcome experience na updates
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SubscribedContent-310093Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Auto-install OEM / Store apps (Candy Crush etc.)
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SilentInstalledAppsEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "PreInstalledAppsEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "OemPreInstalledAppsEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "PreInstalledAppsEverEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Generic content delivery
                new TweakOperation { Path = contentDeliveryManager, ValueName = "FeatureManagementEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SoftLandingEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "ContentDeliveryAllowed", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                new TweakOperation { Path = contentDeliveryManager, ValueName = "SystemPaneSuggestionsEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // "Finish setting up your device" full-screen popup
                new TweakOperation { Path = userProfileEngagement, ValueName = "ScoobeSystemSettingEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
                // Tailored experiences (personalized ads based on diagnostic data)
                new TweakOperation { Path = privacyPath, ValueName = "TailoredExperiencesWithDiagnosticDataEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1 },
            }));

        list.Add(new Tweak(
            id: "Ads.DisableAdvertisingId",
            category: TweakCategory.AdsBloat,
            name: "Disable advertising ID",
            description: "Schakelt de per-user tracking-ID uit die apps gebruiken voor personalized ads.",
            useCase: "Voorkomt dat apps je activity cross-referencen voor advertising profiling.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = advertisingInfo,
                    ValueName = "Enabled",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Ads.HideFileExplorerSyncAds",
            category: TweakCategory.AdsBloat,
            name: "Hide File Explorer OneDrive ads",
            description: "Verwijdert 'sync provider notifications' (OneDrive marketing-banners en upgrade-prompts) bovenin Explorer-vensters.",
            useCase: "Cleaner File Explorer zonder cloud-marketing.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced,
                    ValueName = "ShowSyncProviderNotifications",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Ads.DisableScoobePrompt",
            category: TweakCategory.AdsBloat,
            name: "Disable 'Finish setting up your device' full-screen prompt",
            description: "Schakelt de Scoobe-popup uit die je na major updates probeert te overtuigen om Microsoft Account, OneDrive, Edge, Bing etc. te activeren.",
            useCase: "Geen full-screen marketing meer na elke Windows-upgrade.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = userProfileEngagement,
                    ValueName = "ScoobeSystemSettingEnabled",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Ads.DisableTailoredExperiences",
            category: TweakCategory.AdsBloat,
            name: "Disable Tailored Experiences",
            description: "Voorkomt dat Microsoft je diagnostic data gebruikt voor personalized tips, ads en aanbevelingen.",
            useCase: "Diagnostic data wordt nog steeds verzonden (apart te disablen), maar wordt niet meer voor marketing-personalisatie ingezet.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = privacyPath,
                    ValueName = "TailoredExperiencesWithDiagnosticDataEnabled",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 0,
                    DisabledValue = 1
                }
            }));

        // ── AI / COPILOT (Win11 24H2+) ──────────────────────────────
        // Alle policies in HKLM (machine-scope) → RequiresElevation, batchen
        // in 1 UAC. Research mei 2026 (Microsoft Learn Policy CSP WindowsAI,
        // Manage Recall / Click to Do / Notepad docs). Win+C hotkey-tweak
        // bewust niet opgenomen — TurnOffWindowsCopilot is door Microsoft
        // gedeprecateerd en grotendeels inert op 24H2/25H2. Copilot AppX-
        // removal hoort in de Debloat-tab (AppX uninstall), niet bij de
        // registry-toggles hier.
        const string windowsAiPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI";
        const string windowsAiPolicyHkcu = @"HKCU\SOFTWARE\Policies\Microsoft\Windows\WindowsAI";

        list.Add(new Tweak(
            id: "AiCopilot.DisableRecall",
            category: TweakCategory.AiCopilot,
            name: "Disable Recall",
            description: "Schakelt Recall uit — de Copilot+ PC feature die periodiek screenshots maakt van je activiteit voor AI-zoekbaarheid. Policy DisableAIDataAnalysis. Vereist UAC + sign-out.",
            useCase: "Privacy: geen automatische snapshots van alles wat je op je scherm doet.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = windowsAiPolicy,
                    ValueName = "DisableAIDataAnalysis",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    RequiresElevation = true,
                    // Alternates: HKCU user-policy (gpedit User Config) + de
                    // AllowRecallEnablement=0 policy die Recall als optionele
                    // component helemaal blokkeert — beide tellen als "Recall uit".
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = windowsAiPolicyHkcu, ValueName = "DisableAIDataAnalysis", EnabledValue = 1 },
                        new TweakAlternateSignal { Path = windowsAiPolicy, ValueName = "AllowRecallEnablement", EnabledValue = 0 }
                    }
                }
            }));

        list.Add(new Tweak(
            id: "AiCopilot.DisableClickToDo",
            category: TweakCategory.AiCopilot,
            name: "Disable Click to Do",
            description: "Schakelt Click to Do uit — de 24H2+ feature die AI-acties toevoegt aan het right-click menu voor geselecteerde tekst en afbeeldingen. Policy DisableClickToDo. Vereist UAC + sign-out.",
            useCase: "Strakker context-menu zonder AI-suggesties bij elke selectie.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = windowsAiPolicy,
                    ValueName = "DisableClickToDo",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    RequiresElevation = true,
                    AlternateEnabledPaths = new[]
                    {
                        new TweakAlternateSignal { Path = windowsAiPolicyHkcu, ValueName = "DisableClickToDo", EnabledValue = 1 }
                    }
                }
            }));

        list.Add(new Tweak(
            id: "AiCopilot.DisablePaintAi",
            category: TweakCategory.AiCopilot,
            name: "Disable AI features in Paint",
            description: "Schakelt Cocreator (text-to-image), Generative Fill en Image Creator uit in de Paint-app. Schrijft 3 policy-keys onder CurrentVersion\\Policies\\Paint. Vereist UAC; herstart Paint om effect te zien.",
            useCase: "Voor users die Paint puur als simpele bitmap-editor willen, zonder AI-generatie.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint",
                    ValueName = "DisableCocreator",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    RequiresElevation = true
                },
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint",
                    ValueName = "DisableGenerativeFill",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    RequiresElevation = true
                },
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint",
                    ValueName = "DisableImageCreator",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "AiCopilot.DisableNotepadAi",
            category: TweakCategory.AiCopilot,
            name: "Disable AI features in Notepad",
            description: "Schakelt alle Copilot-features in Notepad uit (AI Rewrite / Summarize). App-level policy DisableAIFeatures onder Policies\\WindowsNotepad. Vereist UAC; herstart Notepad om effect te zien.",
            useCase: "Notepad terug naar de simpele text-editor zonder AI-knoppen.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\WindowsNotepad",
                    ValueName = "DisableAIFeatures",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 1,
                    DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "AiCopilot.DisableGenerativeAiInApps",
            category: TweakCategory.AiCopilot,
            name: "Disable generative AI access for apps",
            description: "Zet de system-brede AppPrivacy policy LetAppsAccessGenerativeAI op 'Force Deny' (2) — apps mogen geen tekst/beeld-generatie via Windows AI-modellen meer doen. Dekt o.a. Image Creator in de Photos-app. Vereist UAC + sign-out.",
            useCase: "Eén schakelaar die generatieve AI-toegang voor alle apps blokkeert.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
                    ValueName = "LetAppsAccessGenerativeAI",
                    Kind = RegistryValueKind.DWord,
                    EnabledValue = 2,   // 2 = Force Deny
                    DisabledValue = 0,  // 0 = user-controlled (Windows default)
                    RequiresElevation = true
                }
            }));

        // ── PRIVACY ─────────────────────────────────────────────────
        // Mix van HKLM-policies (RequiresElevation, batchen in 1 UAC) en
        // HKCU user-keys (geen UAC). Tailored Experiences zit al in de
        // Ads & Tracking-categorie (v0.9.4) — hier niet gedupliceerd.
        const string systemPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";

        list.Add(new Tweak(
            id: "Privacy.DisableActivityHistory",
            category: TweakCategory.Privacy,
            name: "Disable Activity History",
            description: "Schakelt Activity History / Timeline uit — Windows verzamelt geen tijdlijn meer van geopende apps en documenten. Schrijft 3 policy-keys (EnableActivityFeed / PublishUserActivities / UploadUserActivities). Vereist UAC + sign-out.",
            useCase: "Privacy: geen lokale of cloud-gesynchroniseerde activiteitenlijst.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = systemPolicy, ValueName = "EnableActivityFeed",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                },
                new TweakOperation
                {
                    Path = systemPolicy, ValueName = "PublishUserActivities",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                },
                new TweakOperation
                {
                    Path = systemPolicy, ValueName = "UploadUserActivities",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableInkingTypingPersonalization",
            category: TweakCategory.Privacy,
            name: "Disable inking & typing personalization",
            description: "Stopt het verzamelen van je handschrift- en typgegevens voor personalisatie. Schrijft 4 HKCU-keys (RestrictImplicitInkCollection / RestrictImplicitTextCollection / HarvestContacts / AcceptedPrivacyPolicy). Geen UAC.",
            useCase: "Privacy: geen lokale taalmodel-training op basis van wat je typt.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\InputPersonalization", ValueName = "RestrictImplicitInkCollection",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\InputPersonalization", ValueName = "RestrictImplicitTextCollection",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore", ValueName = "HarvestContacts",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Personalization\Settings", ValueName = "AcceptedPrivacyPolicy",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableFeedbackPrompts",
            category: TweakCategory.Privacy,
            name: "Disable Feedback Hub prompts",
            description: "Zet de frequentie waarmee Windows om feedback vraagt op nul (NumberOfSIUFInPeriod=0). Geen UAC.",
            useCase: "Geen 'hoe waarschijnlijk raad je Windows aan'-popups meer.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Siuf\Rules", ValueName = "NumberOfSIUFInPeriod",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableCeip",
            category: TweakCategory.Privacy,
            name: "Disable Customer Experience Improvement Program",
            description: "Schakelt CEIP uit via de SQMClient-policy (CEIPEnable=0). De CEIP scheduled tasks blijven bestaan maar zijn inert zonder actieve CEIP. Vereist UAC.",
            useCase: "Geen CEIP-telemetrie meer over hoe je Windows-features gebruikt.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\SQMClient\Windows", ValueName = "CEIPEnable",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableSuggestedActions",
            category: TweakCategory.Privacy,
            name: "Disable Suggested Actions on clipboard",
            description: "Verwijdert de Win11 22H2+ pop-up die acties suggereert (bellen / agenda) wanneer je een telefoonnummer of datum kopieert. SmartClipboard\\Disabled=1. Geen UAC.",
            useCase: "Geen ongevraagde actie-suggesties bij elke clipboard-copy.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\SmartActionPlatform\SmartClipboard",
                    ValueName = "Disabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableClipboardCloudSync",
            category: TweakCategory.Privacy,
            name: "Disable clipboard cloud sync",
            description: "Blokkeert het synchroniseren van je clipboard naar andere apparaten via je Microsoft-account (AllowCrossDeviceClipboard=0). Lokale clipboard-history blijft werken. Vereist UAC.",
            useCase: "Privacy: gekopieerde data verlaat je PC niet richting Microsoft cloud.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = systemPolicy, ValueName = "AllowCrossDeviceClipboard",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableDiagTrackService",
            category: TweakCategory.Privacy,
            name: "Disable DiagTrack telemetry service",
            description: "Zet de 'Connected User Experiences and Telemetry' service (DiagTrack) op Disabled via de service Start-value (4=disabled, 2=automatic). Vereist UAC; neemt effect na reboot.",
            useCase: "De centrale Windows telemetrie-service draait niet meer op.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", ValueName = "Start",
                    Kind = RegistryValueKind.DWord, EnabledValue = 4, DisabledValue = 2,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableWpbt",
            category: TweakCategory.Privacy,
            name: "Disable WPBT (OEM boot-binary injection)",
            description: "Blokkeert de Windows Platform Binary Table — een mechanisme waarmee firmware/OEM's bij elke boot een binary in Windows kunnen injecteren. DisableWpbtExecution=1. Vereist UAC; neemt effect na reboot.",
            useCase: "Voorkomt dat OEM-firmware ongevraagd software herinstalleert na een schone Windows-install.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", ValueName = "DisableWpbtExecution",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        // ── UI / THEME ──────────────────────────────────────────────
        // Mix HKCU (geen UAC) + HKLM-policy + HKU\.DEFAULT (login-scherm).
        // Eerste categorie met sub-groepen (Tweak.Group) — TweaksPage rendert
        // sub-headers binnen de categorie-Expander. Accent-color override en
        // classic Photo Viewer-restore zijn bewust niet opgenomen: de eerste
        // vereist een color-picker UI (past niet in toggle/multi-choice), de
        // tweede ~15 registry-values + werkt fragiel op Win11 24H2+ (UCPD).
        const string personalize = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string uiThemeColors = "Thema & kleuren";
        const string uiThemeDesktop = "Desktop & vensters";
        const string uiThemeBoot = "Boot & login";
        const string uiThemeSound = "Geluid";

        // System theme als MULTI-CHOICE — mirror van Settings > Personalization
        // > Colors > "Choose your mode". Light = beide keys 1, Dark = beide 0,
        // Custom = dark apps + light Windows-shell (de populaire combinatie).
        TweakChoiceValue[] ThemeValues(int appsLight, int sysLight) => new TweakChoiceValue[]
        {
            new() { Path = personalize, ValueName = "AppsUseLightTheme", Kind = RegistryValueKind.DWord, Value = appsLight },
            new() { Path = personalize, ValueName = "SystemUsesLightTheme", Kind = RegistryValueKind.DWord, Value = sysLight },
        };

        list.Add(new Tweak(
            id: "UiTheme.SystemTheme",
            category: TweakCategory.UiTheme,
            name: "System theme",
            description: "Light / Dark / Custom — mirror van Settings > Personalization > Colors. Custom = donkere apps met lichte taskbar & Start.",
            useCase: "Snel wisselen tussen thema's; Custom geeft de populaire dark-apps + light-shell mix.",
            restart: RestartRequirement.None,
            group: uiThemeColors,
            choices: new[]
            {
                new TweakChoice("Light",  ThemeValues(1, 1)),
                new TweakChoice("Dark",   ThemeValues(0, 0)),
                new TweakChoice("Custom (dark apps, light shell)", ThemeValues(0, 1)),
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableTransparency",
            category: TweakCategory.UiTheme,
            name: "Disable transparency effects",
            description: "Zet de Mica / acrylic doorzichtigheid uit in taskbar, Start en app-vensters (EnableTransparency=0).",
            useCase: "Strakker uiterlijk + minimaal lichtere GPU-belasting op zwakke hardware.",
            restart: RestartRequirement.None,
            group: uiThemeColors,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = personalize, ValueName = "EnableTransparency",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.AccentOnTitleBars",
            category: TweakCategory.UiTheme,
            name: "Accent color on title bars & borders",
            description: "Kleurt actieve venster-titelbalken en -randen met je accentkleur i.p.v. neutraal grijs/wit. DWM\\ColorPrevalence=1.",
            useCase: "Direct zien welk venster focus heeft + een vleugje kleur in de UI.",
            restart: RestartRequirement.None,
            group: uiThemeColors,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\DWM", ValueName = "ColorPrevalence",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.AccentOnStartTaskbar",
            category: TweakCategory.UiTheme,
            name: "Accent color on Start & taskbar",
            description: "Kleurt de taskbar, Start en Action Center met je accentkleur (Themes\\Personalize\\ColorPrevalence=1). Windows toont dit alleen in Dark mode — in Light mode grijst de optie zichzelf uit.",
            useCase: "Meer kleur-identiteit in de shell wanneer je Dark mode draait.",
            restart: RestartRequirement.None,
            group: uiThemeColors,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = personalize, ValueName = "ColorPrevalence",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableAnimations",
            category: TweakCategory.UiTheme,
            name: "Disable window & taskbar animations",
            description: "Zet venster-minimize/maximize animaties (MinAnimate) en taskbar-animaties uit. Snappier UI zonder de volledige 'adjust for best performance' preset.",
            useCase: "UI voelt directer; geen wachttijd op fade/slide-animaties.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            operations: new[]
            {
                new TweakOperation
                {
                    // MinAnimate is een REG_SZ ("0"/"1"), geen DWord.
                    Path = @"HKCU\Control Panel\Desktop\WindowMetrics", ValueName = "MinAnimate",
                    Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "1"
                },
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "TaskbarAnimations",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.ClassicDesktopIcons",
            category: TweakCategory.UiTheme,
            name: "Show This PC / Network / Control Panel on desktop",
            description: "Toont de klassieke desktop-iconen (Deze PC, Netwerk, Configuratiescherm) die Win11 standaard verbergt. Schrijft 3 GUID-keys onder HideDesktopIcons\\NewStartPanel. Druk F5 op het bureaublad om te verversen.",
            useCase: "Snelle toegang tot Deze PC vanaf het bureaublad zonder Explorer te openen.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            operations: new[]
            {
                // 0 = tonen, 1 = verbergen. Win11-default = verborgen (key absent
                // of 1). This PC / Network / Control Panel GUIDs.
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
                    ValueName = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
                    ValueName = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
                    ValueName = "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableSnapAssist",
            category: TweakCategory.UiTheme,
            name: "Disable Snap Assist suggestions",
            description: "Schakelt de flyout uit die andere vensters voorstelt om de lege schermhelft te vullen nadat je een venster snapt. Schrijft EnableSnapAssistFlyout + SnapAssist = 0.",
            useCase: "Snappen zonder dat er een venster-keuzemenu opduikt.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "EnableSnapAssistFlyout",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "SnapAssist",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableAeroShake",
            category: TweakCategory.UiTheme,
            name: "Disable Aero Shake (title bar window shake)",
            description: "Voorkomt dat het schudden van een venster-titelbalk alle andere vensters minimaliseert (DisallowShaking=1).",
            useCase: "Geen vensters die per ongeluk verdwijnen bij een snelle muisbeweging.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "DisallowShaking",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.VerboseLogonMessages",
            category: TweakCategory.UiTheme,
            name: "Verbose logon / shutdown messages",
            description: "Toont gedetailleerde status-meldingen tijdens opstarten, in-/uitloggen en afsluiten i.p.v. de generieke 'Even geduld'. Policy verbosestatus=1. Vereist UAC.",
            useCase: "Zien wélke stap traag is bij een lange boot of shutdown.",
            restart: RestartRequirement.None,
            group: uiThemeBoot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", ValueName = "verbosestatus",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DetailedBsod",
            category: TweakCategory.UiTheme,
            name: "Show detailed Blue Screen info",
            description: "Toont de technische parameters (stop-code adressen) op het blue screen i.p.v. alleen de QR-code. CrashControl\\DisplayParameters=1. Vereist UAC.",
            useCase: "Bij een BSoD direct de technische details kunnen aflezen voor diagnose.",
            restart: RestartRequirement.None,
            group: uiThemeBoot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl", ValueName = "DisplayParameters",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.NumLockAtBoot",
            category: TweakCategory.UiTheme,
            name: "NumLock on at boot",
            description: "Zet NumLock aan op het login-scherm én na inloggen. Schrijft InitialKeyboardIndicators in HKCU (na login) + HKU\\.DEFAULT (login-scherm). De .DEFAULT-key vereist UAC.",
            useCase: "Numpad werkt meteen — geen NumLock-toets zoeken voor je je pincode/wachtwoord typt.",
            restart: RestartRequirement.SignOut,
            group: uiThemeBoot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Control Panel\Keyboard", ValueName = "InitialKeyboardIndicators",
                    Kind = RegistryValueKind.String, EnabledValue = "2", DisabledValue = "0"
                },
                new TweakOperation
                {
                    Path = @"HKU\.DEFAULT\Control Panel\Keyboard", ValueName = "InitialKeyboardIndicators",
                    Kind = RegistryValueKind.String, EnabledValue = "2", DisabledValue = "0",
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableLogonBlur",
            category: TweakCategory.UiTheme,
            name: "Disable login screen background blur",
            description: "Toont de wallpaper scherp op het login-scherm i.p.v. de acrylic-blur. Policy DisableAcrylicBackgroundOnLogon=1. Vereist UAC.",
            useCase: "Volledige wallpaper zichtbaar bij het inloggen.",
            restart: RestartRequirement.None,
            group: uiThemeBoot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = systemPolicy, ValueName = "DisableAcrylicBackgroundOnLogon",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableStartupSound",
            category: TweakCategory.UiTheme,
            name: "Disable Windows startup sound",
            description: "Schakelt het Windows opstart-geluid (de boot-chime) uit. HKLM LogonUI\\BootAnimation\\DisableStartupSound=1. Vereist UAC.",
            useCase: "Stille boot — handig op gedeelde ruimtes of bij vroege ochtend-opstarts.",
            restart: RestartRequirement.None,
            group: uiThemeSound,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation",
                    ValueName = "DisableStartupSound",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        // ── PERFORMANCE ─────────────────────────────────────────────
        // "De schone 9" — research-geverifieerde tweaks met meetbaar of
        // duidelijk effect en veilige revert; geen placebo. powercfg-
        // afhankelijke tweaks (Ultimate Performance power plan, hibernation
        // met hiberfil.sys-reclaim) zijn geparkeerd — die passen niet in het
        // registry-only model. Meeste zijn HKLM → 1 UAC voor de hele batch.
        // DisabledValue=null bij tweaks waar de Windows-default "value absent"
        // is — revert deletet de value dan i.p.v. een waarde te forceren.

        list.Add(new Tweak(
            id: "Performance.DisableFastStartup",
            category: TweakCategory.Performance,
            name: "Disable Fast Startup",
            description: "Schakelt Fast Startup uit (HiberbootEnabled=0) — de hybride-shutdown die soms driver- en update-problemen geeft. Een echte volledige shutdown elke keer. Vereist UAC; effect na reboot.",
            useCase: "Voorspelbaar afsluiten/opstarten; lost rare post-update glitches op.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                    ValueName = "HiberbootEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisablePowerThrottling",
            category: TweakCategory.Performance,
            name: "Disable power throttling",
            description: "Zet Windows power throttling uit (PowerThrottlingOff=1) — Windows knijpt anders achtergrond-apps af om energie te besparen. Vereist UAC; effect na reboot. Let op: kan accuverbruik verhogen op laptops.",
            useCase: "Achtergrondtaken (compiles, conversies) draaien op volle snelheid.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
                    ValueName = "PowerThrottlingOff",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableStorageSense",
            category: TweakCategory.Performance,
            name: "Disable Storage Sense",
            description: "Schakelt Storage Sense uit via de policy (AllowStorageSenseGlobal=0) — Windows ruimt dan niet meer automatisch temp-files / Recycle Bin op. Vereist UAC.",
            useCase: "Volledige controle over wanneer er opgeruimd wordt; geen verrassingen.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\StorageSense",
                    ValueName = "AllowStorageSenseGlobal",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableBackgroundApps",
            category: TweakCategory.Performance,
            name: "Disable background apps (UWP/Store)",
            description: "Blokkeert dat UWP/Store-apps in de achtergrond draaien (AppPrivacy-policy LetAppsAccessBackground op 'Force Deny'). Raakt alleen Store-apps, geen Win32-processen. Vereist UAC + sign-out.",
            useCase: "Minder achtergrond-CPU/netwerk van Store-apps die je niet actief gebruikt.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
                    ValueName = "LetAppsRunInBackground",
                    Kind = RegistryValueKind.DWord, EnabledValue = 2, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.LongPathSupport",
            category: TweakCategory.Performance,
            name: "Enable long path support (>260 tekens)",
            description: "Heft de oude MAX_PATH limiet van 260 tekens op (LongPathsEnabled=1) — handig voor dev-tools, node_modules, diep geneste mappen. Vereist UAC; effect na reboot. Alleen manifested Win32-apps + Store-apps honoreren 't.",
            useCase: "Geen 'path too long'-fouten meer bij dev-werk.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
                    ValueName = "LongPathsEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.PreferIPv4",
            category: TweakCategory.Performance,
            name: "Prefer IPv4 over IPv6",
            description: "Geeft IPv4 voorrang boven IPv6 (DisabledComponents=0x20) — IPv6 blijft volledig functioneel, maar wordt niet meer eerst geprobeerd. Vereist UAC; effect na reboot. Bewust 0x20 — andere waardes kunnen netwerk-services breken.",
            useCase: "Lost trage DNS / connect-vertraging op netwerken zonder goede IPv6-route op.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
                    ValueName = "DisabledComponents",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0x20, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableMpo",
            category: TweakCategory.Performance,
            name: "Disable Multiplane Overlay (MPO)",
            description: "Zet MPO uit (Dwm\\OverlayTestMode=5) — een veelgebruikte fix voor scherm-flikkering / tearing / zwart knipperen op sommige AMD/NVIDIA-setups. Undocumented debug-waarde, maar werkt en is netjes terug te draaien. Vereist UAC; effect na reboot.",
            useCase: "Lost flikkerende of knipperende beeldschermen op die met MPO samenhangen.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm",
                    ValueName = "OverlayTestMode",
                    Kind = RegistryValueKind.DWord, EnabledValue = 5, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.RemoveStartupDelay",
            category: TweakCategory.Performance,
            name: "Remove startup-app delay",
            description: "Verwijdert de kunstmatige ~10s vertraging voordat autostart-apps (Run-key) laden na inloggen (StartupDelayInMSec=0). Geen UAC; effect na sign-out.",
            useCase: "Autostart-apps zijn meteen beschikbaar i.p.v. 10 seconden na de desktop.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
                    ValueName = "StartupDelayInMSec",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableNtfsLastAccess",
            category: TweakCategory.Performance,
            name: "Disable NTFS last-access timestamps",
            description: "Forceert user-managed mode waarin NTFS niet bij elke read een 'last access'-timestamp bijwerkt (NtfsDisableLastAccessUpdate=1). Bescheiden I/O-besparing — modern Windows stelt deze updates standaard al ~1u uit. Vereist UAC; effect na reboot.",
            useCase: "Iets minder schrijf-I/O bij intensief lezen van veel bestanden.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
                    ValueName = "NtfsDisableLastAccessUpdate",
                    // EnabledValue=1 (user-managed, disabled). DisabledValue=null:
                    // revert deletet de value zodat Windows weer system-managed
                    // mode pakt (de moderne default ~0x80000002).
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        // ── CONTEXT MENU ────────────────────────────────────────────
        // Twee sub-groepen: items verwijderen (blokkeren) en items toevoegen.
        // Win11-realiteit: custom shell\-verbs verschijnen alleen onder "Toon
        // meer opties" TENZIJ de Classic context menu tweak (Explorer-cat) aan
        // staat — dan staan ze direct in het menu. CLSID's research-geverifieerd
        // (mei 2026). Alles HKCU → geen UAC. ExplorerRestart zodat het menu
        // de wijziging direct oppikt.
        const string cmRemove = "Items verwijderen";
        const string cmAdd = "Items toevoegen";

        // De Shell Extensions Blocked-lijst: een String-value met de CLSID als
        // naam (lege data) onderdrukt die shell-extension overal. Werkt voor
        // klassieke IContextMenu-handlers én IExplorerCommand-handlers.
        const string blockedKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        Tweak BlockedTweak(string id, string name, string description, string useCase, string clsid) =>
            new Tweak(
                id: id,
                category: TweakCategory.ContextMenu,
                name: name,
                description: description,
                restart: RestartRequirement.ExplorerRestart,
                useCase: useCase,
                group: cmRemove,
                operations: new[]
                {
                    new TweakOperation
                    {
                        Path = blockedKey,
                        ValueName = clsid,
                        Kind = RegistryValueKind.String,
                        EnabledValue = string.Empty,  // value bestaat (lege data) = geblokkeerd
                        DisabledValue = null          // revert: value verwijderen = deblokkeerd
                    }
                });

        list.Add(BlockedTweak("ContextMenu.RemoveEditWithPhotos",
            "Remove 'Edit with Photos'",
            "Verwijdert de 'Edit with Photos' / 'Bewerken met Foto's' optie uit het rechtermuisknop-menu van afbeeldingen. Raakt het hoofdmenu. Blocked-lijst CLSID.",
            "Schoner afbeeldings-context-menu voor wie Foto's niet als editor gebruikt.",
            "{BFE0E2A4-C70C-4AD7-AC3D-10D1ECEBB5B4}"));

        list.Add(BlockedTweak("ContextMenu.RemoveEditWithPaint",
            "Remove 'Edit with Paint'",
            "Verwijdert de 'Edit with Paint' / 'Bewerken met Paint' optie uit het context-menu van afbeeldingen. Raakt het hoofdmenu.",
            "Geen Paint-entry meer bij elk plaatje.",
            "{2430F218-B743-4FD6-97BF-5C76541B4AE9}"));

        list.Add(BlockedTweak("ContextMenu.RemoveScanWithDefender",
            "Remove 'Scan with Microsoft Defender'",
            "Verbergt de 'Scan with Microsoft Defender' menu-optie. Defender blijft gewoon scannen — alleen de menu-entry verdwijnt. Blocked-lijst i.p.v. key-delete (Defender zou een verwijderde key herstellen).",
            "Minder rommel in het context-menu; on-demand scannen kan nog via de Defender-app.",
            "{09A47860-11B0-4DA5-AFA5-26D86198A780}"));

        list.Add(BlockedTweak("ContextMenu.RemoveRestorePreviousVersions",
            "Remove 'Restore previous versions'",
            "Verwijdert de 'Restore previous versions' / 'Vorige versies terugzetten' optie. Shadow-copies / File History blijven werken — alleen de menu-entry verdwijnt.",
            "Opschonen voor wie geen Volume Shadow Copies gebruikt.",
            "{596AB062-B4D2-4215-9F74-E9109B0A8153}"));

        list.Add(BlockedTweak("ContextMenu.RemoveCastToDevice",
            "Remove 'Cast to Device'",
            "Verwijdert de 'Cast to Device' / 'Naar apparaat casten' (Play To) optie uit het media-context-menu.",
            "Schoner menu voor wie niet naar DLNA/Miracast-apparaten cast.",
            "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}"));

        list.Add(BlockedTweak("ContextMenu.RemoveIncludeInLibrary",
            "Remove 'Include in library'",
            "Verwijdert de 'Include in library' / 'Opnemen in bibliotheek' submenu-optie van mappen.",
            "Opschonen voor wie de Windows-bibliotheken niet gebruikt.",
            "{3dad6c5d-2167-4cae-9914-f99e41c12cfa}"));

        list.Add(BlockedTweak("ContextMenu.RemoveShare",
            "Remove 'Give access to' / sharing",
            "Verwijdert de 'Give access to' / 'Toegang verlenen aan' sharing-optie. Bestaande netwerk-shares blijven gewoon werken — alleen de menu-entry verdwijnt.",
            "Schoner menu voor wie geen lokale netwerk-shares aanmaakt via het context-menu.",
            "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}"));

        // ── Items toevoegen ──
        // takeown/icacls + wt.exe verbs. Verb-key krijgt DeleteKeyOnAbsent zodat
        // revert de hele verb-subtree opruimt. HKCU\Software\Classes = per-user.

        const string takeOwnFileCmd =
            "cmd.exe /c takeown /f \"%1\" && icacls \"%1\" /grant administrators:F";
        const string takeOwnDirCmd =
            "cmd.exe /c takeown /f \"%1\" /r /d y && icacls \"%1\" /grant administrators:F /t";

        list.Add(new Tweak(
            id: "ContextMenu.AddTakeOwnership",
            category: TweakCategory.ContextMenu,
            name: "Add 'Take Ownership'",
            description: "Voegt een 'Take Ownership' optie toe aan het context-menu van bestanden en mappen — neemt in één klik eigenaarschap (takeown) + volledige rechten (icacls). De verb heet 'runas' dus 't auto-elevate't met UAC. LET OP: gebruik dit NOOIT op systeemmappen (C:\\Windows, Program Files) — dat kan Windows-updates / TrustedInstaller-bescherming breken.",
            useCase: "Snel een vastzittend bestand/map ontgrendelen dat 'Access denied' geeft.",
            restart: RestartRequirement.ExplorerRestart,
            group: cmAdd,
            operations: new[]
            {
                // Bestanden — *\shell\runas
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\*\shell\runas", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = "Take Ownership",
                    DisabledValue = null, DeleteKeyOnAbsent = true
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\*\shell\runas", ValueName = "HasLUAShield",
                    Kind = RegistryValueKind.String, EnabledValue = string.Empty, DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\*\shell\runas", ValueName = "NoWorkingDirectory",
                    Kind = RegistryValueKind.String, EnabledValue = string.Empty, DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\*\shell\runas\command", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = takeOwnFileCmd, DisabledValue = null
                },
                // Mappen — Directory\shell\runas
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\runas", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = "Take Ownership",
                    DisabledValue = null, DeleteKeyOnAbsent = true
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\runas", ValueName = "HasLUAShield",
                    Kind = RegistryValueKind.String, EnabledValue = string.Empty, DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\runas", ValueName = "NoWorkingDirectory",
                    Kind = RegistryValueKind.String, EnabledValue = string.Empty, DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\runas\command", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = takeOwnDirCmd, DisabledValue = null
                }
            }));

        list.Add(new Tweak(
            id: "ContextMenu.AddMoveCopyTo",
            category: TweakCategory.ContextMenu,
            name: "Add 'Move to folder' / 'Copy to folder'",
            description: "Voegt de klassieke 'Move to folder...' en 'Copy to folder...' opties toe aan het context-menu van bestanden en mappen — die openen een map-kiezer. Gebruikt de ingebouwde Windows shell-handlers.",
            useCase: "Bestanden verplaatsen/kopiëren naar een doelmap zonder twee Explorer-vensters.",
            restart: RestartRequirement.ExplorerRestart,
            group: cmAdd,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\Move To",
                    ValueName = "", Kind = RegistryValueKind.String,
                    EnabledValue = "{C2FBB631-2971-11D1-A18C-00C04FD75D13}",
                    DisabledValue = null, DeleteKeyOnAbsent = true
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\Copy To",
                    ValueName = "", Kind = RegistryValueKind.String,
                    EnabledValue = "{C2FBB630-2971-11D1-A18C-00C04FD75D13}",
                    DisabledValue = null, DeleteKeyOnAbsent = true
                }
            }));

        // wt.exe elevated via PowerShell Start-Process -Verb RunAs. -ArgumentList
        // als comma-array zodat een pad met spaties als 1 argument doorkomt.
        const string terminalAdminCmd =
            "powershell.exe -NoProfile -WindowStyle Hidden -Command \"Start-Process wt.exe -ArgumentList '-d','%V' -Verb RunAs\"";

        list.Add(new Tweak(
            id: "ContextMenu.AddOpenTerminalAdmin",
            category: TweakCategory.ContextMenu,
            name: "Add 'Open Terminal as Admin'",
            description: "Voegt 'Open Terminal as Admin' toe aan het context-menu van mappen en de mapachtergrond. Opent Windows Terminal als administrator in de huidige map. (Win11 heeft al een gewone 'Open in Terminal' in het hoofdmenu — dit is de verhoogde variant.)",
            useCase: "Direct een admin-terminal in de juiste map zonder los te navigeren.",
            restart: RestartRequirement.ExplorerRestart,
            group: cmAdd,
            operations: new[]
            {
                // Mapachtergrond — Directory\Background\shell
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\Background\shell\OpenTerminalAdmin", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = "Open Terminal as Admin",
                    DisabledValue = null, DeleteKeyOnAbsent = true
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\Background\shell\OpenTerminalAdmin", ValueName = "HasLUAShield",
                    Kind = RegistryValueKind.String, EnabledValue = string.Empty, DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\Background\shell\OpenTerminalAdmin", ValueName = "Icon",
                    Kind = RegistryValueKind.String, EnabledValue = "wt.exe", DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\Background\shell\OpenTerminalAdmin\command", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = terminalAdminCmd, DisabledValue = null
                },
                // Op een map zelf — Directory\shell
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\OpenTerminalAdmin", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = "Open Terminal as Admin",
                    DisabledValue = null, DeleteKeyOnAbsent = true
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\OpenTerminalAdmin", ValueName = "HasLUAShield",
                    Kind = RegistryValueKind.String, EnabledValue = string.Empty, DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\OpenTerminalAdmin", ValueName = "Icon",
                    Kind = RegistryValueKind.String, EnabledValue = "wt.exe", DisabledValue = null
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Classes\Directory\shell\OpenTerminalAdmin\command", ValueName = "",
                    Kind = RegistryValueKind.String, EnabledValue = terminalAdminCmd, DisabledValue = null
                }
            }));

        // ── NOTIFICATIONS & LOCK SCREEN ─────────────────────────────
        // Drie sub-groepen: meldingen, notificatiecentrum, vergrendelscherm.
        // Research mei 2026 (web-geverifieerd, Win11 24H2/25H2). De twee HKLM-
        // policy-tweaks (NoLockScreen + DisableLogonBackgroundImage) batchen
        // samen in 1 UAC. Schets-item "Suggest ways to finish setup" is bewust
        // NIET hier — dat zit al in v0.9.4 (Ads.DisableScoobePrompt + OFGB).
        const string nlNotifGroup = "Meldingen";
        const string nlCenterGroup = "Notificatiecentrum";
        const string nlLockGroup = "Vergrendelscherm";

        const string pushNotifications =
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        const string notificationSettings =
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        // ── Meldingen ──
        list.Add(new Tweak(
            id: "NotifLock.DisableAllNotifications",
            category: TweakCategory.NotificationsLock,
            name: "Disable all notifications",
            description: "Schakelt alle toast-meldingen van apps en het systeem volledig uit — de master-toggle bovenaan Instellingen > Systeem > Meldingen. Meldingen verschijnen niet meer en worden ook niet meer in het notificatiecentrum verzameld.",
            useCase: "Volledig meldingsvrij werken; geen onderbrekingen van apps of Windows.",
            restart: RestartRequirement.SignOut,
            group: nlNotifGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = pushNotifications, ValueName = "ToastEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "NotifLock.DisableNotificationSounds",
            category: TweakCategory.NotificationsLock,
            name: "Disable notification sounds",
            description: "Dempt het geluid dat bij elke melding speelt. De meldingen zelf blijven visueel verschijnen — alleen het belletje verdwijnt.",
            useCase: "Meldingen wel zien maar niet horen — rustiger zonder alle meldingen helemaal uit te zetten.",
            restart: RestartRequirement.None,
            group: nlNotifGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = notificationSettings, ValueName = "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        // MessageDuration: hoe lang een toast op het scherm blijft. Windows-
        // default is 5s en de value is dan typisch ABSENT — daarom Value=null
        // voor de default-choice (detecteert absent + revert deletet). Windows
        // klemt buiten 5..300, dus alle choices binnen die range.
        TweakChoiceValue[] DurationValue(int? seconds) => new TweakChoiceValue[]
        {
            new()
            {
                Path = @"HKCU\Control Panel\Accessibility", ValueName = "MessageDuration",
                Kind = RegistryValueKind.DWord, Value = seconds
            }
        };

        list.Add(new Tweak(
            id: "NotifLock.NotificationDisplayTime",
            category: TweakCategory.NotificationsLock,
            name: "Notification display time",
            description: "Hoe lang een melding zichtbaar blijft voor hij naar het notificatiecentrum verdwijnt — mirror van Instellingen > Toegankelijkheid > Visuele effecten. Standaard 5 seconden.",
            useCase: "Langere weergavetijd geeft je meer tijd om op een melding te reageren.",
            restart: RestartRequirement.SignOut,
            group: nlNotifGroup,
            choices: new[]
            {
                new TweakChoice("5 seconden (standaard)", DurationValue(null)),
                new TweakChoice("7 seconden",             DurationValue(7)),
                new TweakChoice("15 seconden",            DurationValue(15)),
                new TweakChoice("30 seconden",            DurationValue(30)),
                new TweakChoice("1 minuut",               DurationValue(60)),
                new TweakChoice("5 minuten",              DurationValue(300)),
            }));

        // ── Notificatiecentrum ──
        // Op Win11 is het notificatiecentrum samengevoegd met de kalender-
        // flyout: DisableNotificationCenter=1 haalt de bel-icoon weg én zorgt
        // dat klikken op de klok geen kalender/meldingen-paneel meer opent
        // (Win10-stijl tray-klok). Eén tweak dekt dus beide schets-items.
        list.Add(new Tweak(
            id: "NotifLock.DisableNotificationCenter",
            category: TweakCategory.NotificationsLock,
            name: "Disable Notification Center & Calendar flyout",
            description: "Schakelt het notificatiecentrum volledig uit: de bel-icoon rechts op de taskbar verdwijnt en klikken op de klok opent geen meldingen-/kalender-paneel meer (Win10-stijl tray-klok). LET OP: dit is een grof middel — je verliest hiermee ook de kalender-popup bij de klok.",
            useCase: "Voor wie het notificatiecentrum nooit gebruikt en een minimale taskbar wil.",
            restart: RestartRequirement.ExplorerRestart,
            group: nlCenterGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Policies\Microsoft\Windows\Explorer",
                    ValueName = "DisableNotificationCenter",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null
                }
            }));

        // ── Vergrendelscherm ──
        list.Add(new Tweak(
            id: "NotifLock.DisableLockScreen",
            category: TweakCategory.NotificationsLock,
            name: "Disable lock screen",
            description: "Slaat het vergrendelscherm (de achtergrond-foto met klok die je wegklikt) over — je komt direct op het inlogscherm. LET OP: als je systeem 'Veilig aanmelden' (Ctrl+Alt+Del verplicht) gebruikt kan het vergrendelscherm niet 100% overgeslagen worden.",
            useCase: "Eén klik / toetsaanslag minder voor je bij het wachtwoordveld bent.",
            restart: RestartRequirement.SignOut,
            group: nlLockGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization",
                    ValueName = "NoLockScreen",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "NotifLock.DisableLockScreenNotifications",
            category: TweakCategory.NotificationsLock,
            name: "Disable notifications on lock screen",
            description: "Verbergt app-meldingen op het vergrendelscherm zodat voorbijgangers geen berichten / herinneringen kunnen meelezen. 2-op: zet zowel de PushNotifications-toggle als de above-lock toast-policy uit.",
            useCase: "Privacy — meldingsinhoud blijft achter het wachtwoord.",
            restart: RestartRequirement.SignOut,
            group: nlLockGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = pushNotifications, ValueName = "LockScreenToastEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = notificationSettings, ValueName = "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "NotifLock.DisableLogonBackgroundImage",
            category: TweakCategory.NotificationsLock,
            name: "Disable lock screen background on sign-in",
            description: "Vervangt de vergrendelscherm-foto op het inlogscherm door een effen accent-kleur. Het vergrendelscherm zelf (vóór het inlogscherm) houdt z'n foto — alleen het wachtwoord-scherm wordt effen.",
            useCase: "Sneller, rustiger inlogscherm zonder zware achtergrond-afbeelding.",
            restart: RestartRequirement.None,
            group: nlLockGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                    ValueName = "DisableLogonBackgroundImage",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        // ── UPDATES ─────────────────────────────────────────────────
        // Twee sub-groepen: updates & herstart, drivers & netwerk. Research
        // mei 2026 (5 web-passes, Win11 24H2/25H2). Alleen de betrouwbaar-
        // werkende set — Microsoft faseert update-beleid actief uit, dus
        // bewust GEEN defer-feature/quality-updates (UI verwijderd op verse
        // 24H2-installs) en GEEN service-disable (herstelt zichzelf). Alles
        // HKLM → batcht in 1 UAC. Side-effect: het Windows Update-scherm
        // toont "Some settings are managed by your organization" — cosmetisch.
        const string upRestartGroup = "Updates & herstart";
        const string upDriverGroup = "Drivers & netwerk";

        // WindowsUpdate-policy keys + de UX\Settings backing-store (= waar de
        // Settings-app zelf naar schrijft; geen policy-key).
        const string auPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        const string wuPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        const string uxSettings = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

        // ── Updates & herstart ──
        list.Add(new Tweak(
            id: "Updates.NoAutoRebootWhileLoggedOn",
            category: TweakCategory.Updates,
            name: "Disable auto-restart while logged on",
            description: "Voorkomt dat Windows Update je PC automatisch herstart terwijl je ingelogd bent. Updates worden nog gewoon op de achtergrond gedownload en geïnstalleerd — alleen de geforceerde herstart blijft achterwege tot jij 'm zelf doet.",
            useCase: "Nooit meer onverwacht werk kwijt door een herstart-tijdens-gebruik.",
            restart: RestartRequirement.None,
            group: upRestartGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = auPolicy, ValueName = "NoAutoRebootWithLoggedOnUsers",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Updates.DisableRestartNotifications",
            category: TweakCategory.Updates,
            name: "Disable update restart notifications",
            description: "Onderdrukt de 'Herstart nodig om updates te voltooien'-meldingen die Windows herhaaldelijk toont. De updates zelf worden niet beïnvloed — alleen de nag-meldingen verdwijnen.",
            useCase: "Rustiger werken zonder herhaalde herstart-herinneringen.",
            restart: RestartRequirement.None,
            group: upRestartGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = uxSettings, ValueName = "RestartNotificationsAllowed2",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Updates.DisableContinuousInnovation",
            category: TweakCategory.Updates,
            name: "Disable 'Get latest updates as soon as available'",
            description: "Schakelt de opt-in uit die je PC voorrang geeft op de nieuwste non-security updates en feature-verbeteringen — mirror van de toggle in Instellingen > Windows Update. Je krijgt updates dan op het reguliere schema i.p.v. zo vroeg mogelijk.",
            useCase: "Wachten tot updates breder uitgerold en stabieler zijn voor je ze krijgt.",
            restart: RestartRequirement.None,
            group: upRestartGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = uxSettings, ValueName = "IsContinuousInnovationOptedIn",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                }
            }));

        // Active hours: het tijdvenster waarin Windows niet automatisch
        // herstart. SmartActiveHoursState=1 = Windows kiest zelf; =0 = handmatig
        // venster via ActiveHoursStart/End (uren 0-23, max 18u bereik). De
        // "Automatisch" choice = alle 3 values absent (= schone Windows-default).
        TweakChoiceValue[] ActiveHoursValues(int? smart, int? start, int? end) => new TweakChoiceValue[]
        {
            new() { Path = uxSettings, ValueName = "SmartActiveHoursState", Kind = RegistryValueKind.DWord, Value = smart, RequiresElevation = true },
            new() { Path = uxSettings, ValueName = "ActiveHoursStart", Kind = RegistryValueKind.DWord, Value = start, RequiresElevation = true },
            new() { Path = uxSettings, ValueName = "ActiveHoursEnd", Kind = RegistryValueKind.DWord, Value = end, RequiresElevation = true },
        };

        list.Add(new Tweak(
            id: "Updates.ActiveHours",
            category: TweakCategory.Updates,
            name: "Active hours",
            description: "Het tijdvenster waarin Windows nooit automatisch herstart voor updates — mirror van Instellingen > Windows Update > Geavanceerde opties. 'Automatisch' laat Windows het venster zelf bepalen op basis van je activiteit.",
            useCase: "Een vast venster instellen zodat herstarts altijd buiten je werkuren vallen.",
            restart: RestartRequirement.None,
            group: upRestartGroup,
            choices: new[]
            {
                new TweakChoice("Automatisch (standaard)", ActiveHoursValues(null, null, null)),
                new TweakChoice("08:00 – 23:00",           ActiveHoursValues(0, 8, 23)),
                new TweakChoice("06:00 – middernacht",     ActiveHoursValues(0, 6, 0)),
            }));

        // ── Drivers & netwerk ──
        list.Add(new Tweak(
            id: "Updates.DisableDriverUpdates",
            category: TweakCategory.Updates,
            name: "Disable driver updates via Windows Update",
            description: "Sluit de 3 paden af waarlangs Windows Update stuurprogramma's pusht: drivers in quality-updates, automatische driver-zoekopdracht bij nieuwe hardware, en het ophalen van device-metadata. Windows overschrijft je eigen OEM- / GPU-vendor-drivers dan niet meer.",
            useCase: "Zelf de controle houden over driver-versies — geen verrassende WU-drivers die een werkende driver vervangen.",
            restart: RestartRequirement.None,
            group: upDriverGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = wuPolicy, ValueName = "ExcludeWUDriversInQualityUpdate",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                },
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching",
                    ValueName = "SearchOrderConfig",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1,
                    RequiresElevation = true
                },
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Device Metadata",
                    ValueName = "PreventDeviceMetadataFromNetwork",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Updates.DisableDeliveryOptimization",
            category: TweakCategory.Updates,
            name: "Disable Delivery Optimization (P2P)",
            description: "Zet de peer-to-peer deling van updates uit (DODownloadMode 0): je PC downloadt updates niet meer van andere PC's en — belangrijker — uploadt ze ook niet meer naar vreemde PC's over het internet. Updates komen rechtstreeks van Microsoft.",
            useCase: "Bandbreedte besparen en geen update-uploadverkeer naar onbekende PC's.",
            restart: RestartRequirement.None,
            group: upDriverGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                    ValueName = "DODownloadMode",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        // ── PERFORMANCE (gaming-gerelateerd) ────────────────────────
        // Game DVR / Game Bar / Xbox-services horen functioneel in de
        // Performance-categorie: het zijn stuk voor stuk achtergrond-overhead
        // reducties (continue capture, overlay-hooks, 4 services). Research
        // mei 2026 (3 web-passes, Win11 24H2/25H2). Game Mode bewust NIET
        // uitgezet — dat is op moderne Windows juist nuttig.
        list.Add(new Tweak(
            id: "Performance.DisableGameDVR",
            category: TweakCategory.Performance,
            name: "Disable background game recording (Game DVR)",
            description: "Schakelt de continue achtergrond-opname uit waarmee Windows steeds de laatste minuten gameplay vasthoudt ('Leg vast wat er net gebeurde'). 2-op HKCU. Handmatig opnemen via de Game Bar blijft mogelijk zolang die niet ook is uitgezet.",
            useCase: "Verwijdert de constante achtergrond-capture overhead — de meest-genoemde gaming-performance tweak.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\System\GameConfigStore", ValueName = "GameDVR_Enabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR",
                    ValueName = "AppCaptureEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableXboxGameBar",
            category: TweakCategory.Performance,
            name: "Disable Xbox Game Bar overlay",
            description: "Schakelt de Xbox Game Bar-overlay uit — de Win+G-balk verschijnt niet meer en de Game Bar opstart-tips worden onderdrukt. 2-op HKCU.",
            useCase: "Geen Game Bar-overlay meer voor wie nooit captures, widgets of de performance-meter gebruikt.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\GameBar", ValueName = "UseNexusForGameBarEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                },
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\GameBar", ValueName = "ShowStartupPanel",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        // Xbox-services: Start=4 (Disabled); default is 3 (Manual, trigger-
        // started). Zelfde patroon als de DiagTrack-service-disable (v0.9.7).
        TweakOperation XboxService(string name) => new TweakOperation
        {
            Path = $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}", ValueName = "Start",
            Kind = RegistryValueKind.DWord, EnabledValue = 4, DisabledValue = 3,
            RequiresElevation = true
        };

        list.Add(new Tweak(
            id: "Performance.DisableXboxServices",
            category: TweakCategory.Performance,
            name: "Disable Xbox services",
            description: "Schakelt de vier Xbox-achtergrondservices uit (XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc). LET OP: dit breekt de Xbox-app, Game Pass en Xbox Live-aanmelding. XboxGipSvc beheert Xbox-controller-accessoires — controllers blijven als gewone gamepad werken, maar firmware-updates en knop-remapping via de Xbox Accessories-app niet meer.",
            useCase: "Voor wie niet via Xbox / Game Pass speelt — geen Xbox-services die op de achtergrond meedraaien.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                XboxService("XblAuthManager"),
                XboxService("XblGameSave"),
                XboxService("XboxNetApiSvc"),
                XboxService("XboxGipSvc"),
            }));

        // ── v0.9.14 GAPS — Explorer + Taskbar ───────────────────────
        // Gap-fill na de Winhance / Win11Debloat gap-analyse (mei 2026).
        // Category = Explorer / Taskbar (beide groeploos → renderen plat op
        // naam). explorerAdvanced-const is hierboven in de EXPLORER-sectie
        // gedefinieerd en in scope binnen deze method.
        const string explorerRoot = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";

        // Drive-letter-positie (Win11Debloat-waarden): 0 = na de naam (default,
        // value-absent), 4 = vóór de naam, 1 = alleen netwerkschijven vóór,
        // 2 = verbergen. Default-choice = absent zodat revert clean deletet.
        TweakChoiceValue[] DriveLetterValue(int? mode) => new TweakChoiceValue[]
        {
            new() { Path = explorerRoot, ValueName = "ShowDriveLettersFirst", Kind = RegistryValueKind.DWord, Value = mode }
        };

        list.Add(new Tweak(
            id: "Explorer.DriveLetterPosition",
            category: TweakCategory.Explorer,
            name: "Drive letter position",
            description: "Waar de schijfletter (C:, D:) verschijnt t.o.v. het volume-label in File Explorer.",
            useCase: "Schijfletters vóór de naam maakt schijven sneller herkenbaar; verbergen geeft een schonere weergave.",
            restart: RestartRequirement.ExplorerRestart,
            choices: new[]
            {
                new TweakChoice("Na de naam (standaard)",        DriveLetterValue(null)),
                new TweakChoice("Vóór de naam",                  DriveLetterValue(4)),
                new TweakChoice("Alleen netwerkschijven vóór",   DriveLetterValue(1)),
                new TweakChoice("Verbergen",                     DriveLetterValue(2)),
            }));

        // Hide Home / Gallery uit de nav-pane. System.IsPinnedToNamespaceTree=0
        // verbergt, =1 toont. HKCU\Software\Classes per-user.
        Tweak HideNavPaneItem(string id, string name, string desc, string clsid) =>
            new Tweak(
                id: id,
                category: TweakCategory.Explorer,
                name: name,
                description: desc,
                restart: RestartRequirement.ExplorerRestart,
                operations: new[]
                {
                    new TweakOperation
                    {
                        Path = $@"HKCU\Software\Classes\CLSID\{clsid}",
                        ValueName = "System.IsPinnedToNamespaceTree",
                        Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                    }
                });

        list.Add(HideNavPaneItem("Explorer.HideHome",
            "Hide 'Home' in navigation pane",
            "Verbergt het 'Home'-item bovenaan de navigatiebalk in File Explorer (de pagina met snelle toegang, favorieten en recente bestanden).",
            "{f874310e-b6b7-47dc-bc84-b9e6b38f5903}"));

        list.Add(HideNavPaneItem("Explorer.HideGallery",
            "Hide 'Gallery' in navigation pane",
            "Verbergt het 'Gallery'-item (foto-tijdlijn) in de navigatiebalk van File Explorer.",
            "{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}"));

        list.Add(new Tweak(
            id: "Explorer.CheckboxSelection",
            category: TweakCategory.Explorer,
            name: "Enable item checkboxes",
            description: "Toont selectie-checkboxes bij bestanden en mappen zodat je meerdere items kunt aanvinken zonder Ctrl ingedrukt te houden.",
            useCase: "Makkelijker multi-select met de muis, vooral op touch.",
            restart: RestartRequirement.ExplorerRestart,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "AutoCheckSelect",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.HideChatButton",
            category: TweakCategory.Taskbar,
            name: "Hide Chat / Teams button",
            description: "Verbergt de Chat- (Microsoft Teams) knop op de taskbar. (Op recente 24H2/25H2-builds is deze knop vaak al weg; de tweak is dan een no-op.)",
            useCase: "Schonere taskbar voor wie de ingebouwde Teams-chat niet gebruikt.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "TaskbarMn",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.DisableShareDragTray",
            category: TweakCategory.Taskbar,
            name: "Disable share drag-tray",
            description: "Schakelt het deel-vak uit dat bovenaan verschijnt wanneer je een bestand naar de taskbar sleept (Win11 24H2+).",
            useCase: "Voorkomt de pop-up-balk bij het per ongeluk slepen van bestanden over de taskbar.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\CDP", ValueName = "DragTrayEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.DisableBadges",
            category: TweakCategory.Taskbar,
            name: "Disable taskbar badges",
            description: "Verbergt de notificatie-tellers (rode badges met aantallen) op taskbar-app-iconen.",
            useCase: "Minder visuele ruis op de taskbar.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "TaskbarBadges",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        // ── v0.9.15 GAPS — Privacy + Security ───────────────────────
        // Gap-fill 2/3 (Winhance / Win11Debloat gap-analyse mei 2026). 5 nieuwe
        // Privacy-tweaks + 1 nieuwe Security-categorie (voorlopig alleen
        // BitLocker; vult later met de geparkeerde caution-tier items). De
        // HKLM-policies batchen samen in 1 UAC.

        list.Add(new Tweak(
            id: "Privacy.DisableLocationServices",
            category: TweakCategory.Privacy,
            name: "Disable Location Services",
            description: "Schakelt de locatievoorziening systeembreed uit via policy — geen enkele app of Windows-functie kan dan je locatie opvragen.",
            useCase: "Maximale locatie-privacy op een desktop die geen locatie nodig heeft.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors",
                    ValueName = "DisableLocation",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableFindMyDevice",
            category: TweakCategory.Privacy,
            name: "Disable Find My Device",
            description: "Schakelt 'Mijn apparaat zoeken' uit — Windows houdt de locatie van het toestel dan niet bij voor terugvinden op afstand.",
            useCase: "Geen periodieke locatie-registratie voor de Find My Device-functie.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\FindMyDevice",
                    ValueName = "AllowFindMyDevice",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableSearchHistory",
            category: TweakCategory.Privacy,
            name: "Disable device search history",
            description: "Stopt het lokaal bijhouden van je zoekgeschiedenis in Windows Search (Start-menu / verkenner-zoekbalk).",
            useCase: "Geen opslag van wat je op het apparaat hebt gezocht.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings",
                    ValueName = "IsDeviceSearchHistoryEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.MinimizeTelemetry",
            category: TweakCategory.Privacy,
            name: "Set telemetry to minimum",
            description: "Zet de diagnostische-data policy op de laagste stand (`AllowTelemetry=0`). Op Home/Pro klemt Windows dit naar 'Basic/Required' (de echte 'Security'-stand is alleen Enterprise/Edu), maar het minimaliseert de verzameling. Vult de DiagTrack-service-tweak aan.",
            useCase: "Zo weinig mogelijk diagnostische data naar Microsoft binnen de grenzen van je editie.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                    ValueName = "AllowTelemetry",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableOnlineSpeechRecognition",
            category: TweakCategory.Privacy,
            name: "Disable online speech recognition",
            description: "Schakelt cloud-gebaseerde spraakherkenning uit — je stem wordt niet meer naar Microsoft gestuurd voor verwerking. Offline spraakherkenning blijft beschikbaar.",
            useCase: "Spraak-input blijft op het apparaat; niets gaat naar de cloud.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy",
                    ValueName = "HasAccepted",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        // ── Security (nieuwe categorie) ──
        list.Add(new Tweak(
            id: "Security.DisableBitLockerAutoEncryption",
            category: TweakCategory.Security,
            name: "Disable automatic BitLocker device encryption",
            description: "Voorkomt dat Windows 11 24H2 je systeemschijf automatisch versleutelt bij een schone installatie / OOBE. LET OP: dit raakt alleen TOEKOMSTIGE automatische encryptie — een al versleutelde schijf blijft versleuteld. Schakel deze tweak in vóór je Windows opnieuw installeert of resetten.",
            useCase: "Geen automatische BitLocker-encryptie waarvan je de recovery-key kunt mislopen (data-verlies-risico bij hardware-wissel).",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\BitLocker",
                    ValueName = "PreventDeviceEncryption",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        // ── v0.9.16 GAPS — Window management + Ads + misc ───────────
        // Gap-fill 3/3 (Winhance / Win11Debloat gap-analyse mei 2026). Window-
        // gedrag → UiTheme-groep "Desktop & vensters" (uiThemeDesktop-const,
        // hierboven gedefinieerd). Ads → AdsBloat (groeploos). Plus muis-accel
        // (Performance) en AI-service auto-start (AiCopilot). HKLM-ops batchen.

        // ── Window management (UiTheme / "Desktop & vensters") ──
        list.Add(new Tweak(
            id: "UiTheme.DisableSnapLayouts",
            category: TweakCategory.UiTheme,
            name: "Disable Snap Layouts",
            description: "Verbergt de Snap Layouts-raster-flyout die verschijnt bij hover over de maximaliseer-knop of bij slepen naar de bovenrand. Handmatig snappen (Win+pijl, slepen naar de zijkant) blijft werken.",
            useCase: "Geen layout-grid-popup voor wie vensters liever zelf positioneert.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "EnableSnapBar",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableWindowSnapping",
            category: TweakCategory.UiTheme,
            name: "Disable window snapping entirely",
            description: "Schakelt ALLE venster-snapping volledig uit (zowel Snap Assist als Snap Layouts als slepen-naar-rand). Grof middel — vensters laten zich daarna nergens meer aan vastklikken.",
            useCase: "Voor wie snapping helemaal niet wil; vensters blijven precies waar je ze loslaat.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Control Panel\Desktop", ValueName = "WindowArrangementActive",
                    Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "1"
                }
            }));

        // Alt+Tab browser-tab-filter (Win11Debloat-waarden): 0 = vensters + 20
        // tabs (default, absent), 1 = +3, 2 = +5, 3 = alleen vensters.
        TweakChoiceValue[] AltTabValue(int? mode) => new TweakChoiceValue[]
        {
            new() { Path = explorerAdvanced, ValueName = "MultiTaskingAltTabFilter", Kind = RegistryValueKind.DWord, Value = mode }
        };

        list.Add(new Tweak(
            id: "UiTheme.AltTabFilter",
            category: TweakCategory.UiTheme,
            name: "Alt+Tab browser tabs",
            description: "Hoeveel Edge-browsertabs er naast je vensters verschijnen in de Alt+Tab-schakelaar.",
            useCase: "Alleen vensters tonen houdt Alt+Tab overzichtelijk als je veel tabs open hebt.",
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            choices: new[]
            {
                new TweakChoice("Vensters + 20 tabs (standaard)", AltTabValue(null)),
                new TweakChoice("Vensters + 5 tabs",             AltTabValue(2)),
                new TweakChoice("Vensters + 3 tabs",             AltTabValue(1)),
                new TweakChoice("Alleen vensters (geen tabs)",   AltTabValue(3)),
            }));

        // ── Ads & Bloat ──
        list.Add(new Tweak(
            id: "Ads.DisableSettings365Ads",
            category: TweakCategory.AdsBloat,
            name: "Disable Microsoft 365 ads in Settings",
            description: "Verwijdert de Microsoft 365 / account-promotie-banner van de Home-pagina van de Instellingen-app.",
            useCase: "Schone Instellingen zonder abonnements-reclame.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent",
                    ValueName = "DisableConsumerAccountStateContent",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Ads.DisableDesktopSpotlight",
            category: TweakCategory.AdsBloat,
            name: "Disable Spotlight desktop background",
            description: "Schakelt de 'Windows Spotlight'-bureaubladachtergrond-collectie uit (de wisselende foto's mét 'Meer informatie'-icoon en advertenties op het bureaublad). HKCU-policy, geen UAC.",
            useCase: "Geen roterende Spotlight-achtergronden / desktop-ads.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Policies\Microsoft\Windows\CloudContent",
                    ValueName = "DisableSpotlightCollectionOnDesktop",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null
                }
            }));

        list.Add(new Tweak(
            id: "Ads.HideSettingsHome",
            category: TweakCategory.AdsBloat,
            name: "Hide Settings 'Home' page",
            description: "Verbergt de 'Home'-pagina van de Instellingen-app (de pagina vol aanbevelingen, account-promoties en Game Pass-banners). Instellingen opent dan direct op 'Systeem'.",
            useCase: "Recht naar de echte instellingen zonder de promotionele landingspagina.",
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer",
                    ValueName = "SettingsPageVisibility",
                    Kind = RegistryValueKind.String, EnabledValue = "hide:home", DisabledValue = null,
                    RequiresElevation = true
                }
            }));

        // ── Misc ──
        list.Add(new Tweak(
            id: "Performance.DisableMouseAcceleration",
            category: TweakCategory.Performance,
            name: "Disable mouse acceleration",
            description: "Schakelt 'Enhance pointer precision' (muis-acceleratie) uit zodat de cursor-afstand exact 1-op-1 met je fysieke muis-beweging meeschaalt — onafhankelijk van hoe snel je beweegt. 3-op (MouseSpeed + 2 thresholds = 0).",
            useCase: "Consistente, voorspelbare muis-bewegingen — vooral gewenst bij gaming en precisiewerk.",
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation { Path = @"HKCU\Control Panel\Mouse", ValueName = "MouseSpeed", Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "1" },
                new TweakOperation { Path = @"HKCU\Control Panel\Mouse", ValueName = "MouseThreshold1", Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "6" },
                new TweakOperation { Path = @"HKCU\Control Panel\Mouse", ValueName = "MouseThreshold2", Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "10" }
            }));

        list.Add(new Tweak(
            id: "AiCopilot.DisableAiServiceAutostart",
            category: TweakCategory.AiCopilot,
            name: "Disable AI service auto-start",
            description: "Zet de Windows AI Fabric-service (`WSAIFabricSvc`) op handmatige start i.p.v. automatisch. Deze service bestaat alleen op Copilot+ PC's — op andere systemen is de tweak een no-op (de service-key is er niet).",
            useCase: "Geen automatisch draaiende AI-achtergrondservice op Copilot+ hardware.",
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Services\WSAIFabricSvc", ValueName = "Start",
                    Kind = RegistryValueKind.DWord, EnabledValue = 3, DisabledValue = 2,
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
