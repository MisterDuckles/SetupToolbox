using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Pages;

// Tweaks tab — checkbox-based pending-changes flow met Apply-batch knop.
// User vinkt aan/uit, app schrijft niets tot 'Apply' geklikt wordt. Voorkomt
// race-condities, rapid-fire writes, en geeft user predictable batch-effect
// (1x explorer-restart aan einde i.p.v. per-toggle restart-overhead).
//
// Flow:
//   1. OnNavigatedTo → DetectStatesAsync vult Tweak.State + SelectedChoiceIndex
//   2. BuildCategoryCards rendert CheckBox per toggle-tweak, ComboBox per choice-tweak
//   3. User changes → _pendingChanges tracker krijgt entry met desired state
//   4. ApplyButton wordt enabled met count "Apply (N)"
//   5. User klikt Apply → ApplyAllAsync schrijft alle changes + side-effects + 1x explorer-restart
//   6. State re-detect, UI rebuild, pending cleared
public sealed partial class TweaksPage : Page
{
    // Map elke CheckBox/ComboBox → Tweak voor reverse-lookup bij events.
    private readonly Dictionary<CheckBox, Tweak> _checkboxTweaks = new();
    private readonly Dictionary<ComboBox, Tweak> _comboTweaks = new();

    // Pending changes — value is bool (toggle desired state) of int (choice index).
    // Een tweak zit alleen in deze dict als de user iets gewijzigd heeft t.o.v.
    // de huidige systeem-state. Bij terug-naar-original → uit dict halen.
    private readonly Dictionary<Tweak, object> _pendingChanges = new();

    public TweaksPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadStateAndRender("Reading Windows state...");
    }

    private async Task LoadStateAndRender(string overlayText)
    {
        LoadingOverlayText.Text = overlayText;
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            await App.Tweaks.DetectStatesAsync();
        }
        catch
        {
            // State-read mislukt globaal — cards komen met "Unknown" state.
        }
        _pendingChanges.Clear();
        BuildCategoryCards();
        UpdateApplyButton();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

    private void BuildCategoryCards()
    {
        CategoriesContainer.Children.Clear();
        _checkboxTweaks.Clear();
        _comboTweaks.Clear();

        var grouped = App.Tweaks.All
            .GroupBy(t => t.Category)
            .OrderBy(g => (int)g.Key);

        foreach (var group in grouped)
        {
            // Header met categorie-naam + "N active / M total" count zodat
            // user direct kan zien welke categorie tweaks bevat die al actief
            // staan zonder de Expander open te klikken.
            var tweaksInGroup = group.OrderBy(t => t.Name).ToList();
            var activeCount = tweaksInGroup.Count(t => t.State == TweakState.Enabled || t.State == TweakState.Partial);
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            header.Children.Add(new TextBlock
            {
                Text = group.Key.DisplayName(),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = $"{activeCount}/{tweaksInGroup.Count} active",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });

            // Content stack — alle tweak-cards van deze categorie onder elkaar.
            var cards = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var tweak in tweaksInGroup)
                cards.Children.Add(BuildTweakCard(tweak));

            // Default collapsed voor overzicht — user opent per categorie wat 'ie
            // wil zien. HorizontalAlignment.Stretch + HorizontalContentAlignment
            // zorgen dat het content-panel de volle breedte krijgt zodat cards
            // niet inkrimpen naar Expander's natuurlijke content-width.
            var expander = new Expander
            {
                Header = header,
                Content = cards,
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            CategoriesContainer.Children.Add(expander);
        }
    }

    private FrameworkElement BuildTweakCard(Tweak tweak)
    {
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

        // Admin/UAC lock-icoon (alleen bij HKLM-tweaks).
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

        // Choice-tweak → ComboBox; toggle-tweak → CheckBox.
        FrameworkElement rightControl;
        if (tweak.IsChoice && tweak.Choices != null)
        {
            var combo = new ComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 180,
                ItemsSource = tweak.Choices.Select(c => c.Label).ToList(),
                SelectedIndex = tweak.SelectedChoiceIndex
            };
            combo.SelectionChanged += ComboBox_SelectionChanged;
            _comboTweaks[combo] = tweak;
            rightControl = combo;
        }
        else
        {
            var checkbox = new CheckBox
            {
                IsChecked = tweak.IsToggleOn,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                Content = string.Empty
            };
            checkbox.Checked += CheckBox_Toggled;
            checkbox.Unchecked += CheckBox_Toggled;
            _checkboxTweaks[checkbox] = tweak;
            rightControl = checkbox;
        }
        Grid.SetColumn(rightControl, 2);
        grid.Children.Add(rightControl);

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

    private void CheckBox_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb) return;
        if (!_checkboxTweaks.TryGetValue(cb, out var tweak)) return;
        var desired = cb.IsChecked == true;
        // Vergelijk met current system-state: alleen pending als anders.
        // tweak.IsToggleOn = (State == Enabled || Partial). Bij Partial-state
        // de checkbox staat visueel checked, dus desired==true matcht initial
        // → niet pending; user moet bewust uitvinken om in pending te komen.
        if (desired == tweak.IsToggleOn) _pendingChanges.Remove(tweak);
        else _pendingChanges[tweak] = desired;
        UpdateApplyButton();
    }

    private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (!_comboTweaks.TryGetValue(combo, out var tweak)) return;
        var idx = combo.SelectedIndex;
        if (idx < 0 || tweak.Choices == null || idx >= tweak.Choices.Count) return;
        // Pending alleen als andere index dan huidige detected state.
        if (idx == tweak.SelectedChoiceIndex) _pendingChanges.Remove(tweak);
        else _pendingChanges[tweak] = idx;
        UpdateApplyButton();
    }

    private void UpdateApplyButton()
    {
        var count = _pendingChanges.Count;
        ApplyButton.IsEnabled = count > 0;
        ApplyButtonText.Text = count > 0 ? $"Apply ({count})" : "Apply";
        DiscardButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingChanges.Count == 0) return;

        var snapshot = _pendingChanges.ToList();
        ApplyButton.IsEnabled = false;
        DiscardButton.IsEnabled = false;
        RestartExplorerButton.IsEnabled = false;
        LoadingOverlayText.Text = $"Applying {snapshot.Count} change{(snapshot.Count == 1 ? "" : "s")}...";
        LoadingOverlay.Visibility = Visibility.Visible;

        var totalSuccess = 0;
        var totalFailed = 0;
        var cancelled = false;
        var anyExplorerRestartTweak = false;
        // Per-tweak failure-tracking voor de InfoBar — laat user precies zien
        // welke tweak faalde + de exception/reg.exe-error string. Anders blijft
        // het bij een nietszeggende "1 change(s) failed" message.
        var failureLines = new List<string>();

        try
        {
            foreach (var (tweak, desiredState) in snapshot)
            {
                TweakApplyResult result;
                if (tweak.IsChoice && desiredState is int idx)
                {
                    result = await App.Tweaks.ApplyChoiceAsync(tweak, idx);
                }
                else if (desiredState is bool apply)
                {
                    result = await App.Tweaks.ApplyAsync(new[] { tweak }, apply);
                }
                else
                {
                    continue;
                }
                totalSuccess += result.SuccessCount;
                totalFailed += result.FailedCount;
                if (result.Cancelled) cancelled = true;
                if (tweak.Restart == RestartRequirement.ExplorerRestart) anyExplorerRestartTweak = true;
                foreach (var msg in result.FailureMessages)
                    failureLines.Add($"{tweak.Name} → {msg}");
            }

            // Eén explorer-restart aan het einde voor shell-cached tweaks
            // (TaskbarAl / NeverCombine / ShowSeconds / ClassicContextMenu).
            // Andere apply-side-effects (WM_SETTINGCHANGE broadcast +
            // SearchHost-restart voor taskbar-rebind) zijn al per-tweak
            // gedaan door ApplyAsync / ApplyChoiceAsync.
            if (anyExplorerRestartTweak)
            {
                // Wacht even zodat de SearchHost-respawn settled — anders kunnen
                // de twee restarts elkaars timing verstoren.
                await Task.Delay(250);
                ShellRefresh.RestartExplorerSilent();
            }

            // Re-detect alle states zodat de nieuwe waardes correct gerender'd worden.
            await App.Tweaks.DetectStatesAsync();
            _pendingChanges.Clear();
            BuildCategoryCards();
            UpdateApplyButton();

            // Resultaat-feedback in InfoBar.
            if (cancelled)
            {
                ResultBar.Severity = InfoBarSeverity.Warning;
                ResultBar.Title = "Apply cancelled";
                ResultBar.Message = "UAC prompt was declined — partial changes may have been written.";
            }
            else if (totalFailed == 0)
            {
                ResultBar.Severity = InfoBarSeverity.Success;
                ResultBar.Title = $"Applied {snapshot.Count} change{(snapshot.Count == 1 ? "" : "s")}";
                ResultBar.Message = anyExplorerRestartTweak
                    ? "Explorer herstart — wacht ~1s tot de taskbar terug is."
                    : "Done.";
            }
            else
            {
                ResultBar.Severity = InfoBarSeverity.Error;
                ResultBar.Title = $"{totalFailed} change(s) failed";
                // Toon de eerste paar failure-lijnen direct in de message zodat
                // user kan zien WELKE tweak/key faalde en de exacte reason. Bij
                // veel failures cappen we op 4 + "..." om de InfoBar leesbaar te
                // houden; de rest blijft te vinden via de tweak-state (Disabled
                // pill = niet doorgekomen).
                var preview = failureLines.Take(4).ToList();
                var more = failureLines.Count - preview.Count;
                var body = string.Join("\n", preview);
                if (more > 0) body += $"\n…en {more} meer.";
                if (totalSuccess > 0) body += $"\n({totalSuccess} writes wel succesvol)";
                ResultBar.Message = body;
            }
            ResultBar.IsOpen = true;
        }
        finally
        {
            ApplyButton.IsEnabled = _pendingChanges.Count > 0;
            DiscardButton.IsEnabled = true;
            RestartExplorerButton.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        // Reset alle checkboxes/comboboxes terug naar de detected systeem-state.
        // Simplest implementation: re-detect + rebuild (zelfde als initial load).
        await LoadStateAndRender("Discarding pending changes...");
    }

    private async void RestartExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        // Manual escape voor zeldzame gevallen waar Apply niet voldoende was.
        var dialog = new ContentDialog
        {
            Title = "Restart Explorer?",
            Content = "Sluit alle open Explorer-vensters en herstart de shell zodat tweaks die geen live-update doen direct zichtbaar worden. Documenten / Downloads etc. blijven veilig op disk.",
            PrimaryButtonText = "Restart Explorer",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            RestartExplorerButton.IsEnabled = false;
            try
            {
                ShellRefresh.RestartExplorerSilent();
                // State re-detect na restart om eventuele veranderingen op te pikken.
                await Task.Delay(1500);
                await LoadStateAndRender("Re-reading state...");
            }
            finally
            {
                RestartExplorerButton.IsEnabled = true;
            }
        }
    }
}
