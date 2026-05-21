using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SetupToolbox.Services;

// Wanneer wordt voor een Tweaks Apply een registry-snapshot gemaakt?
//   Ask    = elke keer dialoog tonen met optie "ja met optionele naam" / "nee" /
//            checkbox "vraag dit niet meer" (zet mode op Always/Never)
//   Always = altijd silent een snapshot maken zonder te vragen
//   Never  = nooit snapshots maken; "Vorige staat herstellen" knop blijft
//            werken op eerder gemaakte snapshots maar nieuwe Apply's voegen
//            niets toe aan de geschiedenis
public enum BackupBeforeApplyMode
{
    Ask = 0,
    Always = 1,
    Never = 2
}

// JSON-backed settings store voor unpackaged WinUI app. Leeft in
// %LOCALAPPDATA%\SetupToolbox\settings.json. Singleton via App.Settings.
// Save() schrijft synchroon — settings zijn klein (paar kb max) dus geen async nodig.
// Bij IO-fouten: defaults blijven actief, app crasht niet.
public sealed class SettingsService
{
    private static readonly string _settingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SetupToolbox");

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

    // Run twee winget installs tegelijk i.p.v. sequentieel. Default false omdat
    // sommige MSI-installers single-instance locks hebben en falen wanneer een
    // andere installer in dezelfde MSI-engine actief is. Voor power users die
    // typisch lossere apps installeren (Firefox, VS Code, Discord) levert het
    // ~2x snelheidswinst zonder problemen op. Hard-cap op 2 — meer dan 2
    // tegelijk wordt riskant op typische Windows-machines.
    public bool ParallelInstalls
    {
        get => _data.ParallelInstalls;
        set
        {
            if (_data.ParallelInstalls == value) return;
            _data.ParallelInstalls = value;
            Save();
        }
    }

    // Heeft user de "wil je parallel installs?" first-time prompt al beantwoord?
    // Wordt true gezet zodra user een keuze maakt — daarna nooit meer vragen.
    // Setting is bedoeld voor users die niet zelf naar Settings navigeren maar
    // wel willen profiteren van de speed-up.
    public bool ParallelInstallsAsked
    {
        get => _data.ParallelInstallsAsked;
        set
        {
            if (_data.ParallelInstallsAsked == value) return;
            _data.ParallelInstallsAsked = value;
            Save();
        }
    }

    // Triggert na een succesvolle uninstall een scan naar overgebleven sporen
    // (registry uninstall keys / Program Files / AppData) van de zojuist
    // verwijderde apps. Default true — meeste users willen netjes opruimen.
    // Wanneer false: geen scan, geen dialog, uninstall flow stopt direct na
    // de batch. User kan handmatig nog een v0.8.6 deep-clean draaien.
    public bool ScanLeftoversAfterUninstall
    {
        get => _data.ScanLeftoversAfterUninstall;
        set
        {
            if (_data.ScanLeftoversAfterUninstall == value) return;
            _data.ScanLeftoversAfterUninstall = value;
            Save();
        }
    }

    // Backup-policy voor Tweaks Apply-batches. Default Ask zodat nieuwe users
    // bewust een keuze maken bij hun eerste Apply; power-users kunnen 't naar
    // Always of Never zetten in Settings. Bij Ask toont TweaksPage een
    // BackupPromptDialog voor elke Apply.
    public BackupBeforeApplyMode BackupBeforeApply
    {
        get => _data.BackupBeforeApply;
        set
        {
            if (_data.BackupBeforeApply == value) return;
            _data.BackupBeforeApply = value;
            Save();
        }
    }

    // Maakt een Windows System Restore Point voor de elevated delete-batch van
    // Deep Clean draait. Standaard ON na first-run config (zie FirstRun-flag).
    // 24h rate-limit: Windows skipt nieuwe checkpoints binnen 24u sinds laatste —
    // niet onze keuze, geen workaround zonder system-level reg-tweak die we
    // niet stiekem willen doen.
    public bool RestorePointBeforeDeepClean
    {
        get => _data.RestorePointBeforeDeepClean;
        set
        {
            if (_data.RestorePointBeforeDeepClean == value) return;
            _data.RestorePointBeforeDeepClean = value;
            Save();
        }
    }

    // Heeft user de first-run "wil je restore points voor Deep Clean?" prompt
    // al beantwoord? Zo niet, toont DeepCleanPage de prompt voor de eerste
    // scan/delete operatie en gebruikt de keuze om RestorePointBeforeDeepClean
    // te zetten + deze flag op true.
    public bool DeepCleanRestorePointConfigured
    {
        get => _data.DeepCleanRestorePointConfigured;
        set
        {
            if (_data.DeepCleanRestorePointConfigured == value) return;
            _data.DeepCleanRestorePointConfigured = value;
            Save();
        }
    }

    // Idem als RestorePointBeforeDeepClean maar voor de Debloat-tab (uninstalls
    // + bloatware verwijderingen). Default ON na first-run config.
    public bool RestorePointBeforeDebloat
    {
        get => _data.RestorePointBeforeDebloat;
        set
        {
            if (_data.RestorePointBeforeDebloat == value) return;
            _data.RestorePointBeforeDebloat = value;
            Save();
        }
    }

    public bool DebloatRestorePointConfigured
    {
        get => _data.DebloatRestorePointConfigured;
        set
        {
            if (_data.DebloatRestorePointConfigured == value) return;
            _data.DebloatRestorePointConfigured = value;
            Save();
        }
    }

    // Self-update (v0.10.1): bij startup checken op een nieuwere GitHub-release.
    // Default true. Bij false → geen achtergrond-check; user kan nog handmatig
    // in Settings checken.
    public bool CheckForUpdatesOnStartup
    {
        get => _data.CheckForUpdatesOnStartup;
        set
        {
            if (_data.CheckForUpdatesOnStartup == value) return;
            _data.CheckForUpdatesOnStartup = value;
            Save();
        }
    }

    // Toont de welkomstbanner op de Apps-pagina. Default true; user dismisst 'm
    // via de X (zet deze op false) of via Settings.
    public bool ShowWelcomeBanner
    {
        get => _data.ShowWelcomeBanner;
        set
        {
            if (_data.ShowWelcomeBanner == value) return;
            _data.ShowWelcomeBanner = value;
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

        [JsonPropertyName("parallelInstalls")]
        public bool ParallelInstalls { get; set; } = false;

        [JsonPropertyName("parallelInstallsAsked")]
        public bool ParallelInstallsAsked { get; set; } = false;

        [JsonPropertyName("scanLeftoversAfterUninstall")]
        public bool ScanLeftoversAfterUninstall { get; set; } = true;

        [JsonPropertyName("backupBeforeApply")]
        public BackupBeforeApplyMode BackupBeforeApply { get; set; } = BackupBeforeApplyMode.Ask;

        [JsonPropertyName("restorePointBeforeDeepClean")]
        public bool RestorePointBeforeDeepClean { get; set; } = true;

        [JsonPropertyName("deepCleanRestorePointConfigured")]
        public bool DeepCleanRestorePointConfigured { get; set; } = false;

        [JsonPropertyName("restorePointBeforeDebloat")]
        public bool RestorePointBeforeDebloat { get; set; } = true;

        [JsonPropertyName("debloatRestorePointConfigured")]
        public bool DebloatRestorePointConfigured { get; set; } = false;

        [JsonPropertyName("checkForUpdatesOnStartup")]
        public bool CheckForUpdatesOnStartup { get; set; } = true;

        [JsonPropertyName("showWelcomeBanner")]
        public bool ShowWelcomeBanner { get; set; } = true;
    }
}
