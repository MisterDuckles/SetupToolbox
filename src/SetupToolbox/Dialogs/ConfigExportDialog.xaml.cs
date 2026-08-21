using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Dispatching;
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
// zit ruis tussen (runtimes, redistributables).
//
// Sinds v1.2.9.2 zoekt de zoekbalk ook in de winget-REPOSITORY, in een eigen
// sectie onder de lijst. Daarmee kun je ook een app in de backup zetten die je
// hier helemaal niet hebt staan — het "verhuis mijn pc"-scenario waarvoor deze
// dialog bestaat, want op de oude machine staat lang niet alles wat je op de
// nieuwe wilt.
public sealed partial class ConfigExportDialog : ContentDialog
{
    // De hoofdlijst krimpt zodra de repo-sectie eronder verschijnt. Zonder dat
    // groeit de dialog met ~190px en valt 'ie van een 768px-hoog scherm af — de
    // bug die we in deze patch juist oplossen.
    private const double ListHeightNormal = 280;
    private const double ListHeightWithRepo = 160;

    private readonly List<ConfigExportAppRow> _all = new();
    private readonly ObservableCollection<ConfigExportAppRow> _visible = new();
    private readonly ObservableCollection<ConfigExportAppRow> _repoVisible = new();

    // Overgenomen van AppsPage.ScheduleWingetSearch, niet opnieuw bedacht: 300ms
    // debounce zodat `winget search` (~1-2s) niet op elke toetsaanslag vuurt, plus
    // een epoch zodat een trage oudere zoekopdracht de resultaten van een nieuwere
    // niet overschrijft.
    private readonly DispatcherQueueTimer _searchDebounce;
    private string _pendingQuery = string.Empty;
    private int _searchEpoch;
    private bool _closed;

    // Zoekresultaten die weggedupt zijn omdat hun id al in _all staat. Winget matcht
    // ook op moniker en tag: zoek "vscode" en je krijgt Microsoft.VisualStudioCode
    // terug, die in de catalogus staat. Zonder deze set zou de repo-sectie 'm
    // wegdedupen terwijl Matches() 'm in de hoofdlijst óók niet toont — de naam en
    // de id bevatten "vscode" namelijk niet. Je zou dan niets zien terwijl de app
    // er gewoon staat.
    private readonly HashSet<string> _repoKnownHits = new(StringComparer.OrdinalIgnoreCase);

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
        // dan de rest van wat op deze pc staat, dan de rest van de catalogus, en
        // onderaan wat je uit de winget-repo hebt bijgezocht.
        _all.Sort(CompareRows);

        AppList.ItemsSource = _visible;
        RepoList.ItemsSource = _repoVisible;
        ApplyFilter(string.Empty);

        _searchDebounce = DispatcherQueue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(300);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += SearchDebounce_Tick;

        // De timer hangt NIET aan de levensduur van de dialog. Tikt 'ie ná het
        // sluiten, dan start er een winget.exe voor een venster dat niemand meer
        // ziet — en RunWingetCommandAsync heeft geen timeout, dus dat proces leeft
        // z'n eigen leven. De epoch-bump gooit meteen weg wat er nog onderweg is.
        Closed += (_, _) =>
        {
            _closed = true;
            _searchEpoch++;
            _searchDebounce.Stop();
        };

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

    private static int CompareRows(ConfigExportAppRow a, ConfigExportAppRow b) =>
        a.SortRank != b.SortRank
            ? a.SortRank - b.SortRank
            : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        // Eerst de repo-sectie bijwerken: die zet _repoKnownHits leeg, en ApplyFilter
        // leest die set.
        ScheduleRepoSearch(sender.Text);
        ApplyFilter(sender.Text);
    }

    private void ScheduleRepoSearch(string query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        _pendingQuery = trimmed;

        // Onder de twee tekens niet zoeken: winget geeft dan honderden hits terug en
        // elke toetsaanslag zou een proces starten.
        if (trimmed.Length < 2)
        {
            _searchDebounce.Stop();
            _searchEpoch++;
            _repoVisible.Clear();
            _repoKnownHits.Clear();
            RepoSection.Visibility = Visibility.Collapsed;
            RepoRing.Visibility = Visibility.Collapsed;
            AppList.Height = ListHeightNormal;
            return;
        }

        // Sectie en spinner meteen tonen zodat je ziet dát er gezocht wordt; de
        // echte aanroep wacht op de debounce.
        RepoSection.Visibility = Visibility.Visible;
        RepoEmptyText.Visibility = Visibility.Collapsed;
        RepoRing.Visibility = Visibility.Visible;
        AppList.Height = ListHeightWithRepo;

        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void SearchDebounce_Tick(DispatcherQueueTimer sender, object args)
    {
        var query = _pendingQuery;
        if (string.IsNullOrWhiteSpace(query)) return;

        var myEpoch = ++_searchEpoch;
        var results = await App.Winget.SearchWingetRepoAsync(query);

        // Bewust NIET de spinner verbergen bij een mismatch: er is dan een nieuwere
        // zoekopdracht onderweg en die doet dat zelf. Zo doet AppsPage het ook.
        if (_closed || myEpoch != _searchEpoch) return;

        var known = new HashSet<string>(_all.Select(r => r.WingetId), StringComparer.OrdinalIgnoreCase);
        _repoVisible.Clear();
        _repoKnownHits.Clear();

        foreach (var app in results)
        {
            // De bestaande rij wint altijd: die draagt de juiste InCatalog- en
            // IsInstalled-vlaggen en mogelijk al een vinkje.
            if (known.Contains(app.WingetId))
            {
                _repoKnownHits.Add(app.WingetId);
                continue;
            }

            var row = new ConfigExportAppRow(app.Name, app.WingetId, isInstalled: false,
                inCatalog: false, source: app.Source);
            row.PropertyChanged += RepoRow_Changed;
            _repoVisible.Add(row);
        }

        RepoRing.Visibility = Visibility.Collapsed;

        if (_repoVisible.Count == 0)
        {
            RepoEmptyText.Text = App.Loc.S("config.export.noRepoMatch", query);
            RepoEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            RepoEmptyText.Visibility = Visibility.Collapsed;
        }

        // Opnieuw filteren zodat de doorlaat op _repoKnownHits meteen effect heeft.
        ApplyFilter(query);
    }

    // Een aangevinkt zoekresultaat verhuist naar de hoofdlijst. Dat MOET: SelectedAppIds
    // en GetSelectedAppDetails lezen uitsluitend _all, dus een vinkje op een rij die
    // daar niet in staat zou zonder waarschuwing uit de export vallen zodra je de
    // zoekterm wist. Uitvinken laat 'm daarna gewoon staan — net als de extra-kaarten
    // op AppsPage — want anders maakt één misklik je hele zoekopdracht ongedaan.
    private void RepoRow_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ConfigExportAppRow row) return;

        if (row.IsSelected && !_all.Any(r =>
                string.Equals(r.WingetId, row.WingetId, StringComparison.OrdinalIgnoreCase)))
        {
            row.PropertyChanged -= RepoRow_Changed;
            row.PropertyChanged += (_, _) => UpdateCount();

            _repoVisible.Remove(row);
            _all.Add(row);
            _all.Sort(CompareRows);
            _repoKnownHits.Add(row.WingetId);
            ApplyFilter(_pendingQuery);
        }

        UpdateCount();
    }

    private void ApplyFilter(string query)
    {
        _visible.Clear();
        foreach (var row in _all.Where(r => r.Matches(query) || _repoKnownHits.Contains(r.WingetId)))
            _visible.Add(row);
        NoMatchText.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Werkt op wat er ZICHTBAAR is, niet op de hele lijst — anders zet
    // "Alles selecteren" tijdens een actief filter stilletjes 160+ apps aan,
    // inclusief alle runtimes en redistributables.
    //
    // Bewust NIET op de repo-sectie: die staat vol met wat winget op je zoekterm
    // teruggeeft, en één klik zou daar twintig pakketten van in je backup zetten.
    // Uit de repo pak je er gericht een of twee.
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
        //
        // "van {1} in de lijst" en niet meer "in de catalogus": van de 147 rijen
        // staan er 39 juist NIET in de catalogus (die dragen de badge), en sinds
        // deze patch kunnen er ook repo-vondsten bij komen.
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
    // erbuiten, 2 = wel in de catalogus maar niet geïnstalleerd, 3 = geen van beide.
    // Die laatste kan per definitie alleen een winget-repo-vondst zijn: alles wat
    // uit de catalogus of uit `winget list` komt valt in 0, 1 of 2.
    public int SortRank => InCatalog ? (IsInstalled ? 0 : 2) : (IsInstalled ? 1 : 3);

    public Visibility InstalledBadgeVisibility =>
        IsInstalled ? Visibility.Visible : Visibility.Collapsed;

    // Alleen op wat er op deze pc staat maar niet in de catalogus. Een repo-vondst
    // staat ook buiten de catalogus maar krijgt zijn eigen badge — anders zou 'ie
    // er twee dragen.
    public Visibility OutsideCatalogBadgeVisibility =>
        !InCatalog && IsInstalled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RepoBadgeVisibility =>
        !InCatalog && !IsInstalled ? Visibility.Visible : Visibility.Collapsed;

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
