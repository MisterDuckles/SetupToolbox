using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class DebloatPage : Page
{
    private enum InstalledFilter { All, Winget, Store, Web, System }

    // Bloatware-items per vendor — gevuld via BloatwareService.DetectAllAsync,
    // detectie-driven dus geen hardcoded lijst meer. Page-load triggert een verse
    // detect zodat IsSelected niet over navigaties heen lekt.
    private List<BloatwareItem> _microsoftItems = new();
    private List<BloatwareItem> _oemItems = new();

    // Unified all-installed lijst. _allEntries = volledige set, _visibleEntries =
    // na search/filter. ItemsRepeater bindt aan _visibleEntries.
    private List<InstalledAppEntry> _allEntries = new();
    private List<InstalledAppEntry> _visibleEntries = new();
    private InstalledFilter _filter = InstalledFilter.All;
    private string _searchText = string.Empty;
    // Default false — Windows system AppX components (AAD.BrokerPlugin, etc.) zijn
    // niet veilig om te uninstallen en zouden anders onnodig veel ruis geven in
    // de unified lijst. User kan ze tonen via de checkbox in de toolbar.
    private bool _showSystemComponents = false;
    private bool _uiReady;

    public DebloatPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Bloatware-items worden detection-driven gevuld in LoadBloatwareAsync.
        // Lijsten initialiseren we leeg — verse detect per page-load.
        _microsoftItems = new List<BloatwareItem>();
        _oemItems = new List<BloatwareItem>();

        _uiReady = true;
        await LoadAsync();
    }

    private async Task LoadAsync(bool forceRefresh = false)
    {
        // Drie bronnen parallel: bloatware (Microsoft + OEM in 1 call) en de unified
        // all-installed lijst (catalog winget list + AppX + registry). Sequentieel
        // zou ~5-7s pagina-init zijn; parallel kapt dat naar de duur van de langste.
        var bloatwareTask = LoadBloatwareAsync();
        var installedTask = LoadInstalledAsync(forceRefresh);
        await Task.WhenAll(bloatwareTask, installedTask);

        // Dedupe: bloatware items zijn ook AppX packages dus zouden zonder filter ook
        // in "All installed apps" verschijnen met Store badge. Skip ze daar — de
        // bloatware-secties zijn de specialized view, unified is "alles wat niet al
        // hierboven categorisch behandeld wordt".
        DedupeBloatwareFromInstalled();
    }

    private void DedupeBloatwareFromInstalled()
    {
        // Drie sets om tegen te checken zodat we matches missen via geen enkel veld:
        //   1. PackageFullName — exact (versie-specifiek)
        //   2. AppX Name — uit PackageFullName geëxtracteerd, format "Name_Version_..."
        //      Vangt het geval waar bloatware en all-apps detect verschillende
        //      versies van hetzelfde package vinden.
        //   3. DisplayName — de friendly naam, case-insensitive. Werkt cross-source
        //      (Store + Web zelfde DisplayName = duplicate).
        var bloatwareFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bloatwareAppxNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bloatwareDisplayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _microsoftItems.Concat(_oemItems))
        {
            bloatwareAppxNames.Add(item.PackageName);
            bloatwareDisplayNames.Add(item.DisplayName);
            foreach (var fn in item.InstalledPackageFullNames)
            {
                bloatwareFullNames.Add(fn);
                // FullName format = Name_Version_Architecture_ResourceId_Publisher
                var appxName = fn.Split('_')[0];
                if (!string.IsNullOrEmpty(appxName)) bloatwareAppxNames.Add(appxName);
            }
        }

        if (bloatwareAppxNames.Count == 0 && bloatwareDisplayNames.Count == 0) return;

        var before = _allEntries.Count;
        _allEntries = _allEntries
            .Where(e => !IsBloatwareDuplicate(e, bloatwareFullNames, bloatwareAppxNames, bloatwareDisplayNames))
            .ToList();

        if (_allEntries.Count != before)
        {
            ApplyFilterAndSearch();
            UpdateInstalledSelection();
            UpdateInstalledSelectAllButton();
            UpdateInstalledCount();
        }
    }

    private static bool IsBloatwareDuplicate(
        InstalledAppEntry entry,
        HashSet<string> fullNames,
        HashSet<string> appxNames,
        HashSet<string> displayNames)
    {
        // Store entries: probeer alle drie de match-strategies (FullName, Name uit
        // FullName, DisplayName).
        if (entry.Source == InstalledSource.Store)
        {
            if (fullNames.Contains(entry.Identifier)) return true;
            var appxName = entry.Identifier.Split('_')[0];
            if (!string.IsNullOrEmpty(appxName) && appxNames.Contains(appxName)) return true;
        }
        // Voor alle sources: DisplayName comparison vangt cross-source duplicaten op
        // (bv. een Web-entry voor "Notepad" terwijl Microsoft-sectie ook Notepad heeft).
        return displayNames.Contains(entry.DisplayName);
    }

    // ── Microsoft + OEM bloatware sectie ──────────────────────────
    private async Task LoadBloatwareAsync()
    {
        ShowBloatwareLoading();

        // Detection-driven: één Get-AppxPackage call vindt álle Microsoft + OEM
        // AppX die op het systeem staan. Items worden runtime geconstrueerd; de
        // BloatwareItem.CuratedMetadata dict verrijkt bekende packages met een
        // friendly display + description, onbekende krijgen de raw package-name.
        var detected = await App.Bloatware.DetectAllAsync();
        _microsoftItems = detected.Where(b => b.Vendor == BloatwareVendor.Microsoft).ToList();
        _oemItems = detected.Where(b => b.Vendor == BloatwareVendor.Oem).ToList();

        if (_microsoftItems.Count == 0)
            ShowBloatwareEmpty();
        else
        {
            BloatwareList.ItemsSource = _microsoftItems;
            ShowBloatwareList();
        }

        if (_oemItems.Count == 0)
        {
            OemSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            OemList.ItemsSource = _oemItems;
            OemSection.Visibility = Visibility.Visible;
        }

        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
        UpdateOemSelection();
        UpdateOemSelectAllButton();
        UpdateBloatwareCount();
        UpdateOemCount();
    }

    // ── Unified "All installed apps" sectie ───────────────────────
    private async Task LoadInstalledAsync(bool forceRefresh)
    {
        ShowInstalledLoading();

        // Safety net: wanneer detectie throwt (bv. een corrupte winget output, of een
        // service die 'n exception gooit op iets dat we niet voorzien) moet de loading
        // ring NIET eeuwig blijven spinnen. Try/catch + finally zorgt dat we altijd
        // door naar de empty/list state, ook al is de detectie mislukt.
        try
        {
            if (forceRefresh)
                await App.Winget.GetInstalledAppIdsAsync(forceRefresh: true);

            _allEntries = await App.InstalledApps.DetectAllAsync();
        }
        catch
        {
            _allEntries = new List<InstalledAppEntry>();
        }
        finally
        {
            // Loading ring uitzetten voor we de lijst tonen — anders overlapt 'ie items
            // (Grid z-order: ring + list zitten in dezelfde container) en bij korte
            // lijsten zie je 'm onder de items doorlopen alsof er nog wat geladen wordt.
            InstalledLoadingRing.Visibility = Visibility.Collapsed;
        }

        ApplyFilterAndSearch();

        UpdateInstalledSelection();
        UpdateInstalledSelectAllButton();
        UpdateInstalledCount();
    }

    private void ApplyFilterAndSearch()
    {
        IEnumerable<InstalledAppEntry> filtered = _allEntries;

        // "System"-filter is een aparte view: laat álleen system components zien,
        // negeer de show-system checkbox (anders zou je 'm óók nog moeten aanvinken).
        // Voor de andere filters geldt de checkbox als gewone hide-filter.
        if (_filter == InstalledFilter.System)
        {
            filtered = filtered.Where(e => e.IsSystemComponent);
        }
        else
        {
            if (!_showSystemComponents)
                filtered = filtered.Where(e => !e.IsSystemComponent);

            filtered = _filter switch
            {
                InstalledFilter.Winget => filtered.Where(e => e.Source == InstalledSource.Winget),
                InstalledFilter.Store => filtered.Where(e => e.Source == InstalledSource.Store),
                InstalledFilter.Web => filtered.Where(e => e.Source == InstalledSource.Web),
                _ => filtered
            };
        }

        // Fuzzy search op DisplayName + Publisher (Identifier voor Winget = winget ID
        // wat informatief is, voor anderen is het minder leesbaar dus alleen voor winget).
        var q = _searchText.Trim();
        if (q.Length > 0)
        {
            filtered = filtered
                .Select(e => (Entry: e, Score: FuzzyMatcher.Score(q, e.DisplayName, e.Publisher,
                    e.Source == InstalledSource.Winget ? e.Identifier : null)))
                .Where(p => p.Score >= FuzzyMatcher.MinScore)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Entry);
        }
        else
        {
            // Geen search-query → sort by source (Winget → Store → Web), dan alfabetisch.
            // "Echte" winget-installs (Source=winget) komen daarmee bovenaan zodat de
            // user de 'managed' apps snel ziet vóór de Store/Web bagger. Voor de System-
            // filter is groeperen op source niet zinvol (alles is Store), dus daar
            // alleen alfabetisch.
            filtered = _filter == InstalledFilter.System
                ? filtered.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered
                    .OrderBy(e => SourceSortKey(e.Source))
                    .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        _visibleEntries = filtered.ToList();

        if (_visibleEntries.Count == 0 && _allEntries.Count > 0)
        {
            InstalledList.ItemsSource = null;
            InstalledList.Visibility = Visibility.Collapsed;
            InstalledEmptyText.Text = q.Length > 0
                ? $"No installed apps matching \"{q}\""
                : _filter == InstalledFilter.System
                    ? "Geen Windows system components gedetecteerd."
                    : $"No apps in source filter \"{_filter}\"";
            InstalledEmptyText.Visibility = Visibility.Visible;
        }
        else if (_allEntries.Count == 0)
        {
            InstalledList.ItemsSource = null;
            InstalledList.Visibility = Visibility.Collapsed;
            InstalledEmptyText.Text = "Geen geïnstalleerde apps gedetecteerd.";
            InstalledEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            InstalledEmptyText.Visibility = Visibility.Collapsed;
            InstalledList.ItemsSource = _visibleEntries;
            InstalledList.Visibility = Visibility.Visible;
        }
    }

    // ── Visibility helpers per sectie ─────────────────────────────
    private void ShowBloatwareLoading()
    {
        BloatwareLoadingRing.Visibility = Visibility.Visible;
        BloatwareEmptyText.Visibility = Visibility.Collapsed;
        BloatwareList.Visibility = Visibility.Collapsed;
    }

    private void ShowBloatwareEmpty()
    {
        BloatwareLoadingRing.Visibility = Visibility.Collapsed;
        BloatwareEmptyText.Visibility = Visibility.Visible;
        BloatwareList.Visibility = Visibility.Collapsed;
    }

    private void ShowBloatwareList()
    {
        BloatwareLoadingRing.Visibility = Visibility.Collapsed;
        BloatwareEmptyText.Visibility = Visibility.Collapsed;
        BloatwareList.Visibility = Visibility.Visible;
    }

    private void ShowInstalledLoading()
    {
        InstalledLoadingRing.Visibility = Visibility.Visible;
        InstalledEmptyText.Visibility = Visibility.Collapsed;
        InstalledList.Visibility = Visibility.Collapsed;
    }

    // ── Refresh ───────────────────────────────────────────────────
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var b in _microsoftItems) b.IsSelected = false;
        foreach (var b in _oemItems) b.IsSelected = false;
        foreach (var entry in _allEntries) entry.IsSelected = false;
        await LoadAsync(forceRefresh: true);
    }

    // ── Microsoft bloatware handlers ──────────────────────────────
    private void BloatwareCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Shared tussen Microsoft + OEM card-templates. Vendor op het item bepaalt
        // welke selection-update we triggeren.
        if (sender is not FrameworkElement fe) return;
        var item = fe.DataContext as BloatwareItem ?? fe.Tag as BloatwareItem;
        if (item == null) return;

        item.IsSelected = !item.IsSelected;
        if (item.Vendor == BloatwareVendor.Microsoft)
        {
            UpdateBloatwareSelection();
            UpdateBloatwareSelectAllButton();
        }
        else
        {
            UpdateOemSelection();
            UpdateOemSelectAllButton();
        }
    }

    // BloatwareService.DetectAllAsync returnt alleen installed items, dus de
    // _microsoftItems en _oemItems lijsten zijn intrinsiek "wat zichtbaar is".
    // Geen extra IsInstalled filter nodig.
    private void BloatwareSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_microsoftItems.Count == 0) return;
        var allSelected = _microsoftItems.All(b => b.IsSelected);
        foreach (var item in _microsoftItems) item.IsSelected = !allSelected;
        UpdateBloatwareSelection();
        UpdateBloatwareSelectAllButton();
    }

    private async void BloatwareRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _microsoftItems.Where(b => b.IsSelected).ToList();
        await ConfirmAndRemoveBloatwareAsync(selected, "Microsoft");
    }

    private void UpdateBloatwareSelection()
    {
        var count = _microsoftItems.Count(b => b.IsSelected);
        BloatwareRemoveButton.IsEnabled = count > 0;
    }

    private void UpdateBloatwareSelectAllButton()
    {
        var allSelected = _microsoftItems.Count > 0 && _microsoftItems.All(b => b.IsSelected);
        BloatwareSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        BloatwareSelectAllButton.IsEnabled = _microsoftItems.Count > 0;
    }

    private void UpdateBloatwareCount()
    {
        var count = _microsoftItems.Count;
        BloatwareCountText.Text = count == 0 ? string.Empty : $"({count})";
    }

    // ── OEM bloatware handlers ────────────────────────────────────
    private void OemSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_oemItems.Count == 0) return;
        var allSelected = _oemItems.All(b => b.IsSelected);
        foreach (var item in _oemItems) item.IsSelected = !allSelected;
        UpdateOemSelection();
        UpdateOemSelectAllButton();
    }

    private async void OemRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _oemItems.Where(b => b.IsSelected).ToList();
        await ConfirmAndRemoveBloatwareAsync(selected, "OEM");
    }

    private void UpdateOemSelection()
    {
        var count = _oemItems.Count(b => b.IsSelected);
        OemRemoveButton.IsEnabled = count > 0;
    }

    private void UpdateOemSelectAllButton()
    {
        var allSelected = _oemItems.Count > 0 && _oemItems.All(b => b.IsSelected);
        OemSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        OemSelectAllButton.IsEnabled = _oemItems.Count > 0;
    }

    private void UpdateOemCount()
    {
        var count = _oemItems.Count;
        OemCountText.Text = count == 0 ? string.Empty : $"({count})";
    }

    private async Task ConfirmAndRemoveBloatwareAsync(List<BloatwareItem> selected, string vendorLabel)
    {
        if (selected.Count == 0) return;

        var confirm = new ContentDialog
        {
            Title = selected.Count == 1
                ? $"Remove {selected[0].DisplayName}?"
                : $"Remove {selected.Count} {vendorLabel} apps?",
            Content = "This permanently removes the selected apps via Remove-AppxPackage. A UAC prompt will appear because this requires administrator rights. Some apps cannot be reinstalled easily — only continue if you're sure.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var dialog = new BloatwareUninstallDialog(selected, App.Bloatware) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        foreach (var item in selected) item.IsSelected = false;
        await LoadBloatwareAsync();
        await LoadInstalledAsync(forceRefresh: true);  // bloatware-uninstall raakt ook unified lijst
    }

    // ── Unified all-installed handlers ────────────────────────────
    private void InstalledAppCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var entry = fe.DataContext as InstalledAppEntry ?? fe.Tag as InstalledAppEntry;
        if (entry == null) return;

        entry.IsSelected = !entry.IsSelected;
        UpdateInstalledSelection();
        UpdateInstalledSelectAllButton();
    }

    private void InstalledSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleEntries.Count == 0) return;
        var allSelected = _visibleEntries.All(en => en.IsSelected);
        foreach (var entry in _visibleEntries) entry.IsSelected = !allSelected;
        UpdateInstalledSelection();
        UpdateInstalledSelectAllButton();
    }

    private void InstalledSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_uiReady) return;
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _searchText = sender.Text ?? string.Empty;
        ApplyFilterAndSearch();
        UpdateInstalledSelectAllButton();
    }

    private void InstalledFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        if (InstalledFilterBox.SelectedIndex < 0) return;
        _filter = (InstalledFilter)InstalledFilterBox.SelectedIndex;
        ApplyFilterAndSearch();
        UpdateInstalledSelectAllButton();
        // System-filter heeft afwijkende count (alleen system items vs alleen
        // niet-system items in andere filters) — moet hier ook refreshen.
        UpdateInstalledCount();
    }

    private void ShowSystemCheckbox_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        _showSystemComponents = ShowSystemCheckbox.IsChecked == true;
        ApplyFilterAndSearch();
        UpdateInstalledSelectAllButton();
        UpdateInstalledCount();
    }

    private async void InstalledUninstallButton_Click(object sender, RoutedEventArgs e)
    {
        // Werkt op _allEntries i.p.v. _visibleEntries: een filter-wissel mag niet
        // de selectie wegvegen, en user verwacht dat "uninstall selected" doet wat
        // ze hebben aangevinkt ongeacht huidige zichtbaarheid.
        var selected = _allEntries.Where(en => en.IsSelected).ToList();
        if (selected.Count == 0) return;

        var hasElevated = selected.Any(en => en.Source != InstalledSource.Winget);
        var content = hasElevated
            ? "This removes the selected apps. A UAC prompt will appear because some items require administrator rights (Store / registry installs). Continue?"
            : "This removes the selected catalog apps via winget. Continue?";

        var confirm = new ContentDialog
        {
            Title = selected.Count == 1
                ? $"Uninstall {selected[0].DisplayName}?"
                : $"Uninstall {selected.Count} apps?",
            Content = content,
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var dialog = new AllAppsUninstallDialog(selected, App.MixedUninstaller) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        // Selectie weggooien is impliciet — de items komen uit _allEntries dat we
        // zo herladen. De nieuwe entries zijn fresh objects zonder IsSelected.
        await LoadInstalledAsync(forceRefresh: true);
        // Bloatware-counts ook refreshen — een Store-app uninstall raakt zowel de
        // unified lijst als de bloatware-secties als de app daar curated in stond.
        await LoadBloatwareAsync();
    }

    private void UpdateInstalledSelection()
    {
        var count = _allEntries.Count(en => en.IsSelected);
        InstalledSelectionCountText.Text = count == 0 ? string.Empty : $"{count} selected";
        InstalledUninstallButton.IsEnabled = count > 0;
    }

    private void UpdateInstalledSelectAllButton()
    {
        var allSelected = _visibleEntries.Count > 0 && _visibleEntries.All(en => en.IsSelected);
        InstalledSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        InstalledSelectAllButton.IsEnabled = _visibleEntries.Count > 0;
    }

    private void UpdateInstalledCount()
    {
        // Count reflecteert wat de user kan inspecteren met huidige show-system-toggle
        // — totaal-verborgen system components meetellen zou misleidend zijn ("(380)"
        // boven een lijst met 200 zichtbare items). System-filter toont alleen system,
        // dus dan is die count = aantal system components.
        int count = _filter == InstalledFilter.System
            ? _allEntries.Count(e => e.IsSystemComponent)
            : _showSystemComponents
                ? _allEntries.Count
                : _allEntries.Count(e => !e.IsSystemComponent);
        InstalledCountText.Text = count == 0 ? string.Empty : $"({count})";
    }

    // Sort-key voor source-groeperen: Winget eerst (echte managed apps), dan Store
    // (Microsoft Store / AppX), dan Web (vendor MSI/EXE). Volgorde matcht de visuele
    // hierarchie: hoe meer "officieel managed", hoe hoger.
    private static int SourceSortKey(InstalledSource s) => s switch
    {
        InstalledSource.Winget => 0,
        InstalledSource.Store => 1,
        InstalledSource.Web => 2,
        _ => 3
    };

    // ── Shared ────────────────────────────────────────────────────
    private void AppCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
            g.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
    }

    private void AppCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid g)
            g.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    }

    private void AppIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img)
            img.Visibility = Visibility.Collapsed;
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);
}
