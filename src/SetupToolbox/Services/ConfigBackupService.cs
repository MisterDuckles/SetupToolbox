using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SetupToolbox.Models;
using AppModel = SetupToolbox.Models.App;

namespace SetupToolbox.Services;

// Eén-klik volledige config-backup (v1.2.9): app-keuze + tweaks + voorkeuren in
// één bestand, zodat je je complete Setup Toolbox-inrichting meeneemt naar een
// andere pc.
//
// FORMAT — bundel met een eigen versie eromheen, de bestaande payloads als
// sub-objecten mét hun eigen versie:
//
//   { version, kind, exportedAt,
//     apps:     { version, exportedAt, appCount, apps: [wingetId, ...],
//                 appDetails: [{ wingetId, name, source }] },   // alleen buiten de catalogus
//     tweaks:   { version, exportedAt, count, tweaks: [{ id, choice? }] },
//     settings: { version, values: { ... } } }
//
// Reden voor de geneste vorm in plaats van één platte lijst met één versienummer:
// de twee bestaande formats (SelectionImportExportService v0.7.4, TweakProfileService
// v0.9.20) staan allebei op "1.0" en moeten los kunnen doorgroeien. Een wijziging
// aan het tweak-deel bumpt zo níét de bundelversie, en de sub-objecten zijn
// byte-identiek aan wat de losse exporters schrijven — een `apps`-sub-object is dus
// gewoon een `my-apps.json` zonder de wrapper.
//
// LEZEN — deze service slikt alle DRIE de bestandsvormen (bundel, losse
// app-selectie, los tweak-profiel), zodat de gebruiker niet hoeft te onthouden
// welke Importeren-knop bij welk bestand hoort. Onderscheid op de vorm van de
// JSON, niet op de bestandsnaam: `kind` aanwezig → bundel; `apps` is een array →
// app-selectie; `tweaks` is een array → tweak-profiel.
//
// TWEAKS — de bundel legt de LIVE GEDETECTEERDE staat vast (welke tweaks staan er
// nú aan op deze pc), niet de handmatig samengestelde wenslijst uit profiel-modus.
// Dat is een ander begrip dan een tweak-profiel, ook al is de bestandsvorm gelijk:
// een profiel is "wat ik wil", een backup is "wat het is". De choice-labels gaan er
// als ENGELS label in — dezelfde v1.2.4-eigenschap als het losse profiel, en beide
// lopen daarvoor via TweakProfileService zodat die regel op één plek staat.
public sealed class ConfigBackupService
{
    public const string CurrentVersion = "1.0";
    public const string FileKind = "setuptoolbox-config";

    // "volg de Windows-weergavetaal" heeft in settings.json geen waarde maar een
    // afwezige key (null). In het bundelbestand is dat een expliciete sentinel:
    // "language": null zou met WhenWritingNull weggeschreven worden en dan is
    // "volg systeem" niet te onderscheiden van "dit bestand weet niets van taal".
    public const string FollowSystemLanguage = "system";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // BackupBeforeApply als leesbare naam ("Ask") in plaats van als 0/1/2.
        // Bewust alleen hier: settings.json zelf houdt zijn bestaande numerieke
        // vorm, anders zou een oude settings.json na een update niet meer laden.
        Converters = { new JsonStringEnumConverter() }
    };

    // ---- EXPORT ----

    public async Task ExportAsync(string filePath, ConfigBackupContent content)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new ConfigBundlePayload
        {
            Version = CurrentVersion,
            Kind = FileKind,
            ExportedAt = now,
            Apps = new AppsSection
            {
                ExportedAt = now,
                AppCount = content.AppIds.Count,
                Apps = content.AppIds.ToList(),
                // Alleen voor apps die NIET in de catalogus staan: daar is de id
                // alleen niet genoeg om ze bij het importeren terug te bouwen.
                AppDetails = content.AppDetails.Count == 0
                    ? null
                    : content.AppDetails
                        .Select(d => new AppDetailDto { WingetId = d.WingetId, Name = d.Name, Source = d.Source })
                        .ToList()
            },
            Tweaks = new TweaksSection
            {
                ExportedAt = now,
                Count = content.Tweaks.Count,
                Tweaks = content.Tweaks
                    .Select(t => new TweakEntryDto { Id = t.Id, Choice = t.Choice })
                    .ToList()
            },
            Settings = content.Settings == null ? null : new SettingsSection { Values = content.Settings }
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Leest de exporteerbare voorkeuren uit de settings-store. De vijf
    /// "is dit al gevraagd"-vlaggen gaan bewust NIET mee — zie het commentaar bij
    /// <see cref="ConfigSettingsValues"/>.
    /// </summary>
    public static ConfigSettingsValues CaptureSettings(SettingsService settings) => new()
    {
        FallbackToDownloadPage = settings.FallbackToDownloadPage,
        MaxParallelInstalls = settings.MaxParallelInstalls,
        ScanLeftoversAfterUninstall = settings.ScanLeftoversAfterUninstall,
        BackupBeforeApply = settings.BackupBeforeApply,
        RestorePointBeforeDeepClean = settings.RestorePointBeforeDeepClean,
        RestorePointBeforeDebloat = settings.RestorePointBeforeDebloat,
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup,
        ErrorLoggingEnabled = settings.ErrorLoggingEnabled,
        UpdateNotificationsEnabled = settings.UpdateNotificationsEnabled,
        Language = settings.Language ?? FollowSystemLanguage
    };

    /// <summary>
    /// Vult de selectie-store met de tweaks die op deze pc al aanstaan, zodat de
    /// checklist op de Tweaks-tab voorgevinkt opent. Vereist dat
    /// <see cref="TweakService.DetectStatesAsync"/> gedraaid heeft — zonder detectie
    /// staat alles nog op Unknown en blijft de store leeg.
    ///
    /// Toggle-tweaks: Enabled én Partial tellen als "aan". Partial meenemen is juist —
    /// op de doelmachine wil je 'm compleet, en StageDelta slaat 'm over als hij daar
    /// al helemaal aanstaat. Choice-tweaks: alleen als er een choice matcht
    /// (SelectedChoiceIndex >= 0); index -1 betekent dat de gebruiker een waarde heeft
    /// die niet bij onze opties hoort en die is niet reproduceerbaar.
    /// </summary>
    public static void PrefillTweakSelection(IEnumerable<Tweak> catalog, TweakPendingService selection)
    {
        selection.Clear();
        foreach (var tweak in catalog)
        {
            if (tweak.IsChoice)
            {
                if (tweak.SelectedChoiceIndex >= 0) selection.Set(tweak, tweak.SelectedChoiceIndex);
            }
            else if (tweak.State == TweakState.Enabled || tweak.State == TweakState.Partial)
            {
                selection.Set(tweak, true);
            }
        }
    }

    /// <summary>
    /// Zet de (door de gebruiker bijgestelde) selectie om in profiel-entries. Loopt
    /// via <see cref="TweakProfileService.EnglishChoiceLabel"/>, dus met dezelfde
    /// taal-onafhankelijke labels als een los tweak-profiel.
    /// </summary>
    public static List<ConfigTweakEntry> FromTweakSelection(
        IReadOnlyList<KeyValuePair<Tweak, object>> selection) => selection
        .OrderBy(kv => kv.Key.Id, StringComparer.OrdinalIgnoreCase)
        .Select(kv => new ConfigTweakEntry(
            kv.Key.Id,
            kv.Value is int idx ? TweakProfileService.EnglishChoiceLabel(kv.Key, idx) : null))
        .ToList();

    /// <summary>
    /// De geïnstalleerde apps die NIET in de catalogus staan en die je met
    /// <c>winget install --id</c> daadwerkelijk terug kunt zetten.
    ///
    /// Er valt fors wat af, en dat is de bedoeling. Gemeten op een echte machine:
    /// 128 geïnstalleerd, 21 in de catalogus, 107 erbuiten — waarvan er maar 58
    /// bruikbaar zijn. De rest zijn <c>MSIX\…</c>- en <c>ARP\…</c>-pakketten, waarvan
    /// de "id" een package-family-string of een ARP-sleutel is en geen winget-pakket,
    /// plus kale GUID's. Die aanbieden zou betekenen dat je ze aanvinkt en er op de
    /// doelmachine niets terugkomt. Dedup op id hoort erbij: eenzelfde pakket staat
    /// er soms meerdere keren in (drie regels <c>Microsoft.DotNet.SDK.10</c>).
    /// </summary>
    public static List<ConfigAppDetail> InstallableNonCatalogApps(
        IEnumerable<WingetListEntry> installed, ISet<string> catalogIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ConfigAppDetail>();
        foreach (var entry in installed)
        {
            var id = entry.Id?.Trim();
            if (string.IsNullOrEmpty(id)) continue;
            if (catalogIds.Contains(id)) continue;
            if (!IsInstallableId(id)) continue;
            if (!seen.Add(id)) continue;

            // Bij de locale-fallback in ParseListOutput is Name gelijk aan Id; dan is
            // de id zelf het beste wat we hebben en tonen we die als naam.
            var name = string.IsNullOrWhiteSpace(entry.Name) ? id : entry.Name.Trim();
            var source = string.IsNullOrWhiteSpace(entry.Source) ? DefaultSource : entry.Source.Trim();
            result.Add(new ConfigAppDetail(id, name, source));
        }
        return result;
    }

    private const string DefaultSource = "winget";

    // Let op de backslash: het prefix is "MSIX\" / "ARP\", niet "MSIX" / "ARP".
    // Zonder die slash zou een echt pakket als MSIXHero.MSIXHero ook wegvallen.
    private static bool IsInstallableId(string id) =>
        !id.StartsWith(@"MSIX\", StringComparison.OrdinalIgnoreCase)
        && !id.StartsWith(@"ARP\", StringComparison.OrdinalIgnoreCase)
        && !GuidOnly.IsMatch(id);

    private static readonly System.Text.RegularExpressions.Regex GuidOnly =
        new(@"^\{[0-9A-Fa-f-]{36}\}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // ---- LEZEN ----

    /// <summary>
    /// Leest een bundel, een losse app-selectie of een los tweak-profiel en
    /// normaliseert alle drie naar hetzelfde <see cref="ConfigBackupContent"/>.
    /// Geeft een vertaalde foutmelding terug in plaats van te gooien.
    /// </summary>
    public async Task<ConfigBackupReadResult> ReadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return new ConfigBackupReadResult(null, App.Loc.S("io.fileNotFound"));

        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            return new ConfigBackupReadResult(null, App.Loc.S("io.jsonReadFailed", ex.Message));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new ConfigBackupReadResult(null, App.Loc.S("io.notAConfigFile"));

            // Vorm 1 — de bundel van deze service.
            if (root.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && string.Equals(kind.GetString(), FileKind, StringComparison.OrdinalIgnoreCase))
            {
                var bundle = JsonSerializer.Deserialize<ConfigBundlePayload>(json, _jsonOptions);
                if (bundle == null) return new ConfigBackupReadResult(null, App.Loc.S("io.notAConfigFile"));

                return new ConfigBackupReadResult(new ConfigBackupContent(
                    bundle.ExportedAt,
                    bundle.Apps?.Apps ?? new List<string>(),
                    (bundle.Tweaks?.Tweaks ?? new List<TweakEntryDto>())
                        .Where(t => !string.IsNullOrEmpty(t.Id))
                        .Select(t => new ConfigTweakEntry(t.Id, t.Choice))
                        .ToList(),
                    bundle.Settings?.Values,
                    (bundle.Apps?.AppDetails ?? new List<AppDetailDto>())
                        .Where(d => !string.IsNullOrEmpty(d.WingetId))
                        .Select(d => new ConfigAppDetail(d.WingetId, d.Name, d.Source))
                        .ToList()), null);
            }

            // Vorm 2 — een los my-apps.json: "apps" is een ARRAY van strings.
            if (root.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
            {
                var ids = apps.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (ids.Count == 0) return new ConfigBackupReadResult(null, App.Loc.S("io.noAppsInFile"));

                return new ConfigBackupReadResult(new ConfigBackupContent(
                    ReadExportedAt(root), ids, new List<ConfigTweakEntry>(), null,
                    new List<ConfigAppDetail>()), null);
            }

            // Vorm 3 — een los my-tweaks.json: "tweaks" is een ARRAY van objecten.
            if (root.TryGetProperty("tweaks", out var tweaks) && tweaks.ValueKind == JsonValueKind.Array)
            {
                var entries = new List<ConfigTweakEntry>();
                foreach (var e in tweaks.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    if (!e.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                    var idText = id.GetString();
                    if (string.IsNullOrWhiteSpace(idText)) continue;
                    string? choice = e.TryGetProperty("choice", out var c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString()
                        : null;
                    entries.Add(new ConfigTweakEntry(idText, choice));
                }
                if (entries.Count == 0) return new ConfigBackupReadResult(null, App.Loc.S("io.noTweaksInFile"));

                return new ConfigBackupReadResult(new ConfigBackupContent(
                    ReadExportedAt(root), new List<string>(), entries, null,
                    new List<ConfigAppDetail>()), null);
            }

            return new ConfigBackupReadResult(null, App.Loc.S("io.notAConfigFile"));
        }
        catch (Exception ex)
        {
            return new ConfigBackupReadResult(null, App.Loc.S("io.jsonReadFailed", ex.Message));
        }
    }

    private static DateTimeOffset ReadExportedAt(JsonElement root) =>
        root.TryGetProperty("exportedAt", out var e)
        && e.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(e.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    // ---- TOEPASSEN ----

    /// <summary>
    /// Zet de aangevinkte onderdelen terug. Taal wordt hier NIET toegepast — dat
    /// vuurt LanguageChanged en dus een her-navigatie van de huidige pagina, midden
    /// in de aanroepende handler. De caller doet dat als laatste stap; wij melden
    /// alleen wat er zou moeten gebeuren.
    /// </summary>
    public async Task<ConfigApplyResult> ApplyAsync(
        ConfigBackupContent content,
        ConfigImportOptions options,
        AppDatabase? db,
        TweakService tweakService,
        TweakPendingService pending)
    {
        var result = new ConfigApplyResult();

        if (options.Apps && content.AppIds.Count > 0)
        {
            SelectionHelper.ClearSelection(db);
            var lookup = SelectionHelper.EnumerateAllApps(db)
                .GroupBy(a => a.WingetId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var details = content.AppDetails
                .GroupBy(d => d.WingetId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var id in content.AppIds)
            {
                // Catalogus eerst. Een app die inmiddels aan apps.json is toegevoegd
                // landt zo alsnog als gewone catalogus-app, ook al stond 'ie bij het
                // exporteren nog in appDetails.
                if (lookup.TryGetValue(id, out AppModel? app))
                {
                    app.IsSelected = true;
                    result.AppsMatched++;
                }
                else if (details.TryGetValue(id, out var detail))
                {
                    // Buiten de catalogus: opnieuw opbouwen als synthetische App,
                    // hetzelfde soort object dat een winget-repo-zoekresultaat
                    // oplevert. Description expliciet zetten, anders doet het model
                    // een catalogus-lookup en levert dat een LOC-MISS op.
                    SelectionHelper.AddExtraSelected(new AppModel
                    {
                        Name = detail.Name,
                        WingetId = detail.WingetId,
                        Source = string.IsNullOrWhiteSpace(detail.Source) ? DefaultSource : detail.Source,
                        Description = App.Loc.S("config.extraApp.desc")
                    });
                    result.AppsMatched++;
                    result.AppsExtra++;
                }
                else
                {
                    result.AppsSkipped++;
                }
            }
            result.AppsApplied = true;
        }

        if (options.Tweaks && content.Tweaks.Count > 0)
        {
            var match = TweakProfileService.MatchEntries(
                content.Tweaks.Select(t => (t.Id, t.Choice)), tweakService.All);
            result.TweaksMatched = match.Matched.Count;
            result.TweaksSkipped = match.SkippedIds.Count;

            if (match.Matched.Count > 0)
            {
                // States moeten vers zijn voor de delta-berekening, anders wordt een
                // al-actieve tweak alsnog gestaged (extra write + onnodige UAC).
                try { await tweakService.DetectStatesAsync(); } catch { }
                var (staged, already) = TweakProfileService.StageDelta(match.Matched, pending);
                result.TweaksStaged = staged;
                result.TweaksAlreadyGood = already;
            }
            result.TweaksApplied = true;
        }

        if (options.Settings && content.Settings != null)
        {
            result.SettingsCount = ApplySettings(App.Settings, content.Settings);
            result.SettingsApplied = true;
        }

        // Alleen melden dat er een taal is als er ook echt een code in staat — de
        // caller zet 'm daarna, want App.Loc.Set() vuurt een her-navigatie.
        if (options.Language && !string.IsNullOrEmpty(content.Settings?.Language))
        {
            result.LanguageCode = content.Settings.Language;
            result.LanguageApplied = true;
        }

        return result;
    }

    /// <summary>
    /// Schrijft de aanwezige voorkeuren in één keer weg (BatchSave, dus één
    /// schrijfactie in plaats van negen). Velden die in het bestand ontbreken
    /// blijven staan zoals ze waren. Taal zit hier bewust NIET bij.
    /// Returnt het aantal toegepaste voorkeuren.
    /// </summary>
    private static int ApplySettings(SettingsService settings, ConfigSettingsValues values)
    {
        var applied = 0;
        using (settings.BatchSave())
        {
            if (values.FallbackToDownloadPage is bool a) { settings.FallbackToDownloadPage = a; applied++; }
            if (values.MaxParallelInstalls is int b) { settings.MaxParallelInstalls = b; applied++; }
            if (values.ScanLeftoversAfterUninstall is bool c) { settings.ScanLeftoversAfterUninstall = c; applied++; }
            if (values.BackupBeforeApply is BackupBeforeApplyMode d) { settings.BackupBeforeApply = d; applied++; }
            if (values.RestorePointBeforeDeepClean is bool e) { settings.RestorePointBeforeDeepClean = e; applied++; }
            if (values.RestorePointBeforeDebloat is bool f) { settings.RestorePointBeforeDebloat = f; applied++; }
            if (values.CheckForUpdatesOnStartup is bool g) { settings.CheckForUpdatesOnStartup = g; applied++; }
            if (values.ErrorLoggingEnabled is bool h) { settings.ErrorLoggingEnabled = h; applied++; }
            if (values.UpdateNotificationsEnabled is bool i) { settings.UpdateNotificationsEnabled = i; applied++; }
        }
        return applied;
    }

    // ---- JSON DTO's ----

    private sealed class ConfigBundlePayload
    {
        [JsonPropertyName("version")] public string Version { get; set; } = CurrentVersion;
        [JsonPropertyName("kind")] public string Kind { get; set; } = FileKind;
        [JsonPropertyName("exportedAt")] public DateTimeOffset ExportedAt { get; set; }
        [JsonPropertyName("apps")] public AppsSection? Apps { get; set; }
        [JsonPropertyName("tweaks")] public TweaksSection? Tweaks { get; set; }
        [JsonPropertyName("settings")] public SettingsSection? Settings { get; set; }
    }

    // Identiek aan de payload van SelectionImportExportService, zodat dit
    // sub-object los geknipt een geldig my-apps.json is.
    private sealed class AppsSection
    {
        // 1.1 (v1.2.9.1): appDetails erbij. "apps" blijft bewust een platte
        // id-lijst, dus dit blok geknipt is nog steeds een geldige my-apps.json en
        // v1.2.9-bestanden blijven leesbaar.
        [JsonPropertyName("version")] public string Version { get; set; } = "1.1";
        [JsonPropertyName("exportedAt")] public DateTimeOffset ExportedAt { get; set; }
        [JsonPropertyName("appCount")] public int AppCount { get; set; }
        [JsonPropertyName("apps")] public List<string> Apps { get; set; } = new();
        [JsonPropertyName("appDetails")] public List<AppDetailDto>? AppDetails { get; set; }
    }

    private sealed class AppDetailDto
    {
        [JsonPropertyName("wingetId")] public string WingetId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("source")] public string Source { get; set; } = DefaultSource;
    }

    // Idem voor TweakProfileService.
    private sealed class TweaksSection
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "1.0";
        [JsonPropertyName("exportedAt")] public DateTimeOffset ExportedAt { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("tweaks")] public List<TweakEntryDto> Tweaks { get; set; } = new();
    }

    private sealed class TweakEntryDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("choice")] public string? Choice { get; set; }
    }

    private sealed class SettingsSection
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "1.0";
        [JsonPropertyName("values")] public ConfigSettingsValues Values { get; set; } = new();
    }
}

// De exporteerbare voorkeuren. Alles nullable zodat een bestand dat een veld niet
// kent die voorkeur op de doelmachine met rust laat in plaats van 'm op de default
// terug te zetten.
//
// BEWUST NIET IN DEZE LIJST — de vijf vlaggen die geen voorkeur zijn maar
// interactie-historie: ParallelInstallsAsked, DontAskAboutScheduling,
// DeepCleanRestorePointConfigured, DebloatRestorePointConfigured en
// ShowWelcomeBanner. Die meenemen onderdrukt op de nieuwe machine first-run-prompts
// en een welkomstbanner die de gebruiker daar nog nooit gezien heeft. De echte
// voorkeuren eronder (RestorePointBeforeDeepClean / -Debloat) gaan wél mee, dus de
// prompt verschijnt daar één keer met jouw antwoord al ingevuld.
public sealed class ConfigSettingsValues
{
    [JsonPropertyName("fallbackToDownloadPage")] public bool? FallbackToDownloadPage { get; set; }
    [JsonPropertyName("maxParallelInstalls")] public int? MaxParallelInstalls { get; set; }
    [JsonPropertyName("scanLeftoversAfterUninstall")] public bool? ScanLeftoversAfterUninstall { get; set; }
    [JsonPropertyName("backupBeforeApply")] public BackupBeforeApplyMode? BackupBeforeApply { get; set; }
    [JsonPropertyName("restorePointBeforeDeepClean")] public bool? RestorePointBeforeDeepClean { get; set; }
    [JsonPropertyName("restorePointBeforeDebloat")] public bool? RestorePointBeforeDebloat { get; set; }
    [JsonPropertyName("checkForUpdatesOnStartup")] public bool? CheckForUpdatesOnStartup { get; set; }
    [JsonPropertyName("errorLoggingEnabled")] public bool? ErrorLoggingEnabled { get; set; }
    [JsonPropertyName("updateNotificationsEnabled")] public bool? UpdateNotificationsEnabled { get; set; }

    // "en" / "nl" / "system" (= volg de Windows-weergavetaal). Telt NIET mee in de
    // settings-teller: taal krijgt bij het importeren een eigen vinkje, want een
    // gedeeld profiel zou anders stilzwijgend de UI-taal van iemand anders omzetten.
    [JsonPropertyName("language")] public string? Language { get; set; }

    /// <summary>Aantal voorkeuren dat dit bestand daadwerkelijk bevat, taal niet meegeteld.</summary>
    [JsonIgnore]
    public int Count =>
        (FallbackToDownloadPage.HasValue ? 1 : 0)
        + (MaxParallelInstalls.HasValue ? 1 : 0)
        + (ScanLeftoversAfterUninstall.HasValue ? 1 : 0)
        + (BackupBeforeApply.HasValue ? 1 : 0)
        + (RestorePointBeforeDeepClean.HasValue ? 1 : 0)
        + (RestorePointBeforeDebloat.HasValue ? 1 : 0)
        + (CheckForUpdatesOnStartup.HasValue ? 1 : 0)
        + (ErrorLoggingEnabled.HasValue ? 1 : 0)
        + (UpdateNotificationsEnabled.HasValue ? 1 : 0);
}

// Eén tweak-regel: id + bij een multi-choice tweak het Engelse optie-label.
public sealed record ConfigTweakEntry(string Id, string? Choice);

// Genormaliseerde inhoud van een ingelezen bestand, ongeacht welke van de drie
// vormen het had.
public sealed record ConfigBackupContent(
    DateTimeOffset ExportedAt,
    IReadOnlyList<string> AppIds,
    IReadOnlyList<ConfigTweakEntry> Tweaks,
    ConfigSettingsValues? Settings,
    IReadOnlyList<ConfigAppDetail> AppDetails);

// Metadata voor een app die niet in apps.json staat. Alleen de id is daar niet
// genoeg: de importkant moet er een App-object van kunnen bouwen om 'm te tonen
// en te installeren, en daarvoor zijn een naam en de winget-bron nodig.
public sealed record ConfigAppDetail(string WingetId, string Name, string Source);

public sealed record ConfigBackupReadResult(ConfigBackupContent? Content, string? Error);

// Welke onderdelen de gebruiker in de voorbeeld-dialog heeft aangevinkt.
public sealed record ConfigImportOptions(bool Apps, bool Tweaks, bool Settings, bool Language);

public sealed class ConfigApplyResult
{
    public bool AppsApplied { get; set; }
    public int AppsMatched { get; set; }
    public int AppsSkipped { get; set; }
    /// <summary>Deelverzameling van AppsMatched: apps buiten de catalogus die als
    /// synthetische extra zijn teruggezet (v1.2.9.1).</summary>
    public int AppsExtra { get; set; }

    public bool TweaksApplied { get; set; }
    public int TweaksMatched { get; set; }
    public int TweaksSkipped { get; set; }
    public int TweaksStaged { get; set; }
    public int TweaksAlreadyGood { get; set; }

    public bool SettingsApplied { get; set; }
    public int SettingsCount { get; set; }

    public bool LanguageApplied { get; set; }
    public string? LanguageCode { get; set; }

    public bool AnythingSkipped => AppsSkipped > 0 || TweaksSkipped > 0;
    public bool AnythingApplied => AppsApplied || TweaksApplied || SettingsApplied || LanguageApplied;
}
