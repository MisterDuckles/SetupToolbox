using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WingetAppDeployer_WinUI.Services;

// JSON-backed settings store voor unpackaged WinUI app. Leeft in
// %LOCALAPPDATA%\WingetAppDeployer.WinUI\settings.json. Singleton via App.Settings.
// Save() schrijft synchroon — settings zijn klein (paar kb max) dus geen async nodig.
// Bij IO-fouten: defaults blijven actief, app crasht niet.
public sealed class SettingsService
{
    private static readonly string _settingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WingetAppDeployer.WinUI");

    private static readonly string _settingsPath = Path.Combine(_settingsDir, "settings.json");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private SettingsData _data;

    public SettingsService()
    {
        _data = Load();
    }

    // Open vendor download page voor apps met DownloadUrl bij install.
    // Default true (= huidig v0.7.1 gedrag). Wanneer false: manual-download apps
    // worden geskipt bij install met een "Manual downloads disabled" status —
    // app blijft selecteerbaar in catalog, maar browser-fallback wordt niet
    // getriggerd. Bedoeld voor users die alleen pure winget-installs willen.
    public bool FallbackToDownloadPage
    {
        get => _data.FallbackToDownloadPage;
        set
        {
            if (_data.FallbackToDownloadPage == value) return;
            _data.FallbackToDownloadPage = value;
            Save();
        }
    }

    // Onderdrukt de post-install "Schedule auto-updates?" prompt zodat user
    // niet bij elke install opnieuw gevraagd wordt. Wordt gezet wanneer user
    // op "Don't ask again" klikt. Default false zodat de prompt minstens één
    // keer verschijnt.
    public bool DontAskAboutScheduling
    {
        get => _data.DontAskAboutScheduling;
        set
        {
            if (_data.DontAskAboutScheduling == value) return;
            _data.DontAskAboutScheduling = value;
            Save();
        }
    }

    private static SettingsData Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new SettingsData();
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SettingsData>(json, _jsonOptions) ?? new SettingsData();
        }
        catch
        {
            return new SettingsData();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var json = JsonSerializer.Serialize(_data, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Settings persist is best-effort — een lock/permissions fail mag de
            // app niet crashen. Volgende keer dat user iets wijzigt proberen we
            // het opnieuw. In-memory state blijft consistent.
        }
    }

    private sealed class SettingsData
    {
        [JsonPropertyName("fallbackToDownloadPage")]
        public bool FallbackToDownloadPage { get; set; } = true;

        [JsonPropertyName("dontAskAboutScheduling")]
        public bool DontAskAboutScheduling { get; set; } = false;
    }
}
