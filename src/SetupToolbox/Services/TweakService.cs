using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using SetupToolbox.Helpers;
using SetupToolbox.Models;

namespace SetupToolbox.Services;

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

        // De tweak-objecten leven langer dan een page: MainWindow navigeert bij een
        // taalwissel de huidige page opnieuw, maar bouwt BuildAll() niet opnieuw op.
        // Naam/omschrijving/use-case zijn nu lookups, dus ze kloppen daarna vanzelf —
        // dit duwt de wijziging alleen nog door naar eventuele gebonden consumers.
        App.Loc.LanguageChanged += (_, _) =>
        {
            foreach (var t in _tweaks) t.RaiseLocalizedTextChanged();
        };
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
            $"SetupToolbox_tweaks_{DateTime.Now:yyyyMMdd_HHmmss}.log");

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
            $"SetupToolbox_tweaks_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
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
                    results[$"{t.Id}::{op.Path}::{op.ValueName}"] = (false, App.Loc.S("elevated.couldNotStart"));
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
                    results[key] = (false, App.Loc.S("elevated.uacDeclined"));
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
                results[key] = (false, App.Loc.S("elevated.didNotRun"));
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
                Diagnostics.Log("SetupToolbox_tweaks.log", $"RestartExplorer failed: {ex.Message}");
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
            restart: RestartRequirement.None,
            choices: new[]
            {
                new TweakChoice(SearchValuesForMode(0)),
                new TweakChoice(SearchValuesForMode(1)),
                new TweakChoice(SearchValuesForMode(2)),
                new TweakChoice(SearchValuesForMode(3)),
            }));

        list.Add(new Tweak(
            id: "Taskbar.HideTaskView",
            category: TweakCategory.Taskbar,
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
            restart: RestartRequirement.None,
            choices: new[]
            {
                new TweakChoice(new TweakChoiceValue[]
                {
                    new() { Path = explorerAdvanced, ValueName = "Start_Layout", Kind = RegistryValueKind.DWord, Value = 0 }
                }),
                new TweakChoice(new TweakChoiceValue[]
                {
                    new() { Path = explorerAdvanced, ValueName = "Start_Layout", Kind = RegistryValueKind.DWord, Value = 1 }
                }),
                new TweakChoice(new TweakChoiceValue[]
                {
                    new() { Path = explorerAdvanced, ValueName = "Start_Layout", Kind = RegistryValueKind.DWord, Value = 2 }
                }),
            }));

        list.Add(new Tweak(
            id: "StartMenu.HideRecommendedSection",
            category: TweakCategory.StartMenu,
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
        const string uiThemeColors = "tweak.group.themeColors";
        const string uiThemeDesktop = "tweak.group.desktopWindows";
        const string uiThemeBoot = "tweak.group.bootLogin";
        const string uiThemeSound = "tweak.group.sound";

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
            restart: RestartRequirement.None,
            group: uiThemeColors,
            choices: new[]
            {
                new TweakChoice(ThemeValues(1, 1)),
                new TweakChoice(ThemeValues(0, 0)),
                new TweakChoice(ThemeValues(0, 1)),
            }));

        list.Add(new Tweak(
            id: "UiTheme.DisableTransparency",
            category: TweakCategory.UiTheme,
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
        const string cmRemove = "tweak.group.removeItems";
        const string cmAdd = "tweak.group.addItems";

        // De Shell Extensions Blocked-lijst: een String-value met de CLSID als
        // naam (lege data) onderdrukt die shell-extension overal. Werkt voor
        // klassieke IContextMenu-handlers én IExplorerCommand-handlers.
        const string blockedKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        Tweak BlockedTweak(string id, string clsid) =>
            new Tweak(
                id: id,
                category: TweakCategory.ContextMenu,
                restart: RestartRequirement.ExplorerRestart,
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
            "{BFE0E2A4-C70C-4AD7-AC3D-10D1ECEBB5B4}"));

        list.Add(BlockedTweak("ContextMenu.RemoveEditWithPaint",
            "{2430F218-B743-4FD6-97BF-5C76541B4AE9}"));

        list.Add(BlockedTweak("ContextMenu.RemoveScanWithDefender",
            "{09A47860-11B0-4DA5-AFA5-26D86198A780}"));

        list.Add(BlockedTweak("ContextMenu.RemoveRestorePreviousVersions",
            "{596AB062-B4D2-4215-9F74-E9109B0A8153}"));

        list.Add(BlockedTweak("ContextMenu.RemoveCastToDevice",
            "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}"));

        list.Add(BlockedTweak("ContextMenu.RemoveIncludeInLibrary",
            "{3dad6c5d-2167-4cae-9914-f99e41c12cfa}"));

        list.Add(BlockedTweak("ContextMenu.RemoveShare",
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
        const string nlNotifGroup = "tweak.group.notifications";
        const string nlCenterGroup = "tweak.group.notificationCenter";
        const string nlLockGroup = "tweak.group.lockScreen";

        const string pushNotifications =
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\PushNotifications";
        const string notificationSettings =
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        // ── Meldingen ──
        list.Add(new Tweak(
            id: "NotifLock.DisableAllNotifications",
            category: TweakCategory.NotificationsLock,
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
            restart: RestartRequirement.SignOut,
            group: nlNotifGroup,
            choices: new[]
            {
                new TweakChoice(DurationValue(null)),
                new TweakChoice(DurationValue(7)),
                new TweakChoice(DurationValue(15)),
                new TweakChoice(DurationValue(30)),
                new TweakChoice(DurationValue(60)),
                new TweakChoice(DurationValue(300)),
            }));

        // ── Notificatiecentrum ──
        // Op Win11 is het notificatiecentrum samengevoegd met de kalender-
        // flyout: DisableNotificationCenter=1 haalt de bel-icoon weg én zorgt
        // dat klikken op de klok geen kalender/meldingen-paneel meer opent
        // (Win10-stijl tray-klok). Eén tweak dekt dus beide schets-items.
        list.Add(new Tweak(
            id: "NotifLock.DisableNotificationCenter",
            category: TweakCategory.NotificationsLock,
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
        const string upRestartGroup = "tweak.group.updatesRestart";
        const string upDriverGroup = "tweak.group.driversNetwork";

        // WindowsUpdate-policy keys + de UX\Settings backing-store (= waar de
        // Settings-app zelf naar schrijft; geen policy-key).
        const string auPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
        const string wuPolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        const string uxSettings = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

        // ── Updates & herstart ──
        list.Add(new Tweak(
            id: "Updates.NoAutoRebootWhileLoggedOn",
            category: TweakCategory.Updates,
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
            restart: RestartRequirement.None,
            group: upRestartGroup,
            choices: new[]
            {
                new TweakChoice(ActiveHoursValues(null, null, null)),
                new TweakChoice(ActiveHoursValues(0, 8, 23)),
                new TweakChoice(ActiveHoursValues(0, 6, 0)),
            }));

        // ── Drivers & netwerk ──
        list.Add(new Tweak(
            id: "Updates.DisableDriverUpdates",
            category: TweakCategory.Updates,
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
            restart: RestartRequirement.ExplorerRestart,
            choices: new[]
            {
                new TweakChoice(DriveLetterValue(null)),
                new TweakChoice(DriveLetterValue(4)),
                new TweakChoice(DriveLetterValue(1)),
                new TweakChoice(DriveLetterValue(2)),
            }));

        // Hide Home / Gallery uit de nav-pane. System.IsPinnedToNamespaceTree=0
        // verbergt, =1 toont. HKCU\Software\Classes per-user.
        Tweak HideNavPaneItem(string id, string clsid) =>
            new Tweak(
                id: id,
                category: TweakCategory.Explorer,
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
            "{f874310e-b6b7-47dc-bc84-b9e6b38f5903}"));

        list.Add(HideNavPaneItem("Explorer.HideGallery",
            "{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}"));

        list.Add(new Tweak(
            id: "Explorer.CheckboxSelection",
            category: TweakCategory.Explorer,
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
            restart: RestartRequirement.None,
            group: uiThemeDesktop,
            choices: new[]
            {
                new TweakChoice(AltTabValue(null)),
                new TweakChoice(AltTabValue(2)),
                new TweakChoice(AltTabValue(1)),
                new TweakChoice(AltTabValue(3)),
            }));

        // ── Ads & Bloat ──
        list.Add(new Tweak(
            id: "Ads.DisableSettings365Ads",
            category: TweakCategory.AdsBloat,
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
            restart: RestartRequirement.SignOut,
            operations: new[]
            {
                new TweakOperation { Path = @"HKCU\Control Panel\Mouse", ValueName = "MouseSpeed", Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "1" },
                new TweakOperation { Path = @"HKCU\Control Panel\Mouse", ValueName = "MouseThreshold1", Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "6" },
                new TweakOperation { Path = @"HKCU\Control Panel\Mouse", ValueName = "MouseThreshold2", Kind = RegistryValueKind.String, EnabledValue = "0", DisabledValue = "10" }
            }));

        // ── v0.9.19 — UI & Performance misc + battery % ─────────────
        // Gap-analyse ronde 2 (winutil + ShutUp10), gecureerde rest. UI-tweaks
        // in nieuwe UiTheme-subgroep "Invoer & weergave".
        const string uiThemeInput = "tweak.group.inputDisplay";

        list.Add(new Tweak(
            id: "UiTheme.DisableStickyKeysPrompt",
            category: TweakCategory.UiTheme,
            restart: RestartRequirement.None,
            group: uiThemeInput,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Control Panel\Accessibility\StickyKeys", ValueName = "Flags",
                    Kind = RegistryValueKind.String, EnabledValue = "506", DisabledValue = "510"
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.AlwaysShowScrollbars",
            category: TweakCategory.UiTheme,
            restart: RestartRequirement.SignOut,
            group: uiThemeInput,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Control Panel\Accessibility", ValueName = "DynamicScrollbars",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "UiTheme.FasterMenuDelay",
            category: TweakCategory.UiTheme,
            restart: RestartRequirement.SignOut,
            group: uiThemeInput,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Control Panel\Desktop", ValueName = "MenuShowDelay",
                    Kind = RegistryValueKind.String, EnabledValue = "200", DisabledValue = "400"
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableHibernation",
            category: TweakCategory.Performance,
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\Power", ValueName = "HibernateEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1, RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.DisableFullscreenOptimizations",
            category: TweakCategory.Performance,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation { Path = @"HKCU\System\GameConfigStore", ValueName = "GameDVR_FSEBehaviorMode", Kind = RegistryValueKind.DWord, EnabledValue = 2, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\System\GameConfigStore", ValueName = "GameDVR_HonorUserFSEBehaviorMode", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\System\GameConfigStore", ValueName = "GameDVR_DXGIHonorFSEWindowsCompatible", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\System\GameConfigStore", ValueName = "GameDVR_EFSEFeatureFlags", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
            }));

        list.Add(new Tweak(
            id: "Performance.RestorePointFrequency",
            category: TweakCategory.Performance,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
                    ValueName = "SystemRestorePointCreationFrequency",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null, RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Performance.EnablePeriodicRegistryBackup",
            category: TweakCategory.Performance,
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager",
                    ValueName = "EnablePeriodicBackup",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Taskbar.ShowBatteryPercentage",
            category: TweakCategory.Taskbar,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = explorerAdvanced, ValueName = "IsBatteryPercentageEnabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0
                }
            }));

        // ── v0.9.18 — Telemetrie-hardening + Privacy-restjes + Office ─
        // Gap-analyse ronde 2 (winutil + O&O ShutUp10). Aanvulling op de
        // bestaande Privacy-tweaks (DiagTrack/CEIP/AllowTelemetry).

        list.Add(new Tweak(
            id: "Privacy.TelemetryHardening",
            category: TweakCategory.Privacy,
            restart: RestartRequirement.Reboot,
            operations: new[]
            {
                new TweakOperation { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat", ValueName = "AITEnable", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat", ValueName = "DisableInventory", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\System", ValueName = "AllowExperimentation", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", ValueName = "DisableOneSettingsDownloads", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", ValueName = "LimitDiagnosticLogCollection", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SYSTEM\CurrentControlSet\Services\dmwappushservice", ValueName = "Start", Kind = RegistryValueKind.DWord, EnabledValue = 4, DisabledValue = 3, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SYSTEM\CurrentControlSet\Control\WMI\Autologger\AutoLogger-Diagtrack-Listener", ValueName = "Start", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1, RequiresElevation = true },
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableErrorReporting",
            category: TweakCategory.Privacy,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", ValueName = "Disabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = 0, RequiresElevation = true
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableHandwritingDataSharing",
            category: TweakCategory.Privacy,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\TabletPC", ValueName = "PreventHandwritingDataSharing", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true },
                new TweakOperation { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports", ValueName = "PreventHandwritingErrorReports", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true },
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableTypingInfo",
            category: TweakCategory.Privacy,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Input\TIPC", ValueName = "Enabled",
                    Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = 1
                }
            }));

        list.Add(new Tweak(
            id: "Privacy.DisableSettingsSync",
            category: TweakCategory.Privacy,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\SettingSync", ValueName = "SyncPolicy",
                    Kind = RegistryValueKind.DWord, EnabledValue = 5, DisabledValue = null
                }
            }));

        list.Add(new Tweak(
            id: "NotifLock.DisableLockScreenCamera",
            category: TweakCategory.NotificationsLock,
            restart: RestartRequirement.None,
            group: nlLockGroup,
            operations: new[]
            {
                new TweakOperation
                {
                    Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Personalization", ValueName = "NoLockScreenCamera",
                    Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null, RequiresElevation = true
                }
            }));

        // Office-bundle: HKCU\Software\Policies\Microsoft\Office\16.0 (dekt
        // Office 2016/2019/2021/365). No-op als Office niet geïnstalleerd is.
        // Geen UAC (HKCU). DisabledValue=null → revert deletet de policy.
        const string officeCommon = @"HKCU\Software\Policies\Microsoft\Office\16.0\Common";
        list.Add(new Tweak(
            id: "Privacy.DisableOfficeTelemetry",
            category: TweakCategory.Privacy,
            restart: RestartRequirement.None,
            operations: new[]
            {
                new TweakOperation { Path = officeCommon + @"\ClientTelemetry", ValueName = "DisableTelemetry", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null },
                new TweakOperation { Path = officeCommon + @"\ClientTelemetry", ValueName = "SendTelemetry", Kind = RegistryValueKind.DWord, EnabledValue = 3, DisabledValue = null },
                new TweakOperation { Path = officeCommon, ValueName = "QMEnable", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = officeCommon, ValueName = "LinkedIn", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\Software\Policies\Microsoft\Office\16.0\OSM", ValueName = "Enablelogging", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\Software\Policies\Microsoft\Office\16.0\OSM", ValueName = "EnableUpload", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\Software\Policies\Microsoft\Office\16.0\OSM", ValueName = "EnableFileObfuscation", Kind = RegistryValueKind.DWord, EnabledValue = 1, DisabledValue = null },
                new TweakOperation { Path = officeCommon + @"\Feedback", ValueName = "Enabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = officeCommon + @"\Feedback", ValueName = "SurveyEnabled", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = officeCommon + @"\Feedback", ValueName = "IncludeEmail", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
                new TweakOperation { Path = officeCommon + @"\Privacy", ValueName = "UserContentDisabled", Kind = RegistryValueKind.DWord, EnabledValue = 2, DisabledValue = null },
                new TweakOperation { Path = officeCommon + @"\Privacy", ValueName = "DownloadContentDisabled", Kind = RegistryValueKind.DWord, EnabledValue = 2, DisabledValue = null },
                new TweakOperation { Path = @"HKCU\Software\Microsoft\Office\16.0\Common\MailSettings", ValueName = "InlineTextPrediction", Kind = RegistryValueKind.DWord, EnabledValue = 0, DisabledValue = null },
            }));

        // ── v0.9.17 — Edge-debloat-bundle ───────────────────────────
        // OFGB-stijl mega-bundle: ~19 Edge-policy-keys onder 1 toggle.
        // Allemaal HKLM\...\Policies\Microsoft\Edge → batchen in 1 UAC.
        // DisabledValue=null overal → revert deletet de policy (Edge terug
        // naar default). Bron: ShutUp10 Edge-set + parking-lot Edge-debloat.
        const string edgePolicy = @"HKLM\SOFTWARE\Policies\Microsoft\Edge";
        TweakOperation EdgeOp(string valueName, int enabledValue) => new TweakOperation
        {
            Path = edgePolicy, ValueName = valueName, Kind = RegistryValueKind.DWord,
            EnabledValue = enabledValue, DisabledValue = null, RequiresElevation = true
        };

        list.Add(new Tweak(
            id: "Ads.DisableEdgeBloat",
            category: TweakCategory.AdsBloat,
            restart: RestartRequirement.None,
            operations: new[]
            {
                EdgeOp("ConfigureDoNotTrack", 1),
                EdgeOp("EdgeShoppingAssistantEnabled", 0),
                EdgeOp("HubsSidebarEnabled", 0),
                EdgeOp("AddressBarMicrosoftSearchInBingProviderEnabled", 0),
                EdgeOp("UserFeedbackAllowed", 0),
                EdgeOp("AutofillCreditCardEnabled", 0),
                EdgeOp("LocalProvidersEnabled", 0),
                EdgeOp("SearchSuggestEnabled", 0),
                EdgeOp("WebWidgetAllowed", 0),
                EdgeOp("NetworkPredictionOptions", 2),
                EdgeOp("PersonalizationReportingEnabled", 0),
                EdgeOp("PaymentMethodQueryEnabled", 0),
                EdgeOp("StartupBoostEnabled", 0),
                EdgeOp("BackgroundModeEnabled", 0),
                EdgeOp("ShowRecommendationsEnabled", 0),
                EdgeOp("SpotlightExperiencesAndRecommendationsEnabled", 0),
                EdgeOp("NewTabPageContentEnabled", 0),
                EdgeOp("NewTabPageHideDefaultTopSites", 1),
                EdgeOp("WalletDonationEnabled", 0),
            }));

        list.Add(new Tweak(
            id: "AiCopilot.DisableAiServiceAutostart",
            category: TweakCategory.AiCopilot,
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
