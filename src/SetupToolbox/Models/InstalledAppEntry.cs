using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SetupToolbox.Models;

// Source-tag voor een geïnstalleerde app op het systeem. Bepaalt zowel het
// uninstall-mechanisme (winget / Remove-AppxPackage / registry UninstallString)
// als de badge die in de UI verschijnt.
public enum InstalledSource
{
    // App is in `winget list` (door deze tool of handmatig via winget). Bonus
    // catalog-metadata (icon, description) als de app ook in onze apps.json staat.
    // Uninstall via winget, geen admin nodig voor user-installed apps.
    Winget,

    // Microsoft Store / AppX / UWP package. Detecteerd via Get-AppxPackage.
    // Uninstall via Remove-AppxPackage in elevated PS.
    Store,

    // Apps in registry uninstall keys die NIET via winget tracked worden — typische
    // vendor-installer downloads van het web (Chrome buiten winget om, oude MSI's
    // van vendor websites, dev tools die hun eigen installer meebrengen).
    // Uninstall via UninstallString in registry (elevated PS).
    Web
}

// Unified representatie van een geïnstalleerde app voor de Debloat-pagina. Drie
// sources samengevoegd in één lijst zodat user multi-select uninstall kan doen
// over alles wat op het systeem staat. Source bepaalt zowel de badge in UI als
// het uninstall-mechanisme dat in de dialog wordt aangeroepen.
public sealed class InstalledAppEntry : INotifyPropertyChanged
{
    public string DisplayName { get; }
    public InstalledSource Source { get; }

    // Source-specifieke identifier:
    //   Winget -> WingetId (bv. "Mozilla.Firefox")
    //   Store  -> PackageFullName (bv. "Microsoft.MicrosoftSolitaireCollection_4.16...")
    //   Web    -> registry sub-key path (bv. "HKLM:Uninstall\\Notepad++")
    public string Identifier { get; }

    public string Publisher { get; }
    public string Version { get; }

    // Voor Winget-apps die ook in onze apps.json staan tonen we het bundled icon.
    // Voor Store/Web (en winget apps zonder catalog-match) tonen we een generieke
    // icon (icon-fetching uit registry/AppX is duur en out-of-scope voor v0.8.4).
    public ImageSource? IconImage { get; }

    // Voor Web-source: het commando om uninstall te triggeren. Komt uit registry
    // (QuietUninstallString of UninstallString). Voor andere sources null.
    public string? UninstallCommand { get; }

    // Voor Winget-source bewaren we optioneel een referentie naar onze App-instance
    // (uit apps.json) zodat we de bundled icon kunnen tonen. Null wanneer de winget-
    // tracked app niet in onze catalog staat — dan is het nog steeds Winget-source
    // maar zonder bonus metadata.
    public App? CatalogApp { get; }

    // True voor AppX-packages die SignatureKind=System hebben (Windows-eigen
    // system components zoals AAD.BrokerPlugin, AccountsControl, BioEnrollment,
    // AsyncTextService, etc.). Default verborgen in de UI achter een "Show
    // system components" checkbox — niet veilig om te uninstallen, en zou
    // Windows-functionaliteit kunnen breken.
    public bool IsSystemComponent { get; }

    public InstalledAppEntry(
        string displayName,
        InstalledSource source,
        string identifier,
        string publisher = "",
        string version = "",
        ImageSource? iconImage = null,
        string? uninstallCommand = null,
        App? catalogApp = null,
        bool isSystemComponent = false)
    {
        DisplayName = displayName;
        Source = source;
        Identifier = identifier;
        Publisher = publisher;
        Version = version;
        IconImage = iconImage;
        UninstallCommand = uninstallCommand;
        CatalogApp = catalogApp;
        IsSystemComponent = isSystemComponent;
    }

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

    // ── UI helpers (badges, visibility) ───────────────────────────
    public string SourceBadgeText => Source switch
    {
        InstalledSource.Winget => "Winget",
        InstalledSource.Store => "Store",
        // "Web" = vendor-installer download (rechtstreeks van een website / vendor),
        // niet via een package manager. Pairt met Winget en Store als de drie
        // hoofdcategorieën waar apps vandaan komen.
        InstalledSource.Web => "Web",
        _ => string.Empty
    };

    // Tooltip voor de source-badge. Belangrijk voor "Winget": die badge betekent
    // niet noodzakelijk "via winget geïnstalleerd" maar "winget kan dit managen"
    // (winget herkent het package en kan upgrades aanbieden). Apps als JetBrains
    // PyCharm verschijnen als Winget ook al zijn ze via JetBrains Toolbox geïnstalleerd
    // — winget kan ze nog steeds matchen. Zonder hulp van winget zelf is dit
    // onderscheid niet betrouwbaar te maken.
    public string SourceTooltip => Source switch
    {
        InstalledSource.Winget => "Beheerd door winget. Kan via winget geïnstalleerd zijn, of via een andere installer (vendor / Toolbox / etc.) waarna winget het package herkent.",
        InstalledSource.Store => "Microsoft Store / AppX package. Uninstall vereist administrator rechten.",
        InstalledSource.Web => "Geïnstalleerd via een vendor-installer (MSI / EXE) — niet bekend bij winget of Microsoft Store.",
        _ => string.Empty
    };

    public Brush SourceBadgeBrush => Source switch
    {
        InstalledSource.Winget => (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"],
        InstalledSource.Store => (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
        InstalledSource.Web => (Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"],
        _ => (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"]
    };

    public Visibility IconVisibility =>
        IconImage != null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GenericIconVisibility =>
        IconImage == null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VersionVisibility =>
        string.IsNullOrWhiteSpace(Version) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SystemBadgeVisibility =>
        IsSystemComponent ? Visibility.Visible : Visibility.Collapsed;

    public string Subtitle
    {
        get
        {
            // Voor Winget-apps tonen we de winget ID + optionele version. Voor
            // Store/Web tonen we Publisher · vVersion. Beide formats zijn optioneel
            // — als velden leeg zijn vallen ze weg uit de string.
            if (Source == InstalledSource.Winget)
            {
                var parts = new System.Collections.Generic.List<string> { Identifier };
                if (!string.IsNullOrWhiteSpace(Version)) parts.Add($"v{Version}");
                return string.Join(" · ", parts);
            }
            else
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add(Publisher);
                if (!string.IsNullOrWhiteSpace(Version)) parts.Add($"v{Version}");
                return string.Join(" · ", parts);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
