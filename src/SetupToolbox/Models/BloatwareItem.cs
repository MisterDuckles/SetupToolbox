using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace SetupToolbox.Models;

// Onderscheid tussen Microsoft-bloatware (komt mee met Windows zelf) en OEM-bloatware
// (komt mee met de hardware-fabrikant: HP/Dell/Lenovo/etc.). Detectie-driven via
// publisher en name patterns — geen hardcoded lijst.
public enum BloatwareVendor
{
    Microsoft,
    Oem
}

// Optionele metadata voor bekende bloatware items. Als een gedetecteerd AppX-package
// in deze dict staat krijgen we een nette display-naam, beschrijving en categorie;
// anders vallen we terug op de raw package name.
//
// Sinds v1.2.5 staan naam en omschrijving niet meer hier maar in de vertaaltabel;
// dit record houdt alleen de stabiele keys over. Key is per PRODUCT, niet per
// package: dezelfde app heet op verschillende Windows-versies anders
// (HP.JumpStart / HPInc.HPJumpStart), en die aliassen delen dus één vertaling.
public sealed record BloatwareMetadata(string Key, string CategoryKey);

// Runtime-construct: één instance per gedetecteerd Microsoft/OEM AppX package.
// Vroeger was dit een gecureerde lijst die we matchten tegen Get-AppxPackage —
// nu draaien we het om: detect alles, optioneel verrijken met curated metadata.
public sealed class BloatwareItem : INotifyPropertyChanged
{
    public BloatwareVendor Vendor { get; }

    // Het AppX `Name`-veld (bv. "Microsoft.MicrosoftSolitaireCollection"). Bewaard
    // voor display-fallback en lookup van curated metadata.
    public string PackageName { get; }

    // Stabiele loc-key van het gecureerde product, of null wanneer we dit package
    // niet kennen. Vertaalde tekst bestaat alleen voor wat we kennen.
    private readonly string? _metadataKey;

    // Weergave voor een ONBEKEND package: de opgeschoonde package-naam. Die is
    // taalonafhankelijk (het is een identifier), dus die mag een gewone string zijn.
    private readonly string _fallbackDisplayName;

    public string CategoryKey { get; }

    /// <summary>Staat dit package in de curated lijst? Alleen dan is er uitleg.</summary>
    public bool IsCurated => _metadataKey != null;

    // Naam / omschrijving / categorie zijn sinds v1.2.5 VERTAALD en worden dus
    // opgezocht in plaats van in de constructor meegegeven.
    public string DisplayName => _metadataKey == null
        ? _fallbackDisplayName
        : SetupToolbox.App.Loc.S($"bloatware.{_metadataKey}.name");

    public string Description => _metadataKey == null
        ? string.Empty
        : SetupToolbox.App.Loc.S($"bloatware.{_metadataKey}.desc");

    public string Category => SetupToolbox.App.Loc.S($"bloatware.category.{CategoryKey}");

    public BloatwareItem(string? metadataKey, string fallbackDisplayName, string categoryKey,
                         BloatwareVendor vendor, string packageName)
    {
        _metadataKey = metadataKey;
        _fallbackDisplayName = fallbackDisplayName;
        CategoryKey = categoryKey;
        Vendor = vendor;
        PackageName = packageName;
    }

    private bool _isInstalled = true;
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnChanged();
            OnChanged(nameof(InstalledVisibility));
        }
    }

    public Visibility InstalledVisibility =>
        _isInstalled ? Visibility.Visible : Visibility.Collapsed;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnChanged();
        }
    }

    // Daadwerkelijke PackageFullName(s) ingevuld door BloatwareService bij detect.
    // Remove-AppxPackage heeft de FullName nodig (DisplayName + Version + Architecture
    // + ResourceId + Publisher), niet alleen de Name.
    public List<string> InstalledPackageFullNames { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ────────────────────────────────────────────────────────────
    // Curated metadata lookup
    // ────────────────────────────────────────────────────────────

    // Curated descriptions voor bekende packages. Niet meer leidend voor detectie —
    // BloatwareService detecteert alles via Get-AppxPackage en filtert op vendor.
    // Wanneer een gedetecteerde Name in deze dictionary staat krijgen we een
    // friendly display + description; anders raw package-name + lege description.
    //
    // Toevoegingen hier zijn dus PURE polish — een nieuwe Microsoft AppX die we
    // niet kennen verschijnt sowieso in de UI, alleen zonder uitleg.
    public static IReadOnlyDictionary<string, BloatwareMetadata> CuratedMetadata { get; } =
        new Dictionary<string, BloatwareMetadata>(StringComparer.OrdinalIgnoreCase)
    {
        // Microsoft — Games / Gaming
        ["Microsoft.MicrosoftSolitaireCollection"] = new("solitaireCollection", "games"),
        ["Microsoft.XboxApp"] = new("xbox", "gaming"),
        ["Microsoft.GamingApp"] = new("xboxGamingApp", "gaming"),
        ["Microsoft.XboxGameOverlay"] = new("xboxGameOverlay", "gaming"),
        ["Microsoft.XboxGamingOverlay"] = new("xboxGamingOverlay", "gaming"),
        ["Microsoft.XboxIdentityProvider"] = new("xboxIdentity", "gaming"),
        ["Microsoft.XboxSpeechToTextOverlay"] = new("xboxSpeechToText", "gaming"),
        ["Microsoft.Xbox.TCUI"] = new("xboxTcui", "gaming"),

        // Microsoft — Communication
        ["Microsoft.SkypeApp"] = new("skype", "communication"),
        ["MicrosoftTeams"] = new("teamsConsumer", "communication"),
        ["MSTeams"] = new("teamsConsumer", "communication"),
        ["microsoft.windowscommunicationsapps"] = new("mailAndCalendar", "communication"),
        ["Microsoft.YourPhone"] = new("phoneLink", "communication"),
        ["Microsoft.People"] = new("people", "communication"),

        // Microsoft — Bing
        ["Microsoft.BingNews"] = new("bingNews", "information"),
        ["Microsoft.BingWeather"] = new("bingWeather", "information"),

        // Microsoft — Personalization / extras
        ["Microsoft.549981C3F5F10"] = new("cortana", "personalization"),
        ["Microsoft.MixedReality.Portal"] = new("mixedRealityPortal", "hardware"),
        ["Microsoft.Microsoft3DViewer"] = new("3dViewer", "tools"),
        ["Microsoft.MSPaint"] = new("paint3d", "tools"),
        ["Microsoft.GetHelp"] = new("getHelp", "tools"),
        ["Microsoft.Getstarted"] = new("tips", "tools"),
        ["Microsoft.WindowsFeedbackHub"] = new("feedbackHub", "tools"),
        ["Microsoft.MicrosoftOfficeHub"] = new("officeHub", "productivity"),
        ["Microsoft.WindowsMaps"] = new("maps", "tools"),
        ["Microsoft.Office.OneNote"] = new("oneNote", "productivity"),
        ["Microsoft.ZuneMusic"] = new("grooveMusic", "media"),
        ["Microsoft.ZuneVideo"] = new("moviesTv", "media"),
        ["Microsoft.MicrosoftStickyNotes"] = new("stickyNotes", "productivity"),
        ["Microsoft.WindowsNotepad"] = new("notepad", "tools"),
        ["Microsoft.Windows.Photos"] = new("photos", "media"),
        ["Microsoft.WindowsCamera"] = new("camera", "hardware"),
        ["Microsoft.WindowsSoundRecorder"] = new("soundRecorder", "tools"),
        ["MicrosoftCorporationII.QuickAssist"] = new("quickAssist", "tools"),
        ["Microsoft.PowerAutomateDesktop"] = new("powerAutomate", "productivity"),
        ["Clipchamp.Clipchamp"] = new("clipchamp", "media"),

        // OEM — HP
        ["HP.JumpStart"] = new("hpJumpStart", "hp"),
        ["HPInc.HPJumpStart"] = new("hpJumpStart", "hp"),
        ["HP.SupportAssistant"] = new("hpSupportAssistant", "hp"),
        ["HPInc.SupportAssistant"] = new("hpSupportAssistant", "hp"),
        ["AD2F1837.HPSmart"] = new("hpSmart", "hp"),
        ["HPInc.HPSmart"] = new("hpSmart", "hp"),
        ["AD2F1837.HPPrinterControl"] = new("hpPrinterControl", "hp"),
        ["HPInc.myHP"] = new("myHP", "hp"),
        ["HP.MyHP"] = new("myHP", "hp"),
        ["HP.QuickDrop"] = new("hpQuickDrop", "hp"),
        ["AD2F1837.HPQuickDrop"] = new("hpQuickDrop", "hp"),

        // OEM — Dell
        ["DellInc.DellSupportAssist"] = new("dellSupportAssist", "dell"),
        ["DellInc.DellSupportAssistforPCs"] = new("dellSupportAssist", "dell"),
        ["DellInc.DellOptimizer"] = new("dellOptimizer", "dell"),
        ["DellInc.PartnerPromo"] = new("dellPartnerPromo", "dell"),

        // OEM — Lenovo
        ["E0469640.LenovoCompanion"] = new("lenovoVantage", "lenovo"),
        ["LenovoCorporation.LenovoVantage"] = new("lenovoVantage", "lenovo"),
        ["E0469640.LenovoUtility"] = new("lenovoUtility", "lenovo"),
        ["LenovoCorporation.LenovoUtility"] = new("lenovoUtility", "lenovo"),
        ["E0469640.LenovoSettings"] = new("lenovoSettings", "lenovo"),
        ["LenovoCorporation.LenovoSettings"] = new("lenovoSettings", "lenovo"),
        ["LenovoCorporation.LenovoSmartConnect"] = new("lenovoSmartConnect", "lenovo"),

        // OEM — ASUS
        ["B9ECED6F.ASUSPCAssistant"] = new("myASUS", "asus"),
        ["AsusTekComputerInc.MyASUS"] = new("myASUS", "asus"),
        ["AsusTekComputerInc.ASUSGiftBox"] = new("asusGiftBox", "asus"),
        ["AsusTek.AsusGlideX"] = new("asusGlideX", "asus"),
        ["AsusTekComputerInc.AsusGlideX"] = new("asusGlideX", "asus"),

        // OEM — Acer
        ["AcerInc.AcerCareCenter"] = new("acerCareCenter", "acer"),
        ["AcerInc.AcerQuickAccess"] = new("acerQuickAccess", "acer"),
        ["AcerInc.AcerJumpStart"] = new("acerJumpStart", "acer"),

        // OEM — MSI
        ["MSI.MSICenter"] = new("msiCenter", "msi"),
        ["9099B36F.MSICenter"] = new("msiCenter", "msi"),
    };

    /// <summary>
    /// Lookup curated metadata voor een AppX package name. Returnt null als het
    /// package niet in onze metadata-dict staat — caller valt dan terug op de raw
    /// package name als display.
    /// </summary>
    public static BloatwareMetadata? LookupMetadata(string packageName) =>
        CuratedMetadata.TryGetValue(packageName, out var m) ? m : null;
}
