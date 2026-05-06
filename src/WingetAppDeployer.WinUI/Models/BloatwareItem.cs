using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace WingetAppDeployer_WinUI.Models;

// Onderscheid tussen Microsoft-bloatware (komt mee met Windows zelf) en OEM-bloatware
// (komt mee met de hardware-fabrikant: HP/Dell/Lenovo/etc.). Bepaalt in welke sectie
// op DebloatPage het item terecht komt — verder identiek qua model en uninstall flow.
public enum BloatwareVendor
{
    Microsoft,
    Oem
}

// Curated lijst van standaard Microsoft bloatware op Windows 10/11. PackageFamilyName
// is wat Get-AppxPackage als unieke key gebruikt; een lijst (i.p.v. één string) zodat
// we varianten van hetzelfde "concept" als één item kunnen tonen (bv. ZuneMusic +
// Microsoft.WindowsCommunicationsApps suite). DisplayName + Description zijn user-facing.
public sealed class BloatwareItem : INotifyPropertyChanged
{
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public BloatwareVendor Vendor { get; }

    // Lijst van AppX-package "families" om tegen Get-AppxPackage's `Name` property
    // te matchen (case-insensitive contains). Eén bloatware-item kan meerdere
    // packages omvatten zodat we een suite (bv. Xbox) onder één checkbox tonen.
    public IReadOnlyList<string> PackageNames { get; }

    public BloatwareItem(string displayName, string description, string category, BloatwareVendor vendor, params string[] packageNames)
    {
        DisplayName = displayName;
        Description = description;
        Category = category;
        Vendor = vendor;
        PackageNames = packageNames;
    }

    private bool _isInstalled;
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

    // Daadwerkelijke PackageFullName(s) ingevuld door BloatwareService na detect.
    // Remove-AppxPackage heeft de FullName nodig (DisplayName + Version + Architecture
    // + ResourceId + Publisher), niet alleen de Name.
    public List<string> InstalledPackageFullNames { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Curated lijst — bewust beperkt tot apps die voor de meeste users bloat zijn.
    // Per item een korte uitleg zodat user begrijpt WAT er weggaat. Wanneer een
    // app in een grijs gebied valt (bv. Sticky Notes — sommige users gebruiken het)
    // staat dat in de Description zodat ze weten waar ze nee tegen zeggen.
    //
    // Microsoft-items en OEM-items in dezelfde lijst — DebloatPage filtert op Vendor
    // om de juiste sectie te vullen. OEM-items pakken meestal een vendor-prefix
    // (HPInc, DellInc, LenovoCorporation, AsusTekComputerInc, AcerInc, MSI) zodat
    // ze niet per ongeluk Microsoft-packages matchen.
    public static IReadOnlyList<BloatwareItem> CuratedList { get; } = new List<BloatwareItem>
    {
        // ============================================================
        // Microsoft bloatware
        // ============================================================

        // Games
        new("Solitaire Collection", "Microsoft Solitaire — bevat advertenties.", "Games", BloatwareVendor.Microsoft,
            "Microsoft.MicrosoftSolitaireCollection"),

        // Xbox suite — alle Xbox-gerelateerde packages onder één item
        new("Xbox apps", "Volledige Xbox suite (app, game bar, identity, speech-to-text). Verwijder als je geen Xbox/PC games speelt.", "Gaming", BloatwareVendor.Microsoft,
            "Microsoft.XboxApp",
            "Microsoft.GamingApp",
            "Microsoft.XboxGameOverlay",
            "Microsoft.XboxGamingOverlay",
            "Microsoft.XboxIdentityProvider",
            "Microsoft.XboxSpeechToTextOverlay",
            "Microsoft.Xbox.TCUI"),

        // Communication
        new("Skype", "Microsoft Skype consumer-versie.", "Communication", BloatwareVendor.Microsoft,
            "Microsoft.SkypeApp"),
        new("Teams (consumer)", "De gratis consumer-versie van Teams die standaard met Win11 meekomt — niet de zakelijke versie.", "Communication", BloatwareVendor.Microsoft,
            "MicrosoftTeams",
            "MSTeams"),
        new("Mail and Calendar", "Microsoft's Mail & Calendar apps. Verwijder als je een andere email-client gebruikt.", "Communication", BloatwareVendor.Microsoft,
            "microsoft.windowscommunicationsapps"),

        // Bing-suite
        new("Bing News", "Microsoft News (Bing-feed).", "Information", BloatwareVendor.Microsoft,
            "Microsoft.BingNews"),
        new("Bing Weather", "Microsoft Weather (Bing-feed).", "Information", BloatwareVendor.Microsoft,
            "Microsoft.BingWeather"),

        // Personalization / extras
        new("Cortana", "Microsoft's voice assistant. Verwijder als je 'm niet gebruikt — search blijft werken.", "Personalization", BloatwareVendor.Microsoft,
            "Microsoft.549981C3F5F10"),
        new("Mixed Reality Portal", "Voor Windows Mixed Reality headsets — overbodig zonder VR hardware.", "Hardware", BloatwareVendor.Microsoft,
            "Microsoft.MixedReality.Portal"),
        new("3D Viewer", "Bekijk 3D modellen. Vrijwel nooit gebruikt door gemiddelde user.", "Tools", BloatwareVendor.Microsoft,
            "Microsoft.Microsoft3DViewer"),
        new("Paint 3D", "Vervangen door reguliere Paint app. Microsoft heeft Paint 3D zelf gedeprecateerd.", "Tools", BloatwareVendor.Microsoft,
            "Microsoft.MSPaint"),
        new("Get Help", "Help-app — links naar Microsoft support documentatie.", "Tools", BloatwareVendor.Microsoft,
            "Microsoft.GetHelp"),
        new("Tips", "Windows getting-started tips.", "Tools", BloatwareVendor.Microsoft,
            "Microsoft.Getstarted"),
        new("Feedback Hub", "Stuur feedback naar Microsoft.", "Tools", BloatwareVendor.Microsoft,
            "Microsoft.WindowsFeedbackHub"),
        new("Office Hub", "Office app launcher.", "Productivity", BloatwareVendor.Microsoft,
            "Microsoft.MicrosoftOfficeHub"),

        // Maps & Misc
        new("Maps", "Bing Maps app. Verwijder als je Google Maps / web gebruikt.", "Tools", BloatwareVendor.Microsoft,
            "Microsoft.WindowsMaps"),
        new("OneNote", "Microsoft OneNote. Verwijder als je 'm niet gebruikt — niet de OneNote uit Office.", "Productivity", BloatwareVendor.Microsoft,
            "Microsoft.Office.OneNote"),

        // Media (Groove Music / Movies & TV — vaak vervangen door Spotify / Netflix)
        new("Groove Music", "Microsoft's music player. Vrijwel altijd vervangen door Spotify/YouTube Music.", "Media", BloatwareVendor.Microsoft,
            "Microsoft.ZuneMusic"),
        new("Movies & TV", "Microsoft's video player. Vrijwel altijd vervangen door Netflix/Disney+/web.", "Media", BloatwareVendor.Microsoft,
            "Microsoft.ZuneVideo"),

        // Sticky Notes — controversial, kan handig zijn
        new("Sticky Notes", "Microsoft Sticky Notes. Sommige users vinden dit handig — let op voor je verwijdert.", "Productivity", BloatwareVendor.Microsoft,
            "Microsoft.MicrosoftStickyNotes"),

        // Your Phone / Phone Link
        new("Phone Link", "Synchroniseert je Android/iPhone met Windows. Verwijder als je geen Microsoft Phone Link setup hebt.", "Communication", BloatwareVendor.Microsoft,
            "Microsoft.YourPhone"),

        // People
        new("People", "Microsoft People app. Stand-alone contacts manager — niet hetzelfde als Outlook contacten.", "Communication", BloatwareVendor.Microsoft,
            "Microsoft.People"),

        // ============================================================
        // OEM bloatware — komt mee met de hardware. Lijst is bewust
        // conservatief; alleen apps waarvan we zeker weten dat ze ware
        // OEM-bloat zijn (geen drivers / system services). Vendor-specifieke
        // utilities zoals "HP Smart" of "MyASUS" zijn vaak wel nuttig voor
        // sommige users (firmware updates etc.) — daarom expliciet in de
        // Description vermelden zodat user weet wat ze opgeven.
        // ============================================================

        // HP
        new("HP JumpStart", "Een HP setup-tour app. Eenmalig nuttig voor de welkomtour, daarna nooit meer.", "HP", BloatwareVendor.Oem,
            "HP.JumpStart",
            "HPInc.HPJumpStart"),
        new("HP Support Assistant", "HP's support tool. Werkt parallel aan Windows Update — nuttig voor HP-firmware/driver updates, maar opdringerig met meldingen. Verwijder als je liever zelf updates checkt.", "HP", BloatwareVendor.Oem,
            "HP.SupportAssistant",
            "HPInc.SupportAssistant"),
        new("HP Smart", "HP's printer-app. Nuttig als je een HP printer hebt, anders weg.", "HP", BloatwareVendor.Oem,
            "AD2F1837.HPSmart",
            "HPInc.HPSmart"),
        new("MyHP", "HP's eigen welcome-app + ad-spam.", "HP", BloatwareVendor.Oem,
            "AD2F1837.HPPrinterControl",
            "HPInc.myHP",
            "HP.MyHP"),
        new("HP QuickDrop", "HP's bestand-naar-telefoon transfer-tool. Vrijwel niemand gebruikt dit.", "HP", BloatwareVendor.Oem,
            "HP.QuickDrop",
            "AD2F1837.HPQuickDrop"),

        // Dell
        new("Dell SupportAssist", "Dell's support tool. Werkt parallel aan Windows Update — kan je drivers/firmware updaten, maar opdringerig met meldingen. Veel users vervangen dit door handmatige Dell-driver-downloads.", "Dell", BloatwareVendor.Oem,
            "DellInc.DellSupportAssist",
            "DellInc.DellSupportAssistforPCs"),
        new("Dell Optimizer", "Dell's 'AI-powered performance' tool. Marginale impact op real-world performance.", "Dell", BloatwareVendor.Oem,
            "DellInc.DellOptimizer"),
        new("Dell PartnerPromo", "Trial-software van Dell partners (vaak McAfee, Dropbox, etc.). Pure bloat.", "Dell", BloatwareVendor.Oem,
            "DellInc.PartnerPromo"),

        // Lenovo
        new("Lenovo Vantage", "Lenovo's all-in-one settings/update app. Werkt parallel aan Windows Update — nuttig voor Lenovo-firmware, maar opdringerig.", "Lenovo", BloatwareVendor.Oem,
            "E0469640.LenovoCompanion",
            "LenovoCorporation.LenovoVantage"),
        new("Lenovo Utility", "Lenovo's hotkey/system-utility app.", "Lenovo", BloatwareVendor.Oem,
            "E0469640.LenovoUtility",
            "LenovoCorporation.LenovoUtility"),
        new("Lenovo Settings", "Lenovo's settings-launcher.", "Lenovo", BloatwareVendor.Oem,
            "E0469640.LenovoSettings",
            "LenovoCorporation.LenovoSettings"),
        new("Lenovo Smart Connect", "Lenovo's phone-to-laptop sync app — vergelijkbaar met Phone Link.", "Lenovo", BloatwareVendor.Oem,
            "LenovoCorporation.LenovoSmartConnect"),

        // ASUS
        new("MyASUS", "ASUS's support/update/welcome app. Vergelijkbaar met Lenovo Vantage.", "ASUS", BloatwareVendor.Oem,
            "B9ECED6F.ASUSPCAssistant",
            "AsusTekComputerInc.MyASUS"),
        new("ASUS GiftBox", "ASUS partner-software promotie (trials).", "ASUS", BloatwareVendor.Oem,
            "AsusTekComputerInc.ASUSGiftBox"),
        new("ASUS GlideX", "ASUS's screen-sharing tool. Niche use-case.", "ASUS", BloatwareVendor.Oem,
            "AsusTek.AsusGlideX",
            "AsusTekComputerInc.AsusGlideX"),

        // Acer
        new("Acer Care Center", "Acer's support/update center. Vergelijkbaar met andere OEM-tools.", "Acer", BloatwareVendor.Oem,
            "AcerInc.AcerCareCenter"),
        new("Acer Quick Access", "Acer's hotkey/system-utility app.", "Acer", BloatwareVendor.Oem,
            "AcerInc.AcerQuickAccess"),
        new("Acer JumpStart", "Acer's welcome/setup app. Eenmalig nuttig, daarna nooit meer.", "Acer", BloatwareVendor.Oem,
            "AcerInc.AcerJumpStart"),

        // MSI
        new("MSI Center", "MSI's all-in-one support/utility app.", "MSI", BloatwareVendor.Oem,
            "MSI.MSICenter",
            "9099B36F.MSICenter"),
    };

    // Convenience filter — DebloatPage gebruikt dit om de twee secties uit één
    // unified lijst te trekken.
    public static IEnumerable<BloatwareItem> CuratedFor(BloatwareVendor vendor) =>
        CuratedList.Where(b => b.Vendor == vendor);
}
