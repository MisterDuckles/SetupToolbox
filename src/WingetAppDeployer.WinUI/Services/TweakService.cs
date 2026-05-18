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

        return list;
    }
}

public sealed record TweakApplyResult(
    int SuccessCount,
    int FailedCount,
    bool Cancelled,
    IReadOnlyList<string> FailureMessages);
