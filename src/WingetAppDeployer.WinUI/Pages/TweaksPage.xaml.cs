using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Pages;

// Tweaks tab — live state-detection + apply/revert per Windows tweak. Tweaks
// zijn geregistreerd in TweakService.BuildAll() (data-driven) en gerenderd hier
// als per-categorie cards. Per tweak een ToggleSwitch met:
//   - State binding op Tweak.IsToggleOn
//   - Visual indicators voor restart-requirement + admin/UAC
//   - Apply/revert via TweakService bij toggle-change
//
// Geen "Apply all" knop hier — elke toggle is immediate (zelfde patroon als
// Windows Settings zelf). User ziet meteen het resultaat in de StateLabel.
public sealed partial class TweaksPage : Page
{
    // Map elke ToggleSwitch → Tweak zodat we bij Toggled event weten welke
    // tweak en wat te doen. Niet via x:Bind want we genereren cards in code.
    private readonly Dictionary<ToggleSwitch, Tweak> _switchTweaks = new();

    public TweaksPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Initial state-detection — eerst overlay tonen, dan registry walken,
        // dan cards renderen met live state. Async zodat de UI-thread niet
        // blokkeert op de registry IO.
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            await App.Tweaks.DetectStatesAsync();
        }
        catch
        {
            // State-read mislukt globaal — cards komen met "Unknown" state.
            // Bewust geen blocking error.
        }
        BuildCategoryCards();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, Microsoft.UI.Xaml.Controls.ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

    private void BuildCategoryCards()
    {
        CategoriesContainer.Children.Clear();
        _switchTweaks.Clear();

        // Group tweaks per category, alleen categorieën met geregistreerde
        // items worden getoond — v0.9.1 heeft alleen Explorer, latere versies
        // vullen de rest aan zonder dat de page hier iets aan hoeft te doen.
        var grouped = App.Tweaks.All
            .GroupBy(t => t.Category)
            .OrderBy(g => (int)g.Key);

        foreach (var group in grouped)
        {
            var section = new StackPanel { Spacing = 8 };
            section.Children.Add(new TextBlock
            {
                Text = group.Key.DisplayName(),
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                Margin = new Thickness(0, 0, 0, 4)
            });

            foreach (var tweak in group.OrderBy(t => t.Name))
            {
                section.Children.Add(BuildTweakCard(tweak));
            }
            CategoriesContainer.Children.Add(section);
        }
    }

    private FrameworkElement BuildTweakCard(Tweak tweak)
    {
        // Card-layout: links titel + omschrijving + use-case, rechts de glyphs
        // (admin/restart) en de ToggleSwitch. Layout-mirror van DeepCleanDialog
        // BuildItemCard zodat de visual taal consistent is over de app.
        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12)
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left content
        var content = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = tweak.Name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });
        // Status-pill: Active / Default / Partial / Unknown — zodat user de
        // huidige state in een glance ziet, niet alleen via de toggle-stand.
        titleRow.Children.Add(BuildStatePill(tweak));
        content.Children.Add(titleRow);
        content.Children.Add(new TextBlock
        {
            Text = tweak.Description,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(tweak.UseCase))
        {
            content.Children.Add(new TextBlock
            {
                Text = tweak.UseCase,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
        }
        Grid.SetColumn(content, 0);
        grid.Children.Add(content);

        // Alleen admin/UAC indicator — restart-icoon weggehaald in v0.9.1
        // (de info komt via de InfoBar na toggle, dat is contextueel duidelijker).
        var glyphStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6
        };
        if (tweak.RequiresElevation)
            glyphStack.Children.Add(BuildGlyphIcon(tweak.AdminGlyph, tweak.AdminTooltip,
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]));
        Grid.SetColumn(glyphStack, 1);
        grid.Children.Add(glyphStack);

        // Toggle-switch — IsOn reflecteert de live registry-state. Toggle event
        // triggert Apply/Revert via TweakService.
        var toggle = new ToggleSwitch
        {
            IsOn = tweak.IsToggleOn,
            OffContent = string.Empty,
            OnContent = string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0
        };
        // Toggled fires bij user-input. Sla SetIsOn vanuit code-paden over door
        // tag-check (zie ToggleSwitch_Toggled).
        toggle.Toggled += ToggleSwitch_Toggled;
        _switchTweaks[toggle] = tweak;
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);

        border.Child = grid;
        return border;
    }

    private static FrameworkElement BuildGlyphIcon(string glyph, string tooltip, Microsoft.UI.Xaml.Media.Brush brush)
    {
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(icon, tooltip);
        return icon;
    }

    private static FrameworkElement BuildStatePill(Tweak tweak)
    {
        // Kleur op basis van state: groen=Enabled, neutraal=Disabled,
        // geel=Partial, grijs=Unknown.
        var bg = tweak.State switch
        {
            TweakState.Enabled => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"],
            TweakState.Partial => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"],
            TweakState.Unknown => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"],
            _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"]
        };
        return new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = tweak.StateLabel,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
            }
        };
    }

    private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        if (!_switchTweaks.TryGetValue(toggle, out var tweak)) return;

        // Tag-flag voorkomt loop: bij re-render zetten we IsOn programmatisch
        // op de nieuwe state. We willen dan geen 2e Apply triggeren.
        if (toggle.Tag is bool isProgrammatic && isProgrammatic)
        {
            toggle.Tag = false;
            return;
        }

        var apply = toggle.IsOn;
        toggle.IsEnabled = false;
        try
        {
            var result = await App.Tweaks.ApplyAsync(new[] { tweak }, apply);
            ShowResult(tweak, apply, result);

            // Re-bind toggle naar de nieuwe state (kan partial blijven als één
            // van de ops faalde, of unchanged blijven bij UAC-denial).
            toggle.Tag = true;
            toggle.IsOn = tweak.IsToggleOn;

            // Voor ExplorerRestart-tweaks: bied user direct optie aan om
            // Explorer te restarten voor zichtbaarheid. Niet auto-do — sommige
            // users hebben unsaved Explorer-state (open dialogs).
            if (result.SuccessCount > 0 && tweak.Restart == RestartRequirement.ExplorerRestart)
            {
                _ = PromptExplorerRestart();
            }
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private void ShowResult(Tweak tweak, bool apply, TweakApplyResult result)
    {
        if (result.Cancelled)
        {
            ResultBar.Severity = InfoBarSeverity.Warning;
            ResultBar.Title = $"{tweak.Name}: cancelled";
            ResultBar.Message = "UAC prompt was declined — no changes were made.";
        }
        else if (result.FailedCount == 0)
        {
            ResultBar.Severity = InfoBarSeverity.Success;
            ResultBar.Title = apply ? $"{tweak.Name}: applied" : $"{tweak.Name}: reverted";
            ResultBar.Message = tweak.Restart switch
            {
                RestartRequirement.ExplorerRestart => "Restart Explorer to see the effect.",
                RestartRequirement.SignOut => "Sign out and back in to see the effect.",
                RestartRequirement.Reboot => "Reboot required for the change to take effect.",
                _ => "Change is active."
            };
        }
        else
        {
            ResultBar.Severity = InfoBarSeverity.Error;
            ResultBar.Title = $"{tweak.Name}: partially applied";
            ResultBar.Message = $"{result.FailedCount} of {result.SuccessCount + result.FailedCount} ops failed. Check tweak state.";
        }
        ResultBar.IsOpen = true;
    }

    private async Task PromptExplorerRestart()
    {
        var dialog = new ContentDialog
        {
            Title = "Restart Explorer?",
            Content = "Restart Windows Explorer nu om de tweak direct zichtbaar te maken? Open Explorer-vensters worden gesloten (Documenten / Downloads etc. blijven veilig op disk).",
            PrimaryButtonText = "Restart Explorer",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await TweakService.RestartExplorerAsync();
        }
    }
}
