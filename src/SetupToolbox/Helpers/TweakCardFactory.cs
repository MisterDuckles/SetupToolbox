using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SetupToolbox.Models;

namespace SetupToolbox.Helpers;

// Gedeelde renderer voor één tweak-card. Sinds de v0.9.8 Tweaks-tab
// herstructurering (category-grid → detail-pagina) wordt dezelfde card
// gebruikt op de detail-pagina én in de globale zoekresultaten op de landing.
// Daarom een centrale factory i.p.v. een per-page methode.
//
// De card schrijft z'n CheckBox/ComboBox-mutaties rechtstreeks naar
// App.TweakPending (de cross-page pending-store). Pages hoeven niets te
// koppelen — ze abonneren op TweakPendingService.Changed voor hun footer.
//
// CheckBox is IsThreeState: Partial → indeterminate vierkant. De cycle
// false→null→true wordt voor Disabled-state opgevangen zodat 1 klik = apply.
public static class TweakCardFactory
{
    /// <summary>
    /// Bouwt de Border-card voor één tweak. De control rechts is een ComboBox
    /// (choice-tweak) of een IsThreeState CheckBox (toggle-tweak), gekoppeld aan
    /// App.TweakPending.
    ///
    /// In <paramref name="profileMode"/> (de v0.9.20 profiel-bouwer) is het een
    /// CLEAN-SLATE selectie: de checkbox/ComboBox negeert de live system-state en
    /// schrijft naar App.ProfileSelection. De status-pill wordt dan verborgen —
    /// het profiel is een gewenste-config, geen weergave van de huidige staat.
    /// </summary>
    public static FrameworkElement Build(Tweak tweak, bool profileMode = false)
    {
        var border = new Border
        {
            Background = Res<Brush>("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Res<Brush>("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12)
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Linker content: naam + status-pill, omschrijving, use-case.
        var content = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = tweak.Name,
            Style = Res<Style>("BodyStrongTextBlockStyle"),
            VerticalAlignment = VerticalAlignment.Center
        });
        // Status-pill alleen buiten profiel-modus — in clean-slate is de live
        // state niet relevant en zou 'm verwarrend zijn.
        if (!profileMode)
            titleRow.Children.Add(BuildStatePill(tweak));
        content.Children.Add(titleRow);
        content.Children.Add(new TextBlock
        {
            Text = tweak.Description,
            Style = Res<Style>("CaptionTextBlockStyle"),
            Foreground = Res<Brush>("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(tweak.UseCase))
        {
            content.Children.Add(new TextBlock
            {
                Text = tweak.UseCase,
                Style = Res<Style>("CaptionTextBlockStyle"),
                Foreground = Res<Brush>("TextFillColorTertiaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
        }
        Grid.SetColumn(content, 0);
        grid.Children.Add(content);

        // Admin/UAC lock-icoon (alleen bij elevated tweaks).
        var glyphStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6
        };
        if (tweak.RequiresElevation)
        {
            var icon = new FontIcon
            {
                Glyph = tweak.AdminGlyph,
                FontSize = 14,
                Foreground = Res<Brush>("SystemFillColorCautionBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrEmpty(tweak.AdminTooltip))
                ToolTipService.SetToolTip(icon, tweak.AdminTooltip);
            glyphStack.Children.Add(icon);
        }
        Grid.SetColumn(glyphStack, 1);
        grid.Children.Add(glyphStack);

        // Rechter control.
        FrameworkElement rightControl = tweak.IsChoice && tweak.Choices != null
            ? BuildChoiceCombo(tweak, profileMode)
            : BuildToggleCheckBox(tweak, profileMode);
        Grid.SetColumn(rightControl, 2);
        grid.Children.Add(rightControl);

        border.Child = grid;
        return border;
    }

    private static ComboBox BuildChoiceCombo(Tweak tweak, bool profileMode)
    {
        if (profileMode)
        {
            // Clean slate: een "— niet in profiel —" sentinel op index 0, daarna
            // de echte opties. Selectie schrijft naar App.ProfileSelection.
            var labels = new List<string> { "— niet in profiel —" };
            labels.AddRange(tweak.Choices!.Select(c => c.Label));
            var initial = 0;
            if (App.ProfileSelection.TryGet(tweak, out var pv) && pv is int pidx) initial = pidx + 1;

            var pc = new ComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 200,
                ItemsSource = labels,
                SelectedIndex = initial
            };
            pc.SelectionChanged += (s, e) =>
            {
                var sel = pc.SelectedIndex;
                if (sel <= 0) App.ProfileSelection.Remove(tweak);   // sentinel = niet opnemen
                else App.ProfileSelection.Set(tweak, sel - 1);      // echte choice-index
            };
            return pc;
        }

        // Initiële selectie: pending desired index als die er is, anders de
        // gedetecteerde SelectedChoiceIndex.
        var initialIndex = tweak.SelectedChoiceIndex;
        if (App.TweakPending.TryGet(tweak, out var pendingVal) && pendingVal is int pendingIdx)
            initialIndex = pendingIdx;

        var combo = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 180,
            ItemsSource = tweak.Choices!.Select(c => c.Label).ToList(),
            SelectedIndex = initialIndex
        };
        combo.SelectionChanged += (s, e) =>
        {
            var idx = combo.SelectedIndex;
            if (idx < 0 || tweak.Choices == null || idx >= tweak.Choices.Count) return;
            // Pending alleen als de keuze afwijkt van de gedetecteerde state.
            if (idx == tweak.SelectedChoiceIndex) App.TweakPending.Remove(tweak);
            else App.TweakPending.Set(tweak, idx);
        };
        return combo;
    }

    private static CheckBox BuildToggleCheckBox(Tweak tweak, bool profileMode)
    {
        if (profileMode)
        {
            // Clean slate: altijd 2-state, initieel UIT (negeert tweak.State).
            // Aangevinkt = opnemen in profiel; schrijft naar App.ProfileSelection.
            var pcb = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                Content = string.Empty,
                IsThreeState = false,
                IsChecked = App.ProfileSelection.Contains(tweak)
            };
            RoutedEventHandler pHandler = (s, e) =>
            {
                if (pcb.IsChecked == true) App.ProfileSelection.Set(tweak, true);
                else App.ProfileSelection.Remove(tweak);
            };
            pcb.Checked += pHandler;
            pcb.Unchecked += pHandler;
            return pcb;
        }

        var checkbox = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Content = string.Empty
        };

        // Pending desired (bool) als de tweak al in App.TweakPending zit.
        bool? pendingDesired = null;
        if (App.TweakPending.TryGet(tweak, out var pv) && pv is bool pb) pendingDesired = pb;

        if (tweak.State == TweakState.Partial)
        {
            // Partial-tweak → 3-state checkbox. De minus (indeterminate) is hier
            // legitiem: de tweak ÍS deels toegepast. Eigen 3-staps cyclus:
            //   null  = geen wijziging (laat partial zoals 't is)
            //   true  = compleet maken (apply)
            //   false = terugdraaien (revert)
            // We sturen de cyclus via een 'logical' veld onafhankelijk van
            // WinUI's interne IsThreeState-volgorde, zodat klik 1 = "compleet
            // maken" is (de gangbare intentie bij een Partial tweak).
            checkbox.IsThreeState = true;
            bool? logical = pendingDesired;            // null als geen pending
            checkbox.IsChecked = logical;
            var reentry = false;
            RoutedEventHandler handler = (s, e) =>
            {
                if (reentry) return;
                logical = logical switch { null => true, true => false, _ => (bool?)null };
                reentry = true;
                checkbox.IsChecked = logical;          // visual synct met ons model
                reentry = false;
                if (logical == null) App.TweakPending.Remove(tweak);
                else App.TweakPending.Set(tweak, logical == true);
            };
            checkbox.Checked += handler;
            checkbox.Unchecked += handler;
            checkbox.Indeterminate += handler;
        }
        else
        {
            // Enabled / Disabled / Unknown → schone 2-state checkbox. Nooit een
            // verwarrende minus: checked = tweak moet AAN, unchecked = UIT.
            checkbox.IsThreeState = false;
            var natural = tweak.State == TweakState.Enabled;
            checkbox.IsChecked = pendingDesired ?? natural;
            RoutedEventHandler handler = (s, e) =>
            {
                var desired = checkbox.IsChecked == true;
                if (desired == natural) App.TweakPending.Remove(tweak);  // terug op natural
                else App.TweakPending.Set(tweak, desired);               // apply / revert
            };
            checkbox.Checked += handler;
            checkbox.Unchecked += handler;
        }
        return checkbox;
    }

    private static FrameworkElement BuildStatePill(Tweak tweak)
    {
        var bg = tweak.State switch
        {
            TweakState.Enabled => Res<Brush>("SystemFillColorSuccessBackgroundBrush"),
            TweakState.Partial => Res<Brush>("SystemFillColorCautionBackgroundBrush"),
            _ => Res<Brush>("ControlAltFillColorSecondaryBrush")
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
                Style = Res<Style>("CaptionTextBlockStyle")
            }
        };
    }

    private static T Res<T>(string key) => (T)Application.Current.Resources[key];
}
