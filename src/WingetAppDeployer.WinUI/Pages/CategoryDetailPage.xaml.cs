using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class CategoryDetailPage : Page
{
    private enum FilterMode { All, Popular, Installed }

    private Category? _category;
    private List<SubcategoryGroup> _allGroups = new();
    private List<SubcategoryGroup> _visibleGroups = new();
    private List<AppModel> _allApps = new();
    private List<AppModel> _visibleApps = new();
    private AppDatabase? _db;
    private FilterMode _filterMode = FilterMode.All;
    private bool _uiReady;

    public CategoryDetailPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Category category) return;

        _category = category;
        CategoryTitle.Text = string.IsNullOrWhiteSpace(category.Icon)
            ? category.Name
            : $"{category.Icon} {category.Name}";

        // Bouw groep-structuur. Categorieën zonder subcats krijgen één lege-name
        // groep (header verborgen), categorieën mét subcats krijgen één groep
        // per subcategorie met de subcat-naam als sectie-header.
        _allGroups = BuildGroups(category);
        _allApps = _allGroups.SelectMany(g => g.Apps).ToList();

        // Cache de full DB voor de globale footer (count / clear / install
        // werken cross-category).
        _db = await App.Database.GetAppDatabaseAsync();

        // _uiReady opent de filter-handlers; daarvoor blijven SearchBox- en
        // FilterModeBox-events no-ops zodat we niet dubbel renderen.
        _uiReady = true;
        ApplyFilter(SearchBox.Text);
        UpdateSelectionCount();
        UpdateSelectAllButton();

        // Installed-state detection draait async — badges popup zodra winget
        // list klaar is.
        await RefreshInstalledStateAsync();
    }

    private static List<SubcategoryGroup> BuildGroups(Category category)
    {
        var groups = new List<SubcategoryGroup>();

        // Direct apps onder de category zelf (categorieën zonder subcats:
        // Browsers, Communication, Gaming) → één lege-name groep zonder header.
        if (category.Apps != null && category.Apps.Count > 0)
            groups.Add(new SubcategoryGroup(string.Empty, category.Apps.ToList()));

        // Eén groep per subcategorie met de subcat-naam als header.
        if (category.Subcategories != null)
            foreach (var sub in category.Subcategories)
                groups.Add(new SubcategoryGroup(sub.Name, sub.Apps.ToList()));

        return groups;
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_uiReady) return;
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ApplyFilter(sender.Text);
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void FilterModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Vuurt al tijdens XAML-parse dankzij IsSelected="True". Skippen tot
        // OnNavigatedTo de UI klaar heeft staan.
        if (!_uiReady) return;
        if (FilterModeBox.SelectedIndex < 0) return;
        _filterMode = (FilterMode)FilterModeBox.SelectedIndex;
        ApplyFilter(SearchBox.Text);
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private bool MatchesFilterMode(AppModel app) => _filterMode switch
    {
        FilterMode.Popular => app.Popular,
        FilterMode.Installed => app.IsInstalled,
        _ => true,
    };

    private void ApplyFilter(string? query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        var hasSearch = trimmed.Length > 0;
        var hasModeFilter = _filterMode != FilterMode.All;

        // Per groep eerst mode-filter, dan optioneel fuzzy search filter.
        // Lege groepen verdwijnen zodra er ook maar één filter actief is, zodat
        // we geen "lege" subcat-headers tonen.
        _visibleGroups = _allGroups
            .Select(g =>
            {
                IEnumerable<AppModel> apps = g.Apps.Where(MatchesFilterMode);
                if (hasSearch)
                {
                    apps = apps
                        .Select(a => (App: a, Score: FuzzyMatcher.Score(trimmed, a.Name, a.WingetId)))
                        .Where(p => p.Score >= FuzzyMatcher.MinScore)
                        .OrderByDescending(p => p.Score)
                        .ThenBy(p => p.App.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(p => p.App);
                }
                return new SubcategoryGroup(g.Name, apps.ToList());
            })
            .Where(g => g.Apps.Count > 0 || (!hasSearch && !hasModeFilter))
            .ToList();

        _visibleApps = _visibleGroups.SelectMany(g => g.Apps).ToList();
        GroupList.ItemsSource = _visibleGroups;

        if (_visibleApps.Count == 0 && (hasSearch || hasModeFilter))
        {
            NoResultsText.Text = ComposeNoResultsMessage(trimmed, hasSearch, hasModeFilter);
            NoResultsText.Visibility = Visibility.Visible;
        }
        else
        {
            NoResultsText.Visibility = Visibility.Collapsed;
        }
    }

    private string ComposeNoResultsMessage(string query, bool hasSearch, bool hasModeFilter)
    {
        var modeLabel = _filterMode switch
        {
            FilterMode.Popular => "popular apps",
            FilterMode.Installed => "installed apps",
            _ => "apps",
        };

        if (hasSearch && hasModeFilter)
            return $"No {modeLabel} in this category matching \"{query}\"";
        if (hasSearch)
            return $"No apps in this category matching \"{query}\"";
        return $"No {modeLabel} in this category";
    }

    private async Task RefreshInstalledStateAsync(bool forceRefresh = false)
    {
        // App.IsInstalled heeft INotifyPropertyChanged, dus de "Installed"-badge
        // refresht automatisch zodra we de property zetten — geen rebind nodig.
        var installedIds = await App.Winget.GetInstalledAppIdsAsync(forceRefresh);
        foreach (var app in _allApps)
            app.IsInstalled = installedIds.Contains(app.WingetId);

        // Installed-filter draait op IsInstalled — als die filter actief is moeten
        // we de zichtbare lijst opnieuw opbouwen wanneer de installed-state binnenkomt.
        if (_filterMode == FilterMode.Installed)
        {
            ApplyFilter(SearchBox.Text);
            UpdateSelectAllButton();
        }
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void AppCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // CheckBox staat op IsHitTestVisible=False, dus de hele Grid vangt taps.
        // App heeft nu INotifyPropertyChanged, dus IsSelected toggelen propageert
        // automatisch naar de gebonden CheckBox — geen rebind nodig.
        var app = ResolveApp(sender);
        if (app == null) return;

        app.IsSelected = !app.IsSelected;
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private AppModel? ResolveApp(object sender)
    {
        if (sender is not FrameworkElement fe) return null;
        if (fe.DataContext is AppModel direct) return direct;

        // Fallback via Tag (string WingetId) zoeken in _allApps.
        if (fe.Tag is string wingetId)
            return _allApps.FirstOrDefault(a => a.WingetId == wingetId);

        return null;
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

    // Apps zonder bundled icon (bv. PuTTY) → verberg de Image zodat de
    // 40x40 reservering blijft staan maar er geen broken-image glyph komt.
    private void AppIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img)
            img.Visibility = Visibility.Collapsed;
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Werkt op de zichtbare subset zodat "Select all" de search-filter
        // respecteert. INPC propageert de IsSelected-changes naar elke CheckBox.
        if (_visibleApps.Count == 0) return;
        var allSelected = _visibleApps.All(a => a.IsSelected);
        foreach (var app in _visibleApps)
            app.IsSelected = !allSelected;

        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        // Install ALLE globally selected apps, niet alleen die van deze categorie.
        var selected = SelectionHelper.GetSelectedApps(_db);
        if (selected.Count == 0) return;

        await Helpers.ParallelInstallsPrompt.MaybeShowAsync(this.XamlRoot);

        var dialog = new InstallDialog(selected) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        foreach (var app in selected) app.IsSelected = false;
        await RefreshInstalledStateAsync(forceRefresh: true);
        UpdateSelectionCount();
        UpdateSelectAllButton();

        if (dialog.HadSuccessfulInstall)
            await Helpers.ScheduleAutoUpdatePrompt.MaybeShowAsync(this.XamlRoot);
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        SelectionHelper.ClearSelection(_db);
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void UpdateSelectionCount()
    {
        var count = SelectionHelper.GetSelectedCount(_db);
        SelectionCountText.Text = $"{count} app{(count == 1 ? "" : "s")} selected";
        InstallButton.IsEnabled = count > 0;
        ClearSelectionButton.IsEnabled = count > 0;
    }

    private void UpdateSelectAllButton()
    {
        var allSelected = _visibleApps.Count > 0 && _visibleApps.All(a => a.IsSelected);
        SelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        SelectAllButton.IsEnabled = _visibleApps.Count > 0;
    }
}
