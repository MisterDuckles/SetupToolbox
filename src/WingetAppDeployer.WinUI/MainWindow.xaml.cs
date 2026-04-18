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
                "Debloat" => typeof(DebloatPage),
                _ => null
            };
        }

        if (pageType == null) return;
        if (ContentFrame.CurrentSourcePageType == pageType) return;

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }
}
