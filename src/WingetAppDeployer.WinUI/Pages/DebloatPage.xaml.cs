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
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class DebloatPage : Page
{
    private List<AppModel> _allCatalogApps = new();
    private List<AppModel> _installedApps = new();

    public DebloatPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Clear uninstall-selection bij wegnavigeren zodat een vergeten selectie
        // niet nog steeds in de footer-count staat als user later terugkomt.
        foreach (var app in _allCatalogApps) app.IsSelectedForUninstall = false;
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        ShowLoading();

        if (_allCatalogApps.Count == 0)
        {
            var db = await App.Database.GetAppDatabaseAsync();
            if (db != null)
            {
                foreach (var cat in db.Categories)
                {
                    if (cat.Apps != null) _allCatalogApps.AddRange(cat.Apps);
                    if (cat.Subcategories != null)
                        foreach (var sub in cat.Subcategories)
                            _allCatalogApps.AddRange(sub.Apps);
                }
            }
        }

        var installedIds = await App.Winget.GetInstalledAppIdsAsync(forceRefresh);
        _installedApps = _allCatalogApps
            .Where(a => installedIds.Contains(a.WingetId))
            .OrderBy(a => a.Name)
            .ToList();

        // App.IsInstalled bijwerken zodat andere pagina's (AppsPage / CategoryDetail)
        // ook de juiste badge-state laten zien als de user na uninstall terugkeert.
        foreach (var app in _allCatalogApps)
            app.IsInstalled = installedIds.Contains(app.WingetId);

        if (_installedApps.Count == 0)
        {
            ShowEmpty();
        }
        else
        {
            InstalledAppsList.ItemsSource = _installedApps;
            ShowList();
        }

        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void ShowLoading()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        AppsScroller.Visibility = Visibility.Collapsed;
    }

    private void ShowEmpty()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        AppsScroller.Visibility = Visibility.Collapsed;
    }

    private void ShowList()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        AppsScroller.Visibility = Visibility.Visible;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear de uninstall-selectie bij refresh — apps die er niet meer staan
        // zouden anders nog steeds als selected meetellen in de count.
        foreach (var app in _allCatalogApps) app.IsSelectedForUninstall = false;
        await LoadAsync(forceRefresh: true);
    }

    private void AppCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // CheckBox staat op IsHitTestVisible=False, dus de hele Grid vangt taps.
        // App heeft INPC dus IsSelectedForUninstall toggelen propageert direct naar
        // de gebonden CheckBox.
        if (sender is not FrameworkElement fe) return;
        var app = fe.DataContext as AppModel ?? fe.Tag as AppModel;
        if (app == null) return;

        app.IsSelectedForUninstall = !app.IsSelectedForUninstall;
        UpdateSelectionCount();
        UpdateSelectAllButton();
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

    private void AppIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img)
            img.Visibility = Visibility.Collapsed;
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installedApps.Count == 0) return;
        var allSelected = _installedApps.All(a => a.IsSelectedForUninstall);
        foreach (var app in _installedApps)
            app.IsSelectedForUninstall = !allSelected;

        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _installedApps) app.IsSelectedForUninstall = false;
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _installedApps.Where(a => a.IsSelectedForUninstall).ToList();
        if (selected.Count == 0) return;

        var confirm = new ContentDialog
        {
            Title = selected.Count == 1
                ? $"Uninstall {selected[0].Name}?"
                : $"Uninstall {selected.Count} apps?",
            Content = "This removes the selected apps via winget. The operation runs sequentially and can take a few minutes for large batches.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            // Geen DefaultButton = Close: dat zou de AccentButtonStyle op de Cancel
            // knop forceren (WinUI override) waardoor ALLEBEI de knoppen blauw werden.
            // Geen DefaultButton = Primary: voor destructive actions willen we geen
            // Enter-shortcut die per ongeluk uninstall start. None = beide neutraal,
            // user moet expliciet klikken. Zelfde patroon als v0.7.3 Disable-dialog.
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var dialog = new UninstallDialog(selected) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        // Clear selection en herlaad — apps die geslaagd zijn verdwijnen uit de lijst,
        // failed apps blijven staan zodat user kan retry'en.
        foreach (var app in selected) app.IsSelectedForUninstall = false;
        await LoadAsync(forceRefresh: true);
    }

    private void UpdateSelectionCount()
    {
        var count = _installedApps.Count(a => a.IsSelectedForUninstall);
        SelectionCountText.Text = $"{count} app{(count == 1 ? "" : "s")} selected";
        UninstallButton.IsEnabled = count > 0;
        ClearSelectionButton.IsEnabled = count > 0;
    }

    private void UpdateSelectAllButton()
    {
        var allSelected = _installedApps.Count > 0 && _installedApps.All(a => a.IsSelectedForUninstall);
        SelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        SelectAllButton.IsEnabled = _installedApps.Count > 0;
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);
}
