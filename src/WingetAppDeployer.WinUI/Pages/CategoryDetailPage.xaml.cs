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
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class CategoryDetailPage : Page
{
    private Category? _category;
    private List<AppModel> _allApps = new();
    private List<AppModel> _visibleApps = new();
    private AppDatabase? _db;

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

        // Flatten apps across subcategories for v0.3.0. Subcategory grouping
        // comes later.
        _allApps = new List<AppModel>();
        if (category.Apps != null) _allApps.AddRange(category.Apps);
        if (category.Subcategories != null)
            foreach (var sub in category.Subcategories)
                _allApps.AddRange(sub.Apps);

        // Cache the full DB so the footer can count / clear / collect selections
        // across every category (global selection, not per-page).
        _db = await App.Database.GetAppDatabaseAsync();

        ApplyFilter(SearchBox.Text);
        UpdateSelectionCount();
        UpdateSelectAllButton();

        // Kick off installed-state detection in the background so the page
        // renders immediately; badges pop in once winget list returns.
        await RefreshInstalledStateAsync();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ApplyFilter(sender.Text);
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void ApplyFilter(string? query)
    {
        var trimmed = (query ?? string.Empty).Trim();
        _visibleApps = trimmed.Length == 0
            ? _allApps
            : _allApps.Where(a => Matches(a, trimmed)).ToList();

        AppList.ItemsSource = _visibleApps;

        if (_visibleApps.Count == 0 && trimmed.Length > 0)
        {
            NoResultsText.Text = $"No apps in this category matching \"{trimmed}\"";
            NoResultsText.Visibility = Visibility.Visible;
        }
        else
        {
            NoResultsText.Visibility = Visibility.Collapsed;
        }
    }

    private static bool Matches(AppModel a, string query) =>
        Contains(a.Name, query) || Contains(a.Description, query) || Contains(a.WingetId, query);

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private async Task RefreshInstalledStateAsync(bool forceRefresh = false)
    {
        var installedIds = await App.Winget.GetInstalledAppIdsAsync(forceRefresh);
        var changed = false;
        foreach (var app in _allApps)
        {
            var nowInstalled = installedIds.Contains(app.WingetId);
            if (app.IsInstalled != nowInstalled)
            {
                app.IsInstalled = nowInstalled;
                changed = true;
            }
        }

        if (changed)
        {
            // Re-trigger x:Bind evaluation for the list by reassigning. Keep the
            // active filter so search results don't jump back to the full list.
            AppList.ItemsSource = null;
            AppList.ItemsSource = _visibleApps;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void AppCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // De hele kaart is klikbaar (CheckBox staat op IsHitTestVisible=False).
        // Via Tag="{x:Bind}" in de DataTemplate zit het App-object op de Grid,
        // wat betrouwbaarder is dan FrameworkElement.DataContext in
        // ItemsRepeater + x:DataType (daar blijft die soms null).
        if (sender is FrameworkElement fe && fe.Tag is AppModel app)
        {
            app.IsSelected = !app.IsSelected;

            // Re-bind zodat de CheckBox z'n nieuwe IsSelected oppikt — App
            // implementeert geen INotifyPropertyChanged, dus TwoWay x:Bind
            // krijgt de wijziging anders niet mee.
            AppList.ItemsSource = null;
            AppList.ItemsSource = _visibleApps;

            UpdateSelectionCount();
            UpdateSelectAllButton();
        }
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

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Operate on the currently visible subset so "Select all" respects the
        // active search filter — if a user typed "jetbrains" they only want to
        // toggle JetBrains apps, not every app in the category.
        if (_visibleApps.Count == 0) return;
        var allSelected = _visibleApps.All(a => a.IsSelected);
        foreach (var app in _visibleApps)
            app.IsSelected = !allSelected;

        // Refresh ItemsRepeater by reassigning the source
        AppList.ItemsSource = null;
        AppList.ItemsSource = _visibleApps;

        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        // Install ALL globally selected apps, not just the ones in this category.
        // Otherwise a user that selected Chrome in Browsers and Notepad++ in
        // Utilities would lose half their selection on install.
        var selected = SelectionHelper.GetSelectedApps(_db);
        if (selected.Count == 0) return;

        var dialog = new InstallDialog(selected)
        {
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();

        // De-select every installed app globally so the next batch starts clean,
        // then re-query winget list so the new Installed badges show up.
        foreach (var app in selected) app.IsSelected = false;
        await RefreshInstalledStateAsync(forceRefresh: true);
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private async void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        SelectionHelper.ClearSelection(_db);
        // Force the list to re-bind so all visible checkboxes update visually.
        AppList.ItemsSource = null;
        AppList.ItemsSource = _visibleApps;
        UpdateSelectionCount();
        UpdateSelectAllButton();
        await Task.CompletedTask;
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
        // Reflects the visible subset — if everything the user currently sees
        // is checked, the button flips to "Deselect all".
        var allSelected = _visibleApps.Count > 0 && _visibleApps.All(a => a.IsSelected);
        SelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        SelectAllButton.IsEnabled = _visibleApps.Count > 0;
    }
}
