using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WingetAppDeployer_WinUI.Pages;
using WinRT;

namespace WingetAppDeployer_WinUI;

public sealed partial class MainWindow : Window
{
    private WindowsSystemDispatcherQueueHelper? _wsdqHelper;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Desktop Acrylic (Thin) backdrop via official controller snippet
        TrySetAcrylicBackdrop(useAcrylicThin: true);

        // Select the first menu item on startup — fires SelectionChanged which
        // handles the Frame navigation.
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

    // ============== Desktop Acrylic backdrop ==============

    private bool TrySetAcrylicBackdrop(bool useAcrylicThin)
    {
        if (!DesktopAcrylicController.IsSupported())
            return false;

        _wsdqHelper = new WindowsSystemDispatcherQueueHelper();
        _wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

        _configurationSource = new SystemBackdropConfiguration();
        Activated += Window_Activated;
        Closed += Window_Closed;
        if (Content is FrameworkElement root)
            root.ActualThemeChanged += Window_ThemeChanged;

        _configurationSource.IsInputActive = true;
        SetConfigurationSourceTheme();

        _acrylicController = new DesktopAcrylicController
        {
            Kind = useAcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base
        };

        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        return true;
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
            _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_acrylicController != null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }
        Activated -= Window_Activated;
        _configurationSource = null;
    }

    private void Window_ThemeChanged(FrameworkElement sender, object args)
    {
        if (_configurationSource != null)
            SetConfigurationSourceTheme();
    }

    private void SetConfigurationSourceTheme()
    {
        if (_configurationSource == null || Content is not FrameworkElement root) return;
        _configurationSource.Theme = root.ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }
}
