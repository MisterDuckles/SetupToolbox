using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Models;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class DebloatPage : Page
{
    private List<AppModel> _allCatalogApps = new();

    public DebloatPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadAsync();
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        ShowLoading();

        // Flatten all apps from the catalog so we can match installed IDs.
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
        var installedApps = _allCatalogApps
            .Where(a => installedIds.Contains(a.WingetId))
            .OrderBy(a => a.Name)
            .ToList();

        if (installedApps.Count == 0)
        {
            ShowEmpty();
        }
        else
        {
            InstalledAppsList.ItemsSource = installedApps;
            ShowList();
        }
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
        await LoadAsync(forceRefresh: true);
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string wingetId) return;

        var app = _allCatalogApps.FirstOrDefault(a => a.WingetId == wingetId);
        var name = app?.Name ?? wingetId;

        var confirm = new ContentDialog
        {
            Title = $"Uninstall {name}?",
            Content = $"Dit verwijdert {name} ({wingetId}) via winget.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // Show in-progress dialog while winget runs.
        var progress = new ContentDialog
        {
            Title = $"Uninstalling {name}",
            Content = new ProgressRing { IsActive = true, Width = 40, Height = 40 },
            XamlRoot = this.XamlRoot
        };

        var uninstallTask = App.Winget.UninstallAppAsync(wingetId);
        _ = progress.ShowAsync();
        var (success, message) = await uninstallTask;
        progress.Hide();

        var outcome = new ContentDialog
        {
            Title = success ? $"{name} uninstalled" : $"Failed to uninstall {name}",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await outcome.ShowAsync();

        // Refresh list regardless of outcome so the UI reflects reality.
        await LoadAsync(forceRefresh: true);
    }
}
