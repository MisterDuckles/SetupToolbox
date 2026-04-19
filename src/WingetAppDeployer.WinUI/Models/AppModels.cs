using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WingetAppDeployer_WinUI.Models;

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

public class App
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("wingetId")]
    public string WingetId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("popular")]
    public bool Popular { get; set; }

    [JsonIgnore]
    public bool IsSelected { get; set; }

    [JsonIgnore]
    public bool IsInstalled { get; set; }
}
