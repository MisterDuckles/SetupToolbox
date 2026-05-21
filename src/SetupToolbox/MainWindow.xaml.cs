using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SetupToolbox.Pages;
using SetupToolbox.Services;

namespace SetupToolbox;

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

        // Self-update: stille achtergrond-check bij startup (indien aan).
        _ = CheckForUpdatesOnStartupAsync();
    }

    // ---------------------------------------------------------------
    // SELF-UPDATE (v0.10.1)
    // ---------------------------------------------------------------

    private UpdateInfo? _pendingUpdate;

    private async System.Threading.Tasks.Task CheckForUpdatesOnStartupAsync()
    {
        if (!App.Settings.CheckForUpdatesOnStartup) return;
        try
        {
            var result = await App.GitHub.CheckForUpdateAsync();
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update != null)
                ShowUpdate(result.Update);
        }
        catch { /* startup-check is best-effort, nooit storend */ }
    }

    // Toont de update-balk. Ook aangeroepen door SettingsPage na een handmatige
    // "Check for updates now" die een update vond.
    public void ShowUpdate(UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = "Update beschikbaar";
        UpdateBar.Message = $"Versie {info.Version} is beschikbaar (je hebt {App.GitHub.CurrentVersion}).";
        UpdateBar.IsClosable = true;
        UpdateNowButton.Visibility = Visibility.Visible;
        UpdateNowButton.IsEnabled = true;
        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = false;
        UpdateBar.IsOpen = true;
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;

        UpdateNowButton.IsEnabled = false;
        UpdateBar.IsClosable = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        UpdateBar.Message = "Bezig met downloaden...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p;
                UpdateBar.Message = $"Bezig met downloaden... {p:P0}";
            });
            var setupPath = await App.GitHub.DownloadInstallerAsync(_pendingUpdate, progress);

            UpdateProgress.IsIndeterminate = true;
            UpdateBar.Message = "Installeren — de app sluit en herstart automatisch...";
            // Installer sluit deze app via Restart Manager en herstart 'm. We
            // exiten niet zelf zodat /RESTARTAPPLICATIONS de app terugbrengt.
            App.GitHub.LaunchInstaller(setupPath);
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "Update mislukt";
            UpdateBar.Message = ex.Message;
            UpdateBar.IsClosable = true;
            UpdateNowButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    // Vanuit Settings ("Profiel maken"): start de Tweaks-tab in profiel-modus —
    // clean slate, alle vinkjes uit. We wissen ook de normale pending changes om
    // mode-bleed te voorkomen.
    public void EnterTweakProfileMode()
    {
        App.ProfileMode = true;
        App.ProfileSelection.Clear();
        App.TweakPending.Clear();
        SelectTweaksNav();
    }

    // Vanuit Settings na een profiel-import: spring naar de Tweaks-tab (normale
    // modus) zodat de gebruiker de klaargezette pending changes ziet + Apply kan.
    public void NavigateToTweaks()
    {
        App.ProfileMode = false;
        SelectTweaksNav();
    }

    // Selecteert het "Tweaks" nav-item. Bij een echte selectie-wissel vuurt
    // SelectionChanged → ContentFrame.Navigate(TweaksPage); als het item al
    // geselecteerd is forceren we een re-render in de (mogelijk gewijzigde) modus.
    private void SelectTweaksNav()
    {
        foreach (var mi in NavView.MenuItems)
        {
            if (mi is NavigationViewItem nvi && nvi.Tag is string tag && tag == "Tweaks")
            {
                if (!ReferenceEquals(NavView.SelectedItem, nvi))
                    NavView.SelectedItem = nvi;
                else if (ContentFrame.Content is TweaksPage tp)
                    tp.RefreshForModeChange();
                return;
            }
        }
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
