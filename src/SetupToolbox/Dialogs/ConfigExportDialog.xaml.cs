using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Services;
using AppModel = SetupToolbox.Models.App;

namespace SetupToolbox.Dialogs;

// Kiest welke apps er in de config-backup terechtkomen (v1.2.9). De op deze pc
// GEÏNSTALLEERDE catalogus-apps staan bovenaan en zijn voorgevinkt — dat is de
// backup die je wilt als je naar een nieuwe machine verhuist — en de rest van de
// catalogus staat eronder om er extra's bij te vinken. Uitvinken kan dus ook:
// een app die je hier hebt maar niet mee wilt nemen laat je gewoon leeg.
//
// Sinds v1.2.9.1 staan er ook apps in die NIET in apps.json zitten: alles wat
// winget hier geïnstalleerd ziet en wat met `winget install --id` terug te zetten
// is. Die zijn standaard UIT — het zijn er op een echte machine tientallen, en er
// zit ruis tussen (runtimes, redistributables). De zoekbalk filtert over alle
// groepen, dus dat is de manier om er eentje bij te zoeken.
public sealed partial class ConfigExportDialog : ContentDialog
{
    private readonly List<ConfigExportAppRow> _all = new();
    private readonly ObservableCollection<ConfigExportAppRow> _visible = new();

    public ConfigExportDialog(
        IEnumerable<AppModel> catalogApps,
        ISet<string> installedIds,
        IEnumerable<ConfigAppDetail> nonCatalogApps,
        int tweakCount,
        int settingsCount)
    {
        InitializeComponent();

        foreach (var app in catalogApps
                     .GroupBy(a => a.WingetId, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()))
        {
            var installed = installedIds.Contains(app.WingetId);
            var row = new ConfigExportAppRow(app.Name, app.WingetId, installed, inCatalog: true, source: null)
            {
                IsSelected = installed
            };
            row.PropertyChanged += (_, _) => UpdateCount();
            _all.Add(row);
        }

        foreach (var extra in nonCatalogApps)
        {
            // Per definitie geïnstalleerd (ze komen uit `winget list`), maar bewust
            // niet voorgevinkt.
            var row = new ConfigExportAppRow(extra.Name, extra.WingetId, isInstalled: true,
                inCatalog: false, source: extra.Source);
            row.PropertyChanged += (_, _) => UpdateCount();
            _all.Add(row);
        }

        // Volgorde: eerst wat voorgevinkt staat (geïnstalleerde catalogus-apps),
        // dan de rest van wat op deze pc staat, dan de rest van de catalogus.
        _all.Sort((a, b) => a.SortRank != b.SortRank
            ? a.SortRank - b.SortRank
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        AppList.ItemsSource = _visible;
        ApplyFilter(string.Empty);

        TweakSummaryText.Text = App.Loc.S("config.export.tweaksIncluded",
            App.Loc.Plural("common.tweakCount", tweakCount));
        SettingsSummaryText.Text = App.Loc.S("config.export.settingsIncluded", settingsCount);
        UpdateCount();
    }

    /// <summary>De aangevinkte winget-id's, gesorteerd zoals de losse app-export dat doet.</summary>
    public List<string> SelectedAppIds => _all
        .Where(r => r.IsSelected)
        .Select(r => r.WingetId)
        .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Metadata voor de aangevinkte apps die niet in de catalogus staan —
    /// zonder naam en bron kan de import ze niet terugbouwen.
    ///
    /// Bewust een METHODE en geen property: de XAML-typegenerator loopt de publieke
    /// properties van elk XAML-type af en probeert er setters voor te genereren, en
    /// op een record met init-only properties levert dat CS8852 op. Een methode ziet
    /// 'ie niet, dus blijft ConfigAppDetail gewoon immutable.</summary>
    public List<ConfigAppDetail> GetSelectedAppDetails() => _all
        .Where(r => r.IsSelected && !r.InCatalog)
        .OrderBy(r => r.WingetId, StringComparer.OrdinalIgnoreCase)
        .Select(r => new ConfigAppDetail(r.WingetId, r.Name, r.Source ?? "winget"))
        .ToList();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ApplyFilter(sender.Text);
    }

    private void ApplyFilter(string query)
    {
        _visible.Clear();
        foreach (var row in _all.Where(r => r.Matches(query)))
            _visible.Add(row);
        NoMatchText.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Werkt op wat er ZICHTBAAR is, niet op de hele lijst — anders zet
    // "Alles selecteren" tijdens een actief filter stilletjes 160+ apps aan,
    // inclusief alle runtimes en redistributables.
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _visible) row.IsSelected = true;
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _visible) row.IsSelected = false;
    }

    private void UpdateCount()
    {
        var n = _all.Count(r => r.IsSelected);
        // Bewust GEEN gate op n == 0: een backup zonder apps is geldig, want de
        // tweaks en voorkeuren zitten er nog steeds in. De losse app-export weigert
        // een lege selectie wél — daar is de app-lijst het hele bestand.
        CountText.Text = App.Loc.S("config.export.appsSelected",
            App.Loc.Plural("common.appCount", n), _all.Count);
    }
}

// Eén regel in de app-lijst. INotifyPropertyChanged omdat de CheckBox TwoWay bindt
// en de teller onderaan mee moet lopen.
public sealed class ConfigExportAppRow : INotifyPropertyChanged
{
    public ConfigExportAppRow(string name, string wingetId, bool isInstalled, bool inCatalog, string? source)
    {
        Name = name;
        WingetId = wingetId;
        IsInstalled = isInstalled;
        InCatalog = inCatalog;
        Source = source;
    }

    public string Name { get; }
    public string WingetId { get; }
    public bool IsInstalled { get; }
    public bool InCatalog { get; }
    public string? Source { get; }

    // 0 = geïnstalleerd én in de catalogus (voorgevinkt), 1 = geïnstalleerd maar
    // erbuiten, 2 = wel in de catalogus maar niet geïnstalleerd.
    public int SortRank => InCatalog ? (IsInstalled ? 0 : 2) : 1;

    public Visibility InstalledBadgeVisibility =>
        IsInstalled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OutsideCatalogBadgeVisibility =>
        InCatalog ? Visibility.Collapsed : Visibility.Visible;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool Matches(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || WingetId.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
