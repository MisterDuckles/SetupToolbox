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

        // Teksten van de shell zetten vóór de eerste navigatie (v1.2.2).
        ApplyLanguage();
        // NavView.SettingsItem bestaat pas nadat het control-template is toegepast,
        // dus in de constructor is 'ie nog null. Nog een keer na Loaded, anders houdt
        // dat ene item z'n door WinUI ingevulde label.
        NavView.Loaded += (_, _) => ApplyLanguage();
        App.Loc.LanguageChanged += OnLanguageChanged;

        // Select the first menu item on startup — fires SelectionChanged which
        // handles the Frame navigation. Mica backdrop is configured declaratively
        // in MainWindow.xaml via <Window.SystemBackdrop><MicaBackdrop/></Window.SystemBackdrop>.
        NavView.SelectedItem = NavView.MenuItems[0];

        // Self-update: stille achtergrond-check bij startup (indien aan).
        _ = CheckForUpdatesOnStartupAsync();
    }

    // ---------------------------------------------------------------
    // TAAL (v1.2.2)
    // ---------------------------------------------------------------

    // De shell (titelbalk + navigatie) overleeft een taalwissel en wordt dus niet
    // opnieuw geparsed. Die teksten zetten we daarom hier in code; alles binnen
    // een Page komt uit {loc:Localize} en wordt vers opgebouwd bij de re-navigatie
    // hieronder.
    private void ApplyLanguage()
    {
        Title = App.Loc.S("app.title");
        AppTitleBar.Title = App.Loc.S("app.title");

        NavApps.Content = App.Loc.S("nav.apps");
        NavTweaks.Content = App.Loc.S("nav.tweaks");
        NavDebloat.Content = App.Loc.S("nav.debloat");
        NavDebloatApps.Content = App.Loc.S("nav.debloat.apps");
        NavDeepClean.Content = App.Loc.S("nav.debloat.deepClean");

        // Het ingebouwde Settings-item krijgt z'n label van WinUI zelf, via MRT.
        // Dat volgt niet onze keuze én niet eens de Windows-weergavetaal: MRT kijkt
        // naar de gebruikers-taallijst. Op deze machine leverde dat een Nederlands
        // "Instellingen" op naast een verder Engelse UI. Zelf overschrijven dus.
        if (NavView.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.Content = App.Loc.S("nav.settings");

        UpdateNowButton.Content = App.Loc.S("update.now");

        // Staat de update-balk open, dan hangt z'n tekst aan de vorige taal.
        // Opnieuw opbouwen uit de bewaarde UpdateInfo.
        if (UpdateBar.IsOpen && _pendingUpdate != null)
        {
            UpdateBar.Title = App.Loc.S("update.available.title");
            UpdateBar.Message = App.Loc.S("update.available.body",
                _pendingUpdate.Version, App.GitHub.CurrentVersion);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLanguage();

        // Markup-extensions evalueren alleen tijdens het parsen, dus de huidige
        // page moet opnieuw opgebouwd worden. Dat mag simpelweg door opnieuw naar
        // hetzelfde type te navigeren: geen enkele page zet NavigationCacheMode,
        // dus de Frame maakt gegarandeerd een verse instantie. Zonder transitie,
        // anders krijg je een animatie bij wat een in-place refresh moet lijken.
        var current = ContentFrame.CurrentSourcePageType;
        if (current == null) return;
        ContentFrame.Navigate(current, null, new SuppressNavigationTransitionInfo());
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
        UpdateBar.Title = App.Loc.S("update.available.title");
        UpdateBar.Message = App.Loc.S("update.available.body", info.Version, App.GitHub.CurrentVersion);
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
        UpdateBar.Message = App.Loc.S("update.downloading");

        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p;
                UpdateBar.Message = App.Loc.S("update.downloadingPct", p);
            });
            var setupPath = await App.GitHub.DownloadInstallerAsync(_pendingUpdate, progress);

            UpdateProgress.IsIndeterminate = true;
            UpdateBar.Message = App.Loc.S("update.installing");
            // Installer sluit deze app via Restart Manager en herstart 'm. We
            // exiten niet zelf zodat /RESTARTAPPLICATIONS de app terugbrengt.
            App.GitHub.LaunchInstaller(setupPath);
        }
        catch (Exception ex)
        {
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = App.Loc.S("update.failed.title");
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
