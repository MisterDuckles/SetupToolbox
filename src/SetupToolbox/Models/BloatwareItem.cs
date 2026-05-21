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
public sealed record BloatwareMetadata(string DisplayName, string Description, string Category);

// Runtime-construct: één instance per gedetecteerd Microsoft/OEM AppX package.
// Vroeger was dit een gecureerde lijst die we matchten tegen Get-AppxPackage —
// nu draaien we het om: detect alles, optioneel verrijken met curated metadata.
public sealed class BloatwareItem : INotifyPropertyChanged
{
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public BloatwareVendor Vendor { get; }

    // Het AppX `Name`-veld (bv. "Microsoft.MicrosoftSolitaireCollection"). Bewaard
    // voor display-fallback en lookup van curated metadata.
    public string PackageName { get; }

    public BloatwareItem(string displayName, string description, string category,
                         BloatwareVendor vendor, string packageName)
    {
        DisplayName = displayName;
        Description = description;
        Category = category;
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
        ["Microsoft.MicrosoftSolitaireCollection"] = new("Solitaire Collection", "Microsoft Solitaire — bevat advertenties.", "Games"),
        ["Microsoft.XboxApp"] = new("Xbox", "Xbox companion app.", "Gaming"),
        ["Microsoft.GamingApp"] = new("Xbox (Gaming app)", "Vervanger van XboxApp op Win11.", "Gaming"),
        ["Microsoft.XboxGameOverlay"] = new("Xbox Game Overlay", "In-game overlay (FPS, screenshots).", "Gaming"),
        ["Microsoft.XboxGamingOverlay"] = new("Xbox Gaming Overlay", "Win+G game bar overlay.", "Gaming"),
        ["Microsoft.XboxIdentityProvider"] = new("Xbox Identity", "Xbox Live login broker.", "Gaming"),
        ["Microsoft.XboxSpeechToTextOverlay"] = new("Xbox Speech-to-Text", "Live captions in Xbox party chat.", "Gaming"),
        ["Microsoft.Xbox.TCUI"] = new("Xbox TCUI", "Trusted-clean UI shell voor Xbox.", "Gaming"),

        // Microsoft — Communication
        ["Microsoft.SkypeApp"] = new("Skype", "Microsoft Skype consumer-versie.", "Communication"),
        ["MicrosoftTeams"] = new("Teams (consumer)", "De gratis consumer-versie van Teams die met Win11 meekomt.", "Communication"),
        ["MSTeams"] = new("Teams (consumer)", "De gratis consumer-versie van Teams die met Win11 meekomt.", "Communication"),
        ["microsoft.windowscommunicationsapps"] = new("Mail and Calendar", "Microsoft's Mail & Calendar apps.", "Communication"),
        ["Microsoft.YourPhone"] = new("Phone Link", "Synchroniseert je Android/iPhone met Windows.", "Communication"),
        ["Microsoft.People"] = new("People", "Stand-alone contacts manager.", "Communication"),

        // Microsoft — Bing
        ["Microsoft.BingNews"] = new("Bing News", "Microsoft News (Bing-feed).", "Information"),
        ["Microsoft.BingWeather"] = new("Bing Weather", "Microsoft Weather (Bing-feed).", "Information"),

        // Microsoft — Personalization / extras
        ["Microsoft.549981C3F5F10"] = new("Cortana", "Microsoft's voice assistant.", "Personalization"),
        ["Microsoft.MixedReality.Portal"] = new("Mixed Reality Portal", "Voor Windows Mixed Reality headsets.", "Hardware"),
        ["Microsoft.Microsoft3DViewer"] = new("3D Viewer", "Bekijk 3D modellen.", "Tools"),
        ["Microsoft.MSPaint"] = new("Paint 3D", "Paint 3D — door Microsoft gedeprecateerd.", "Tools"),
        ["Microsoft.GetHelp"] = new("Get Help", "Help-app — links naar Microsoft support docs.", "Tools"),
        ["Microsoft.Getstarted"] = new("Tips", "Windows getting-started tips.", "Tools"),
        ["Microsoft.WindowsFeedbackHub"] = new("Feedback Hub", "Stuur feedback naar Microsoft.", "Tools"),
        ["Microsoft.MicrosoftOfficeHub"] = new("Office Hub", "Office app launcher.", "Productivity"),
        ["Microsoft.WindowsMaps"] = new("Maps", "Bing Maps app.", "Tools"),
        ["Microsoft.Office.OneNote"] = new("OneNote", "Microsoft OneNote (niet de Office-versie).", "Productivity"),
        ["Microsoft.ZuneMusic"] = new("Groove Music", "Microsoft's music player.", "Media"),
        ["Microsoft.ZuneVideo"] = new("Movies & TV", "Microsoft's video player.", "Media"),
        ["Microsoft.MicrosoftStickyNotes"] = new("Sticky Notes", "Sommige users vinden dit handig — let op voor je verwijdert.", "Productivity"),
        ["Microsoft.WindowsNotepad"] = new("Notepad", "Sinds Win11 een Store-app. Verwijder als je een andere editor gebruikt.", "Tools"),
        ["Microsoft.Windows.Photos"] = new("Photos", "Microsoft's foto-viewer — relatief zwaar.", "Media"),
        ["Microsoft.WindowsCamera"] = new("Camera", "Microsoft Camera — overbodig zonder webcam.", "Hardware"),
        ["Microsoft.WindowsSoundRecorder"] = new("Sound Recorder", "Microsoft Sound Recorder.", "Tools"),
        ["MicrosoftCorporationII.QuickAssist"] = new("Quick Assist", "Microsoft's remote-support tool.", "Tools"),
        ["Microsoft.PowerAutomateDesktop"] = new("Power Automate", "Robotic-process-automation tool. Voor enterprise.", "Productivity"),
        ["Clipchamp.Clipchamp"] = new("Clipchamp", "Microsoft's video editor (overgenomen 2021).", "Media"),

        // OEM — HP
        ["HP.JumpStart"] = new("HP JumpStart", "Een HP setup-tour app.", "HP"),
        ["HPInc.HPJumpStart"] = new("HP JumpStart", "Een HP setup-tour app.", "HP"),
        ["HP.SupportAssistant"] = new("HP Support Assistant", "HP's support tool — opdringerig met meldingen.", "HP"),
        ["HPInc.SupportAssistant"] = new("HP Support Assistant", "HP's support tool — opdringerig met meldingen.", "HP"),
        ["AD2F1837.HPSmart"] = new("HP Smart", "HP's printer-app. Nuttig met HP printer.", "HP"),
        ["HPInc.HPSmart"] = new("HP Smart", "HP's printer-app. Nuttig met HP printer.", "HP"),
        ["AD2F1837.HPPrinterControl"] = new("HP Printer Control", "HP printer settings app.", "HP"),
        ["HPInc.myHP"] = new("MyHP", "HP's eigen welcome-app + ad-spam.", "HP"),
        ["HP.MyHP"] = new("MyHP", "HP's eigen welcome-app + ad-spam.", "HP"),
        ["HP.QuickDrop"] = new("HP QuickDrop", "HP's bestand-naar-telefoon transfer-tool.", "HP"),
        ["AD2F1837.HPQuickDrop"] = new("HP QuickDrop", "HP's bestand-naar-telefoon transfer-tool.", "HP"),

        // OEM — Dell
        ["DellInc.DellSupportAssist"] = new("Dell SupportAssist", "Dell's support tool.", "Dell"),
        ["DellInc.DellSupportAssistforPCs"] = new("Dell SupportAssist", "Dell's support tool.", "Dell"),
        ["DellInc.DellOptimizer"] = new("Dell Optimizer", "Dell's 'AI-powered performance' tool.", "Dell"),
        ["DellInc.PartnerPromo"] = new("Dell PartnerPromo", "Trial-software van Dell partners — pure bloat.", "Dell"),

        // OEM — Lenovo
        ["E0469640.LenovoCompanion"] = new("Lenovo Vantage", "Lenovo's all-in-one settings/update app.", "Lenovo"),
        ["LenovoCorporation.LenovoVantage"] = new("Lenovo Vantage", "Lenovo's all-in-one settings/update app.", "Lenovo"),
        ["E0469640.LenovoUtility"] = new("Lenovo Utility", "Lenovo's hotkey/system-utility app.", "Lenovo"),
        ["LenovoCorporation.LenovoUtility"] = new("Lenovo Utility", "Lenovo's hotkey/system-utility app.", "Lenovo"),
        ["E0469640.LenovoSettings"] = new("Lenovo Settings", "Lenovo's settings-launcher.", "Lenovo"),
        ["LenovoCorporation.LenovoSettings"] = new("Lenovo Settings", "Lenovo's settings-launcher.", "Lenovo"),
        ["LenovoCorporation.LenovoSmartConnect"] = new("Lenovo Smart Connect", "Lenovo's phone-to-laptop sync app.", "Lenovo"),

        // OEM — ASUS
        ["B9ECED6F.ASUSPCAssistant"] = new("MyASUS", "ASUS's support/update/welcome app.", "ASUS"),
        ["AsusTekComputerInc.MyASUS"] = new("MyASUS", "ASUS's support/update/welcome app.", "ASUS"),
        ["AsusTekComputerInc.ASUSGiftBox"] = new("ASUS GiftBox", "ASUS partner-software promotie (trials).", "ASUS"),
        ["AsusTek.AsusGlideX"] = new("ASUS GlideX", "ASUS's screen-sharing tool.", "ASUS"),
        ["AsusTekComputerInc.AsusGlideX"] = new("ASUS GlideX", "ASUS's screen-sharing tool.", "ASUS"),

        // OEM — Acer
        ["AcerInc.AcerCareCenter"] = new("Acer Care Center", "Acer's support/update center.", "Acer"),
        ["AcerInc.AcerQuickAccess"] = new("Acer Quick Access", "Acer's hotkey/system-utility app.", "Acer"),
        ["AcerInc.AcerJumpStart"] = new("Acer JumpStart", "Acer's welcome/setup app.", "Acer"),

        // OEM — MSI
        ["MSI.MSICenter"] = new("MSI Center", "MSI's all-in-one support/utility app.", "MSI"),
        ["9099B36F.MSICenter"] = new("MSI Center", "MSI's all-in-one support/utility app.", "MSI"),
    };

    /// <summary>
    /// Lookup curated metadata voor een AppX package name. Returnt null als het
    /// package niet in onze metadata-dict staat — caller valt dan terug op de raw
    /// package name als display.
    /// </summary>
    public static BloatwareMetadata? LookupMetadata(string packageName) =>
        CuratedMetadata.TryGetValue(packageName, out var m) ? m : null;
}
