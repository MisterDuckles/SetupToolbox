using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class AppsPage : Page
{
    private List<Category> _allCategories = new();
    private AppDatabase? _db;

    public AppsPage()
    {
        InitializeComponent();
        Loaded += AppsPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Refresh the footer whenever the user comes back to this page — e.g.
        // returned from CategoryDetailPage after tweaking selections.
        UpdateSelectionFooter();
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
    }

    private void ApplyFilter(string? query)
    {
        if (_allCategories.Count == 0) return;

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            CategoryList.ItemsSource = _allCategories;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        var filtered = _allCategories.Where(c => MatchesCategory(c, trimmed)).ToList();
        CategoryList.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            NoResultsText.Text = $"No categories or apps matching \"{trimmed}\"";
            NoResultsText.Visibility = Visibility.Visible;
        }
        else
        {
            NoResultsText.Visibility = Visibility.Collapsed;
        }
    }

    private static bool MatchesCategory(Category c, string query)
    {
        if (Contains(c.Name, query) || Contains(c.Description, query)) return true;

        // Match on any app name or description inside the category — so searching
        // "chrome" surfaces the Browsers card even though Chrome isn't in the
        // category name itself.
        if (c.Apps != null)
            foreach (var app in c.Apps)
                if (Contains(app.Name, query) || Contains(app.Description, query) || Contains(app.WingetId, query))
                    return true;

        if (c.Subcategories != null)
            foreach (var sub in c.Subcategories)
            {
                if (Contains(sub.Name, query) || Contains(sub.Description, query)) return true;
                foreach (var app in sub.Apps)
                    if (Contains(app.Name, query) || Contains(app.Description, query) || Contains(app.WingetId, query))
                        return true;
            }

        return false;
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
