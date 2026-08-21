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
// Alleen catalogus-apps. Een winget-id dat niet in apps.json staat heeft aan de
// importkant nergens een plek om te landen (de import zet IsSelected op een
// catalogus-app), dus die zouden gegarandeerd als "niet gevonden" terugkomen.
public sealed partial class ConfigExportDialog : ContentDialog
{
    private readonly List<ConfigExportAppRow> _all = new();
    private readonly ObservableCollection<ConfigExportAppRow> _visible = new();

    public ConfigExportDialog(
        IEnumerable<AppModel> catalogApps,
        ISet<string> installedIds,
        int tweakCount,
        int settingsCount)
    {
        InitializeComponent();

        foreach (var app in catalogApps
                     .GroupBy(a => a.WingetId, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First()))
        {
            var installed = installedIds.Contains(app.WingetId);
            var row = new ConfigExportAppRow(app.Name, app.WingetId, installed) { IsSelected = installed };
            row.PropertyChanged += (_, _) => UpdateCount();
            _all.Add(row);
        }

        // Geïnstalleerd eerst (dat is de voorgevinkte set), daarna alfabetisch.
        _all.Sort((a, b) => a.IsInstalled != b.IsInstalled
            ? (a.IsInstalled ? -1 : 1)
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
    }

    // Werkt op wat er ZICHTBAAR is, niet op de hele catalogus — anders zet
    // "Alles selecteren" tijdens een actief filter stilletjes 108 apps aan.
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
    public ConfigExportAppRow(string name, string wingetId, bool isInstalled)
    {
        Name = name;
        WingetId = wingetId;
        IsInstalled = isInstalled;
    }

    public string Name { get; }
    public string WingetId { get; }
    public bool IsInstalled { get; }

    public Visibility InstalledBadgeVisibility =>
        IsInstalled ? Visibility.Visible : Visibility.Collapsed;

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
