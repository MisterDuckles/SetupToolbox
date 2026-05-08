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
    // Microsoft-bloatware filter — search-text. _visibleMicrosoftItems wordt
    // afgeleid van _microsoftItems + filter en is wat de UI bind. OEM heeft (nog)
    // geen search omdat de lijst typisch kort is.
    private List<BloatwareItem> _visibleMicrosoftItems = new();
    private string _msSearchText = string.Empty;

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
        {
            _visibleMicrosoftItems = new List<BloatwareItem>();
            ShowBloatwareEmpty();
        }
        else
        {
            ApplyMicrosoftFilter();
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

        UpdateBloatwareSelectAllButton();
        UpdateOemSelectAllButton();
        UpdateBloatwareCount();
        UpdateOemCount();
        UpdateInstalledSelection();  // sticky footer count includes MS + OEM nu
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
            UpdateBloatwareSelectAllButton();
        else
            UpdateOemSelectAllButton();
        UpdateInstalledSelection();  // sticky footer reflects total selection
    }

    // BloatwareService.DetectAllAsync returnt alleen installed items, dus de
    // _microsoftItems en _oemItems lijsten zijn intrinsiek "wat zichtbaar is".
    // Geen extra IsInstalled filter nodig. Select all werkt op de zichtbare
    // (na search-filter) subset zodat een filter-search zichtbaar effect heeft.
    private void BloatwareSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleMicrosoftItems.Count == 0) return;
        var allSelected = _visibleMicrosoftItems.All(b => b.IsSelected);
        foreach (var item in _visibleMicrosoftItems) item.IsSelected = !allSelected;
        UpdateBloatwareSelectAllButton();
        UpdateInstalledSelection();
    }

    private void BloatwareSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_uiReady) return;
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _msSearchText = sender.Text ?? string.Empty;
        ApplyMicrosoftFilter();
        UpdateBloatwareSelectAllButton();
    }

    private void ApplyMicrosoftFilter()
    {
        var q = _msSearchText.Trim();
        if (q.Length == 0)
        {
            _visibleMicrosoftItems = _microsoftItems.ToList();
        }
        else
        {
            _visibleMicrosoftItems = _microsoftItems
                .Select(b => (Item: b, Score: FuzzyMatcher.Score(q, b.DisplayName, b.Description, b.PackageName)))
                .Where(p => p.Score >= FuzzyMatcher.MinScore)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Item)
                .ToList();
        }
        BloatwareList.ItemsSource = _visibleMicrosoftItems;
    }

    private void UpdateBloatwareSelectAllButton()
    {
        var allSelected = _visibleMicrosoftItems.Count > 0 && _visibleMicrosoftItems.All(b => b.IsSelected);
        BloatwareSelectAllButton.Content = allSelected ? "Deselect all" : "Select all";
        BloatwareSelectAllButton.IsEnabled = _visibleMicrosoftItems.Count > 0;
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
        UpdateOemSelectAllButton();
        UpdateInstalledSelection();
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
        // Unified uninstall over alle drie de secties (Microsoft + OEM bloatware +
        // All apps). Selecties komen uit de volledige lists, niet de visible-subset
        // — een filter/search wissel mag de selectie niet wegvegen.
        var bloatwareSelected = _microsoftItems.Concat(_oemItems).Where(b => b.IsSelected).ToList();
        var appsSelected = _allEntries.Where(en => en.IsSelected).ToList();
        var totalCount = bloatwareSelected.Count + appsSelected.Count;
        if (totalCount == 0) return;

        // Eén confirm-dialog die de hele batch dekt. Tekst is bewust generiek —
        // de details (welke source, hoeveel UAC prompts) komen in de batch-dialogen
        // zelf naar voren via headers en source-badges.
        var hasElevated = bloatwareSelected.Count > 0 || appsSelected.Any(en => en.Source != InstalledSource.Winget);
        var content = hasElevated
            ? "This removes the selected apps. A UAC prompt will appear because some items require administrator rights. Continue?"
            : "This removes the selected apps via winget. Continue?";

        var confirm = new ContentDialog
        {
            Title = totalCount == 1
                ? $"Uninstall {(bloatwareSelected.Count == 1 ? bloatwareSelected[0].DisplayName : appsSelected[0].DisplayName)}?"
                : $"Uninstall {totalCount} apps?",
            Content = content,
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)Application.Current.Resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        // Refs verzamelen voor de gecombineerde leftover-scan na afloop. Per
        // succesvol verwijderd item één UninstalledAppRef.
        var leftoverRefs = new List<UninstalledAppRef>();

        // Stap 1: Microsoft + OEM bloatware in één Remove-AppxPackage batch
        // (zelfde mechanisme — verschil is alleen vendor-classificatie). Eén UAC
        // prompt voor de hele AppX-batch.
        if (bloatwareSelected.Count > 0)
        {
            var bloatDialog = new BloatwareUninstallDialog(bloatwareSelected, App.Bloatware) { XamlRoot = this.XamlRoot };
            await bloatDialog.ShowAsync();
            foreach (var b in bloatDialog.SuccessfulItems)
                leftoverRefs.Add(new UninstalledAppRef(
                    DisplayName: b.DisplayName,
                    Publisher: null,
                    PackageName: b.PackageName,
                    WingetId: null));
        }

        // Stap 2: All apps via MixedSourceUninstaller. Winget sequential zonder UAC,
        // Store + Web in een aparte elevated batch (tweede UAC prompt — pijnpunt
        // voor mixed selecties, maar acceptabel: bloatware en mixed zijn
        // verschillende elevation contexts en moeilijk te combineren zonder de
        // BloatwareService API te herschrijven).
        if (appsSelected.Count > 0)
        {
            var appsDialog = new AllAppsUninstallDialog(appsSelected, App.MixedUninstaller) { XamlRoot = this.XamlRoot };
            await appsDialog.ShowAsync();
            leftoverRefs.AddRange(appsDialog.SuccessfulItems.Select(BuildAppRef));
        }

        // Reload beide secties — een Store-app uninstall kan zowel de bloatware-
        // lijst als de unified lijst raken. Forceer winget cache refresh.
        await LoadBloatwareAsync();
        await LoadInstalledAsync(forceRefresh: true);

        // Stap 3: gecombineerde leftover-scan over álle succesvol verwijderde
        // items. Eén InfoBar-feedback + eventueel één cleanup-dialog voor het
        // hele zooitje, niet per sectie apart.
        if (App.Settings.ScanLeftoversAfterUninstall && leftoverRefs.Count > 0)
        {
            await RunLeftoverScanAsync(leftoverRefs);
        }
    }

    // Map een succesvol-uninstalled InstalledAppEntry naar een UninstalledAppRef
    // voor de scanner. PackageName is bij Store-source de eerste segment van de
    // PackageFullName ("Microsoft.MicrosoftSolitaireCollection" uit "Microsoft.MicrosoftSolitaireCollection_4.16.._x64..").
    // Voor Winget en Web hebben we geen PackageName — scanner valt terug op
    // DisplayName + Publisher matching.
    private static UninstalledAppRef BuildAppRef(InstalledAppEntry entry)
    {
        string? packageName = null;
        if (entry.Source == InstalledSource.Store && !string.IsNullOrEmpty(entry.Identifier))
            packageName = entry.Identifier.Split('_')[0];

        string? wingetId = entry.Source == InstalledSource.Winget ? entry.Identifier : null;

        return new UninstalledAppRef(
            DisplayName: entry.DisplayName,
            Publisher: string.IsNullOrWhiteSpace(entry.Publisher) ? null : entry.Publisher,
            PackageName: packageName,
            WingetId: wingetId);
    }

    private async Task RunLeftoverScanAsync(IReadOnlyList<UninstalledAppRef> refs)
    {
        if (refs.Count == 0) return;
        var leftovers = await App.LeftoverScanner.ScanAsync(refs);

        // Altijd feedback geven aan user dat de scan gelopen heeft — anders denkt
        // ie "het werkt niet" wanneer de scan niets vindt (= meestal het geval bij
        // winget-uninstalls die zichzelf netjes opruimen). InfoBar blijft staan
        // tot user 'm zelf sluit.
        var appsLabel = refs.Count == 1 ? refs[0].DisplayName : $"{refs.Count} apps";
        if (leftovers.Count == 0)
        {
            CleanupResultBar.Severity = InfoBarSeverity.Success;
            CleanupResultBar.Title = "Cleanup scan: no leftovers found";
            CleanupResultBar.Message = $"Scanned registry / Program Files / AppData for traces of {appsLabel}. Niets gevonden — opruiming compleet.";
            CleanupResultBar.IsOpen = true;
            return;
        }

        CleanupResultBar.Severity = InfoBarSeverity.Informational;
        CleanupResultBar.Title = $"Cleanup scan: {leftovers.Count} leftover item(s) found";
        CleanupResultBar.Message = $"Found possible traces of {appsLabel} — review and pick what to delete.";
        CleanupResultBar.IsOpen = true;

        var cleanup = new LeftoverCleanupDialog(leftovers, App.LeftoverScanner) { XamlRoot = this.XamlRoot };
        await cleanup.ShowAsync();

        // Folder-deletes raken Program Files / AppData — als user iets weggooide
        // verandert dat niet de installed-status, maar refresh maakt eventuele
        // size-displays consistent voor toekomstige scans.
        if (cleanup.DeleteResult is { SuccessCount: > 0 })
        {
            CleanupResultBar.Severity = InfoBarSeverity.Success;
            CleanupResultBar.Title = $"Cleanup done: {cleanup.DeleteResult.SuccessCount} item(s) deleted";
            CleanupResultBar.Message = cleanup.DeleteResult.FailedCount > 0
                ? $"{cleanup.DeleteResult.FailedCount} item(s) couldn't be deleted — see details in the dialog log."
                : "All selected leftovers were removed.";
            await LoadInstalledAsync(forceRefresh: false);
        }
        else if (cleanup.DeleteResult is { Cancelled: true })
        {
            CleanupResultBar.Severity = InfoBarSeverity.Warning;
            CleanupResultBar.Title = "Cleanup cancelled";
            CleanupResultBar.Message = "UAC prompt was declined — no leftovers were deleted.";
        }
    }

    private void UpdateInstalledSelection()
    {
        // Sticky footer telt over álle drie de secties (MS bloat + OEM bloat +
        // All apps) zodat user een totale "X selected" ziet ongeacht of de
        // selectie verspreid is. Bottom Uninstall-button dekt dezelfde batch
        // — één klik = één confirm + sequential bloatware-batch + apps-batch.
        var msCount = _microsoftItems.Count(b => b.IsSelected);
        var oemCount = _oemItems.Count(b => b.IsSelected);
        var appCount = _allEntries.Count(en => en.IsSelected);
        var total = msCount + oemCount + appCount;

        InstalledSelectionCountText.Text = total == 0
            ? "Nothing selected"
            : $"{total} app{(total == 1 ? "" : "s")} selected" +
              BuildSelectionBreakdown(msCount, oemCount, appCount);
        InstalledUninstallButton.IsEnabled = total > 0;
    }

    private static string BuildSelectionBreakdown(int ms, int oem, int app)
    {
        // Geef hint per sectie alleen wanneer er meer dan één sectie iets heeft —
        // anders is het overbodig (3 selected · 3 in MS = redundant).
        var sources = 0;
        if (ms > 0) sources++;
        if (oem > 0) sources++;
        if (app > 0) sources++;
        if (sources < 2) return string.Empty;

        var parts = new List<string>();
        if (ms > 0) parts.Add($"{ms} MS");
        if (oem > 0) parts.Add($"{oem} OEM");
        if (app > 0) parts.Add($"{app} apps");
        return $" ({string.Join(" + ", parts)})";
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
