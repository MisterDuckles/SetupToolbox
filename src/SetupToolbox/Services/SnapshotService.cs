using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;
using SetupToolbox.Helpers;
using SetupToolbox.Models;

namespace SetupToolbox.Services;

// Registry-snapshot service voor de Tweaks tab. Vóór elke Apply leest deze
// service de actuele waardes (incl. "was absent" markers) van álle ops die de
// user gaat schrijven, en parkeert ze als JSON in
//   %LOCALAPPDATA%\SetupToolbox\snapshots\<id>.json
//
// Restore zet die exacte staat terug: schrijft de previousValue waar mogelijk,
// of deletet de value als die origineel absent was. Splitst lokale (HKCU
// non-policy) van elevated (HKLM + HKCU Policies) ops; elevated gaat via één
// PowerShell + reg.exe batch — zelfde patroon als TweakService.ApplyAsync.
//
// Snapshots zijn klein (paar KB elk) en hebben een max van 20 — auto-prune
// houdt de map onder de duim. Naam-veld is user-defined of auto-gegenereerd
// uit de tweak-lijst zodat user in SnapshotBrowserDialog snel ziet wat elke
// snapshot dekte.
public sealed class SnapshotService
{
    private static readonly string _snapshotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SetupToolbox",
        "snapshots");

    private const int MaxSnapshots = 20;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Enum als string zodat een JSON-bestand zonder onze code te lezen
        // begrijpelijk blijft (DWord ipv 4).
        Converters = { new JsonStringEnumConverter() }
    };

    public SnapshotService()
    {
        try { Directory.CreateDirectory(_snapshotDir); } catch { /* best-effort */ }
    }

    // ---------------------------------------------------------------
    // CAPTURE
    // ---------------------------------------------------------------

    /// <summary>
    /// Leest de huidige registry-waarde van elke op die de tweaks zouden gaan
    /// schrijven en parkeert dat als snapshot op disk. Returnt de metadata van
    /// de aangemaakte snapshot. Throwt niet bij IO-fouten — returnt null als
    /// snapshot niet kon worden weggeschreven (in dat geval gewoon doorgaan
    /// met Apply, zonder veiligheidsnet).
    /// </summary>
    public async Task<SnapshotMetadata?> CaptureAsync(IReadOnlyList<Tweak> tweaks, string description)
    {
        if (tweaks == null || tweaks.Count == 0) return null;

        var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}".Substring(0, 23);
        var entries = new List<SnapshotEntry>();
        var tweakIds = new List<string>();

        foreach (var tweak in tweaks)
        {
            tweakIds.Add(tweak.Id);

            // Toggle-tweaks: één entry per op. Choice-tweaks: één entry per
            // value van álle choices die theoretisch geschreven kunnen worden
            // (zo'n choice-tweak schrijft één set per click, maar als user
            // tussen choices wisselt zijn alle paden relevant).
            if (tweak.IsChoice && tweak.Choices != null)
            {
                // Voor choices kapture we de unieke (Path, ValueName) paren over
                // alle choices heen. Verschillende choices kunnen overlappende
                // value-namen hebben met andere targetValues, maar het pad +
                // value-name set is identiek (dat is wat een choice-tweak doet:
                // dezelfde keys, andere waarden).
                var uniqueKeys = new HashSet<(string, string)>();
                foreach (var choice in tweak.Choices)
                {
                    foreach (var v in choice.Values)
                    {
                        if (!uniqueKeys.Add((v.Path, v.ValueName))) continue;
                        var (exists, value) = TryReadValue(v.Path, v.ValueName);
                        entries.Add(new SnapshotEntry
                        {
                            Path = v.Path,
                            ValueName = v.ValueName,
                            Kind = v.Kind,
                            WasAbsent = !exists,
                            PreviousValue = exists ? ConvertValueForJson(value, v.Kind) : null,
                            RequiresElevation = v.RequiresElevation
                        });
                    }
                }
            }
            else
            {
                foreach (var op in tweak.Operations)
                {
                    var (exists, value) = TryReadValue(op.Path, op.ValueName);
                    entries.Add(new SnapshotEntry
                    {
                        Path = op.Path,
                        ValueName = op.ValueName,
                        Kind = op.Kind,
                        WasAbsent = !exists,
                        PreviousValue = exists ? ConvertValueForJson(value, op.Kind) : null,
                        RequiresElevation = op.RequiresElevation
                    });
                }
            }
        }

        var metadata = new SnapshotMetadata
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            Description = string.IsNullOrWhiteSpace(description) ? AutoDescription(tweaks) : description.Trim(),
            TweakIds = tweakIds,
            Entries = entries
        };

        try
        {
            Directory.CreateDirectory(_snapshotDir);
            var path = Path.Combine(_snapshotDir, $"{id}.json");
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
            PruneOldSnapshots();
            return metadata;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SetupToolbox_snapshots.log", $"Capture failed: {ex.Message}");
            return null;
        }
    }

    private static string AutoDescription(IReadOnlyList<Tweak> tweaks)
    {
        if (tweaks.Count == 1) return tweaks[0].Name;
        if (tweaks.Count <= 3) return string.Join(", ", tweaks.Select(t => t.Name));
        return App.Loc.S("snapshot.plusOthers", tweaks[0].Name, tweaks.Count - 1);
    }

    // ---------------------------------------------------------------
    // RESTORE
    // ---------------------------------------------------------------

    /// <summary>
    /// Zet alle registry-waardes uit de snapshot terug. Was de value origineel
    /// absent → delete; was er een waarde → schrijven. Splitst local/elevated
    /// (zelfde patroon als TweakService.ApplyAsync). Returnt resultaat met
    /// success/fail counts + cancel-flag bij UAC-denial.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(string snapshotId)
    {
        var snapshot = Load(snapshotId);
        if (snapshot == null) return new RestoreResult(0, 0, false, new[] { "Snapshot niet gevonden of corrupt." });

        var localEntries = new List<SnapshotEntry>();
        var elevatedEntries = new List<SnapshotEntry>();
        foreach (var entry in snapshot.Entries)
            (entry.RequiresElevation ? elevatedEntries : localEntries).Add(entry);

        var successCount = 0;
        var failures = new List<string>();

        // 1) Local (HKCU non-policy) in-process — geen UAC.
        foreach (var entry in localEntries)
        {
            try
            {
                ApplyEntryLocal(entry);
                successCount++;
            }
            catch (Exception ex)
            {
                failures.Add($"{ShortKey(entry)}: {ex.Message}");
            }
        }

        // 2) Elevated batch — 1 UAC voor de hele subset.
        var cancelled = false;
        if (elevatedEntries.Count > 0)
        {
            var (elevSuccess, elevFailures, wasCancelled) = await RunElevatedRestoreAsync(elevatedEntries);
            successCount += elevSuccess;
            failures.AddRange(elevFailures);
            cancelled = wasCancelled;
        }

        return new RestoreResult(successCount, snapshot.Entries.Count - successCount, cancelled, failures);
    }

    private static void ApplyEntryLocal(SnapshotEntry entry)
    {
        var (hive, view, subPath) = ParsePath(entry.Path);
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);

        if (entry.WasAbsent)
        {
            // Origineel afwezig → delete de value (key blijft staan; we hebben
            // 'm niet zelf aangemaakt voor deze entry, kunnen 'm dus niet veilig
            // tree-deleten).
            using var key = baseKey.OpenSubKey(subPath, writable: true);
            key?.DeleteValue(entry.ValueName, throwOnMissingValue: false);
            return;
        }

        using var writeKey = baseKey.CreateSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"Cannot open key: {entry.Path}");
        var value = ConvertValueFromJson(entry.PreviousValue, entry.Kind);
        writeKey.SetValue(entry.ValueName, value, entry.Kind);
    }

    private static async Task<(int success, List<string> failures, bool cancelled)>
        RunElevatedRestoreAsync(IReadOnlyList<SnapshotEntry> entries)
    {
        var logPath = Path.Combine(Path.GetTempPath(),
            $"SetupToolbox_snapshot-restore_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$logPath = '{Escape(logPath)}'");
        sb.AppendLine("function Log($msg) { Add-Content -Path $logPath -Value $msg }");
        sb.AppendLine("Log \"START\"");

        foreach (var entry in entries)
        {
            var key = ShortKey(entry);
            sb.AppendLine("try {");
            if (entry.WasAbsent)
            {
                var valueArg = string.IsNullOrEmpty(entry.ValueName) ? "/ve" : $"/v '{Escape(entry.ValueName)}'";
                sb.AppendLine($"    & reg.exe delete '{Escape(entry.Path)}' {valueArg} /f | Out-Null");
                // reg.exe delete returnt non-zero als key/value niet bestaat.
                // Voor restore-naar-absent is dat oké — accepteer beide.
                sb.AppendLine($"    Log \"RESULT|{Escape(key)}|OK|Deleted\"");
            }
            else
            {
                var regType = entry.Kind switch
                {
                    RegistryValueKind.DWord => "REG_DWORD",
                    RegistryValueKind.QWord => "REG_QWORD",
                    RegistryValueKind.String => "REG_SZ",
                    RegistryValueKind.ExpandString => "REG_EXPAND_SZ",
                    RegistryValueKind.MultiString => "REG_MULTI_SZ",
                    RegistryValueKind.Binary => "REG_BINARY",
                    _ => "REG_SZ"
                };
                var valueArg = string.IsNullOrEmpty(entry.ValueName) ? "/ve" : $"/v '{Escape(entry.ValueName)}'";
                var data = FormatValueForReg(entry.PreviousValue, entry.Kind);
                sb.AppendLine($"    & reg.exe add '{Escape(entry.Path)}' {valueArg} /t {regType} /d '{Escape(data)}' /f | Out-Null");
                sb.AppendLine($"    if ($LASTEXITCODE -ne 0) {{ throw \"reg.exe exit $LASTEXITCODE\" }}");
                sb.AppendLine($"    Log \"RESULT|{Escape(key)}|OK|Restored\"");
            }
            sb.AppendLine("} catch {");
            sb.AppendLine($"    Log \"RESULT|{Escape(key)}|FAIL|$($_.Exception.Message)\"");
            sb.AppendLine("}");
        }
        sb.AppendLine("Log \"END\"");

        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"SetupToolbox_snapshot-restore_{DateTime.Now:yyyyMMdd_HHmmss}.ps1");
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

        var success = 0;
        var failures = new List<string>();
        var cancelled = false;
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                foreach (var e in entries) failures.Add($"{ShortKey(e)}: kon elevated process niet starten");
                return (0, failures, false);
            }
            await proc.WaitForExitAsync();

            if (File.Exists(logPath))
            {
                var lines = await File.ReadAllLinesAsync(logPath);
                foreach (var line in lines)
                {
                    if (!line.StartsWith("RESULT|")) continue;
                    var parts = line.Split('|', 4);
                    if (parts.Length < 3) continue;
                    if (parts[2] == "OK") success++;
                    else failures.Add($"{parts[1]}: {(parts.Length >= 4 ? parts[3] : "Failed")}");
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            cancelled = true;
            foreach (var e in entries) failures.Add($"{ShortKey(e)}: UAC geweigerd");
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
        return (success, failures, cancelled);
    }

    // ---------------------------------------------------------------
    // LIST / DELETE
    // ---------------------------------------------------------------

    /// <summary>
    /// Alle snapshots op disk, gesort van nieuw naar oud.
    /// </summary>
    public IReadOnlyList<SnapshotMetadata> List()
    {
        if (!Directory.Exists(_snapshotDir)) return Array.Empty<SnapshotMetadata>();
        var snapshots = new List<SnapshotMetadata>();
        foreach (var file in Directory.EnumerateFiles(_snapshotDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var snap = JsonSerializer.Deserialize<SnapshotMetadata>(json, _jsonOptions);
                if (snap != null) snapshots.Add(snap);
            }
            catch { /* corrupte snapshot — skip in lijst */ }
        }
        return snapshots.OrderByDescending(s => s.CreatedAt).ToList();
    }

    /// <summary>
    /// De meest recente snapshot, of null als er geen zijn.
    /// </summary>
    public SnapshotMetadata? GetLatest() => List().FirstOrDefault();

    public void Delete(string snapshotId)
    {
        try
        {
            var path = Path.Combine(_snapshotDir, $"{snapshotId}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SetupToolbox_snapshots.log", $"Delete failed: {ex.Message}");
        }
    }

    private SnapshotMetadata? Load(string snapshotId)
    {
        try
        {
            var path = Path.Combine(_snapshotDir, $"{snapshotId}.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SnapshotMetadata>(json, _jsonOptions);
        }
        catch { return null; }
    }

    private static void PruneOldSnapshots()
    {
        if (!Directory.Exists(_snapshotDir)) return;
        try
        {
            var files = Directory.EnumerateFiles(_snapshotDir, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Skip(MaxSnapshots)
                .ToList();
            foreach (var fi in files)
            {
                try { fi.Delete(); } catch { }
            }
        }
        catch { }
    }

    // ---------------------------------------------------------------
    // REGISTRY HELPERS (mirror van TweakService — bewust gedupliceerd zodat
    // SnapshotService geen lock-coupling met TweakService heeft)
    // ---------------------------------------------------------------

    private static (bool exists, object? value) TryReadValue(string path, string valueName)
    {
        try
        {
            var (hive, view, sub) = ParsePath(path);
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(sub, writable: false);
            if (key == null) return (false, null);
            var values = key.GetValueNames();
            if (!values.Contains(valueName, StringComparer.OrdinalIgnoreCase))
                return (false, null);
            var value = key.GetValue(valueName);
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
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            _ => throw new ArgumentException($"Unsupported hive: {hiveName}")
        };
        var view = subPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
            ? RegistryView.Registry32
            : RegistryView.Registry64;
        return (hive, view, subPath);
    }

    // Object → JSON-vriendelijk waarde (string voor binary/multistring,
    // long voor numerics, string voor SZ/ExpandString). Bij deserialisatie
    // wordt 't omgekeerde gedaan.
    private static object? ConvertValueForJson(object? value, RegistryValueKind kind)
    {
        if (value == null) return null;
        return kind switch
        {
            RegistryValueKind.DWord or RegistryValueKind.QWord => Convert.ToInt64(value),
            RegistryValueKind.MultiString when value is string[] arr => string.Join("\n", arr),
            RegistryValueKind.Binary when value is byte[] bytes => Convert.ToBase64String(bytes),
            _ => value.ToString()
        };
    }

    // JSON-waarde (na deserialisatie verschijnt 't als JsonElement of het
    // primitive type afhankelijk van System.Text.Json) → registry-waarde van
    // het juiste type voor SetValue.
    private static object ConvertValueFromJson(object? jsonValue, RegistryValueKind kind)
    {
        if (jsonValue == null) return 0;

        // System.Text.Json deserialiseert "object?" als JsonElement.
        if (jsonValue is JsonElement je)
        {
            return kind switch
            {
                RegistryValueKind.DWord => (int)je.GetInt64(),
                RegistryValueKind.QWord => je.GetInt64(),
                RegistryValueKind.MultiString => je.GetString()?.Split('\n') ?? Array.Empty<string>(),
                RegistryValueKind.Binary => Convert.FromBase64String(je.GetString() ?? string.Empty),
                _ => je.GetString() ?? string.Empty
            };
        }

        // Fallback voor andere call-paths (bv direct gebruikt).
        return kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(jsonValue),
            RegistryValueKind.QWord => Convert.ToInt64(jsonValue),
            RegistryValueKind.MultiString when jsonValue is string s => s.Split('\n'),
            RegistryValueKind.Binary when jsonValue is string b64 => Convert.FromBase64String(b64),
            _ => jsonValue.ToString() ?? string.Empty
        };
    }

    private static string FormatValueForReg(object? value, RegistryValueKind kind)
    {
        if (value == null) return string.Empty;
        if (value is JsonElement je)
        {
            return kind switch
            {
                RegistryValueKind.DWord or RegistryValueKind.QWord => je.GetInt64().ToString(),
                _ => je.GetString() ?? string.Empty
            };
        }
        return kind switch
        {
            RegistryValueKind.DWord or RegistryValueKind.QWord => Convert.ToInt64(value).ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string Escape(string s) => s.Replace("'", "''");

    private static string ShortKey(SnapshotEntry entry)
    {
        var idx = entry.Path.LastIndexOf('\\');
        var tail = idx >= 0 ? entry.Path.Substring(idx + 1) : entry.Path;
        return string.IsNullOrEmpty(entry.ValueName) ? tail : $"{tail}\\{entry.ValueName}";
    }
}

// On-disk snapshot format.
public sealed class SnapshotMetadata
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> TweakIds { get; set; } = new();
    public List<SnapshotEntry> Entries { get; set; } = new();
}

public sealed class SnapshotEntry
{
    public string Path { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public RegistryValueKind Kind { get; set; } = RegistryValueKind.DWord;
    public bool WasAbsent { get; set; }
    // PreviousValue is null wanneer WasAbsent=true. Anders bevat 't de
    // geserializeerde representatie van de oude registry-waarde.
    public object? PreviousValue { get; set; }
    public bool RequiresElevation { get; set; }
}

public sealed record RestoreResult(int SuccessCount, int FailedCount, bool Cancelled, IReadOnlyList<string> FailureMessages);
