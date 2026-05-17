using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
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
        UpdateRestoreButton();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    // "Vorige staat herstellen" knop alleen zichtbaar wanneer er minstens 1
    // snapshot bestaat. Wordt elke keer ge-re-evalueerd na een Apply (nieuwe
    // snapshot komt erbij) of een restore (state verandert maar snapshot
    // blijft, knop blijft zichtbaar).
    private void UpdateRestoreButton()
    {
        var hasSnapshots = App.Snapshots.GetLatest() != null;
        RestoreButton.Visibility = hasSnapshots ? Visibility.Visible : Visibility.Collapsed;
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
            // IsThreeState=true zodat Partial visueel als indeterminate (vierkant
            // ipv vinkje) wordt getoond, en de cycle false→null→true beschikbaar
            // is. Vanuit Partial klikt user direct naar true (= apply); vanuit
            // Disabled vangt CheckBox_Toggled de null-intermediate op en springt
            // door naar true zodat 1 klik = apply blijft.
            var checkbox = new CheckBox
            {
                IsThreeState = true,
                IsChecked = tweak.State switch
                {
                    TweakState.Enabled => true,
                    TweakState.Partial => (bool?)null,
                    _ => false
                },
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                Content = string.Empty
            };
            checkbox.Checked += CheckBox_Toggled;
            checkbox.Unchecked += CheckBox_Toggled;
            checkbox.Indeterminate += CheckBox_Toggled;
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

        var newChecked = cb.IsChecked;  // bool? — null = indeterminate
        // "Natural" cb-waarde voor de huidige tweak state — wat de checkbox toont
        // als user nog niets gewijzigd heeft.
        bool? natural = tweak.State switch
        {
            TweakState.Enabled => true,
            TweakState.Partial => (bool?)null,
            _ => false  // Disabled of Unknown
        };

        // Skip de null-intermediate van de IsThreeState cycle wanneer user
        // vanaf Disabled (false) één klik doet — default WinUI cycle gaat dan
        // false→null, maar user wil naar true (apply). Forceer door naar true.
        // Voor Enabled (true) is de cycle true→false→null, dus null bereikt
        // user pas na 2 klikken — daar is null een legit "no change" intent.
        if (newChecked == null && natural == false)
        {
            cb.IsChecked = true;  // triggert Checked event → re-entry hier
            return;
        }

        if (newChecked == natural)
        {
            // User terug op natural state → niets pending.
            _pendingChanges.Remove(tweak);
        }
        else if (newChecked == true)
        {
            // Apply intent: alle ops naar EnabledValue. Voor Partial state
            // betekent dit "complete it tot fully Active".
            _pendingChanges[tweak] = true;
        }
        else if (newChecked == false)
        {
            // Revert intent: alle ops naar DisabledValue.
            _pendingChanges[tweak] = false;
        }
        else
        {
            // newChecked == null en natural != null (= Enabled). User klikte
            // 2x van Enabled, zit nu op indeterminate → "geen change" intent.
            _pendingChanges.Remove(tweak);
        }

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
        var pendingTweaks = snapshot.Select(kv => kv.Key).ToList();

        // Backup-flow vóór de daadwerkelijke Apply. Afhankelijk van
        // SettingsService.BackupBeforeApply:
        //   Ask    → BackupPromptDialog tonen, user kiest custom naam +
        //            "ja met backup" / "nee zonder" / "annuleer"
        //   Always → silent snapshot maken met auto-description
        //   Never  → niets, direct door naar apply
        var mode = App.Settings.BackupBeforeApply;
        var captureSnapshot = false;
        var snapshotName = string.Empty;

        if (mode == BackupBeforeApplyMode.Ask)
        {
            var prompt = new BackupPromptDialog { XamlRoot = this.XamlRoot };
            var result = await prompt.ShowAsync();
            if (result == ContentDialogResult.None) return;  // user canceled = abort apply

            captureSnapshot = (result == ContentDialogResult.Primary);
            snapshotName = prompt.CustomName;

            // "Niet meer vragen" → settings switchen naar Always/Never zodat
            // dezelfde keuze automatisch hergebruikt wordt.
            if (prompt.DontAskAgain)
            {
                App.Settings.BackupBeforeApply = captureSnapshot
                    ? BackupBeforeApplyMode.Always
                    : BackupBeforeApplyMode.Never;
            }
        }
        else if (mode == BackupBeforeApplyMode.Always)
        {
            captureSnapshot = true;
        }
        // Never → captureSnapshot blijft false

        ApplyButton.IsEnabled = false;
        DiscardButton.IsEnabled = false;
        RestartExplorerButton.IsEnabled = false;
        LoadingOverlayText.Text = captureSnapshot
            ? "Snapshot maken..."
            : $"Applying {snapshot.Count} change{(snapshot.Count == 1 ? "" : "s")}...";
        LoadingOverlay.Visibility = Visibility.Visible;

        // Snapshot voor de écht écht écht eerste write — anders zit de "voor"-
        // state al gepollueerd door de eerste op-write. CaptureAsync leest
        // synchroon de registry voor we wat doen.
        if (captureSnapshot)
        {
            try
            {
                await App.Snapshots.CaptureAsync(pendingTweaks, snapshotName);
            }
            catch
            {
                // Snapshot is een vangnet — bij faal niet aborten, door met
                // apply zoals voorheen.
            }
            LoadingOverlayText.Text = $"Applying {snapshot.Count} change{(snapshot.Count == 1 ? "" : "s")}...";
        }

        var totalSuccess = 0;
        var totalFailed = 0;
        var cancelled = false;
        var anyExplorerRestartTweak = false;
        var anySignOutTweak = false;
        // Per-tweak failure-tracking voor de InfoBar — laat user precies zien
        // welke tweak faalde + de exception/reg.exe-error string. Anders blijft
        // het bij een nietszeggende "1 change(s) failed" message.
        var failureLines = new List<string>();

        try
        {
            // Split pending changes in 3 buckets. Toggle-tweaks worden GEBATCHT
            // per richting (apply / revert) in één ApplyAsync-call — die
            // verzamelt zelf alle elevated ops van alle meegegeven tweaks in
            // ÉÉN RunElevatedBatchAsync = 1 UAC-prompt voor de hele batch.
            // Eerder werd ApplyAsync per-tweak aangeroepen → 1 UAC per elevated
            // tweak (4 AI-tweaks aanvinken = 4 prompts). Choice-tweaks blijven
            // individueel (ApplyChoiceAsync neemt 1 tweak) — die zijn in de
            // praktijk allemaal HKCU/geen-UAC dus geen prompt-spam.
            var toggleApply = new List<Tweak>();
            var toggleRevert = new List<Tweak>();
            var choiceChanges = new List<(Tweak tweak, int idx)>();
            foreach (var (tweak, desiredState) in snapshot)
            {
                if (tweak.IsChoice && desiredState is int idx)
                    choiceChanges.Add((tweak, idx));
                else if (desiredState is bool apply)
                    (apply ? toggleApply : toggleRevert).Add(tweak);
            }

            // Lokale helper die één ApplyAsync-batch verwerkt + de resultaat-
            // aggregatie doet (success/fail counts, restart-flags, failures).
            async Task RunToggleBatch(IReadOnlyList<Tweak> tweaks, bool apply)
            {
                if (tweaks.Count == 0) return;
                var result = await App.Tweaks.ApplyAsync(tweaks, apply);
                totalSuccess += result.SuccessCount;
                totalFailed += result.FailedCount;
                if (result.Cancelled) cancelled = true;
                foreach (var t in tweaks)
                {
                    if (t.Restart == RestartRequirement.ExplorerRestart) anyExplorerRestartTweak = true;
                    // SignOut/Reboot-tweaks (Recall, Click to Do, generative-AI
                    // policy) hebben pas effect na uitloggen. Alleen markeren
                    // wanneer er writes slaagden.
                    if (result.SuccessCount > 0 &&
                        (t.Restart == RestartRequirement.SignOut || t.Restart == RestartRequirement.Reboot))
                    {
                        anySignOutTweak = true;
                    }
                }
                failureLines.AddRange(result.FailureMessages);
            }

            await RunToggleBatch(toggleApply, apply: true);
            await RunToggleBatch(toggleRevert, apply: false);

            // Choice-tweaks individueel — ApplyChoiceAsync neemt 1 tweak.
            foreach (var (tweak, idx) in choiceChanges)
            {
                var result = await App.Tweaks.ApplyChoiceAsync(tweak, idx);
                totalSuccess += result.SuccessCount;
                totalFailed += result.FailedCount;
                if (result.Cancelled) cancelled = true;
                if (tweak.Restart == RestartRequirement.ExplorerRestart) anyExplorerRestartTweak = true;
                if (result.SuccessCount > 0 &&
                    (tweak.Restart == RestartRequirement.SignOut || tweak.Restart == RestartRequirement.Reboot))
                {
                    anySignOutTweak = true;
                }
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
            UpdateRestoreButton();

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
                // Prioriteit van de melding: sign-out vereist > explorer-restart
                // > niets. Sign-out tweaks (AI-policies etc.) hebben pas effect
                // na uitloggen, dat moet de user weten.
                if (anySignOutTweak)
                    ResultBar.Message = "Sommige tweaks (zoals AI-policies) hebben pas effect nadat je uitlogt of de PC herstart.";
                else if (anyExplorerRestartTweak)
                    ResultBar.Message = "Explorer herstart — wacht ~1s tot de taskbar terug is.";
                else
                    ResultBar.Message = "Done.";
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

    // "Vorige staat herstellen" — toont een dialoog die alle snapshots laat
    // zien zodat user kan kiezen welke staat 'ie terug wil (niet alleen de
    // laatste). Dialoog handelt zelf de restore-call + confirm-prompt af.
    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var browser = new SnapshotBrowserDialog { XamlRoot = this.XamlRoot };
        await browser.ShowAsync();
        // Na sluiten: re-detect states want de restore kan registry hebben
        // gewijzigd. Pending changes blijven niet bestaan (gebruiker heeft
        // page-state niet aangeraakt tijdens de modal dialog).
        await LoadStateAndRender("Re-reading state...");
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
