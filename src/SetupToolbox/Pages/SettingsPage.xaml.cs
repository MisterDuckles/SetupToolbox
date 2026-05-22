using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SetupToolbox.Dialogs;
using SetupToolbox.Helpers;
using SetupToolbox.Services;

namespace SetupToolbox.Pages;

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
        ParallelCountBox.Value = App.Settings.MaxParallelInstalls;
        LeftoverToggle.IsOn = App.Settings.ScanLeftoversAfterUninstall;
        DeepCleanRestoreToggle.IsOn = App.Settings.RestorePointBeforeDeepClean;
        DebloatRestoreToggle.IsOn = App.Settings.RestorePointBeforeDebloat;
        UpdateCheckToggle.IsOn = App.Settings.CheckForUpdatesOnStartup;
        WelcomeBannerToggle.IsOn = App.Settings.ShowWelcomeBanner;
        ErrorLoggingToggle.IsOn = App.Settings.ErrorLoggingEnabled;
        SyncBackupModeRadios();
        _suppressToggleEvent = false;

        AppVersionText.Text = $"Huidige versie: {App.GitHub.CurrentVersion}";
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

    private void ParallelCountBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressToggleEvent) return;
        // Leeg veld → NaN; val dan terug op 2 en herstel de box zodat hij niet leeg blijft.
        if (double.IsNaN(args.NewValue))
        {
            App.Settings.MaxParallelInstalls = 2;
            sender.Value = App.Settings.MaxParallelInstalls;
            return;
        }
        App.Settings.MaxParallelInstalls = (int)args.NewValue;
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
        var file = await FilePickerHelper.PickSaveFileAsync(suggestedName, "SetupToolbox selection", ".json");
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

    // ── TWEAK-PROFIELEN (v0.9.20) ──

    // Opent de Tweaks-tab in profiel-modus (clean slate) zodat de user een set
    // tweaks kan samenstellen en opslaan.
    private void ProfileMakeButton_Click(object sender, RoutedEventArgs e)
    {
        App.Window?.EnterTweakProfileMode();
    }

    // Importeert een profielbestand: matcht tegen de catalog, detecteert states,
    // en zet alléén de delta klaar in TweakPending. Springt daarna (na bevestiging)
    // naar de Tweaks-tab waar de user Apply klikt.
    private async void ProfileImportButton_Click(object sender, RoutedEventArgs e)
    {
        var file = await FilePickerHelper.PickOpenFileAsync(".json");
        if (file == null) return;

        var result = await App.TweakProfileIO.ImportAsync(file.Path, App.Tweaks.All.ToList());
        if (result.Error != null)
        {
            ShowProfileInfo(InfoBarSeverity.Error, "Import mislukt", result.Error);
            return;
        }
        if (result.Matched.Count == 0)
        {
            var extra = result.SkippedIds.Count > 0 ? $" ({result.SkippedIds.Count} onbekend)" : "";
            ShowProfileInfo(InfoBarSeverity.Warning, "Geen bekende tweaks",
                $"Dit bestand bevat geen tweaks die in deze versie bestaan{extra}.");
            return;
        }

        // States detecteren voor de delta-berekening (al-actieve tweaks overslaan).
        try { await App.Tweaks.DetectStatesAsync(); }
        catch { }
        var (staged, already) = TweakProfileService.StageDelta(result.Matched, App.TweakPending);

        if (staged == 0)
        {
            ShowProfileInfo(InfoBarSeverity.Success, "Alles staat al goed",
                $"Alle {result.Matched.Count} tweak{(result.Matched.Count == 1 ? "" : "s")} uit dit profiel staan al in de gewenste staat — niets toe te passen.");
            return;
        }

        var note = $"{staged} tweak{(staged == 1 ? "" : "s")} klaargezet om toe te passen.";
        if (already > 0) note += $" {already} stond{(already == 1 ? "" : "en")} al goed.";
        if (result.SkippedIds.Count > 0) note += $" {result.SkippedIds.Count} onbekend en overgeslagen.";

        var dialog = new ContentDialog
        {
            Title = "Profiel geïmporteerd",
            Content = $"{note}\n\nGa je naar de Tweaks-tab om ze toe te passen?",
            PrimaryButtonText = "Naar Tweaks",
            CloseButtonText = "Blijf hier",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            App.Window?.NavigateToTweaks();
        else
            ShowProfileInfo(InfoBarSeverity.Informational, "Klaargezet",
                note + " Open de Tweaks-tab en klik Apply.");
    }

    private void ShowProfileInfo(InfoBarSeverity severity, string title, string message)
    {
        TweakProfileResultBar.Severity = severity;
        TweakProfileResultBar.Title = title;
        TweakProfileResultBar.Message = message;
        TweakProfileResultBar.IsOpen = true;
    }

    // ── APP-UPDATES (self-update, v0.10.1) ──

    private void UpdateCheckToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.CheckForUpdatesOnStartup = UpdateCheckToggle.IsOn;
    }

    private void WelcomeBannerToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.ShowWelcomeBanner = WelcomeBannerToggle.IsOn;
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButtonText.Text = "Bezig met checken...";
        UpdateCheckResultBar.IsOpen = false;
        try
        {
            var result = await App.GitHub.CheckForUpdateAsync();
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update != null:
                    ShowUpdateCheckInfo(InfoBarSeverity.Success, "Update beschikbaar",
                        $"Versie {result.Update.Version} is beschikbaar — bovenaan het venster kun je 'm installeren.");
                    App.Window?.ShowUpdate(result.Update);
                    break;
                case UpdateCheckStatus.UpToDate:
                    ShowUpdateCheckInfo(InfoBarSeverity.Success, "Up-to-date",
                        $"Je hebt de nieuwste versie ({App.GitHub.CurrentVersion}).");
                    break;
                default:
                    ShowUpdateCheckInfo(InfoBarSeverity.Error, "Check mislukt",
                        result.Error ?? "Onbekende fout bij het ophalen van releases.");
                    break;
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButtonText.Text = "Check for updates now";
        }
    }

    private void ShowUpdateCheckInfo(InfoBarSeverity severity, string title, string message)
    {
        UpdateCheckResultBar.Severity = severity;
        UpdateCheckResultBar.Title = title;
        UpdateCheckResultBar.Message = message;
        UpdateCheckResultBar.IsOpen = true;
    }

    // ── DIAGNOSTIEK (foutlogging) ──

    private void ErrorLoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.ErrorLoggingEnabled = ErrorLoggingToggle.IsOn;
    }

    // Opent de logmap (%LocalAppData%\SetupToolbox\logs) in Explorer. Maakt 'm
    // aan als 'ie nog niet bestaat (bv. als er nog nooit gelogd is).
    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Diagnostics.LogDir;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch { /* best-effort — geen UI-fout als Explorer niet opent */ }
    }

    private void ScrollView_ScrollAnimationStarting(ScrollView sender, ScrollingScrollAnimationStartingEventArgs args) =>
        ScrollViewSpeedup.OnStarting(sender, args);
}
