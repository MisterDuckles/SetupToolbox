using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace WingetAppDeployer_WinUI.Models;

// Curated lijst van standaard Microsoft bloatware op Windows 10/11. PackageFamilyName
// is wat Get-AppxPackage als unieke key gebruikt; een lijst (i.p.v. één string) zodat
// we varianten van hetzelfde "concept" als één item kunnen tonen (bv. ZuneMusic +
// Microsoft.WindowsCommunicationsApps suite). DisplayName + Description zijn user-facing.
public sealed class BloatwareItem : INotifyPropertyChanged
{
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }

    // Lijst van AppX-package "families" om tegen Get-AppxPackage's `Name` property
    // te matchen (case-insensitive contains). Eén bloatware-item kan meerdere
    // packages omvatten zodat we een suite (bv. Xbox) onder één checkbox tonen.
    public IReadOnlyList<string> PackageNames { get; }

    public BloatwareItem(string displayName, string description, string category, params string[] packageNames)
    {
        DisplayName = displayName;
        Description = description;
        Category = category;
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
    public static IReadOnlyList<BloatwareItem> CuratedList { get; } = new List<BloatwareItem>
    {
        // Games
        new("Solitaire Collection", "Microsoft Solitaire — bevat advertenties.", "Games",
            "Microsoft.MicrosoftSolitaireCollection"),

        // Xbox suite — alle Xbox-gerelateerde packages onder één item
        new("Xbox apps", "Volledige Xbox suite (app, game bar, identity, speech-to-text). Verwijder als je geen Xbox/PC games speelt.", "Gaming",
            "Microsoft.XboxApp",
            "Microsoft.GamingApp",
            "Microsoft.XboxGameOverlay",
            "Microsoft.XboxGamingOverlay",
            "Microsoft.XboxIdentityProvider",
            "Microsoft.XboxSpeechToTextOverlay",
            "Microsoft.Xbox.TCUI"),

        // Communication
        new("Skype", "Microsoft Skype consumer-versie.", "Communication",
            "Microsoft.SkypeApp"),
        new("Teams (consumer)", "De gratis consumer-versie van Teams die standaard met Win11 meekomt — niet de zakelijke versie.", "Communication",
            "MicrosoftTeams",
            "MSTeams"),
        new("Mail and Calendar", "Microsoft's Mail & Calendar apps. Verwijder als je een andere email-client gebruikt.", "Communication",
            "microsoft.windowscommunicationsapps"),

        // Bing-suite
        new("Bing News", "Microsoft News (Bing-feed).", "Information",
            "Microsoft.BingNews"),
        new("Bing Weather", "Microsoft Weather (Bing-feed).", "Information",
            "Microsoft.BingWeather"),

        // Personalization / extras
        new("Cortana", "Microsoft's voice assistant. Verwijder als je 'm niet gebruikt — search blijft werken.", "Personalization",
            "Microsoft.549981C3F5F10"),
        new("Mixed Reality Portal", "Voor Windows Mixed Reality headsets — overbodig zonder VR hardware.", "Hardware",
            "Microsoft.MixedReality.Portal"),
        new("3D Viewer", "Bekijk 3D modellen. Vrijwel nooit gebruikt door gemiddelde user.", "Tools",
            "Microsoft.Microsoft3DViewer"),
        new("Paint 3D", "Vervangen door reguliere Paint app. Microsoft heeft Paint 3D zelf gedeprecateerd.", "Tools",
            "Microsoft.MSPaint"),
        new("Get Help", "Help-app — links naar Microsoft support documentatie.", "Tools",
            "Microsoft.GetHelp"),
        new("Tips", "Windows getting-started tips.", "Tools",
            "Microsoft.Getstarted"),
        new("Feedback Hub", "Stuur feedback naar Microsoft.", "Tools",
            "Microsoft.WindowsFeedbackHub"),
        new("Office Hub", "Office app launcher.", "Productivity",
            "Microsoft.MicrosoftOfficeHub"),

        // Maps & Misc
        new("Maps", "Bing Maps app. Verwijder als je Google Maps / web gebruikt.", "Tools",
            "Microsoft.WindowsMaps"),
        new("OneNote", "Microsoft OneNote. Verwijder als je 'm niet gebruikt — niet de OneNote uit Office.", "Productivity",
            "Microsoft.Office.OneNote"),

        // Media (Groove Music / Movies & TV — vaak vervangen door Spotify / Netflix)
        new("Groove Music", "Microsoft's music player. Vrijwel altijd vervangen door Spotify/YouTube Music.", "Media",
            "Microsoft.ZuneMusic"),
        new("Movies & TV", "Microsoft's video player. Vrijwel altijd vervangen door Netflix/Disney+/web.", "Media",
            "Microsoft.ZuneVideo"),

        // Sticky Notes — controversial, kan handig zijn
        new("Sticky Notes", "Microsoft Sticky Notes. Sommige users vinden dit handig — let op voor je verwijdert.", "Productivity",
            "Microsoft.MicrosoftStickyNotes"),

        // Your Phone / Phone Link
        new("Phone Link", "Synchroniseert je Android/iPhone met Windows. Verwijder als je geen Microsoft Phone Link setup hebt.", "Communication",
            "Microsoft.YourPhone"),

        // People
        new("People", "Microsoft People app. Stand-alone contacts manager — niet hetzelfde als Outlook contacten.", "Communication",
            "Microsoft.People"),
    };
}
