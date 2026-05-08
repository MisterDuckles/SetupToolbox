using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Dialogs;

// Cleanup-dialog na een succesvolle uninstall. Twee fases:
//   1. Preview — items per LeftoverType groepering, checkboxes, user kiest.
//   2. Delete  — UAC prompt voor de elevated subset (HKLM / Program Files /
//      ProgramData), local subset (HKCU / AppData) wordt direct in-process
//      verwijderd. Daarna summary, dialog blijft open tot user Close klikt.
//
// "Always preview, never auto-delete" is de leidraad uit de roadmap. Daarom
// secondary-button "Skip" — niets verwijderen is altijd een geldige uitkomst.
public sealed partial class LeftoverCleanupDialog : ContentDialog
{
    private readonly IReadOnlyList<LeftoverItem> _items;
    private readonly LeftoverScannerService _scanner;
    private bool _deleteRunning;
    private bool _deleteCompleted;  // true zodra de delete-batch afgerond is — vervolgklikken op "Close" mogen dialog wél sluiten
    public LeftoverDeleteResult? DeleteResult { get; private set; }

    public LeftoverCleanupDialog(IReadOnlyList<LeftoverItem> items, LeftoverScannerService scanner)
    {
        _items = items;
        _scanner = scanner;
        InitializeComponent();
        BuildGroupedList();
        UpdateSelectionStatus();
        UpdatePrimaryEnabled();
    }

    private void BuildGroupedList()
    {
        // Bouw per LeftoverType een sub-card met header + items. Volgorde uit
        // de scanner is al gesorteerd op (Type, Confidence, Path), dus we
        // kunnen lineair door de lijst lopen en bij elke type-overgang een
        // header inserten.
        GroupContainer.Children.Clear();

        var distinctSources = _items.Select(i => i.SourceAppName).Distinct().ToList();
        var appsLabel = distinctSources.Count == 1
            ? distinctSources[0]
            : $"{distinctSources.Count} apps";
        HeaderText.Text = $"Found {_items.Count} possible leftover items from {appsLabel}";

        foreach (var group in _items.GroupBy(i => i.Type))
        {
            var sectionPanel = new StackPanel { Spacing = 6 };
            sectionPanel.Children.Add(new TextBlock
            {
                Text = SectionTitle(group.Key, group.Count()),
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });

            foreach (var item in group)
            {
                sectionPanel.Children.Add(BuildItemCard(item));
            }
            GroupContainer.Children.Add(sectionPanel);
        }
    }

    private static string SectionTitle(LeftoverType type, int count) => type switch
    {
        LeftoverType.RegistryKey => $"Registry uninstall keys ({count})",
        LeftoverType.ProgramFilesFolder => $"Program Files folders ({count})",
        LeftoverType.AppDataFolder => $"AppData folders ({count})",
        _ => $"Other ({count})"
    };

    private FrameworkElement BuildItemCard(LeftoverItem item)
    {
        // CheckBox links + content rechts. Content = path + (size + confidence + source).
        // Confidence-tier kleurt de rand subtiel zodat user in 1 oogopslag ziet
        // "high match" (groen-ish) vs "loose match" (grijs). High default checked.
        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = ConfidenceBorderBrush(item.Confidence),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnSpacing = 12;

        var check = new CheckBox
        {
            IsChecked = item.IsSelected,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Tag = item
        };
        check.Checked += ItemCheck_Toggled;
        check.Unchecked += ItemCheck_Toggled;
        Grid.SetColumn(check, 0);
        grid.Children.Add(check);

        var content = new StackPanel { Spacing = 2 };
        var pathBlock = new TextBlock
        {
            Text = item.Path,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        content.Children.Add(pathBlock);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        meta.Children.Add(new TextBlock
        {
            Text = item.ConfidenceLabel,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        if (!string.IsNullOrEmpty(item.SizeLabel))
        {
            meta.Children.Add(new TextBlock
            {
                Text = "·",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            meta.Children.Add(new TextBlock
            {
                Text = item.SizeLabel,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        meta.Children.Add(new TextBlock
        {
            Text = "·",
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        meta.Children.Add(new TextBlock
        {
            Text = $"from {item.SourceAppName}",
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        if (item.RequiresElevation)
        {
            meta.Children.Add(new TextBlock
            {
                Text = "· admin",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
        }
        content.Children.Add(meta);

        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        border.Child = grid;
        return border;
    }

    private static Microsoft.UI.Xaml.Media.Brush ConfidenceBorderBrush(LeftoverConfidence c) => c switch
    {
        LeftoverConfidence.High => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"],
        LeftoverConfidence.Medium => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        LeftoverConfidence.Low => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        _ => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
    };

    private void ItemCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is LeftoverItem item)
        {
            item.IsSelected = cb.IsChecked == true;
            UpdateSelectionStatus();
            UpdatePrimaryEnabled();
        }
    }

    private void ToggleAllButton_Click(object sender, RoutedEventArgs e)
    {
        var allSelected = _items.All(i => i.IsSelected);
        var newState = !allSelected;
        // Update both the bound item én de XAML CheckBoxes — we hebben geen
        // x:Bind TwoWay omdat we de cards programmatisch bouwen. Dus loopen
        // we door de hierarchie en zetten per CheckBox.
        foreach (var cb in EnumerateItemCheckBoxes())
        {
            cb.IsChecked = newState;
        }
    }

    private IEnumerable<CheckBox> EnumerateItemCheckBoxes()
    {
        foreach (var section in GroupContainer.Children.OfType<StackPanel>())
            foreach (var border in section.Children.OfType<Border>())
                if (border.Child is Grid g)
                    foreach (var cb in g.Children.OfType<CheckBox>())
                        yield return cb;
    }

    private void UpdateSelectionStatus()
    {
        var selected = _items.Count(i => i.IsSelected);
        var elevated = _items.Count(i => i.IsSelected && i.RequiresElevation);
        if (selected == 0)
        {
            SelectionStatusText.Text = "Nothing selected";
        }
        else
        {
            SelectionStatusText.Text = elevated > 0
                ? $"{selected} selected · {elevated} need administrator rights"
                : $"{selected} selected";
        }
        ToggleAllButton.Content = _items.All(i => i.IsSelected) ? "Deselect all" : "Select all";
    }

    private void UpdatePrimaryEnabled()
    {
        IsPrimaryButtonEnabled = !_deleteRunning && _items.Any(i => i.IsSelected);
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Na delete is de Primary-knop een "Close" — laat de click gewoon
        // doorgaan naar default-close. Geen deferral meer nodig.
        if (_deleteCompleted) return;

        // Defer-pattern: anders sluit de dialog meteen na de click. Deferral gives
        // ons async ruimte om de delete af te ronden voordat het dialog verdwijnt
        // / een nieuwe ronde knoppen toont.
        var deferral = args.GetDeferral();
        try
        {
            var selected = _items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                args.Cancel = true;
                return;
            }

            // Switch UI naar progress-mode.
            _deleteRunning = true;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressStatusText.Text = $"Deleting {selected.Count} item(s)...";
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            ToggleAllButton.IsEnabled = false;
            // Disable all checkboxes — niet meer interactief tijdens delete.
            foreach (var cb in EnumerateItemCheckBoxes()) cb.IsEnabled = false;

            DeleteResult = await _scanner.DeleteAsync(selected);

            // Update UI met summary, swap knoppen om dialog op "Close" te zetten.
            args.Cancel = true;  // dialog NIET sluiten — user moet eerst summary zien
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            if (DeleteResult.Cancelled)
            {
                ProgressStatusText.Text = "Cancelled — UAC prompt declined. No leftovers were deleted.";
            }
            else if (DeleteResult.FailedCount == 0)
            {
                ProgressStatusText.Text = $"Done — {DeleteResult.SuccessCount} item(s) deleted.";
            }
            else
            {
                ProgressStatusText.Text = $"Done — {DeleteResult.SuccessCount} deleted, {DeleteResult.FailedCount} failed.";
            }

            // Primary wordt nu een Close-knop (geen extra delete meer mogelijk).
            _deleteCompleted = true;
            PrimaryButtonText = "Close";
            SecondaryButtonText = string.Empty;
            IsPrimaryButtonEnabled = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
