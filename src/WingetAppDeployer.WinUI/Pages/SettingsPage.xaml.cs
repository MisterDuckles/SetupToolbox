using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;
using WingetAppDeployer_WinUI.Helpers;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private bool _suppressToggleEvent;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Suppress Toggled event tijdens initial sync — anders schrijft elke
        // page-navigatie de current value terug naar disk (no-op maar onnodig IO).
        _suppressToggleEvent = true;
        FallbackToggle.IsOn = App.Settings.FallbackToDownloadPage;
        ParallelToggle.IsOn = App.Settings.ParallelInstalls;
        LeftoverToggle.IsOn = App.Settings.ScanLeftoversAfterUninstall;
        DeepCleanRestoreToggle.IsOn = App.Settings.RestorePointBeforeDeepClean;
        DebloatRestoreToggle.IsOn = App.Settings.RestorePointBeforeDebloat;
        SyncBackupModeRadios();
        _suppressToggleEvent = false;

        UpdateBrowseSnapshotsLabel();
        await RefreshScheduleStatusAsync();
        await RefreshRestorePointStatusAsync();
    }

    private void SyncBackupModeRadios()
    {
        var mode = App.Settings.BackupBeforeApply;
        foreach (var item in BackupModeRadios.Items)
        {
            if (item is RadioButton rb && rb.Tag is string tag && tag == mode.ToString())
            {
                BackupModeRadios.SelectedItem = rb;
                return;
            }
        }
    }

    private void UpdateBrowseSnapshotsLabel()
    {
        var count = App.Snapshots.List().Count;
        BrowseSnapshotsText.Text = count == 0 ? "Geen snapshots" : $"Bekijk snapshots ({count})";
        BrowseSnapshotsButton.IsEnabled = count > 0;
    }

    private void BackupModeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        if (BackupModeRadios.SelectedItem is not RadioButton rb) return;
        if (rb.Tag is not string tag) return;
        if (Enum.TryParse<BackupBeforeApplyMode>(tag, out var mode))
        {
            App.Settings.BackupBeforeApply = mode;
        }
    }

    private async void BrowseSnapshotsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.SnapshotBrowserDialog { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
        UpdateBrowseSnapshotsLabel();
    }

    private void DeepCleanRestoreToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.RestorePointBeforeDeepClean = DeepCleanRestoreToggle.IsOn;
        // Markeren als configured zodat de first-run popup niet meer triggert
        // (user heeft hier expliciet een keuze gemaakt).
        App.Settings.DeepCleanRestorePointConfigured = true;
    }

    private void DebloatRestoreToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.RestorePointBeforeDebloat = DebloatRestoreToggle.IsOn;
        App.Settings.DebloatRestorePointConfigured = true;
    }

    // Opent rstrui.exe — de native Windows System Restore wizard. Vereist
    // admin: zonder elevation komt rstrui terug met "to perform this task
    // you must log on as a system administrator". Daarom Verb=runas zodat
    // Windows een UAC-prompt toont voor de wizard zelf.
    private void OpenSystemRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "rstrui.exe",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC geweigerd — user koos om geen admin-rechten te geven. Geen
            // error tonen, gewoon stil stoppen (gebruiker maakte bewust een
            // keuze, geen falure).
        }
        catch (Exception ex)
        {
            RestorePointGlobalStatus.Severity = InfoBarSeverity.Error;
            RestorePointGlobalStatus.Title = "Kon System Restore wizard niet openen";
            RestorePointGlobalStatus.Message = ex.Message;
            RestorePointGlobalStatus.IsOpen = true;
        }
    }

    // Toont system-protection-status en de leeftijd van het laatste restore
    // point. Belangrijke signalen voor user:
    //  - System Protection uit → uitroepteken op beide toggles, tooltip
    //    legt uit dat de feature niet bruikbaar is zonder dat aan te zetten
    //  - Laatste restore point < 24u geleden → uitroepteken op beide toggles,
    //    tooltip "Windows skipt nieuwe punten binnen 24u"
    private async System.Threading.Tasks.Task RefreshRestorePointStatusAsync()
    {
        var status = await App.RestorePoint.GetStatusAsync();
        DeepCleanWarningGlyph.Visibility = Visibility.Collapsed;
        DebloatWarningGlyph.Visibility = Visibility.Collapsed;
        RestorePointGlobalStatus.IsOpen = false;

        if (status.BlockedReason != null && status.BlockedReason.Contains("System Protection"))
        {
            // Blocking case: System Protection helemaal uit
            DeepCleanWarningGlyph.Visibility = Visibility.Visible;
            DebloatWarningGlyph.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(DeepCleanWarningGlyph, status.BlockedReason);
            ToolTipService.SetToolTip(DebloatWarningGlyph, status.BlockedReason);
            RestorePointGlobalStatus.Severity = InfoBarSeverity.Warning;
            RestorePointGlobalStatus.Title = "System Protection is uit";
            RestorePointGlobalStatus.Message = "Restore points kunnen niet worden gemaakt zonder System Protection aan voor je systeemschijf. Open System Properties > System Protection om dit aan te zetten.";
            RestorePointGlobalStatus.IsOpen = true;
        }
        else if (!status.CanCreate && status.HoursSinceLast.HasValue)
        {
            // 24h rate-limit case
            var hours = status.HoursSinceLast.Value;
            var tt = $"Laatste restore point was {RestorePointService.FormatAgo(TimeSpan.FromHours(hours))} geleden. Windows skipt nieuwe punten binnen 24u — bij volgende Deep Clean/Debloat wordt de checkpoint-stap silent geskipt.";
            DeepCleanWarningGlyph.Visibility = Visibility.Visible;
            DebloatWarningGlyph.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(DeepCleanWarningGlyph, tt);
            ToolTipService.SetToolTip(DebloatWarningGlyph, tt);
        }
        else if (status.HoursSinceLast.HasValue)
        {
            var ago = RestorePointService.FormatAgo(TimeSpan.FromHours(status.HoursSinceLast.Value));
            DeepCleanRestorePointStatus.Text =
                $"Voor elke delete-batch een Windows System Restore Point maken. Laatste restore point was {ago} geleden.";
            DebloatRestorePointStatus.Text =
                $"Voor elke uninstall-batch een Windows System Restore Point maken. Laatste restore point was {ago} geleden.";
        }
    }

    private void FallbackToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.FallbackToDownloadPage = FallbackToggle.IsOn;
    }

    private void ParallelToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.ParallelInstalls = ParallelToggle.IsOn;
    }

    private void LeftoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.ScanLeftoversAfterUninstall = LeftoverToggle.IsOn;
    }

    private async System.Threading.Tasks.Task RefreshScheduleStatusAsync()
    {
        var exists = await App.TaskScheduler.TaskExistsAsync();

        if (exists)
        {
            ScheduleStatusText.Text = "A scheduled auto-update task is active. Winget upgrades all apps automatically on the configured trigger.";
            ScheduleButtonText.Text = "Change";
            DisableButton.Visibility = Visibility.Visible;
        }
        else
        {
            ScheduleStatusText.Text = "Not scheduled. Configure a Windows Task Scheduler entry that runs 'winget upgrade --all' silently on a recurring schedule.";
            ScheduleButtonText.Text = "Set up";
            DisableButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void ScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScheduleDialog { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
        await RefreshScheduleStatusAsync();
    }

    private async void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var confirm = new ContentDialog
        {
            Title = "Disable auto-updates?",
            Content = "This removes the Windows scheduled task. You can re-create it any time.",
            PrimaryButtonText = "Disable",
            CloseButtonText = "Cancel",
            // DefaultButton.None — anders krijgt de aangewezen button auto-accent
            // styling die onze CloseButtonStyle overschrijft (beide knoppen blauw).
            // Voor destructive actions sowieso veiliger: geen Enter-shortcut.
            DefaultButton = ContentDialogButton.None,
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
            PrimaryButtonStyle = (Microsoft.UI.Xaml.Style)resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Microsoft.UI.Xaml.Style)resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var ok = await App.TaskScheduler.DeleteUpdateTaskAsync();
        var result = new ContentDialog
        {
            Title = ok ? "Auto-updates disabled" : "Could not disable",
            Content = ok
                ? "The scheduled task has been removed."
                : "schtasks.exe failed to delete the task (admin prompt geweigerd of schtasks-fout).",
            CloseButtonText = "OK",
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
            CloseButtonStyle = (Microsoft.UI.Xaml.Style)resources["DialogDefaultButtonStyle"],
            XamlRoot = this.XamlRoot
        };
        await result.ShowAsync();

        await RefreshScheduleStatusAsync();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var db = await App.Database.GetAppDatabaseAsync();
        var selectedCount = SelectionHelper.GetSelectedCount(db);
        if (selectedCount == 0)
        {
            ShowSelectionInfo(InfoBarSeverity.Warning, "Niets om te exporteren",
                "Je hebt momenteel geen apps geselecteerd. Selecteer eerst een paar apps op de Apps-pagina.");
            return;
        }

        var suggestedName = $"my-apps-{DateTime.Now:yyyy-MM-dd}";
        var file = await FilePickerHelper.PickSaveFileAsync(suggestedName, "WingetAppDeployer selection", ".json");
        if (file == null) return;

        try
        {
            await App.SelectionIO.ExportAsync(file.Path, db);
            ShowSelectionInfo(InfoBarSeverity.Success, "Selection exported",
                $"{selectedCount} app{(selectedCount == 1 ? "" : "s")} weggeschreven naar {file.Name}.");
        }
        catch (Exception ex)
        {
            ShowSelectionInfo(InfoBarSeverity.Error, "Export failed", ex.Message);
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var file = await FilePickerHelper.PickOpenFileAsync(".json");
        if (file == null) return;

        var db = await App.Database.GetAppDatabaseAsync();
        var result = await App.SelectionIO.ImportAsync(file.Path, db, clearFirst: true);

        if (result.Error != null)
        {
            ShowSelectionInfo(InfoBarSeverity.Error, "Import failed", result.Error);
            return;
        }

        var severity = result.Skipped > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        var msg = result.Skipped == 0
            ? $"{result.Matched} app{(result.Matched == 1 ? "" : "s")} geselecteerd."
            : $"{result.Matched} app{(result.Matched == 1 ? "" : "s")} geselecteerd, {result.Skipped} niet gevonden in de huidige catalog.";
        ShowSelectionInfo(severity, "Selection imported", msg);
    }

    private void ShowSelectionInfo(InfoBarSeverity severity, string title, string message)
    {
        SelectionResultBar.Severity = severity;
        SelectionResultBar.Title = title;
        SelectionResultBar.Message = message;
        SelectionResultBar.IsOpen = true;
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);
}
