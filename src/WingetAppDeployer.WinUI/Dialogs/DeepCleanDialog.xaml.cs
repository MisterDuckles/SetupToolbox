using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Dialogs;

// Preview + delete dialog voor de deep clean. Twee fases zoals
// LeftoverCleanupDialog (v0.8.5):
//   1. Preview — items per category gegroepeerd, checkboxes, user kiest.
//   2. Delete — UAC voor elevated subset, summary daarna.
//
// "Always preview, never auto-delete" — ook hier. Caution-items (browser
// caches / Windows.old / orphaned folders) default uitgevinkt zodat user
// niet per ongeluk z'n rollback-folder weggooit.
public sealed partial class DeepCleanDialog : ContentDialog
{
    private readonly IReadOnlyList<DeepCleanItem> _items;
    private readonly DeepCleanService _service;
    private bool _deleteRunning;
    private bool _deleteCompleted;
    public DeepCleanDeleteResult? DeleteResult { get; private set; }

    public DeepCleanDialog(IReadOnlyList<DeepCleanItem> items, DeepCleanService service)
    {
        _items = items;
        _service = service;
        InitializeComponent();
        SetScanLocations();
        BuildGroupedList();
        UpdateSelectionStatus();
        UpdatePrimaryEnabled();
    }

    private void SetScanLocations()
    {
        // Bepaal aan de hand van de eerste item-categorie of dit een caches- of
        // orphans-scan was, en toon de bijbehorende lijst paden zodat user een
        // mental model heeft van wat er nét gescand is.
        var isOrphanScan = _items.Any(i => i.Category == DeepCleanCategory.OrphanedFolder);
        var locations = isOrphanScan
            ? DeepCleanService.GetOrphanedScanLocations()
            : DeepCleanService.GetCacheScanLocations();
        ScanLocationsText.Text = $"Scanned: {string.Join(" · ", locations)}";
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);

    private void BuildGroupedList()
    {
        GroupContainer.Children.Clear();

        var totalSize = _items.Sum(i => i.SizeBytes);
        HeaderText.Text = $"Found {_items.Count} cleanup item(s) — {FormatBytes(totalSize)} total";

        // Group by IsSafe eerst (caution items onderaan), dan binnen elke tier:
        // bundel items met dezelfde DisplayName (bv. "VMware" in 3 locaties)
        // onder één card zodat user ze als één geheel kan toggle. Single items
        // blijven als losse cards.
        foreach (var safetyGroup in _items.GroupBy(i => i.IsSafe).OrderByDescending(g => g.Key))
        {
            var sectionPanel = new StackPanel { Spacing = 6 };
            sectionPanel.Children.Add(new TextBlock
            {
                Text = safetyGroup.Key
                    ? $"Safe to clean ({safetyGroup.Count()})"
                    : $"Caution — review carefully ({safetyGroup.Count()})",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });

            // Bundle by shared significant tokens. Voorbeeld:
            //   "AnthropicClaude" → tokens [anthropic, claude]
            //   "Claude"          → tokens [claude]
            //   "ClaudeCodeExtension" → tokens [claude, code, extension]
            // Delen "claude" → komen in dezelfde bundle. Label = most-common
            // significante token (hier "claude"). VMware-rijtje (3x folder
            // "VMware") deelt allemaal "vmware" → bundle label "VMware".
            var bundles = BundleByTokenOverlap(safetyGroup.ToList())
                .OrderByDescending(b => b.Items.Sum(i => i.SizeBytes))
                .ToList();

            foreach (var bundle in bundles)
            {
                var bundleItems = bundle.Items.OrderBy(i => (int)i.Category).ThenBy(i => i.Path).ToList();
                if (bundleItems.Count == 1)
                {
                    sectionPanel.Children.Add(BuildItemCard(bundleItems[0]));
                }
                else
                {
                    sectionPanel.Children.Add(BuildBundleCard(bundleItems, bundle.Label));
                }
            }
            GroupContainer.Children.Add(sectionPanel);
        }
    }

    /// <summary>
    /// Groepeert items waarvan de DisplayNames één of meer significante tokens
    /// delen. Tokens komen uit camelCase-splitting + word-splitting van de
    /// folder-naam, met generieke woorden (Pro / App / for / etc.) eruit
    /// gefilterd. Union-find clustert items: elk paar dat een token deelt
    /// belandt in dezelfde bundle.
    /// </summary>
    private static List<(string Label, List<DeepCleanItem> Items)> BundleByTokenOverlap(List<DeepCleanItem> items)
    {
        var n = items.Count;
        if (n == 0) return new();

        // Per-item token-set
        var tokensPerItem = items.Select(i => TokenizeFolderName(i.DisplayName)).ToList();

        // Union-find structuur: parent[i] = i initieel.
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        // Pre-pass: items met dezelfde genormaliseerde DisplayName moeten ALTIJD
        // bundelen, ongeacht of hun token-set leeg is na length/generic filter.
        // Zonder dit zouden twee folders "User Data" (tokens [user, data] beide
        // generic → empty set) niet bundelen, omdat HashSet.Overlaps op lege
        // sets false retourneert.
        var nameGroups = items
            .Select((it, idx) => (Idx: idx, Key: Normalize(it.DisplayName)))
            .Where(x => !string.IsNullOrEmpty(x.Key))
            .GroupBy(x => x.Key)
            .Where(g => g.Count() > 1);
        foreach (var group in nameGroups)
        {
            var indices = group.Select(x => x.Idx).ToList();
            for (int k = 1; k < indices.Count; k++)
                Union(indices[0], indices[k]);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (tokensPerItem[i].Overlaps(tokensPerItem[j]))
                    Union(i, j);
            }
        }

        // Cluster op root, label per cluster = meest-voorkomende token (bij
        // gelijkspel: langste). Als geen significante tokens → val terug op
        // de eerste DisplayName als label.
        var clusters = Enumerable.Range(0, n)
            .GroupBy(idx => Find(idx))
            .Select(g =>
            {
                var clusterItems = g.Select(idx => items[idx]).ToList();
                var clusterTokens = g.SelectMany(idx => tokensPerItem[idx]).ToList();
                string label;
                if (clusterTokens.Count == 0)
                {
                    label = clusterItems[0].DisplayName;
                }
                else
                {
                    label = clusterTokens
                        .GroupBy(t => t)
                        .OrderByDescending(grp => grp.Count())
                        .ThenByDescending(grp => grp.Key.Length)
                        .First().Key;
                    // Capitalize first letter for display.
                    if (label.Length > 0)
                        label = char.ToUpperInvariant(label[0]) + label.Substring(1);
                }
                return (Label: label, Items: clusterItems);
            })
            .ToList();

        return clusters;
    }

    /// <summary>
    /// Splitst een folder-naam in significante tokens. Twee bronnen:
    ///   1. Word-splitting op separators (spaces, hyphens, underscores, etc.)
    ///   2. CamelCase-splitting (lowercase → uppercase boundary)
    /// Filter generieke tokens en tokens &lt; 4 chars zodat folder-pairs niet
    /// per ongeluk bundelen op "the" of "app".
    /// </summary>
    private static HashSet<string> TokenizeFolderName(string name)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(name)) return result;

        // Pattern: split op camelCase boundary OF op niet-alfanum separators.
        var parts = Regex.Split(name, @"(?<=[a-z0-9])(?=[A-Z])|[^a-zA-Z0-9]+");
        foreach (var p in parts)
        {
            var norm = Normalize(p);
            if (norm.Length < 4) continue;
            if (IsGenericFolderToken(norm)) continue;
            result.Add(norm);
        }
        return result;
    }

    /// <summary>
    /// Tokens die we niet als bundle-key willen gebruiken — vergelijkbaar met
    /// DeepCleanService.IsGenericToken maar specifiek voor folder-names. Als
    /// twee folders alleen op "data" of "cache" samen vallen → niet bundelen,
    /// dat is geen vendor-relatie.
    /// </summary>
    private static bool IsGenericFolderToken(string token) => token switch
    {
        "data" or "cache" or "temp" or "logs" or "config" or "settings" => true,
        "user" or "users" or "local" or "roaming" => true,
        "common" or "shared" or "public" => true,
        "files" or "folder" or "folders" => true,
        "pro" or "plus" or "lite" or "free" => true,
        "app" or "apps" or "tool" or "tools" => true,
        _ => false
    };

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Bundel-card voor 2+ items met dezelfde DisplayName (typisch orphans als
    /// "VMware" in Program Files + Program Files (x86) + ProgramData). Eén
    /// master-checkbox toggelt alle children. Sub-paths inline gelist met
    /// hun individuele groottes — user weet zo dat 'ie meerdere mappen tegelijk
    /// raakt en kan inschatten of dat ok is.
    /// </summary>
    private FrameworkElement BuildBundleCard(List<DeepCleanItem> items, string label)
    {
        var totalSize = items.Sum(i => i.SizeBytes);
        var anyElevation = items.Any(i => i.RequiresElevation);
        var first = items[0];

        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = first.IsSafe
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnSpacing = 12;

        // Master checkbox — sluit alle children-checkboxes aan via Tag = items.
        var masterCheck = new CheckBox
        {
            IsChecked = items.All(i => i.IsSelected),
            VerticalAlignment = VerticalAlignment.Top,
            MinWidth = 0,
            Tag = items
        };
        masterCheck.Checked += BundleCheck_Toggled;
        masterCheck.Unchecked += BundleCheck_Toggled;
        Grid.SetColumn(masterCheck, 0);
        grid.Children.Add(masterCheck);

        var content = new StackPanel { Spacing = 4 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(new Border
        {
            Background = first.CategoryBadgeBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = first.CategoryLabel,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"]
            }
        });
        titleRow.Children.Add(new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"{items.Count} folders",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"]
            }
        });
        content.Children.Add(titleRow);

        // Sub-line met de individuele folder-namen zodat user weet waarom dit
        // bundle is — bv. label "Claude" met sub "AnthropicClaude · Claude".
        var memberNames = string.Join(" · ", items.Select(i => i.DisplayName).Distinct());
        if (memberNames != label)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Folders: {memberNames}",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        // Per-path regel met individuele size — geeft user vertrouwen dat 'ie
        // weet welke mappen geraakt worden bij het bundle-toggle.
        foreach (var member in items)
        {
            var pathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            pathRow.Children.Add(new TextBlock
            {
                Text = "•",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
            pathRow.Children.Add(new TextBlock
            {
                Text = member.Path,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            });
            pathRow.Children.Add(new TextBlock
            {
                Text = $"({member.SizeLabel})",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
            content.Children.Add(pathRow);
        }

        if (!string.IsNullOrEmpty(first.Description))
        {
            content.Children.Add(new TextBlock
            {
                Text = first.Description,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        var sizePanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 2
        };
        sizePanel.Children.Add(new TextBlock
        {
            Text = FormatBytes(totalSize),
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextAlignment = TextAlignment.Right
        });
        if (anyElevation)
        {
            sizePanel.Children.Add(new TextBlock
            {
                Text = "admin",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                TextAlignment = TextAlignment.Right
            });
        }
        Grid.SetColumn(sizePanel, 2);
        grid.Children.Add(sizePanel);

        border.Child = grid;
        return border;
    }

    private void BundleCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is List<DeepCleanItem> bundleItems)
        {
            var newState = cb.IsChecked == true;
            foreach (var item in bundleItems)
                item.IsSelected = newState;
            UpdateSelectionStatus();
            UpdatePrimaryEnabled();
        }
    }

    private FrameworkElement BuildItemCard(DeepCleanItem item)
    {
        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = item.IsSafe
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });
        titleRow.Children.Add(new Border
        {
            Background = item.CategoryBadgeBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = item.CategoryLabel,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"]
            }
        });
        content.Children.Add(titleRow);

        content.Children.Add(new TextBlock
        {
            Text = item.Path,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        });

        if (!string.IsNullOrEmpty(item.Description))
        {
            content.Children.Add(new TextBlock
            {
                Text = item.Description,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        if (item.LastModified != null)
        {
            content.Children.Add(new TextBlock
            {
                Text = $"Last modified: {item.LastModifiedLabel}",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
        }

        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        // Size badge rechts — meest scanbare info bij scanning van een lange lijst.
        var sizePanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 2
        };
        sizePanel.Children.Add(new TextBlock
        {
            Text = item.SizeLabel,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextAlignment = TextAlignment.Right
        });
        if (item.RequiresElevation)
        {
            sizePanel.Children.Add(new TextBlock
            {
                Text = "admin",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                TextAlignment = TextAlignment.Right
            });
        }
        Grid.SetColumn(sizePanel, 2);
        grid.Children.Add(sizePanel);

        border.Child = grid;
        return border;
    }

    private void ItemCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is DeepCleanItem item)
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
        foreach (var cb in EnumerateItemCheckBoxes())
            cb.IsChecked = newState;
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
        var selectedItems = _items.Where(i => i.IsSelected).ToList();
        var elevated = selectedItems.Count(i => i.RequiresElevation || i.Category == DeepCleanCategory.RecycleBin);
        var totalBytes = selectedItems.Sum(i => i.SizeBytes);

        if (selectedItems.Count == 0)
        {
            SelectionStatusText.Text = "Nothing selected";
        }
        else
        {
            var label = $"{selectedItems.Count} selected · {FormatBytes(totalBytes)} to free";
            if (elevated > 0) label += $" · {elevated} need administrator rights";
            SelectionStatusText.Text = label;
        }
        ToggleAllButton.Content = _items.All(i => i.IsSelected) ? "Deselect all" : "Select all";
    }

    private void UpdatePrimaryEnabled()
    {
        IsPrimaryButtonEnabled = !_deleteRunning && _items.Any(i => i.IsSelected);
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_deleteCompleted) return;

        var deferral = args.GetDeferral();
        try
        {
            var selected = _items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                args.Cancel = true;
                return;
            }

            _deleteRunning = true;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressStatusText.Text = $"Deleting {selected.Count} item(s)...";
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            ToggleAllButton.IsEnabled = false;
            foreach (var cb in EnumerateItemCheckBoxes()) cb.IsEnabled = false;

            DeleteResult = await _service.DeleteAsync(selected);

            args.Cancel = true;
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            if (DeleteResult.Cancelled)
            {
                ProgressStatusText.Text = "Cancelled — UAC prompt declined.";
            }
            else if (DeleteResult.FailedCount == 0)
            {
                ProgressStatusText.Text =
                    $"Done — {DeleteResult.SuccessCount} item(s) cleaned, {FormatBytes(DeleteResult.BytesFreed)} freed.";
            }
            else
            {
                ProgressStatusText.Text =
                    $"Done — {DeleteResult.SuccessCount} cleaned, {DeleteResult.FailedCount} failed, {FormatBytes(DeleteResult.BytesFreed)} freed.";
            }

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

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int unitIdx = -1;
        do { v /= 1024; unitIdx++; } while (v >= 1024 && unitIdx < units.Length - 1);
        return v >= 100 ? $"{v:0} {units[unitIdx]}" : $"{v:0.#} {units[unitIdx]}";
    }
}
