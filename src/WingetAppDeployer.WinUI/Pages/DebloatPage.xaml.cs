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
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class DebloatPage : Page
{
    private List<AppModel> _allCatalogApps = new();
    private List<AppModel> _installedApps = new();

    // Curated bloatware items — geinitialiseerd vanuit de static lijst zodat alle
    // page-instances dezelfde IsSelected-state delen niet wenselijk; we kopiëren
    // de items naar nieuwe instances per page-load. Dat geeft elke navigatie
    // een schone selectie.
    private List<BloatwareItem> _bloatwareItems = new();

    public DebloatPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Bouw bloatware items vers per page-load. Get-AppxPackage detect duurt 1-2s
        // dus async; UI toont spinner ondertussen.
        _bloatwareItems = BloatwareItem.CuratedList
            .Select(b => new BloatwareItem(b.DisplayName, b.Description, b.Category, b.PackageNames.ToArray()))
            .ToList();

        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        foreach (var app in _allCatalogApps) app.IsSelectedForUninstall = false;
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        // Beide secties parallel laden — Get-AppxPackage en winget list zijn beide
        // ~1-2s elk, en ze zijn onafhankelijk dus simultaan veiliger gebruikersgevoel
        // dan sequentieel.
        var catalogTask = LoadCatalogAsync(forceRefresh);
        var bloatwareTask = LoadBloatwareAsync();
        await Task.WhenAll(catalogTask, bloatwareTask);
    }

    private async Task LoadCatalogAsync(bool forceRefresh)
    {
        ShowCatalogLoading();

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

        foreach (var app in _allCatalogApps)
            app.IsInstalled = installedIds.Contains(app.WingetId);

        if (_installedApps.Count == 0)
        {
            ShowCatalogEmpty();
        }
        else
        {
            InstalledAppsList.ItemsSource = _installedApps;
            ShowCatalogList();
        }

        UpdateCatalogSelection();
        UpdateCatalogSelectAllButton();
    }

    private async Task LoadBloatwareAsync()
    {
        ShowBloatwareLoading();

        await App.Bloatware.DetectInstalledAsync(_bloatwareItems);

        // Alleen items die ook daadwerkelijk geïnstalleerd zijn tonen — anders
        // staat de lijst vol met items waar je toch niets mee kan ("Cortana not
        // installed" zou alleen ruis zijn).
        var visible = _bloatwareItems.Where(b => b.IsInstalled).ToList();

        if (visible.Count == 0)
        {
            ShowBloatwareEmpty();
        }
        else
        {
            BloatwareList.ItemsSource = visible;
            ShowBloatwareList();
        }

        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
    }

    // ── Catalog visibility helpers ────────────────────────────────
    private void ShowCatalogLoading()
    {
        CatalogLoadingRing.Visibility = Visibility.Visible;
        CatalogEmptyText.Visibility = Visibility.Collapsed;
        InstalledAppsList.Visibility = Visibility.Collapsed;
    }

    private void ShowCatalogEmpty()
    {
        CatalogLoadingRing.Visibility = Visibility.Collapsed;
        CatalogEmptyText.Visibility = Visibility.Visible;
        InstalledAppsList.Visibility = Visibility.Collapsed;
    }

    private void ShowCatalogList()
    {
        CatalogLoadingRing.Visibility = Visibility.Collapsed;
        CatalogEmptyText.Visibility = Visibility.Collapsed;
        InstalledAppsList.Visibility = Visibility.Visible;
    }

    // ── Bloatware visibility helpers ──────────────────────────────
    private void ShowBloatwareLoading()
    {
        BloatwareLoadingRing.Visibility = Visibility.Visible;
        BloatwareEmptyText.Visibility = Visibility.Collapsed;
        BloatwareList.Visibility = Visibility.Collapsed;
    }

    private void ShowBloatwareEmpty()
    {
        BloatwareLoadingRing.Visibility = Visibility.Collapsed;
        BloatwareEmptyText.Visibility = Visibility.Visible;
        BloatwareList.Visibility = Visibility.Collapsed;
    }

    private void ShowBloatwareList()
    {
        BloatwareLoadingRing.Visibility = Visibility.Collapsed;
        BloatwareEmptyText.Visibility = Visibility.Collapsed;
        BloatwareList.Visibility = Visibility.Visible;
    }

    // ── Refresh ───────────────────────────────────────────────────
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _allCatalogApps) app.IsSelectedForUninstall = false;
        foreach (var b in _bloatwareItems) b.IsSelected = false;
        await LoadAsync(forceRefresh: true);
    }

    // ── Catalog handlers ──────────────────────────────────────────
    private void AppCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var app = fe.DataContext as AppModel ?? fe.Tag as AppModel;
        if (app == null) return;

        app.IsSelectedForUninstall = !app.IsSelectedForUninstall;
        UpdateCatalogSelection();
        UpdateCatalogSelectAllButton();
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installedApps.Count == 0) return;
        var allSelected = _installedApps.All(a => a.IsSelectedForUninstall);
        foreach (var app in _installedApps)
            app.IsSelectedForUninstall = !allSelected;

        UpdateCatalogSelection();
        UpdateCatalogSelectAllButton();
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
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var dialog = new UninstallDialog(selected) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        foreach (var app in selected) app.IsSelectedForUninstall = false;
        await LoadCatalogAsync(forceRefresh: true);
    }

    private void UpdateCatalogSelection()
    {
        var count = _installedApps.Count(a => a.IsSelectedForUninstall);
        CatalogSelectionCountText.Text = count == 0 ? string.Empty : $"{count} selected";
        UninstallButton.IsEnabled = count > 0;
    }

    private void UpdateCatalogSelectAllButton()
    {
        var allSelected = _installedApps.Count > 0 && _installedApps.All(a => a.IsSelectedForUninstall);
        SelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        SelectAllButton.IsEnabled = _installedApps.Count > 0;
    }

    // ── Bloatware handlers ────────────────────────────────────────
    private void BloatwareCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var item = fe.DataContext as BloatwareItem ?? fe.Tag as BloatwareItem;
        if (item == null) return;

        item.IsSelected = !item.IsSelected;
        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
    }

    private void BloatwareSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        var visible = _bloatwareItems.Where(b => b.IsInstalled).ToList();
        if (visible.Count == 0) return;
        var allSelected = visible.All(b => b.IsSelected);
        foreach (var item in visible)
            item.IsSelected = !allSelected;

        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
    }

    private async void BloatwareRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _bloatwareItems.Where(b => b.IsInstalled && b.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = new ContentDialog
        {
            Title = selected.Count == 1
                ? $"Remove {selected[0].DisplayName}?"
                : $"Remove {selected.Count} Microsoft apps?",
            Content = "This permanently removes the selected Microsoft apps via Remove-AppxPackage. A UAC prompt will appear because this requires administrator rights. Some apps cannot be reinstalled easily — only continue if you're sure.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var dialog = new BloatwareUninstallDialog(selected, App.Bloatware) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        foreach (var item in selected) item.IsSelected = false;
        await LoadBloatwareAsync();
    }

    private void UpdateBloatwareSelection()
    {
        var count = _bloatwareItems.Count(b => b.IsInstalled && b.IsSelected);
        BloatwareRemoveButton.IsEnabled = count > 0;
    }

    private void UpdateBloatwareSelectAllButton()
    {
        var visible = _bloatwareItems.Where(b => b.IsInstalled).ToList();
        var allSelected = visible.Count > 0 && visible.All(b => b.IsSelected);
        BloatwareSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        BloatwareSelectAllButton.IsEnabled = visible.Count > 0;
    }

    // ── Shared ────────────────────────────────────────────────────
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

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);
}
