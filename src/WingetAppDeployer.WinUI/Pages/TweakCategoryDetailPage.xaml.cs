using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Pages;

// Detail-pagina voor één tweak-categorie. Bereikt via een tile-klik op de
// TweaksPage landing. Toont de tweak-cards van die categorie; bij categorieën
// met sub-groepen (Tweak.Group) wordt hybride gerenderd:
//   - geen Group           → platte lijst, op naam gesorteerd
//   - Group + < 8 tweaks   → platte lijst met niet-inklapbare sub-headers
//   - Group + >= 8 tweaks  → inklapbare Expander per sub-groep (default open)
//
// Pending changes + Apply lopen via App.TweakPending + TweakApplyRunner —
// dezelfde gedeelde infra als de landing-footer.
public sealed partial class TweakCategoryDetailPage : Page
{
    // Drempel waarboven sub-groepen inklapbaar worden i.p.v. vaste headers.
    private const int CollapsibleGroupThreshold = 8;

    private TweakCategory _category;

    public TweakCategoryDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not TweakCategory category)
        {
            // Geen geldige parameter — terug naar de landing.
            if (Frame.CanGoBack) Frame.GoBack();
            return;
        }
        _category = category;
        App.TweakPending.Changed += OnPendingChanged;

        CategoryIcon.Text = category.Icon();
        CategoryTitle.Text = category.DisplayName();

        BuildContent();
        UpdateFooter();
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
    // CONTENT (hybride sub-groep rendering)
    // ---------------------------------------------------------------

    private void BuildContent()
    {
        ContentContainer.Children.Clear();
        var tweaks = App.Tweaks.InCategory(_category).ToList();
        var hasGroups = tweaks.Any(t => t.Group != null);

        if (!hasGroups)
        {
            // Platte lijst op naam.
            foreach (var tweak in tweaks.OrderBy(t => t.Name))
                ContentContainer.Children.Add(TweakCardFactory.Build(tweak));
            return;
        }

        // Ongegroepeerde tweaks eerst (plat).
        foreach (var tweak in tweaks.Where(t => t.Group == null))
            ContentContainer.Children.Add(TweakCardFactory.Build(tweak));

        // Groepen in insertievolgorde.
        var groupNames = tweaks.Where(t => t.Group != null)
            .Select(t => t.Group!)
            .Distinct()
            .ToList();
        var collapsible = tweaks.Count >= CollapsibleGroupThreshold;

        foreach (var gName in groupNames)
        {
            var inGroup = tweaks.Where(t => t.Group == gName).ToList();
            if (collapsible)
                ContentContainer.Children.Add(BuildCollapsibleGroup(gName, inGroup));
            else
            {
                ContentContainer.Children.Add(BuildSubHeader(gName, inGroup));
                foreach (var tweak in inGroup)
                    ContentContainer.Children.Add(TweakCardFactory.Build(tweak));
            }
        }
    }

    // Inklapbare Expander voor één sub-groep — default DICHT voor overzicht;
    // de user klapt zelf open wat 'ie wil zien.
    private static FrameworkElement BuildCollapsibleGroup(string name, IReadOnlyList<Tweak> tweaks)
    {
        var active = tweaks.Count(t => t.State == TweakState.Enabled || t.State == TweakState.Partial);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{active}/{tweaks.Count} actief",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        var cards = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var tweak in tweaks)
            cards.Children.Add(TweakCardFactory.Build(tweak));

        return new Expander
        {
            Header = header,
            Content = cards,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    // Niet-inklapbare sub-header voor kleinere categorieën.
    private static FrameworkElement BuildSubHeader(string name, IReadOnlyList<Tweak> tweaks)
    {
        var active = tweaks.Count(t => t.State == TweakState.Enabled || t.State == TweakState.Partial);
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(2, 10, 0, 0)
        };
        panel.Children.Add(new TextBlock
        {
            Text = name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{active}/{tweaks.Count}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
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

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        App.TweakPending.Clear();
        BuildContent();  // checkboxes terug naar gedetecteerde state
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
        BuildContent();
        UpdateFooter();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
