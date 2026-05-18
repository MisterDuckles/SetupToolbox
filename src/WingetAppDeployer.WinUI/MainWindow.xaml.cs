using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WingetAppDeployer_WinUI.Pages;

namespace WingetAppDeployer_WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Select the first menu item on startup — fires SelectionChanged which
        // handles the Frame navigation. Mica backdrop is configured declaratively
        // in MainWindow.xaml via <Window.SystemBackdrop><MicaBackdrop/></Window.SystemBackdrop>.
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type? pageType = null;

        if (args.IsSettingsSelected)
        {
            pageType = typeof(SettingsPage);
        }
        else if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            pageType = tag switch
            {
                "Apps" => typeof(AppsPage),
                "Tweaks" => typeof(TweaksPage),
                // "Debloat" parent zelf navigeert niet (SelectsOnInvoked=False in
                // XAML) — alleen z'n twee kinderen. Apps = de bloatware/uninstall
                // flows, Deep clean = system-wide cache/orphan cleanup.
                "DebloatApps" => typeof(DebloatPage),
                "DebloatDeepClean" => typeof(DeepCleanPage),
                _ => null
            };
        }

        if (pageType == null) return;
        if (ContentFrame.CurrentSourcePageType == pageType) return;

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // ItemInvoked fired ook bij een klik op het ál-geselecteerde item, waar
        // SelectionChanged stil blijft. Dat hebben we nodig voor twee scenarios:
        //   1. User staat in een detail-pagina (CategoryDetailPage of
        //      TweakCategoryDetailPage) — NavView's selectie is nog steeds de
        //      parent ("Apps"/"Tweaks"), dus normale navigatie doet niks.
        //   2. User staat op de landing met een actieve search — zelfde verhaal.
        if (args.IsSettingsInvoked) return;
        if (args.InvokedItemContainer is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;

        if (tag == "Apps")
        {
            if (ContentFrame.CurrentSourcePageType == typeof(CategoryDetailPage))
            {
                ContentFrame.Navigate(typeof(AppsPage), null, new EntranceNavigationTransitionInfo());
                return;
            }
            if (ContentFrame.Content is AppsPage apps)
                apps.ResetToRoot();  // wis search, toon categorie-grid
        }
        else if (tag == "Tweaks")
        {
            if (ContentFrame.CurrentSourcePageType == typeof(TweakCategoryDetailPage))
            {
                // Terug uit een tweak-detail-pagina naar de categorie-grid.
                ContentFrame.Navigate(typeof(TweaksPage), null, new EntranceNavigationTransitionInfo());
                return;
            }
            if (ContentFrame.Content is TweaksPage tweaks)
                tweaks.ResetToRoot();  // wis search, toon categorie-grid
        }
    }
}
