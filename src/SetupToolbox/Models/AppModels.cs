using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SetupToolbox.Models;

// UI-construct (geen JSON-deserialisatie target): een groep apps onder dezelfde
// subcategorie-header. CategoryDetailPage rendert per groep een sectie-header
// (bij gevulde Name) plus de apps. Categorieën zonder subcats krijgen één
// groep met lege Name → header verborgen.
public sealed class SubcategoryGroup
{
    public string Name { get; }
    public List<App> Apps { get; set; }

    public SubcategoryGroup(string name, List<App> apps)
    {
        Name = name;
        Apps = apps;
    }

    public Visibility HasName =>
        string.IsNullOrEmpty(Name) ? Visibility.Collapsed : Visibility.Visible;
}

public class AppDatabase
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdated")]
    public string LastUpdated { get; set; } = string.Empty;

    [JsonPropertyName("categories")]
    public List<Category> Categories { get; set; } = new();
}

public class Category
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    // Naam en omschrijving staan sinds v1.2.6 in de vertaaltabel en niet meer in
    // apps.json. De key hangt aan de stabiele Id, dus hernoemen in de UI raakt
    // de catalogus niet en omgekeerd. Icon blijft een emoji: taalonafhankelijk.
    [JsonIgnore]
    public string Name => SetupToolbox.App.Loc.S($"appCategory.{Id}.name");

    [JsonIgnore]
    public string Description => SetupToolbox.App.Loc.S($"appCategory.{Id}.desc");

    [JsonPropertyName("apps")]
    public List<App>? Apps { get; set; }

    [JsonPropertyName("subcategories")]
    public List<SubCategory>? Subcategories { get; set; }
}

public class SubCategory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    // Zie Category.Name. Deze rendert als de SubcategoryGroup-header op
    // CategoryDetailPage.
    [JsonIgnore]
    public string Name => SetupToolbox.App.Loc.S($"appSubcategory.{Id}.name");

    // v1.2.9.6: een subcategorie heeft GEEN eigen omschrijving meer. Het veld stond
    // in apps.json en werd wel gedeserialiseerd, maar CategoryDetailPage bindt
    // alleen Name voor de groepsheader - de 24 zinnen zijn nooit door iemand gezien.
    // Weggehaald in plaats van alsnog getoond: de kaarten onder zo'n header dragen
    // zelf al een omschrijving per app, dus een tweede omschrijvingslaag maakt de
    // pagina drukker zonder nieuwe informatie. Wil je ze terug, dan horen ze in de
    // stringtabellen als appSubcategory.<id>.desc en moet check-catalog-keys.py de
    // verwachte keyset meebewegen (152 -> 176).

    [JsonPropertyName("apps")]
    public List<App> Apps { get; set; } = new();
}

// INotifyPropertyChanged op IsSelected + IsInstalled zodat x:Bind OneWay/TwoWay
// automatisch refresht wanneer we deze runtime-state wijzigen. Zonder INPC
// moest elke toggle ItemsSource=null+reassign forceren — heavy, slow, en
// triggerde verkeerde hover-events op buren bij card-rebuild.
public class App : INotifyPropertyChanged
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("wingetId")]
    public string WingetId { get; set; } = string.Empty;

    // De omschrijving staat sinds v1.2.6 in de vertaaltabel, met de WingetId als
    // stabiele key. Name blijft wél een gewoon JSON-veld: dat zijn stuk voor stuk
    // eigennamen ("Google Chrome", "VLC Media Player") die in het Nederlands
    // hetzelfde heten — 108 keys die 1-op-1 kopieën zouden zijn.
    //
    // De setter blijft bestaan omdat NIET elke App uit de catalogus komt: winget-
    // repo-zoekresultaten (WingetService.ParseSearchOutput) en de reconstructie in
    // de ge-eleveerde install-runner bouwen App-objecten die geen key hebben. Die
    // zetten hun eigen tekst; alleen als niemand iets zet valt hij terug op de
    // tabel. Zo blijft een écht ontbrekende catalogus-key wél een LOC-MISS.
    private string? _description;

    [JsonIgnore]
    public string Description
    {
        get => _description ?? SetupToolbox.App.Loc.S($"catalogApp.{WingetId}.desc");
        set => _description = value;
    }

    [JsonPropertyName("popular")]
    public bool Popular { get; set; }

    // Welke winget-source we moeten gebruiken bij install. Default = "winget"
    // (de community-repo). Voor Microsoft Store-only apps zoals WhatsApp en
    // Apple Music staat hier "msstore" — dan voegt WingetService.InstallAppAsync
    // de --source msstore vlag toe.
    [JsonPropertyName("source")]
    public string Source { get; set; } = "winget";

    // Optionele fallback voor apps die niet (goed) in winget staan — bv. VMware
    // Workstation Pro, ON1 Photo RAW, Nvidia App. Wanneer DownloadUrl niet null is,
    // skipt InstallDialog de winget-call en opent de URL in de default browser zodat
    // user de installer handmatig kan downloaden. Card toont een "Manual download"
    // badge i.p.v. de install-flow.
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    // Apps waarvan de installer weigert in een ge-eleveerde (admin) context te
    // draaien — typisch Spotify. Die zouden in de ge-eleveerde batch falen met een
    // generieke/hash-fout i.p.v. een nette "prohibits elevation"-code, waardoor
    // detectie-achteraf onbetrouwbaar is. Met deze vlag worden ze proactief
    // in-process ONGE-eleveerd geïnstalleerd (zoals msstore-apps), nooit via de batch.
    [JsonPropertyName("requiresUnelevated")]
    public bool RequiresUnelevated { get; set; }

    // Apps waarvan de installer een expliciete installatielocatie vereist — typisch
    // Battle.net. Met deze vlag toont InstallDialog proactief een pad-dialog en draait
    // `winget install --location <pad>`, i.p.v. winget eerst te laten falen en de
    // fouttekst te parsen (fragiel, taal-/manifest-afhankelijk).
    [JsonPropertyName("requiresLocation")]
    public bool RequiresLocation { get; set; }

    // Apps met een MSI-based installer die op de globale Windows Installer-mutex
    // botsen als ze parallel draaien — typisch VirtualBox, PostgreSQL en MySQL
    // Workbench (alle drie met een VCRedist-dependency). Winget meldt dan "Waiting
    // for another install/uninstall to complete..." en faalt uiteindelijk met
    // 0x8A150006 / 0x8A150102. Met deze vlag serialiseert WingetService ze: ze
    // draaien nooit gelijktijdig met een andere serialize-app, terwijl losse
    // EXE-installers gewoon parallel blijven lopen.
    [JsonPropertyName("serializeInstall")]
    public bool SerializeInstall { get; set; }

    [JsonIgnore]
    public bool IsManualDownload => !string.IsNullOrWhiteSpace(DownloadUrl);

    [JsonIgnore]
    public Visibility ManualDownloadVisibility =>
        IsManualDownload ? Visibility.Visible : Visibility.Collapsed;

    private bool _isSelected;
    [JsonIgnore]
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

    private bool _isInstalled;
    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnChanged();
        }
    }

    // Selection-state voor de Debloat-pagina, los van IsSelected (= install-selectie
    // op AppsPage / CategoryDetailPage). Beide pagina's gebruiken dezelfde App-instances
    // dankzij AppDatabaseService caching, dus zonder aparte flag zou Debloat-checkboxes
    // de install-selection vervuilen en omgekeerd.
    private bool _isSelectedForUninstall;
    [JsonIgnore]
    public bool IsSelectedForUninstall
    {
        get => _isSelectedForUninstall;
        set
        {
            if (_isSelectedForUninstall == value) return;
            _isSelectedForUninstall = value;
            OnChanged();
        }
    }

    // BitmapImage i.p.v. string-pad: x:Bind doet geen automatische conversie
    // van string → ImageSource (alleen XAML-markup wel via TypeConverter).
    // Filename = WingetId met dots vervangen door hyphens — Windows PRI parser
    // ziet anders ".64-bit.png" als scale qualifier en weigert te resolven.
    private ImageSource? _iconImage;
    [JsonIgnore]
    public ImageSource IconImage =>
        _iconImage ??= new BitmapImage(new Uri($"ms-appx:///Icons/{WingetId.Replace('.', '-')}.png"));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
