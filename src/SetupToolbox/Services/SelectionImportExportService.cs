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

    // ImportAsync is in v1.2.9.4 verwijderd samen met de losse Importeren-knop:
    // het lezen loopt nu volledig via ConfigBackupService.ReadAsync, die dit
    // formaat al herkent en er een voorbeeld-dialog omheen zet. ExportAsync
    // hierboven blijft wel - die schrijft nog steeds een losse my-apps.json.
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
