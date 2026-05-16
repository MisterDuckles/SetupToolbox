using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Pages;

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

        var label = kind == ScanKind.Caches ? "Windows caches" : "Leftovers";
        if (items.Count == 0)
        {
            // Empty-state panel toont een groen check-icon + heading + uitleg.
            // Bij auto-refresh (verify-pass) houden we de success-InfoBar van
            // de vorige delete intact zodat user de "X freed" feedback nog
            // ziet. Bij een normale handmatige scan ruimen we de bar op want
            // het empty-panel zegt al hetzelfde.
            if (!isAutoRefresh) CleanupResultBar.IsOpen = false;
            EmptyStateTitle.Text = isAutoRefresh
                ? $"{label}: cleanup verified"
                : "Looking clean!";
            EmptyStateDescription.Text = isAutoRefresh
                ? "Alle aangevinkte items zijn verwijderd en blijken nu echt weg te zijn."
                : kind == ScanKind.Caches
                    ? "Alle bekende cache-locaties zijn al leeg of bestaan niet."
                    : "Geen leftovers gevonden — alles op je systeem matcht met een geïnstalleerde app.";
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
            CleanupResultBar.Title = $"{label}: {items.Count} item(s) still present after cleanup";
            CleanupResultBar.Message = "Sommige items konden niet verwijderd worden (typisch: in gebruik, of permissions). Klik nogmaals \"Scan\" om opnieuw te proberen.";
            CleanupResultBar.IsOpen = true;
            return;
        }

        var totalSize = items.Sum(i => i.SizeBytes);
        CleanupResultBar.Severity = InfoBarSeverity.Informational;
        CleanupResultBar.Title = $"{label}: {items.Count} item(s) found ({FormatBytes(totalSize)})";
        if (kind == ScanKind.Caches)
        {
            CleanupResultBar.Message = "Review and pick what to delete.";
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
            if (folderCount > 0) parts.Add($"{folderCount} folders");
            if (regCount > 0) parts.Add($"{regCount} registry");
            if (appPathCount > 0) parts.Add($"{appPathCount} App Paths");
            if (muiCount > 0) parts.Add($"{muiCount} MUIcache");
            if (classCount > 0) parts.Add($"{classCount} class handlers");
            if (shortcutCount > 0) parts.Add($"{shortcutCount} shortcuts");
            if (taskCount > 0) parts.Add($"{taskCount} scheduled tasks");
            if (firewallCount > 0) parts.Add($"{firewallCount} firewall rules");
            if (serviceCount > 0) parts.Add($"{serviceCount} services");
            if (hkcuCount > 0) parts.Add($"{hkcuCount} HKCU vendor keys");
            CleanupResultBar.Message = $"{string.Join(" · ", parts)}. Review and pick what to delete.";
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
                OperationName = "Deep Clean",
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
            CleanupResultBar.Title = $"{label}: {dialog.DeleteResult.SuccessCount} item(s) deleted";
            CleanupResultBar.Message = dialog.DeleteResult.FailedCount > 0
                ? $"{FormatBytes(dialog.DeleteResult.BytesFreed)} freed · {dialog.DeleteResult.FailedCount} item(s) couldn't be deleted."
                : $"{FormatBytes(dialog.DeleteResult.BytesFreed)} freed.";
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
            CleanupResultBar.Title = $"{label}: cleanup cancelled";
            CleanupResultBar.Message = "UAC prompt was declined — nothing was deleted.";
            CleanupResultBar.IsOpen = true;
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
