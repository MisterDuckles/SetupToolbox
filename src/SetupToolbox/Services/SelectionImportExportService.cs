using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SetupToolbox.Models;

namespace SetupToolbox.Services;

// Schrijft de huidige selection naar JSON en omgekeerd. Format = lijst van
// WingetIds (apps zelf zijn al beschikbaar via apps.json bundled met de exe;
// alleen de keuze hoeft persisted). Synthetische winget-search apps zitten
// niet in de catalog dus die worden niet meegenomen — bij import vinden we
// alleen apps die bestaan in de huidige apps.json.
public sealed class SelectionImportExportService
{
    private const string CurrentVersion = "1.0";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Schrijft alle currently-selected apps (incl. extra winget-search apps)
    // naar het opgegeven pad. Gooit op IO-fouten — caller toont error.
    public async Task ExportAsync(string filePath, AppDatabase? db)
    {
        var selected = SelectionHelper.GetSelectedApps(db);
        var payload = new SelectionExportPayload
        {
            Version = CurrentVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            AppCount = selected.Count,
            Apps = selected.Select(a => a.WingetId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    // Leest een my-apps.json en zet IsSelected=true op elke matching app in
    // de catalog. Apps die niet matchen worden geteld als "skipped" zodat
    // user weet dat een paar IDs niet meer in de huidige catalog staan
    // (bv. iemand die een file van een nieuwere versie importeert).
    public async Task<SelectionImportResult> ImportAsync(string filePath, AppDatabase? db, bool clearFirst = true)
    {
        if (!File.Exists(filePath))
            return new SelectionImportResult(0, 0, App.Loc.S("io.fileNotFound"));

        SelectionExportPayload? payload;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            payload = JsonSerializer.Deserialize<SelectionExportPayload>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            return new SelectionImportResult(0, 0, App.Loc.S("io.jsonReadFailed", ex.Message));
        }

        if (payload?.Apps == null || payload.Apps.Count == 0)
            return new SelectionImportResult(0, 0, App.Loc.S("io.noAppsInFile"));

        if (clearFirst) SelectionHelper.ClearSelection(db);

        // Build lookup (case-insensitive) zodat exports met andere case nog matchen.
        var catalogLookup = SelectionHelper.EnumerateAllApps(db)
            .GroupBy(a => a.WingetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var matched = 0;
        var skipped = new List<string>();
        foreach (var wingetId in payload.Apps)
        {
            if (catalogLookup.TryGetValue(wingetId, out var app))
            {
                app.IsSelected = true;
                matched++;
            }
            else
            {
                skipped.Add(wingetId);
            }
        }

        return new SelectionImportResult(matched, skipped.Count, null, skipped);
    }

    private sealed class SelectionExportPayload
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = CurrentVersion;

        [JsonPropertyName("exportedAt")]
        public DateTimeOffset ExportedAt { get; set; }

        [JsonPropertyName("appCount")]
        public int AppCount { get; set; }

        [JsonPropertyName("apps")]
        public List<string> Apps { get; set; } = new();
    }
}

public sealed record SelectionImportResult(
    int Matched,
    int Skipped,
    string? Error = null,
    IReadOnlyList<string>? SkippedIds = null);
