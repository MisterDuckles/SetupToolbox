using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using SetupToolbox.Helpers;
using SetupToolbox.Models;
using SetupToolbox.Services;

namespace SetupToolbox.Dialogs;

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
    // Filter-query uit de AutoSuggestBox bovenaan. Lege string = geen filter.
    // Matcht case-insensitive op bundle-label + item DisplayName + Path.
    private string _filterQuery = string.Empty;
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

    /// <summary>
    /// Filter-textbox handler. Alleen reageren op user-input (typing), niet
    /// op programmatic SuggestionChosen — voorkomt onnodige rebuilds. Bij
    /// elke change rebuilden we de lijst met de nieuwe query als filter.
    /// </summary>
    private void FilterBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _filterQuery = (sender.Text ?? string.Empty).Trim();
        BuildGroupedList();
        UpdateSelectionStatus();
    }

    private void BuildGroupedList()
    {
        GroupContainer.Children.Clear();

        var totalSize = _items.Sum(i => i.SizeBytes);
        HeaderText.Text = string.IsNullOrEmpty(_filterQuery)
            ? $"Found {_items.Count} cleanup item(s) — {App.Loc.FormatBytes(totalSize)} total"
            : $"Found {_items.Count} item(s) — filter active";

        // Apply filter eerst: query matcht case-insensitive op DisplayName,
        // Path én category-label. Daarna pas bundle-by-token zodat een filter
        // op "brave" precies de Brave-cluster terugbrengt. Empty query = alles.
        var filteredItems = string.IsNullOrEmpty(_filterQuery)
            ? _items
            : (IReadOnlyList<DeepCleanItem>)_items.Where(MatchesFilter).ToList();

        if (filteredItems.Count == 0)
        {
            var noMatch = new TextBlock
            {
                Text = $"Geen items matchen \"{_filterQuery}\".",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["BodyTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };
            GroupContainer.Children.Add(noMatch);
            return;
        }

        // Group by IsSafe eerst (caution items onderaan), dan binnen elke tier:
        // bundel items met dezelfde DisplayName (bv. "VMware" in 3 locaties)
        // onder één card zodat user ze als één geheel kan toggle. Single items
        // blijven als losse cards.
        foreach (var safetyGroup in filteredItems.GroupBy(i => i.IsSafe).OrderByDescending(g => g.Key))
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
    /// Filter-match voor de search box: query matcht op DisplayName, Path of
    /// CategoryLabel van een item. Case-insensitive substring + extra normalized
    /// vergelijking (alfanum-only) zodat een query "bravesoftware" ook matcht
    /// met DisplayName "BraveSoftware · Promo" (`Â·` weggestript via normalize).
    /// </summary>
    private bool MatchesFilter(DeepCleanItem item)
    {
        if (string.IsNullOrEmpty(_filterQuery)) return true;
        var q = _filterQuery;
        if (item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.Path.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.CategoryLabel.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;

        // Genormaliseerde fallback voor namen met scheidingstekens (· / _ / -)
        // die de raw substring-match missen.
        var normQuery = Normalize(q);
        if (normQuery.Length == 0) return false;
        if (Normalize(item.DisplayName).Contains(normQuery)) return true;
        if (Normalize(item.Path).Contains(normQuery)) return true;
        return false;
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
        // Eén badge per unieke category in de bundle. Voor een mixed bundle
        // (bv. folder + HKCU + registry van zelfde app) ziet user nu alle 3
        // de subcategorie-labels naast elkaar i.p.v. alleen de category van
        // het eerste item. Count per category in de badge zodat user weet of
        // het bv. 1 folder of 3 folders zijn.
        foreach (var categoryGroup in items
            .GroupBy(i => i.Category)
            .OrderBy(g => (int)g.Key))
        {
            var sample = categoryGroup.First();
            var count = categoryGroup.Count();
            var badgeText = count > 1
                ? $"{sample.CategoryLabel} ×{count}"
                : sample.CategoryLabel;
            titleRow.Children.Add(new Border
            {
                Background = sample.CategoryBadgeBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = badgeText,
                    Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"]
                }
            });
        }
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

        // Per-path lijst achter een ingeklapte Expander: default zien we geen
        // paden meer (cleaner card), user kan klikken om alle locaties te
        // bekijken. Path-rows in een Grid met width=* zodat lange MUIcache /
        // App Paths entries netjes met ellipsis afgebroken worden i.p.v. door
        // de card-rand heen te steken.
        var pathExpander = new Expander
        {
            Header = $"Show {items.Count} location{(items.Count == 1 ? "" : "s")}",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = false,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var pathStack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var member in items)
        {
            var pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathGrid.ColumnSpacing = 6;

            var bullet = new TextBlock
            {
                Text = "•",
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            };
            Grid.SetColumn(bullet, 0);
            pathGrid.Children.Add(bullet);

            // Path als HyperlinkButton met TextWrapping=Wrap — volledige path
            // altijd zichtbaar (geen truncation meer), klik opent het pad in
            // Explorer (folders/shortcuts) of Regedit (registry-paden).
            var pathBlock = new TextBlock
            {
                Text = member.Path,
                Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };
            var pathBtn = new HyperlinkButton
            {
                Content = pathBlock,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = member
            };
            pathBtn.Click += PathLink_Click;
            ToolTipService.SetToolTip(pathBtn, "Click to open in Explorer / Regedit");
            Grid.SetColumn(pathBtn, 1);
            pathGrid.Children.Add(pathBtn);

            // Size alleen tonen als er ruimte is (folders hebben size, registry-
            // entries niet — voor die laatste blijft de label leeg).
            if (member.SizeBytes > 0)
            {
                var sizeBlock = new TextBlock
                {
                    Text = $"({member.SizeLabel})",
                    Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                };
                Grid.SetColumn(sizeBlock, 2);
                pathGrid.Children.Add(sizeBlock);
            }
            pathStack.Children.Add(pathGrid);
        }
        pathExpander.Content = pathStack;
        content.Children.Add(pathExpander);

        // Geen description meer hier — die is generiek voor de category en
        // wordt eenmalig bovenaan de dialog getoond i.p.v. per card herhaald.

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
            Text = App.Loc.FormatBytes(totalSize),
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

    /// <summary>
    /// Click op een path-link: open het pad in Explorer (folders/shortcuts)
    /// of Regedit (registry-paden). Voor MUIcache-items wijst Path naar de
    /// pseudo-key + value-name; we openen de echte MUIcache key in regedit.
    /// </summary>
    private void PathLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton btn || btn.Tag is not DeepCleanItem item) return;

        try
        {
            switch (item.Category)
            {
                case DeepCleanCategory.OrphanedFolder:
                case DeepCleanCategory.UserTemp:
                case DeepCleanCategory.SystemTemp:
                case DeepCleanCategory.UpdateCache:
                case DeepCleanCategory.Prefetch:
                case DeepCleanCategory.WindowsOld:
                case DeepCleanCategory.BrowserCache:
                    OpenInExplorer(item.Path);
                    break;
                case DeepCleanCategory.OrphanedShortcut:
                    // Selecteer het .lnk file in Explorer zodat user de shortcut zelf ziet.
                    OpenInExplorer(item.Path, selectFile: true);
                    break;
                case DeepCleanCategory.OrphanedRegistry:
                case DeepCleanCategory.OrphanedAppPath:
                case DeepCleanCategory.OrphanedClassHandler:
                    OpenInRegedit(item.Path);
                    break;
                case DeepCleanCategory.OrphanedMuiCache:
                    // MUIcache: Path bevat "<key> → <value-name>" voor unique identifier.
                    // Open de key zelf (zonder de → suffix) in regedit.
                    var arrowIdx = item.Path.IndexOf(" → ", StringComparison.Ordinal);
                    var keyOnly = arrowIdx > 0 ? item.Path.Substring(0, arrowIdx) : item.Path;
                    OpenInRegedit(keyOnly);
                    break;
                case DeepCleanCategory.RecycleBin:
                    OpenInExplorer("shell:RecycleBinFolder");
                    break;
                case DeepCleanCategory.OrphanedScheduledTask:
                    // Task Scheduler MMC console — kan niet rechtstreeks naar
                    // een specifieke task navigeren via command line, dus
                    // alleen het console openen.
                    Process.Start(new ProcessStartInfo { FileName = "taskschd.msc", UseShellExecute = true });
                    break;
                case DeepCleanCategory.OrphanedFirewallRule:
                    // Windows Defender Firewall met geavanceerde beveiliging
                    Process.Start(new ProcessStartInfo { FileName = "wf.msc", UseShellExecute = true });
                    break;
                case DeepCleanCategory.OrphanedService:
                    // services.msc — Services console. Kan niet rechtstreeks
                    // naar een specifieke service navigeren, alleen het console
                    // openen zodat user manueel kan inspecteren.
                    Process.Start(new ProcessStartInfo { FileName = "services.msc", UseShellExecute = true });
                    break;
                case DeepCleanCategory.OrphanedHkcuVendor:
                    // HKCU\Software\<Vendor>\<App> — open in regedit op die key.
                    OpenInRegedit(item.Path);
                    break;
            }
        }
        catch
        {
            // Klik faalt stil — geen blocking error voor de user.
        }
    }

    private static void OpenInExplorer(string path, bool selectFile = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        psi.Arguments = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(psi);
    }

    /// <summary>
    /// Open Regedit op de gegeven registry-key. Truc: schrijf het pad naar
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Applets\Regedit\LastKey
    /// en start regedit.exe — die opent automatisch op de LastKey. Path-format
    /// dat regedit verwacht is `Computer\HKEY_<HIVE>\...`.
    /// </summary>
    private static void OpenInRegedit(string registryPath)
    {
        // Convert "HKLM\..." → "Computer\HKEY_LOCAL_MACHINE\..."
        // Convert "HKCU\..." → "Computer\HKEY_CURRENT_USER\..."
        string regPath = registryPath;
        if (regPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            regPath = "Computer\\HKEY_LOCAL_MACHINE\\" + regPath.Substring(5);
        else if (regPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            regPath = "Computer\\HKEY_CURRENT_USER\\" + regPath.Substring(5);
        else
            regPath = "Computer\\" + regPath;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            key?.SetValue("LastKey", regPath);
        }
        catch { /* if we can't set LastKey, regedit just opens at last position */ }

        Process.Start(new ProcessStartInfo { FileName = "regedit.exe", UseShellExecute = true });
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

        // Path als HyperlinkButton met TextWrapping=Wrap zodat user altijd het
        // volledige pad ziet en kan klikken om in Explorer/Regedit te openen.
        var pathTextBlock = new TextBlock
        {
            Text = item.Path,
            Style = (Microsoft.UI.Xaml.Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var pathButton = new HyperlinkButton
        {
            Content = pathTextBlock,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = item
        };
        pathButton.Click += PathLink_Click;
        ToolTipService.SetToolTip(pathButton, "Click to open in Explorer / Regedit");
        content.Children.Add(pathButton);

        // Geen per-item description meer — de generieke uitleg per category
        // staat eenmalig bovenaan de dialog.

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
            var label = $"{selectedItems.Count} selected · {App.Loc.FormatBytes(totalBytes)} to free";
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

            // Optionele Windows System Restore Point vóór de delete. Setting
            // wordt voorafgaand aan dialog-open geconfigureerd (zie
            // DeepCleanPage first-run flow). Bij rate-limit (<24u) wordt
            // 't checkpoint silent overgeslagen in de elevated PS-batch.
            string? rpDescription = null;
            if (App.Settings.RestorePointBeforeDeepClean)
            {
                rpDescription = $"SetupToolbox Deep Clean ({selected.Count} items)";
            }

            DeleteResult = await _service.DeleteAsync(selected, rpDescription);

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
                    $"Done — {DeleteResult.SuccessCount} item(s) cleaned, {App.Loc.FormatBytes(DeleteResult.BytesFreed)} freed.";
            }
            else
            {
                ProgressStatusText.Text =
                    $"Done — {DeleteResult.SuccessCount} cleaned, {DeleteResult.FailedCount} failed, {App.Loc.FormatBytes(DeleteResult.BytesFreed)} freed.";
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

}
