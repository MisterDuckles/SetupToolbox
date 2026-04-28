using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
