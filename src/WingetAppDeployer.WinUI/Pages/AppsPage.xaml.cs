using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class AppsPage : Page
{
    private List<Category> _allCategories = new();
    private AppDatabase? _db;

    // Debounce voor de winget-repo search: winget search duurt ~1-2s en mag
    // niet op elke toetsaanslag vuren. 300ms is een prima balans tussen snappy
    // en niet-spammy.
    private DispatcherQueueTimer? _searchDebounce;
    private string _pendingWingetQuery = string.Empty;
    private int _wingetSearchEpoch = 0;
    private List<AppModel> _wingetResults = new();

    public AppsPage()
    {
        InitializeComponent();
        Loaded += AppsPage_Loaded;

        _searchDebounce = DispatcherQueue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(300);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += SearchDebounce_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Refresh the footer whenever the user comes back to this page — e.g.
        // returned from CategoryDetailPage after tweaking selections.
        UpdateSelectionFooter();
    }

    /// <summary>
    /// Reset de pagina naar de categorie-grid: wist de search box en laat
    /// eventuele winget-resultaten verdwijnen. Aangeroepen vanuit MainWindow
    /// wanneer de user op "Apps" in de sidebar klikt terwijl AppsPage al open
    /// staat — dan verwacht de user dat hij terug naar het rootbeeld gaat.
    /// </summary>
    public void ResetToRoot()
    {
        // Programmatisch SearchBox leegmaken firet TextChanged met reason
        // ProgrammaticChange, en die slaan we over in de handler. Dus hier
        // expliciet de filter + winget-zoekactie terugdraaien.
        SearchBox.Text = string.Empty;
        ApplyFilter(string.Empty);
        ScheduleWingetSearch(string.Empty);
    }

    private async void AppsPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        CategoryScroller.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;

        _db = await App.Database.GetAppDatabaseAsync();

        LoadingPanel.Visibility = Visibility.Collapsed;

        if (_db == null || _db.Categories.Count == 0)
        {
            ErrorBar.IsOpen = true;
            return;
        }

        _allCategories = _db.Categories;
        ApplyFilter(SearchBox.Text);
        CategoryScroller.Visibility = Visibility.Visible;
        UpdateSelectionFooter();
    }

    private void UpdateSelectionFooter()
    {
        var count = SelectionHelper.GetSelectedCount(_db);
        SelectionCountText.Text = $"{count} app{(count == 1 ? "" : "s")} selected";
        InstallButton.IsEnabled = count > 0;
        ClearSelectionButton.IsEnabled = count > 0;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectionHelper.GetSelectedApps(_db);
        if (selected.Count == 0) return;

        var dialog = new InstallDialog(selected) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        foreach (var app in selected) app.IsSelected = false;
        UpdateSelectionFooter();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        SelectionHelper.ClearSelection(_db);
        UpdateSelectionFooter();
    }

    private async void CategoryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string categoryId) return;

        var db = await App.Database.GetAppDatabaseAsync();
        var category = db?.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null) return;

        Frame.Navigate(typeof(CategoryDetailPage), category, new DrillInNavigationTransitionInfo());
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Only re-filter on actual user typing, not when we programmatically set
        // the text (e.g. on page load). The ProgrammaticChange reason flags that.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ApplyFilter(sender.Text);
        ScheduleWingetSearch(sender.Text);
    }

    private void ScheduleWingetSearch(string query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        _pendingWingetQuery = trimmed;

        // Lege query: geen winget-call, verberg sectie + clear resultaten.
        if (trimmed.Length < 2)
        {
            _searchDebounce?.Stop();
            WingetSearchingRing.Visibility = Visibility.Collapsed;
            WingetSection.Visibility = Visibility.Collapsed;
            _wingetResults = new List<AppModel>();
            WingetResultsList.ItemsSource = null;
            return;
        }

        // Toon de sectie + spinner meteen zodat user weet dat we aan het zoeken
        // zijn; de echte call wacht tot de debounce-timer afloopt.
        WingetSection.Visibility = Visibility.Visible;
        WingetEmptyText.Visibility = Visibility.Collapsed;
        WingetSearchingRing.Visibility = Visibility.Visible;

        _searchDebounce?.Stop();
        _searchDebounce?.Start();
    }

    private async void SearchDebounce_Tick(DispatcherQueueTimer sender, object args)
    {
        var query = _pendingWingetQuery;
        if (string.IsNullOrWhiteSpace(query)) return;

        // Epoch check voorkomt dat een oudere winget-call resultaten van een
        // nieuwere query overschrijft (user typt sneller dan winget searcht).
        var myEpoch = ++_wingetSearchEpoch;
        var results = await App.Winget.SearchWingetRepoAsync(query);
        if (myEpoch != _wingetSearchEpoch) return;

        // Verwijder resultaten die al in onze catalogus zitten — die ziet de user
        // immers al in de categorie-grid. Voor duplicaten re-usen we het bestaande
        // "extra"-object zodat een eerder aangevinkt item aangevinkt blijft.
        var filtered = new List<AppModel>();
        foreach (var app in results)
        {
            if (SelectionHelper.IsInCatalog(_db, app.WingetId)) continue;

            var existing = SelectionHelper.FindExtra(app.WingetId);
            filtered.Add(existing ?? app);
        }

        _wingetResults = filtered;
        WingetResultsList.ItemsSource = _wingetResults;
        WingetSearchingRing.Visibility = Visibility.Collapsed;

        if (_wingetResults.Count == 0)
        {
            WingetEmptyText.Text = $"No winget apps matching \"{query}\" (outside our curated list).";
            WingetEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            WingetEmptyText.Visibility = Visibility.Collapsed;
        }
    }

    private void CatalogCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Tag="{x:Bind}" in de DataTemplate zet het App-object op de Grid.
        // App heeft INPC, dus IsSelected wijzigen propageert direct naar de
        // CheckBox — geen rebind nodig.
        if (sender is not FrameworkElement fe || fe.Tag is not AppModel app) return;
        app.IsSelected = !app.IsSelected;
        UpdateSelectionFooter();
    }

    private void WingetCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not AppModel app) return;
        app.IsSelected = !app.IsSelected;

        // Sync de globale "extra selected" lijst: winget-result apps zitten niet
        // in onze apps.json, dus SelectionHelper moet ze apart tracken.
        if (app.IsSelected)
            SelectionHelper.AddExtraSelected(app);
        else
            SelectionHelper.RemoveExtraSelected(app.WingetId);

        UpdateSelectionFooter();
    }

    private void AppCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
            g.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
    }

    private void AppCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
            g.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

    private void ApplyFilter(string? query)
    {
        if (_allCategories.Count == 0) return;

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            // Geen search: categorie-grid is de hoofd-view, geen app-lijsten
            // of winget-sectie nodig.
            CategoryList.ItemsSource = _allCategories;
            CategoryList.Visibility = Visibility.Visible;
            CatalogResultsSection.Visibility = Visibility.Collapsed;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        // Search-modus: verberg de categorie-grid en toon een platte lijst van
        // matchende apps uit onze catalogus. Categorie-navigatie voegt tijdens
        // zoeken niks toe — user wil de app zelf direct kunnen aanvinken.
        CategoryList.Visibility = Visibility.Collapsed;
        CatalogResultsSection.Visibility = Visibility.Visible;

        // Fuzzy score per app; match op score + sort descending zodat de meest
        // relevante resultaten bovenaan staan. Naam weegt het zwaarst (meest
        // betekenisvol), description en winget-ID zijn tiebreakers.
        var matchingApps = FlattenApps()
            .Select(a => (App: a, Score: FuzzyMatcher.Score(trimmed, a.Name, a.WingetId)))
            .Where(pair => pair.Score >= FuzzyMatcher.MinScore)
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.App.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.App)
            .ToList();
        CatalogResultsList.ItemsSource = matchingApps;

        if (matchingApps.Count == 0)
        {
            CatalogEmptyText.Text = $"No apps in your curated list matching \"{trimmed}\"";
            CatalogEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            CatalogEmptyText.Visibility = Visibility.Collapsed;
        }

        // NoResultsText (de oude boven-de-grid melding) niet meer nodig — de
        // twee sectie-specifieke empty states nemen de rol over.
        NoResultsText.Visibility = Visibility.Collapsed;
    }

    private IEnumerable<AppModel> FlattenApps()
    {
        foreach (var cat in _allCategories)
        {
            if (cat.Apps != null)
                foreach (var app in cat.Apps) yield return app;
            if (cat.Subcategories != null)
                foreach (var sub in cat.Subcategories)
                    foreach (var app in sub.Apps) yield return app;
        }
    }

}
