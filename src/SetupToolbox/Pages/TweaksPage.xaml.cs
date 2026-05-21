using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using SetupToolbox.Dialogs;
using SetupToolbox.Helpers;
using SetupToolbox.Models;
using SetupToolbox.Services;

namespace SetupToolbox.Pages;

// Tweaks-tab landing — category-grid (zoals de Apps-tab). Klik een tile →
// TweakCategoryDetailPage met de tweaks van die categorie. Globale zoekbalk
// toont tweak-matches plat over alle categorieën. Footer onderaan toont de
// pending changes + Apply; dezelfde footer-logica zit op de detail-pagina.
//
// State-flow:
//   - OnNavigatedTo → DetectStatesAsync (registry-walk) → grid (her)bouwen
//   - pending changes leven in App.TweakPending (cross-page) — footer abonneert
//   - Apply via TweakApplyRunner (gedeeld met de detail-pagina)
public sealed partial class TweaksPage : Page
{
    private static bool _statesDetectedOnce;

    public TweaksPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Beide stores abonneren: normale modus muteert App.TweakPending, profiel-
        // modus muteert App.ProfileSelection. UpdateFooter leest de juiste store.
        App.TweakPending.Changed += OnPendingChanged;
        App.ProfileSelection.Changed += OnPendingChanged;

        // States detecteren bij de eerste navigatie; bij terugkeer van een
        // detail-pagina zijn ze al vers (de detail-pagina / apply-runner
        // re-detect). Eerste keer: overlay tonen tijdens de registry-walk.
        if (!_statesDetectedOnce)
        {
            LoadingOverlayText.Text = "Reading Windows state...";
            LoadingOverlay.Visibility = Visibility.Visible;
            try { await App.Tweaks.DetectStatesAsync(); }
            catch { /* cards komen met Unknown state */ }
            _statesDetectedOnce = true;
        }
        // Overlay start op Visible in XAML — bij een tweede paginabezoek
        // (nieuwe instance, detection al gedaan) hier expliciet verbergen.
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (App.ProfileMode) BuildProfileChecklist();
        else BuildCategoryGrid();
        ApplyModeChrome();
        UpdateFooter();
        UpdateRestoreButton();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.TweakPending.Changed -= OnPendingChanged;
        App.ProfileSelection.Changed -= OnPendingChanged;

        // Een profiel-bouw is niet persistent tot 'Opslaan' — wegnavigeren van de
        // Tweaks-tab annuleert 'm. Sluiten/Toepassen zetten de flag zelf al uit en
        // navigeren niet weg, dus dit vangt alleen het echt-weg-klikken op.
        if (App.ProfileMode)
        {
            App.ProfileMode = false;
            App.ProfileSelection.Clear();
        }
    }

    // Aangeroepen door MainWindow als de Tweaks-page al de actieve content is en
    // de modus extern is gewijzigd (zeldzaam — normaal navigeert SelectionChanged
    // naar een verse instance).
    public void RefreshForModeChange()
    {
        if (App.ProfileMode) BuildProfileChecklist();
        else BuildCategoryGrid();
        ApplyModeChrome();
        UpdateFooter();
        UpdateRestoreButton();
    }

    private void OnPendingChanged() => UpdateFooter();

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

    // Aangeroepen door MainWindow wanneer de user op de al-geselecteerde
    // "Tweaks" sidebar-item klikt terwijl 'ie al op de landing staat: wis de
    // search zodat de categorie-grid weer in beeld komt.
    public void ResetToRoot()
    {
        SearchBox.Text = string.Empty;
        if (App.ProfileMode)
        {
            BuildProfileChecklist();
            return;
        }
        CategoryGrid.Visibility = Visibility.Visible;
        SearchResultsSection.Visibility = Visibility.Collapsed;
    }

    // ---------------------------------------------------------------
    // CATEGORY GRID
    // ---------------------------------------------------------------

    private void BuildCategoryGrid()
    {
        var tiles = App.Tweaks.All
            .GroupBy(t => t.Category)
            .OrderBy(g => (int)g.Key)
            .Select(g =>
            {
                var list = g.ToList();
                var active = list.Count(t => t.State == TweakState.Enabled || t.State == TweakState.Partial);
                var pending = App.TweakPending.CountInCategory(g.Key);
                return new TweakCategoryTile
                {
                    Category = g.Key,
                    Name = g.Key.DisplayName(),
                    Icon = g.Key.Icon(),
                    Blurb = g.Key.Blurb(),
                    CountLabel = $"{active} / {list.Count} actief",
                    PendingLabel = pending > 0 ? $"{pending} pending" : string.Empty,
                    IsFullyApplied = active == list.Count && list.Count > 0
                };
            })
            .ToList();
        CategoryGrid.ItemsSource = tiles;
    }

    private void CategoryTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TweakCategory category) return;
        Frame.Navigate(typeof(TweakCategoryDetailPage), category, new DrillInNavigationTransitionInfo());
    }

    // ---------------------------------------------------------------
    // SEARCH
    // ---------------------------------------------------------------

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var query = sender.Text?.Trim() ?? string.Empty;

        // In profiel-modus filtert de zoekbalk de clean-slate checklist.
        if (App.ProfileMode)
        {
            BuildProfileChecklist(query);
            return;
        }

        if (query.Length == 0)
        {
            // Terug naar category-grid.
            CategoryGrid.Visibility = Visibility.Visible;
            SearchResultsSection.Visibility = Visibility.Collapsed;
            return;
        }

        CategoryGrid.Visibility = Visibility.Collapsed;
        SearchResultsSection.Visibility = Visibility.Visible;

        var matches = App.Tweaks.All
            .Where(t => Matches(t, query))
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .ToList();

        SearchResultsList.Children.Clear();
        foreach (var tweak in matches)
            SearchResultsList.Children.Add(BuildSearchResultCard(tweak));

        SearchResultsHeader.Text = matches.Count == 1
            ? "1 tweak gevonden"
            : $"{matches.Count} tweaks gevonden";
        SearchEmptyText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(Tweak tweak, string query)
    {
        bool Has(string? s) => s != null && s.Contains(query, StringComparison.OrdinalIgnoreCase);
        return Has(tweak.Name)
            || Has(tweak.Description)
            || Has(tweak.UseCase)
            || Has(tweak.Category.DisplayName());
    }

    // Zoekresultaat = de gedeelde tweak-card met een kleine categorie-prefix
    // erboven zodat de user ziet uit welke categorie de match komt.
    private FrameworkElement BuildSearchResultCard(Tweak tweak)
    {
        var wrapper = new StackPanel { Spacing = 2 };
        wrapper.Children.Add(new TextBlock
        {
            Text = $"{tweak.Category.Icon()}  {tweak.Category.DisplayName()}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });
        wrapper.Children.Add(TweakCardFactory.Build(tweak));
        return wrapper;
    }

    // ---------------------------------------------------------------
    // PROFIEL-MODUS (v0.9.20)
    // ---------------------------------------------------------------

    // Vlakke checklist van alle tweaks, gegroepeerd per categorie, clean-slate
    // (cards in profileMode). Optioneel gefilterd op de zoekterm.
    private void BuildProfileChecklist(string query = "")
    {
        ProfileChecklist.Children.Clear();

        var byCat = App.Tweaks.All
            .Where(t => string.IsNullOrEmpty(query) || Matches(t, query))
            .GroupBy(t => t.Category)
            .OrderBy(g => (int)g.Key);

        var total = 0;
        foreach (var grp in byCat)
        {
            var list = grp.OrderBy(t => t.Group).ThenBy(t => t.Name).ToList();
            if (list.Count == 0) continue;
            total += list.Count;

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };
            header.Children.Add(new TextBlock { Text = grp.Key.Icon(), FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(new TextBlock
            {
                Text = grp.Key.DisplayName(),
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });
            ProfileChecklist.Children.Add(header);

            var cards = new StackPanel { Spacing = 8 };
            foreach (var tweak in list)
                cards.Children.Add(TweakCardFactory.Build(tweak, profileMode: true));
            ProfileChecklist.Children.Add(cards);
        }

        if (total == 0)
        {
            ProfileChecklist.Children.Add(new TextBlock
            {
                Text = "Geen tweaks gevonden.",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
    }

    // Toont de juiste UI-elementen voor de actieve modus (profiel vs normaal).
    private void ApplyModeChrome()
    {
        var profile = App.ProfileMode;

        // Banner: ook Visibility togglen (niet alleen IsOpen). Een gesloten InfoBar
        // blijft anders Visible-met-0-hoogte → de body-StackPanel (Spacing=16) zou
        // er dan 16px ruimte boven de cards omheen zetten. Collapsed = uit de layout.
        ProfileBanner.IsOpen = profile;
        ProfileBanner.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
        ProfileChecklist.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
        NormalFooter.Visibility = profile ? Visibility.Collapsed : Visibility.Visible;
        ProfileFooter.Visibility = profile ? Visibility.Visible : Visibility.Collapsed;
        // Utility-knoppen zijn niet relevant tijdens profiel-bouw.
        RestartExplorerButton.Visibility = profile ? Visibility.Collapsed : Visibility.Visible;

        if (profile)
        {
            CategoryGrid.Visibility = Visibility.Collapsed;
            SearchResultsSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            CategoryGrid.Visibility = Visibility.Visible;
        }
    }

    private void ProfileCloseButton_Click(object sender, RoutedEventArgs e)
    {
        App.ProfileMode = false;
        App.ProfileSelection.Clear();
        SearchBox.Text = string.Empty;
        ApplyModeChrome();
        BuildCategoryGrid();
        UpdateFooter();
        UpdateRestoreButton();
    }

    private async void ProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        var n = App.ProfileSelection.Count;
        if (n == 0) return;

        var file = await FilePickerHelper.PickSaveFileAsync(
            $"my-tweaks-{DateTime.Now:yyyy-MM-dd}", "SetupToolbox tweak-profiel", ".json");
        if (file == null) return;

        try
        {
            await App.TweakProfileIO.ExportAsync(file.Path, App.ProfileSelection.Snapshot());
            ShowResult(InfoBarSeverity.Success, "Profiel opgeslagen",
                $"{n} tweak{(n == 1 ? "" : "s")} weggeschreven naar {file.Name}.");
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Opslaan mislukt", ex.Message);
        }
    }

    private async void ProfileApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.ProfileSelection.Count == 0) return;

        var matched = App.ProfileSelection.Snapshot()
            .Select(kv => new TweakProfileMatch(kv.Key, kv.Value))
            .ToList();
        var (staged, _) = TweakProfileService.StageDelta(matched, App.TweakPending);

        if (staged == 0)
        {
            ShowResult(InfoBarSeverity.Informational, "Niets toe te passen",
                $"Alle {matched.Count} tweak{(matched.Count == 1 ? "" : "s")} uit je profiel staan al in de gewenste staat.");
            return;
        }

        // Verlaat profiel-modus en draai de normale apply-flow (backup-prompt + UAC).
        App.ProfileMode = false;
        App.ProfileSelection.Clear();
        SearchBox.Text = string.Empty;
        ApplyModeChrome();

        var outcome = await TweakApplyRunner.RunAsync(this.XamlRoot, onWorkStarting: () =>
        {
            LoadingOverlayText.Text = "Wijzigingen toepassen...";
            LoadingOverlay.Visibility = Visibility.Visible;
        });

        LoadingOverlay.Visibility = Visibility.Collapsed;
        TweakApplyRunner.ShowOutcome(ResultBar, outcome);
        BuildCategoryGrid();
        UpdateFooter();
        UpdateRestoreButton();
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    // ---------------------------------------------------------------
    // FOOTER
    // ---------------------------------------------------------------

    private void UpdateFooter()
    {
        if (App.ProfileMode)
        {
            var sel = App.ProfileSelection.Count;
            ProfileSaveButton.IsEnabled = sel > 0;
            ProfileApplyButton.IsEnabled = sel > 0;
            ProfileSelectedText.Text = sel == 0
                ? "Niets geselecteerd — vink tweaks aan voor je profiel"
                : $"{sel} tweak{(sel == 1 ? "" : "s")} geselecteerd";
            return;
        }

        var count = App.TweakPending.Count;
        ApplyButton.IsEnabled = count > 0;
        DiscardButton.IsEnabled = count > 0;
        ApplyButtonText.Text = count > 0 ? $"Apply ({count})" : "Apply";
        PendingCountText.Text = count == 0
            ? "Geen openstaande wijzigingen"
            : $"{count} openstaande wijziging{(count == 1 ? "" : "en")}";
    }

    private void UpdateRestoreButton()
    {
        RestoreButton.Visibility = (!App.ProfileMode && App.Snapshots.GetLatest() != null)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        App.TweakPending.Clear();
        // Search-resultaten herbouwen zodat de checkboxes terug naar de
        // gedetecteerde state springen; grid-badges verversen.
        RebuildVisibleContent();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.TweakPending.Count == 0) return;

        ApplyButton.IsEnabled = false;
        DiscardButton.IsEnabled = false;

        var outcome = await TweakApplyRunner.RunAsync(this.XamlRoot, onWorkStarting: () =>
        {
            LoadingOverlayText.Text = "Wijzigingen toepassen...";
            LoadingOverlay.Visibility = Visibility.Visible;
        });

        LoadingOverlay.Visibility = Visibility.Collapsed;
        TweakApplyRunner.ShowOutcome(ResultBar, outcome);
        RebuildVisibleContent();
        UpdateFooter();
        UpdateRestoreButton();
    }

    // Herbouwt wat zichtbaar is — grid altijd (badges), zoekresultaten als
    // search actief is.
    private void RebuildVisibleContent()
    {
        BuildCategoryGrid();
        if (SearchResultsSection.Visibility == Visibility.Visible)
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            SearchResultsList.Children.Clear();
            foreach (var tweak in App.Tweaks.All.Where(t => Matches(t, query))
                         .OrderBy(t => t.Category).ThenBy(t => t.Name))
                SearchResultsList.Children.Add(BuildSearchResultCard(tweak));
        }
    }

    // ---------------------------------------------------------------
    // UTILITY BUTTONS
    // ---------------------------------------------------------------

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var browser = new SnapshotBrowserDialog { XamlRoot = this.XamlRoot };
        await browser.ShowAsync();
        // Restore kan registry hebben gewijzigd → states re-detecten + UI bij.
        LoadingOverlayText.Text = "Re-reading state...";
        LoadingOverlay.Visibility = Visibility.Visible;
        try { await App.Tweaks.DetectStatesAsync(); }
        catch { }
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RebuildVisibleContent();
        UpdateFooter();
    }

    private async void RestartExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Restart Explorer?",
            Content = "Sluit alle open Explorer-vensters en herstart de shell zodat tweaks die geen live-update doen direct zichtbaar worden. Documenten / Downloads etc. blijven veilig op disk.",
            PrimaryButtonText = "Restart Explorer",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        RestartExplorerButton.IsEnabled = false;
        try
        {
            ShellRefresh.RestartExplorerSilent();
            await Task.Delay(1500);
            LoadingOverlayText.Text = "Re-reading state...";
            LoadingOverlay.Visibility = Visibility.Visible;
            try { await App.Tweaks.DetectStatesAsync(); }
            catch { }
            LoadingOverlay.Visibility = Visibility.Collapsed;
            RebuildVisibleContent();
        }
        finally
        {
            RestartExplorerButton.IsEnabled = true;
        }
    }
}
