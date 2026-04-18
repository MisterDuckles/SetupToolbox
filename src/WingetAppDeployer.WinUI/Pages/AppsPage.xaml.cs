using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class AppsPage : Page
{
    public AppsPage()
    {
        InitializeComponent();
        Loaded += AppsPage_Loaded;
    }

    private async void AppsPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingPanel.Visibility = Visibility.Visible;
        CategoryScroller.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;

        var db = await App.Database.GetAppDatabaseAsync();

        LoadingPanel.Visibility = Visibility.Collapsed;

        if (db == null || db.Categories.Count == 0)
        {
            ErrorBar.IsOpen = true;
            return;
        }

        CategoryList.ItemsSource = db.Categories;
        CategoryScroller.Visibility = Visibility.Visible;
    }

    private async void CategoryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string categoryId) return;

        var db = await App.Database.GetAppDatabaseAsync();
        var category = db?.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null) return;

        Frame.Navigate(typeof(CategoryDetailPage), category, new DrillInNavigationTransitionInfo());
    }
}
