using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SetupToolbox.Models;

namespace SetupToolbox.Services;

// Export/import van een Tweak-PROFIEL (v0.9.20) — een door de user samengestelde
// set "gewenste" tweaks (apps-stijl, NIET een snapshot van de huidige staat). Het
// bestand bevat tweak-Id's (+ bij multi-choice het gekozen optie-LABEL, label i.p.v.
// index zodat herordening van choices het profiel niet stilletjes corrupt maakt).
//
// Deze service doet puur de file-IO + matching tegen de huidige
// TweakService.BuildAll(). Het daadwerkelijk toepassen gebeurt elders: StageDelta
// zet alléén de tweaks die nog niet in de gewenste staat staan in TweakPending,
// waarna de bestaande TweakApplyRunner de Apply (backup-prompt + 1 UAC) afhandelt.
//
// Mirror van SelectionImportExportService (apps), maar dan voor tweaks.
public sealed class TweakProfileService
{
    private const string CurrentVersion = "1.0";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Schrijft de profiel-selectie naar JSON. selection = snapshot uit
    // App.ProfileSelection: value bool(true) = toggle opnemen, value int = choice-index.
    public async Task ExportAsync(string filePath, IReadOnlyList<KeyValuePair<Tweak, object>> selection)
    {
        var entries = selection
            .OrderBy(kv => kv.Key.Id, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                string? choice = null;
                if (kv.Value is int idx && kv.Key.Choices != null && idx >= 0 && idx < kv.Key.Choices.Count)
                {
                    // Bewust het ENGELSE label, niet het actieve: sinds v1.2.4 zijn
                    // choice-labels vertaald, en een profiel moet leesbaar blijven
                    // als je later van taal wisselt of het bestand deelt met iemand
                    // die de app in de andere taal draait. Engels is de brontaal en
                    // bestaat dus gegarandeerd. ResolveChoiceIndex accepteert bij het
                    // inlezen beide talen, zodat oudere profielen blijven werken.
                    var c = kv.Key.Choices[idx];
                    choice = App.Loc.Raw(c.Key, AppLanguage.English) ?? c.Label;
                }
                return new TweakProfileEntry { Id = kv.Key.Id, Choice = choice };
            })
            .ToList();

        var payload = new TweakProfilePayload
        {
            Version = CurrentVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            Count = entries.Count,
            Tweaks = entries
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    // Leest een profielbestand en matcht de entries tegen de huidige tweak-catalog
    // op Id. Onbekende Id's → SkippedIds (bv. profiel van een nieuwere/oudere
    // app-versie). Voor choice-entries wordt het opgeslagen label terug naar een
    // index geresolved; een label dat niet meer bestaat → ook skipped.
    public async Task<TweakProfileImportResult> ImportAsync(string filePath, IEnumerable<Tweak> catalog)
    {
        if (!File.Exists(filePath))
            return new TweakProfileImportResult(Array.Empty<TweakProfileMatch>(), Array.Empty<string>(), App.Loc.S("io.fileNotFound"));

        TweakProfilePayload? payload;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            payload = JsonSerializer.Deserialize<TweakProfilePayload>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            return new TweakProfileImportResult(Array.Empty<TweakProfileMatch>(), Array.Empty<string>(), App.Loc.S("io.jsonReadFailed", ex.Message));
        }

        if (payload?.Tweaks == null || payload.Tweaks.Count == 0)
            return new TweakProfileImportResult(Array.Empty<TweakProfileMatch>(), Array.Empty<string>(), App.Loc.S("io.noTweaksInFile"));

        var lookup = catalog
            .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<TweakProfileMatch>();
        var skipped = new List<string>();
        foreach (var entry in payload.Tweaks)
        {
            if (string.IsNullOrEmpty(entry.Id) || !lookup.TryGetValue(entry.Id, out var tweak))
            {
                if (!string.IsNullOrEmpty(entry.Id)) skipped.Add(entry.Id);
                continue;
            }

            if (tweak.IsChoice && tweak.Choices != null)
            {
                var idx = ResolveChoiceIndex(tweak, entry.Choice);
                if (idx < 0) { skipped.Add(entry.Id); continue; }   // optie bestaat niet meer
                matched.Add(new TweakProfileMatch(tweak, idx));
            }
            else
            {
                matched.Add(new TweakProfileMatch(tweak, true));    // toggle: opnemen = aanzetten
            }
        }

        return new TweakProfileImportResult(matched, skipped, null);
    }

    // Matcht het opgeslagen label tegen ALLE talen, niet alleen de actieve. Nieuwe
    // exports schrijven het Engelse label, maar profielen van vóór v1.2.4 bevatten
    // het label zoals het toen hardcoded stond — dat was een mix van Engels
    // ("Search box") en Nederlands ("5 seconden (standaard)"). Beide tabellen
    // afgaan houdt die bestanden dus gewoon importeerbaar.
    private static int ResolveChoiceIndex(Tweak tweak, string? label)
    {
        if (label == null || tweak.Choices == null) return -1;
        for (int i = 0; i < tweak.Choices.Count; i++)
        {
            var c = tweak.Choices[i];
            if (Eq(c.Label, label)
                || Eq(App.Loc.Raw(c.Key, AppLanguage.English), label)
                || Eq(App.Loc.Raw(c.Key, AppLanguage.Dutch), label))
                return i;
        }
        return -1;

        static bool Eq(string? a, string b) =>
            a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // Zet alléén de DELTA van een matched-set in de pending-store: tweaks die al
    // in de gewenste staat staan worden overgeslagen (geen redundante write/UAC).
    // Vereist dat de states al gedetecteerd zijn (App.Tweaks.DetectStatesAsync()).
    // Returnt (staged, alreadyApplied).
    public static (int staged, int alreadyApplied) StageDelta(IEnumerable<TweakProfileMatch> matched, TweakPendingService pending)
    {
        int staged = 0, already = 0;
        foreach (var m in matched)
        {
            if (m.Desired is bool b && b)
            {
                if (m.Tweak.State == TweakState.Enabled) { already++; continue; }
                pending.Set(m.Tweak, true);
                staged++;
            }
            else if (m.Desired is int idx)
            {
                if (m.Tweak.SelectedChoiceIndex == idx) { already++; continue; }
                pending.Set(m.Tweak, idx);
                staged++;
            }
        }
        return (staged, already);
    }

    // ---- JSON DTO's ----
    private sealed class TweakProfilePayload
    {
        [JsonPropertyName("version")] public string Version { get; set; } = CurrentVersion;
        [JsonPropertyName("exportedAt")] public DateTimeOffset ExportedAt { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("tweaks")] public List<TweakProfileEntry> Tweaks { get; set; } = new();
    }

    private sealed class TweakProfileEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("choice")] public string? Choice { get; set; }
    }
}

// Eén gematchte profiel-entry: de tweak + de gewenste waarde (bool true = toggle
// aanzetten, int = choice-index).
public sealed record TweakProfileMatch(Tweak Tweak, object Desired);

public sealed record TweakProfileImportResult(
    IReadOnlyList<TweakProfileMatch> Matched,
    IReadOnlyList<string> SkippedIds,
    string? Error);
