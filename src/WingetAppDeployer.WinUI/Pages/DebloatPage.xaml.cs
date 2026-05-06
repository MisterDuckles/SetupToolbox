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

    // Bloatware-items per vendor. Vers gekopiëerd uit BloatwareItem.CuratedList per
    // page-load zodat IsSelected-state niet over navigaties heen lekt. Microsoft + OEM
    // gebruiken hetzelfde model, alleen Vendor verschilt → twee lijsten gefilterd uit
    // dezelfde curated bron.
    private List<BloatwareItem> _microsoftItems = new();
    private List<BloatwareItem> _oemItems = new();

    public DebloatPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _microsoftItems = BloatwareItem.CuratedFor(BloatwareVendor.Microsoft)
            .Select(b => new BloatwareItem(b.DisplayName, b.Description, b.Category, b.Vendor, b.PackageNames.ToArray()))
            .ToList();
        _oemItems = BloatwareItem.CuratedFor(BloatwareVendor.Oem)
            .Select(b => new BloatwareItem(b.DisplayName, b.Description, b.Category, b.Vendor, b.PackageNames.ToArray()))
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
        // Drie sources parallel: catalog (winget list), Microsoft AppX en OEM AppX.
        // Get-AppxPackage draait per call ~1-2s; sequentieel zou de pagina 4-5s
        // unresponsive zijn. Microsoft + OEM detect kunnen gecombineerd in één
        // call (één Get-AppxPackage matcht beide lijsten) — DetectInstalledAsync
        // accepteert een lijst dus we voegen ze samen voor de detect en splitsen
        // daarna terug per vendor.
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
        UpdateCatalogCount();
    }

    private async Task LoadBloatwareAsync()
    {
        ShowBloatwareLoading();

        // Combineer Microsoft + OEM voor één Get-AppxPackage call. DetectInstalledAsync
        // matcht elke item tegen de PowerShell output; Microsoft- en OEM-items zijn
        // qua package-prefixes disjunct (Microsoft.* vs HPInc.* / DellInc.* / etc.)
        // dus er kan geen kruisverontreiniging zijn tussen vendoren.
        var allItems = _microsoftItems.Concat(_oemItems).ToList();
        await App.Bloatware.DetectInstalledAsync(allItems);

        // Microsoft sectie
        var msVisible = _microsoftItems.Where(b => b.IsInstalled).ToList();
        if (msVisible.Count == 0)
            ShowBloatwareEmpty();
        else
        {
            BloatwareList.ItemsSource = msVisible;
            ShowBloatwareList();
        }

        // OEM sectie — verberg helemaal als geen items gedetecteerd. Lege OEM-sectie
        // tonen zou alleen ruis zijn voor de vele users zonder OEM-AppX-bloat.
        var oemVisible = _oemItems.Where(b => b.IsInstalled).ToList();
        if (oemVisible.Count == 0)
        {
            OemSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            OemList.ItemsSource = oemVisible;
            OemSection.Visibility = Visibility.Visible;
        }

        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
        UpdateOemSelection();
        UpdateOemSelectAllButton();
        UpdateBloatwareCount();
        UpdateOemCount();
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

    // ── Microsoft bloatware visibility helpers ────────────────────
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
        foreach (var b in _microsoftItems) b.IsSelected = false;
        foreach (var b in _oemItems) b.IsSelected = false;
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

    private void UpdateCatalogCount()
    {
        var count = _installedApps.Count;
        CatalogCountText.Text = count == 0 ? string.Empty : $"({count})";
    }

    // ── Microsoft bloatware handlers ──────────────────────────────
    private void BloatwareCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Shared handler tussen Microsoft + OEM card-templates. We weten niet
        // direct uit welke lijst de getapte item komt — Vendor op het item zelf
        // bepaalt welke selection-update we moeten triggeren.
        if (sender is not FrameworkElement fe) return;
        var item = fe.DataContext as BloatwareItem ?? fe.Tag as BloatwareItem;
        if (item == null) return;

        item.IsSelected = !item.IsSelected;
        if (item.Vendor == BloatwareVendor.Microsoft)
        {
            UpdateBloatwareSelection();
            UpdateBloatwareSelectAllButton();
        }
        else
        {
            UpdateOemSelection();
            UpdateOemSelectAllButton();
        }
    }

    private void BloatwareSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        var visible = _microsoftItems.Where(b => b.IsInstalled).ToList();
        if (visible.Count == 0) return;
        var allSelected = visible.All(b => b.IsSelected);
        foreach (var item in visible)
            item.IsSelected = !allSelected;

        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
    }

    private async void BloatwareRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _microsoftItems.Where(b => b.IsInstalled && b.IsSelected).ToList();
        await ConfirmAndRemoveBloatwareAsync(selected, "Microsoft");
    }

    private void UpdateBloatwareSelection()
    {
        var count = _microsoftItems.Count(b => b.IsInstalled && b.IsSelected);
        BloatwareRemoveButton.IsEnabled = count > 0;
    }

    private void UpdateBloatwareSelectAllButton()
    {
        var visible = _microsoftItems.Where(b => b.IsInstalled).ToList();
        var allSelected = visible.Count > 0 && visible.All(b => b.IsSelected);
        BloatwareSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        BloatwareSelectAllButton.IsEnabled = visible.Count > 0;
    }

    private void UpdateBloatwareCount()
    {
        var count = _microsoftItems.Count(b => b.IsInstalled);
        BloatwareCountText.Text = count == 0 ? string.Empty : $"({count})";
    }

    // ── OEM bloatware handlers ────────────────────────────────────
    private void OemSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        var visible = _oemItems.Where(b => b.IsInstalled).ToList();
        if (visible.Count == 0) return;
        var allSelected = visible.All(b => b.IsSelected);
        foreach (var item in visible)
            item.IsSelected = !allSelected;

        UpdateOemSelection();
        UpdateOemSelectAllButton();
    }

    private async void OemRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _oemItems.Where(b => b.IsInstalled && b.IsSelected).ToList();
        await ConfirmAndRemoveBloatwareAsync(selected, "OEM");
    }

    private void UpdateOemSelection()
    {
        var count = _oemItems.Count(b => b.IsInstalled && b.IsSelected);
        OemRemoveButton.IsEnabled = count > 0;
    }

    private void UpdateOemSelectAllButton()
    {
        var visible = _oemItems.Where(b => b.IsInstalled).ToList();
        var allSelected = visible.Count > 0 && visible.All(b => b.IsSelected);
        OemSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        OemSelectAllButton.IsEnabled = visible.Count > 0;
    }

    private void UpdateOemCount()
    {
        var count = _oemItems.Count(b => b.IsInstalled);
        OemCountText.Text = count == 0 ? string.Empty : $"({count})";
    }

    // ── Shared bloatware confirm + remove flow ────────────────────
    private async Task ConfirmAndRemoveBloatwareAsync(List<BloatwareItem> selected, string vendorLabel)
    {
        if (selected.Count == 0) return;

        var confirm = new ContentDialog
        {
            Title = selected.Count == 1
                ? $"Remove {selected[0].DisplayName}?"
                : $"Remove {selected.Count} {vendorLabel} apps?",
            Content = "This permanently removes the selected apps via Remove-AppxPackage. A UAC prompt will appear because this requires administrator rights. Some apps cannot be reinstalled easily — only continue if you're sure.",
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
