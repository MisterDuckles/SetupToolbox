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
//   2. Orphaned folders — folders in Program Files / AppData zonder bijbehorende
//      installed app. Langzamere scan (size-walk per kandidaat), conservatief
//      (alles default uit) want false positives kunnen portable apps treffen.
//
// Beide eindigen in een DeepCleanDialog met preview + delete fases. Resultaat-
// feedback komt op de eigen InfoBar bovenaan deze pagina.
public sealed partial class DeepCleanPage : Page
{
    public DeepCleanPage()
    {
        InitializeComponent();
    }

    private async void ScanCachesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(scanCaches: true);
    }

    private async void ScanOrphanedButton_Click(object sender, RoutedEventArgs e)
    {
        await RunScanAsync(scanCaches: false);
    }

    private async Task RunScanAsync(bool scanCaches)
    {
        // Disable beide knoppen tijdens scan zodat user niet 2x klikt en we
        // geen overlappende scans krijgen. Spinner zichtbaar onder de knoppen
        // zodat user weet dat er iets loopt.
        ScanCachesButton.IsEnabled = false;
        ScanOrphanedButton.IsEnabled = false;
        DeepCleanScanRing.Visibility = Visibility.Visible;

        List<DeepCleanItem> items;
        try
        {
            items = scanCaches
                ? await App.DeepClean.ScanWindowsCachesAsync()
                : await App.DeepClean.ScanOrphanedFoldersAsync();
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

        var label = scanCaches ? "Windows caches" : "Orphaned folders";
        if (items.Count == 0)
        {
            CleanupResultBar.Severity = InfoBarSeverity.Success;
            CleanupResultBar.Title = $"{label}: nothing to clean";
            CleanupResultBar.Message = scanCaches
                ? "Alle bekende cache-locaties zijn al leeg of bestaan niet."
                : "Geen orphaned folders gevonden — elke folder matcht met een geïnstalleerde app.";
            CleanupResultBar.IsOpen = true;
            return;
        }

        var totalSize = items.Sum(i => i.SizeBytes);
        CleanupResultBar.Severity = InfoBarSeverity.Informational;
        CleanupResultBar.Title = $"{label}: {items.Count} item(s) found ({FormatBytes(totalSize)})";
        CleanupResultBar.Message = "Review and pick what to delete.";
        CleanupResultBar.IsOpen = true;

        var dialog = new DeepCleanDialog(items, App.DeepClean) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();

        if (dialog.DeleteResult is { SuccessCount: > 0 })
        {
            CleanupResultBar.Severity = InfoBarSeverity.Success;
            CleanupResultBar.Title = $"{label}: {dialog.DeleteResult.SuccessCount} item(s) deleted";
            CleanupResultBar.Message = dialog.DeleteResult.FailedCount > 0
                ? $"{FormatBytes(dialog.DeleteResult.BytesFreed)} freed · {dialog.DeleteResult.FailedCount} item(s) couldn't be deleted."
                : $"{FormatBytes(dialog.DeleteResult.BytesFreed)} freed.";
        }
        else if (dialog.DeleteResult is { Cancelled: true })
        {
            CleanupResultBar.Severity = InfoBarSeverity.Warning;
            CleanupResultBar.Title = $"{label}: cleanup cancelled";
            CleanupResultBar.Message = "UAC prompt was declined — nothing was deleted.";
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
