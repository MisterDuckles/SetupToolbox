using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Pages;

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
        App.TweakPending.Changed += OnPendingChanged;

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

        BuildCategoryGrid();
        UpdateFooter();
        UpdateRestoreButton();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.TweakPending.Changed -= OnPendingChanged;
    }

    private void OnPendingChanged() => UpdateFooter();

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

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
    // FOOTER
    // ---------------------------------------------------------------

    private void UpdateFooter()
    {
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
        RestoreButton.Visibility = App.Snapshots.GetLatest() != null
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
