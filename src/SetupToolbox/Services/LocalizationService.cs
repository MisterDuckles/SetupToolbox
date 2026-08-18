using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SetupToolbox.Services;

public enum AppLanguage
{
    English,
    Dutch
}

// Vertaal-service voor de NL/EN-toggle (v1.2.2).
//
// WAAROM GEEN .resw + x:Uid — dit is gemeten, niet aangenomen (2026-08-16).
// Op een UNPACKAGED WinUI 3-app is er geen enkele manier om de app-brede taal
// te overrulen, en x:Uid volgt daardoor altijd de Windows-weergavetaal:
//   - Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride
//       -> gooit InvalidOperationException
//   - SetProcessPreferredUILanguages (Win32)
//       -> slaagt op Win32-niveau maar MRT negeert 'm; ook getest met de
//          WinAppSDK-bootstrap uitgesteld, dus "MRT was al warm" is geen
//          verklaring
//   - Windows.ApplicationModel.Resources.Core.ResourceContext
//     .SetGlobalQualifierValue
//       -> kill't het proces hard, geen vangbare exception
// Een x:Uid-toggle zou dus stil niets doen voor élke XAML-string. Vandaar een
// eigen tabel; dat volgt bovendien exact het data/apps.json-patroon dat in deze
// app al bewezen werkt (<Content> + PreserveNewest naast de exe).
//
// Engels is de brontaal: elke key bestaat gegarandeerd in strings.en.json en
// dat bestand is de fallback voor een ontbrekende Nederlandse vertaling.
public sealed class LocalizationService
{
    // Codes komen 1-op-1 terug in de bestandsnaam: data/strings.<code>.json.
    private const string EnglishCode = "en";
    private const string DutchCode = "nl";

    private readonly Dictionary<string, string> _english;
    private readonly Dictionary<string, string> _dutch;

    // null tot de eerste keer dat iemand Current leest. Lazy omdat de resolutie
    // App.Settings nodig heeft en we niet van static-init-volgorde willen afhangen.
    private AppLanguage? _current;

    // Onthoudt welke keys al als ontbrekend gelogd zijn, zodat een key die in een
    // ItemsRepeater 100x resolved niet 100 logregels oplevert.
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);

    public LocalizationService()
    {
        _english = Load(EnglishCode);
        _dutch = Load(DutchCode);
    }

    /// <summary>Vuurt nadat de taal daadwerkelijk gewisseld is.</summary>
    public event EventHandler? LanguageChanged;

    public AppLanguage Current
    {
        get => _current ??= Resolve();
    }

    /// <summary>
    /// True wanneer de gebruiker geen expliciete keuze heeft gemaakt en de app
    /// dus de Windows-weergavetaal volgt. Belangrijk onderscheid: zonder dit is
    /// "gebruiker koos Engels" niet te scheiden van "systeem is Engels", en zou
    /// een latere systeemwissel stil zijn keuze overschrijven.
    /// </summary>
    public bool FollowsSystem => App.Settings.Language == null;

    /// <summary>De taal die we zouden kiezen als we het systeem volgen.</summary>
    public static AppLanguage SystemLanguage =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals(DutchCode, StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Dutch
            : AppLanguage.English;

    /// <summary>
    /// Culture voor getallen, datums en bestandsgroottes. Bewust aan de GEKOZEN
    /// taal gekoppeld en niet aan CultureInfo.CurrentUICulture: anders toont een
    /// gebruiker op Nederlands Windows die Engels kiest nog steeds "1,5 GB".
    /// </summary>
    public CultureInfo Culture =>
        Current == AppLanguage.Dutch
            ? CultureInfo.GetCultureInfo("nl-NL")
            : CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Zet de taal. null = volg de Windows-weergavetaal. Vuurt LanguageChanged
    /// alleen wanneer de effectieve taal daadwerkelijk verandert.
    /// </summary>
    public void Set(AppLanguage? language)
    {
        var before = Current;
        App.Settings.Language = language switch
        {
            AppLanguage.English => EnglishCode,
            AppLanguage.Dutch => DutchCode,
            _ => null
        };
        _current = language ?? SystemLanguage;

        if (_current != before)
            LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Zoekt een string op. Volgorde: actieve taal → Engels (brontaal) → de key
    /// zelf. Die laatste stap is bewust zichtbaar in de UI: een ontbrekende key
    /// moet opvallen tijdens het vertalen, niet stil als lege tekst verdwijnen.
    /// </summary>
    public string S(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        var table = Current == AppLanguage.Dutch ? _dutch : _english;
        if (table.TryGetValue(key, out var value)) return value;

        if (_english.TryGetValue(key, out var fallback))
        {
            ReportMissing(key, "geen vertaling");
            return fallback;
        }

        ReportMissing(key, "onbekende key");
        return key;
    }

    /// <summary>
    /// Bestaat deze key in de brontaal? Voor optionele teksten (een tweak zonder
    /// use-case) — die mogen ontbreken en zijn dus géén LOC-MISS. Logt daarom niet.
    /// </summary>
    public bool Has(string key) => !string.IsNullOrEmpty(key) && _english.ContainsKey(key);

    /// <summary>
    /// Zoekt op in een EXPLICIETE taal in plaats van de actieve. Nodig waar een
    /// string ook buiten de UI leeft: een tweak-profiel bewaart het choice-label,
    /// en dat bestand moet leesbaar blijven als je later van taal wisselt (of het
    /// met iemand deelt die de app in de andere taal draait). Zie TweakProfileService.
    /// Geen fallback en geen LOC-MISS: dit is een vergelijking, geen weergave.
    /// </summary>
    public string? Raw(string key, AppLanguage language)
    {
        var table = language == AppLanguage.Dutch ? _dutch : _english;
        return table.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Zoekt op en formatteert met de culture van de gekozen taal, zodat getallen
    /// in de tekst bij de taal passen en niet bij de systeem-locale.
    /// </summary>
    public string S(string key, params object?[] args)
    {
        var template = S(key);
        try
        {
            return string.Format(Culture, template, args);
        }
        catch (FormatException)
        {
            // Placeholder-mismatch tussen de twee talen mag nooit de UI slopen.
            ReportMissing(key, "format-mismatch");
            return template;
        }
    }

    /// <summary>
    /// Meervoud. Kiest tussen "&lt;key&gt;.one" en "&lt;key&gt;.other" en geeft
    /// het aantal als {0} mee. Vervangt de "item(s)"-ontwijking, die in het
    /// Nederlands sowieso niet werkt.
    /// </summary>
    public string Plural(string keyBase, int count) =>
        S(count == 1 ? $"{keyBase}.one" : $"{keyBase}.other", count);

    /// <summary>
    /// Opsomming met het juiste voegwoord: "A, B and C" / "A, B en C". Dit is
    /// grammatica, geen string — vandaar een helper per taal in plaats van een
    /// vertaalde losse tekst. (Was ToastHelper.JoinDutch.)
    /// </summary>
    public string JoinList(IReadOnlyList<string> items) =>
        items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " " + S("common.listAnd") + " " + items[^1]
        };

    /// <summary>
    /// Bestandsgrootte in de culture van de GEKOZEN taal. Stond eerder vier keer
    /// in de codebase (DeepCleanDialog, DeepCleanItem, LeftoverItem en
    /// DeepCleanPage), elk op de systeem-culture — waardoor een gebruiker op
    /// Nederlands Windows die Engels kiest "1,5 GB" bleef zien.
    /// Afrondgedrag is bewust identiek aan de vier originelen: vanaf 100 geen
    /// decimaal meer, zodat "1023 MB" niet "1023,4 MB" wordt.
    /// </summary>
    public string FormatBytes(long bytes)
    {
        if (bytes <= 0) return string.Format(Culture, "{0} B", 0);
        if (bytes < 1024) return string.Format(Culture, "{0} B", bytes);

        double value = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        } while (value >= 1024 && unit < units.Length - 1);

        return string.Format(Culture, value >= 100 ? "{0:0} {1}" : "{0:0.#} {1}", value, units[unit]);
    }

    private AppLanguage Resolve() => App.Settings.Language switch
    {
        DutchCode => AppLanguage.Dutch,
        EnglishCode => AppLanguage.English,
        // Onbekende of afwezige waarde => systeemtaal, met Engels als fallback.
        _ => SystemLanguage
    };

    private static Dictionary<string, string> Load(string code)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", $"strings.{code}.json");
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            // Een kapotte of ontbrekende tabel mag de app niet omleggen — dan
            // tonen we keys. Zichtbaar kapot is beter dan een lege UI.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void ReportMissing(string key, string reason)
    {
        if (!_reportedMissing.Add(key)) return;
        Helpers.Diagnostics.Log("install.log", $"LOC-MISS {reason} lang={Current} key={key}");
    }
}
