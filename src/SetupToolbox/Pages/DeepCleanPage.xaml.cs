using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Dialogs;
using SetupToolbox.Models;

namespace SetupToolbox.Pages;

// "Debloat → Deep clean" sub-page. Twee scan-flows:
//   1. Windows caches — predefined paden (Temp / Update cache / Recycle / Prefetch /
//      Windows.old / browser caches). Snelle scan, hoge ROI.
//   2. Leftovers — orphan folders (Program Files / AppData zonder matching
//      installed app) PLUS orphan registry uninstall entries (keys waarvan
//      alle pad-velden naar dode bestanden wijzen). Beide bronnen parallel,
//      gecombineerd in één DeepCleanDialog. Bundle-by-name in de dialog
//      groept registry+folder van dezelfde app automatisch (bv. registry
//      "Claude" + folder "AnthropicClaude" delen token "claude").
//
// Beide eindigen in een DeepCleanDialog met preview + delete fases. Resultaat-
// feedback komt op de eigen InfoBar bovenaan deze pagina.
public sealed partial class DeepCleanPage : Page
{
    private enum ScanKind { Caches, Leftovers }

    public DeepCleanPage()
    {
        InitializeComponent();
    }

    private async void ScanCachesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(ScanKind.Caches);
    }

    private async void ScanOrphanedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(ScanKind.Leftovers);
    }

    // isAutoRefresh = true wanneer deze scan automatisch wordt getriggerd na
    // een delete-batch (verificatie-pass). UI-tekst is in dat geval anders zodat
    // user weet dat het een verify-run is i.p.v. een nieuwe scan.
    private async Task RunScanAsync(ScanKind kind, bool isAutoRefresh = false)
    {
        // Disable beide knoppen tijdens scan zodat user niet 2x klikt en we
        // geen overlappende scans krijgen. Spinner zichtbaar onder de knoppen
        // zodat user weet dat er iets loopt.
        ScanCachesButton.IsEnabled = false;
        ScanOrphanedButton.IsEnabled = false;
        DeepCleanScanRing.Visibility = Visibility.Visible;
        EmptyStatePanel.Visibility = Visibility.Collapsed;

        List<DeepCleanItem> items;
        try
        {
            if (kind == ScanKind.Caches)
            {
                items = await App.DeepClean.ScanWindowsCachesAsync();
            }
            else
            {
                // Alle leftover-bronnen parallel:
                //   - folders (Program Files / AppData zonder matching app)
                //   - uninstall registry keys met dode paden
                //   - App Paths registry met dode exe-paden
                //   - MUIcache values (recently-used programs met dode paden)
                //   - Class handlers (\Software\Classes\Applications) met dode exes
                //   - Start Menu / Desktop shortcuts met dode targets
                //   - Scheduled tasks + Firewall rules met dode program-paden
                //   - Orphan services (Stopped + Manual/Disabled + dode ImagePath)
                //   - HKCU\Software vendor-keys met enkel dode pad-values
                // Folder-scan is de langzaamste; rest is snel. Bundle-by-name in
                // de dialog groept gerelateerde items van zelfde app onder één
                // card (registry + folder + MUIcache van "Claude" → 1 bundle).
                var folderTask = App.DeepClean.ScanOrphanedFoldersAsync();
                var registryTask = App.DeepClean.ScanOrphanedRegistryAsync();
                var appPathsTask = App.DeepClean.ScanOrphanedAppPathsAsync();
                var muiCacheTask = App.DeepClean.ScanOrphanedMuiCacheAsync();
                var classHandlersTask = App.DeepClean.ScanOrphanedClassHandlersAsync();
                var shortcutsTask = App.DeepClean.ScanOrphanedShortcutsAsync();
                var tasksTask = App.DeepClean.ScanOrphanedScheduledTasksAsync();
                var firewallTask = App.DeepClean.ScanOrphanedFirewallRulesAsync();
                var servicesTask = App.DeepClean.ScanOrphanedServicesAsync();
                var hkcuVendorTask = App.DeepClean.ScanOrphanedHkcuVendorAsync();
                await Task.WhenAll(folderTask, registryTask, appPathsTask, muiCacheTask, classHandlersTask, shortcutsTask, tasksTask, firewallTask, servicesTask, hkcuVendorTask);
                items = (await folderTask)
                    .Concat(await registryTask)
                    .Concat(await appPathsTask)
                    .Concat(await muiCacheTask)
                    .Concat(await classHandlersTask)
                    .Concat(await shortcutsTask)
                    .Concat(await tasksTask)
                    .Concat(await firewallTask)
                    .Concat(await servicesTask)
                    .Concat(await hkcuVendorTask)
                    .ToList();
            }
        }
        catch (Exception)
        {
            items = new List<DeepCleanItem>();
        }
        finally
        {
            DeepCleanScanRing.Visibility = Visibility.Collapsed;
            ScanCachesButton.IsEnabled = true;
            ScanOrphanedButton.IsEnabled = true;
        }

        var label = App.Loc.S(kind == ScanKind.Caches ? "deepclean.label.caches" : "deepclean.label.leftovers");
        if (items.Count == 0)
        {
            // Empty-state panel toont een groen check-icon + heading + uitleg.
            // Bij auto-refresh (verify-pass) houden we de success-InfoBar van
            // de vorige delete intact zodat user de "X freed" feedback nog
            // ziet. Bij een normale handmatige scan ruimen we de bar op want
            // het empty-panel zegt al hetzelfde.
            if (!isAutoRefresh) CleanupResultBar.IsOpen = false;
            EmptyStateTitle.Text = isAutoRefresh
                ? App.Loc.S("deepclean.verified.title", label)
                : App.Loc.S("deepclean.clean.title");
            EmptyStateDescription.Text = isAutoRefresh
                ? App.Loc.S("deepclean.verified.body")
                : kind == ScanKind.Caches
                    ? App.Loc.S("deepclean.caches.empty")
                    : App.Loc.S("deepclean.leftovers.empty");
            EmptyStatePanel.Visibility = Visibility.Visible;
            return;
        }

        // Auto-refresh modus met items > 0: de delete-batch was deels effectief
        // maar er zijn items overgebleven (typisch failed deletes door in-use
        // files). Toon dat in de InfoBar maar open GEEN nieuwe dialog — user
        // heeft net z'n eerste delete-poging gedaan, een tweede automatisch
        // openen voelt opdringerig.
        if (isAutoRefresh)
        {
            CleanupResultBar.Severity = InfoBarSeverity.Warning;
            CleanupResultBar.Title = App.Loc.S("deepclean.stillPresent.title", label, App.Loc.Plural("common.itemCount", items.Count));
            CleanupResultBar.Message = App.Loc.S("deepclean.stillPresent.body");
            CleanupResultBar.IsOpen = true;
            return;
        }

        var totalSize = items.Sum(i => i.SizeBytes);
        CleanupResultBar.Severity = InfoBarSeverity.Informational;
        CleanupResultBar.Title = App.Loc.S("deepclean.found.title", label, App.Loc.Plural("common.itemCount", items.Count), App.Loc.FormatBytes(totalSize));
        if (kind == ScanKind.Caches)
        {
            CleanupResultBar.Message = App.Loc.S("deepclean.found.simple");
        }
        else
        {
            // Per-categorie breakdown zodat user direct ziet waar de hits zitten.
            var folderCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedFolder);
            var regCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedRegistry);
            var appPathCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedAppPath);
            var muiCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedMuiCache);
            var classCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedClassHandler);
            var shortcutCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedShortcut);
            var taskCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedScheduledTask);
            var firewallCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedFirewallRule);
            var serviceCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedService);
            var hkcuCount = items.Count(i => i.Category == DeepCleanCategory.OrphanedHkcuVendor);
            var parts = new List<string>();
            if (folderCount > 0) parts.Add(App.Loc.S("deepclean.part.folders", folderCount));
            if (regCount > 0) parts.Add(App.Loc.S("deepclean.part.registry", regCount));
            if (appPathCount > 0) parts.Add(App.Loc.S("deepclean.part.appPaths", appPathCount));
            if (muiCount > 0) parts.Add(App.Loc.S("deepclean.part.muicache", muiCount));
            if (classCount > 0) parts.Add(App.Loc.S("deepclean.part.classHandlers", classCount));
            if (shortcutCount > 0) parts.Add(App.Loc.S("deepclean.part.shortcuts", shortcutCount));
            if (taskCount > 0) parts.Add(App.Loc.S("deepclean.part.tasks", taskCount));
            if (firewallCount > 0) parts.Add(App.Loc.S("deepclean.part.firewall", firewallCount));
            if (serviceCount > 0) parts.Add(App.Loc.S("deepclean.part.services", serviceCount));
            if (hkcuCount > 0) parts.Add(App.Loc.S("deepclean.part.hkcuVendor", hkcuCount));
            CleanupResultBar.Message = App.Loc.S("deepclean.found.breakdown", string.Join(" · ", parts));
        }
        CleanupResultBar.IsOpen = true;

        // First-run config voor System Restore Points: alleen bij de
        // allereerste Deep Clean scan, vraag user éénmalig of we restore
        // points willen maken voor de toekomstige delete-batches. Setting
        // wordt opgeslagen + DeepCleanRestorePointConfigured flag op true,
        // dus volgende keer komt deze popup niet meer.
        if (!App.Settings.DeepCleanRestorePointConfigured)
        {
            var cfg = new Dialogs.RestorePointConfigDialog
            {
                OperationName = App.Loc.S("nav.debloat.deepClean"),
                XamlRoot = this.XamlRoot
            };
            var result = await cfg.ShowAsync();
            App.Settings.RestorePointBeforeDeepClean = (result == ContentDialogResult.Primary);
            App.Settings.DeepCleanRestorePointConfigured = true;
        }

        var dialog = new DeepCleanDialog(items, App.DeepClean) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        if (dialog.DeleteResult is { SuccessCount: > 0 })
        {
            CleanupResultBar.Severity = InfoBarSeverity.Success;
            CleanupResultBar.Title = App.Loc.S("deepclean.deleted.title", label, App.Loc.Plural("common.itemCount", dialog.DeleteResult.SuccessCount));
            CleanupResultBar.Message = dialog.DeleteResult.FailedCount > 0
                ? App.Loc.S("deepclean.deleted.partial", App.Loc.FormatBytes(dialog.DeleteResult.BytesFreed), App.Loc.Plural("common.itemCount", dialog.DeleteResult.FailedCount))
                : App.Loc.S("deepclean.deleted.all", App.Loc.FormatBytes(dialog.DeleteResult.BytesFreed));
            CleanupResultBar.IsOpen = true;

            // Auto-refresh: na een succesvolle delete-batch dezelfde scan
            // opnieuw runnen zodat user direct ziet of de verwijderde items
            // ook écht weg zijn. Skip wanneer we al in een auto-refresh zitten
            // (anders kan een falende delete in een loop blijven hangen).
            if (!isAutoRefresh)
            {
                await RunScanAsync(kind, isAutoRefresh: true);
            }
        }
        else if (dialog.DeleteResult is { Cancelled: true })
        {
            CleanupResultBar.Severity = InfoBarSeverity.Warning;
            CleanupResultBar.Title = App.Loc.S("deepclean.cancelled.title", label);
            CleanupResultBar.Message = App.Loc.S("deepclean.cancelled.body");
            CleanupResultBar.IsOpen = true;
        }
    }

}
