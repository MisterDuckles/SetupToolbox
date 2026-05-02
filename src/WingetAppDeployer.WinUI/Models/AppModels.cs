using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WingetAppDeployer_WinUI.Models;

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

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("apps")]
    public List<App>? Apps { get; set; }

    [JsonPropertyName("subcategories")]
    public List<SubCategory>? Subcategories { get; set; }
}

public class SubCategory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

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

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

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
