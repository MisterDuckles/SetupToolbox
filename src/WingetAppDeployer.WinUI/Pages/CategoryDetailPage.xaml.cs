using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Models;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class CategoryDetailPage : Page
{
    private Category? _category;
    private List<AppModel> _allApps = new();

    public CategoryDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
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

        AppList.ItemsSource = _allApps;
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void AppCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        // The TwoWay x:Bind updates App.IsSelected as part of the same event
        // batch. Defer the count to run after the dispatcher completes the
        // current event so the backing list definitely has the new state.
        DispatcherQueue.TryEnqueue(() =>
        {
            // Safety net — also sync from the actual CheckBox state in case
            // the binding direction is reversed for some reason.
            if (sender is CheckBox cb && cb.DataContext is AppModel app)
                app.IsSelected = cb.IsChecked == true;

            UpdateSelectionCount();
            UpdateSelectAllButton();
        });
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        var allSelected = _allApps.Count > 0 && _allApps.All(a => a.IsSelected);
        foreach (var app in _allApps)
            app.IsSelected = !allSelected;

        // Refresh ItemsRepeater by reassigning the source
        AppList.ItemsSource = null;
        AppList.ItemsSource = _allApps;

        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        var dialog = new InstallDialog(selected)
        {
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();

        // After install finishes, de-select everything so the user can start a
        // fresh batch.
        foreach (var app in selected) app.IsSelected = false;
        AppList.ItemsSource = null;
        AppList.ItemsSource = _allApps;
        UpdateSelectionCount();
        UpdateSelectAllButton();
    }

    private void UpdateSelectionCount()
    {
        var count = _allApps.Count(a => a.IsSelected);
        SelectionCountText.Text = $"{count} app{(count == 1 ? "" : "s")} selected";
        InstallButton.IsEnabled = count > 0;
    }

    private void UpdateSelectAllButton()
    {
        var allSelected = _allApps.Count > 0 && _allApps.All(a => a.IsSelected);
        SelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
    }
}
