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

    // Uitkomst van een config-import die nog getoond moet worden nadat een
    // taalwissel deze page opnieuw heeft laten opbouwen. Static omdat de instantie
    // die de import deed dan al vervangen is.
    private static ConfigApplyResult? _pendingConfigResult;

    // Zet elke besturing gelijk aan wat er in de settings-store staat. Los van
    // OnNavigatedTo omdat een config-import dezelfde hersynchronisatie nodig heeft.
    //
    // Suppress Toggled event tijdens de sync — anders schrijft elke page-navigatie
    // de current value terug naar disk (no-op maar onnodig IO).
    private void SyncAllControls()
    {
        _suppressToggleEvent = true;
        FallbackToggle.IsOn = App.Settings.FallbackToDownloadPage;
        ParallelCountBox.Value = App.Settings.MaxParallelInstalls;
        LeftoverToggle.IsOn = App.Settings.ScanLeftoversAfterUninstall;
        DeepCleanRestoreToggle.IsOn = App.Settings.RestorePointBeforeDeepClean;
        DebloatRestoreToggle.IsOn = App.Settings.RestorePointBeforeDebloat;
        UpdateNotificationsToggle.IsOn = App.Settings.UpdateNotificationsEnabled;
        UpdateCheckToggle.IsOn = App.Settings.CheckForUpdatesOnStartup;
        WelcomeBannerToggle.IsOn = App.Settings.ShowWelcomeBanner;
        ErrorLoggingToggle.IsOn = App.Settings.ErrorLoggingEnabled;
        SyncBackupModeRadios();
        SyncLanguageCombo();
        _suppressToggleEvent = false;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        SyncAllControls();

        // Een config-import die de taal omzet laat MainWindow deze page opnieuw
        // opbouwen, waardoor de resultaat-InfoBar meteen weer weg zou zijn. De
        // uitkomst wordt daarom als DATA geparkeerd en hier opnieuw gerenderd —
        // in de nieuwe taal, want de tekst wordt vers opgebouwd.
        if (_pendingConfigResult != null)
        {
            ShowConfigResult(_pendingConfigResult);
            _pendingConfigResult = null;
        }

        AppVersionText.Text = App.Loc.S("settings.checkNow.version", App.GitHub.CurrentVersion);
        UpdateBrowseSnapshotsLabel();
        await RefreshScheduleStatusAsync();
        await RefreshRestorePointStatusAsync();
    }

    // ── TAAL (v1.2.2) ──

    // Drie opties: systeem volgen (default) + de twee talen expliciet. De
    // systeem-optie toont welke taal dat nú oplevert, zodat de keuze niet blind is.
    private void SyncLanguageCombo()
    {
        var systemName = App.Loc.S(LocalizationService.SystemLanguage == AppLanguage.Dutch
            ? "settings.language.dutch"
            : "settings.language.english");

        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add($"{App.Loc.S("settings.language.followSystem")} — {systemName}");
        LanguageCombo.Items.Add(App.Loc.S("settings.language.english"));
        LanguageCombo.Items.Add(App.Loc.S("settings.language.dutch"));

        LanguageCombo.SelectedIndex = App.Loc.FollowsSystem
            ? 0
            : App.Loc.Current == AppLanguage.Dutch ? 2 : 1;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToggleEvent) return;

        // Set() vuurt LanguageChanged alleen wanneer de effectieve taal wijzigt;
        // MainWindow bouwt deze page dan opnieuw op, dus hier verder niets doen.
        App.Loc.Set(LanguageCombo.SelectedIndex switch
        {
            1 => AppLanguage.English,
            2 => AppLanguage.Dutch,
            _ => null
        });
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
        BrowseSnapshotsText.Text = count == 0
            ? App.Loc.S("settings.backup.none")
            : App.Loc.S("settings.backup.browseCount", count);
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
            RestorePointGlobalStatus.Title = App.Loc.S("settings.restore.wizardFailed.title");
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

        if (status.ProtectionOff)
        {
            // Blocking case: System Protection helemaal uit
            var off = App.Loc.S("restorePoint.protectionOff");
            DeepCleanWarningGlyph.Visibility = Visibility.Visible;
            DebloatWarningGlyph.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(DeepCleanWarningGlyph, off);
            ToolTipService.SetToolTip(DebloatWarningGlyph, off);
            RestorePointGlobalStatus.Severity = InfoBarSeverity.Warning;
            RestorePointGlobalStatus.Title = App.Loc.S("settings.restore.protectionOff.title");
            RestorePointGlobalStatus.Message = App.Loc.S("settings.restore.protectionOff.body");
            RestorePointGlobalStatus.IsOpen = true;
        }
        else if (!status.CanCreate && status.HoursSinceLast.HasValue)
        {
            // 24h rate-limit case
            var hours = status.HoursSinceLast.Value;
            var tt = App.Loc.S("settings.restore.rateLimited.tooltip",
                RestorePointService.FormatAgo(TimeSpan.FromHours(hours)));
            DeepCleanWarningGlyph.Visibility = Visibility.Visible;
            DebloatWarningGlyph.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(DeepCleanWarningGlyph, tt);
            ToolTipService.SetToolTip(DebloatWarningGlyph, tt);
        }
        else if (status.HoursSinceLast.HasValue)
        {
            var ago = RestorePointService.FormatAgo(TimeSpan.FromHours(status.HoursSinceLast.Value));
            DeepCleanRestorePointStatus.Text = App.Loc.S("settings.restore.deepClean.descWithAge", ago);
            DebloatRestorePointStatus.Text = App.Loc.S("settings.restore.debloat.descWithAge", ago);
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
            ScheduleStatusText.Text = App.Loc.S("settings.schedule.active");
            ScheduleButtonText.Text = App.Loc.S("settings.schedule.change");
            DisableButton.Visibility = Visibility.Visible;
        }
        else
        {
            ScheduleStatusText.Text = App.Loc.S("settings.schedule.notScheduled");
            ScheduleButtonText.Text = App.Loc.S("settings.schedule.setUp");
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
            Title = App.Loc.S("settings.schedule.disableConfirm.title"),
            Content = App.Loc.S("settings.schedule.disableConfirm.body"),
            PrimaryButtonText = App.Loc.S("settings.schedule.disable"),
            CloseButtonText = App.Loc.S("common.cancel"),
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
            Title = App.Loc.S(ok ? "settings.schedule.disabled.title" : "settings.schedule.disableFailed.title"),
            Content = App.Loc.S(ok ? "settings.schedule.disabled.body" : "settings.schedule.disableFailed.body"),
            CloseButtonText = App.Loc.S("common.ok"),
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
            ShowSelectionInfo(InfoBarSeverity.Warning,
                App.Loc.S("settings.selection.nothing.title"),
                App.Loc.S("settings.selection.nothing.body"));
            return;
        }

        var suggestedName = $"my-apps-{DateTime.Now:yyyy-MM-dd}";
        // fileTypeName komt in de "Opslaan als type"-dropdown van het Windows-
        // opslaanvenster terecht en is dus gewoon zichtbare tekst (v1.2.7).
        var file = await FilePickerHelper.PickSaveFileAsync(
            suggestedName, App.Loc.S("io.fileType.appSelection"), ".json");
        if (file == null) return;

        try
        {
            await App.SelectionIO.ExportAsync(file.Path, db);
            ShowSelectionInfo(InfoBarSeverity.Success,
                App.Loc.S("settings.selection.exported.title"),
                App.Loc.S("settings.selection.exported.body",
                    App.Loc.Plural("common.appCount", selectedCount), file.Name));
        }
        catch (Exception ex)
        {
            ShowSelectionInfo(InfoBarSeverity.Error, App.Loc.S("settings.selection.exportFailed.title"), ex.Message);
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
            ShowSelectionInfo(InfoBarSeverity.Error, App.Loc.S("settings.selection.importFailed.title"), result.Error);
            return;
        }

        var severity = result.Skipped > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        var matched = App.Loc.Plural("common.appCount", result.Matched);
        var msg = result.Skipped == 0
            ? App.Loc.S("settings.selection.imported.body", matched)
            : App.Loc.S("settings.selection.imported.bodySkipped", matched, result.Skipped);
        ShowSelectionInfo(severity, App.Loc.S("settings.selection.imported.title"), msg);
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
            ShowProfileInfo(InfoBarSeverity.Error, App.Loc.S("settings.profiles.importFailed.title"), result.Error);
            return;
        }
        if (result.Matched.Count == 0)
        {
            var extra = result.SkippedIds.Count > 0
                ? App.Loc.S("settings.profiles.noKnown.unknownSuffix", result.SkippedIds.Count)
                : "";
            ShowProfileInfo(InfoBarSeverity.Warning,
                App.Loc.S("settings.profiles.noKnown.title"),
                App.Loc.S("settings.profiles.noKnown.body", extra));
            return;
        }

        // States detecteren voor de delta-berekening (al-actieve tweaks overslaan).
        try { await App.Tweaks.DetectStatesAsync(); }
        catch { }
        var (staged, already) = TweakProfileService.StageDelta(result.Matched, App.TweakPending);

        if (staged == 0)
        {
            ShowProfileInfo(InfoBarSeverity.Success,
                App.Loc.S("settings.profiles.allGood.title"),
                App.Loc.S("settings.profiles.allGood.body",
                    App.Loc.Plural("common.tweakCount", result.Matched.Count)));
            return;
        }

        var note = App.Loc.S("settings.profiles.staged", App.Loc.Plural("common.tweakCount", staged));
        if (already > 0)
            note += App.Loc.S(already == 1
                ? "settings.profiles.alreadyGood.one"
                : "settings.profiles.alreadyGood.other", already);
        if (result.SkippedIds.Count > 0)
            note += App.Loc.S("settings.profiles.skipped", result.SkippedIds.Count);

        var dialog = new ContentDialog
        {
            Title = App.Loc.S("settings.profiles.imported.title"),
            Content = App.Loc.S("settings.profiles.imported.body", note),
            PrimaryButtonText = App.Loc.S("settings.profiles.goToTweaks"),
            CloseButtonText = App.Loc.S("settings.profiles.stayHere"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            App.Window?.NavigateToTweaks();
        else
            ShowProfileInfo(InfoBarSeverity.Informational,
                App.Loc.S("settings.profiles.ready.title"),
                App.Loc.S("settings.profiles.ready.body", note));
    }

    private void ShowProfileInfo(InfoBarSeverity severity, string title, string message)
    {
        TweakProfileResultBar.Severity = severity;
        TweakProfileResultBar.Title = title;
        TweakProfileResultBar.Message = message;
        TweakProfileResultBar.IsOpen = true;
    }

    // ── VOLLEDIGE CONFIGURATIE (v1.2.9) ──

    // Eén bestand met app-keuze + tweaks + voorkeuren. Het exporteren is sinds
    // v1.2.9.1 een flow van drie stappen, en die begint hier maar eindigt op de
    // Tweaks-tab: eerst kies je daar de tweaks (checklist, voorgevinkt met wat er
    // aanstaat), dan de apps in een dialog, dan het bestand. Reden om de tweaks
    // niet in de dialog te proppen: bij een backup wil je ook kunnen bijvinken wat
    // nu úít staat, en dan wil je de omschrijving erbij zien.
    //
    // Wat hier gebeurt is dus alleen het dure voorwerk — één winget list en één
    // registry-walk — plus het vullen van de selectie-store waaruit de checklist
    // zijn beginstand leest.
    private async void ConfigExportButton_Click(object sender, RoutedEventArgs e)
    {
        SetConfigButtonsEnabled(false);
        try
        {
            // Verse winget list, want dit is precies de vraag "wat staat hier nu".
            // Duurt seconden, vandaar de melding.
            ShowConfigInfo(InfoBarSeverity.Informational,
                App.Loc.S("config.export.scanning.title"),
                App.Loc.S("config.export.scanning.body"));

            await App.Winget.GetInstalledAppsListAsync(forceRefresh: true);
            try { await App.Tweaks.DetectStatesAsync(); } catch { }

            ConfigBackupService.PrefillTweakSelection(App.Tweaks.All, App.ProfileSelection);
            ConfigResultBar.IsOpen = false;
            App.Window?.EnterConfigBackupMode();
        }
        catch (Exception ex)
        {
            ShowConfigInfo(InfoBarSeverity.Error, App.Loc.S("config.export.failed.title"), ex.Message);
        }
        finally
        {
            SetConfigButtonsEnabled(true);
        }
    }

    // Slikt de bundel én de twee losse formats, zodat je hier ook een oude
    // my-apps.json of my-tweaks.json kunt openen zonder de juiste knop te zoeken.
    private async void ConfigImportButton_Click(object sender, RoutedEventArgs e)
    {
        var file = await FilePickerHelper.PickOpenFileAsync(".json");
        if (file == null) return;

        SetConfigButtonsEnabled(false);
        try
        {
            var read = await App.ConfigIO.ReadAsync(file.Path);
            if (read.Error != null || read.Content == null)
            {
                ShowConfigInfo(InfoBarSeverity.Error,
                    App.Loc.S("config.import.failed.title"),
                    read.Error ?? App.Loc.S("io.notAConfigFile"));
                return;
            }

            var content = read.Content;
            var dialog = new ConfigImportDialog(content, DescribeImportedLanguage(content))
            {
                XamlRoot = this.XamlRoot
            };
            ConfigResultBar.IsOpen = false;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var db = await App.Database.GetAppDatabaseAsync();
            var result = await App.ConfigIO.ApplyAsync(
                content, dialog.Options, db, App.Tweaks, App.TweakPending);

            // Voorkeuren kunnen gewijzigd zijn, dus de besturingen op deze page
            // lopen anders achter op wat er in settings.json staat.
            if (result.SettingsApplied) SyncAllControls();

            // Taal als laatste: Set() vuurt LanguageChanged en MainWindow bouwt deze
            // page dan opnieuw op. Alleen wanneer de EFFECTIEVE taal wijzigt — anders
            // gebeurt er niets en moet het resultaat gewoon hier getoond worden.
            var target = ParseLanguageCode(result.LanguageApplied ? result.LanguageCode : null);
            if (target.HasValue && target.Value.effective != App.Loc.Current)
            {
                _pendingConfigResult = result;
                App.Loc.Set(target.Value.setting);
                return;
            }

            ShowConfigResult(result);
        }
        catch (Exception ex)
        {
            ShowConfigInfo(InfoBarSeverity.Error, App.Loc.S("config.import.failed.title"), ex.Message);
        }
        finally
        {
            SetConfigButtonsEnabled(true);
        }
    }

    // Menselijke naam van de taal in het bestand, of null wanneer het bestand geen
    // taal draagt of dezelfde keuze heeft als deze machine — dan is er niets te
    // bevestigen en hoort het vinkje er ook niet te staan.
    private static string? DescribeImportedLanguage(ConfigBackupContent content)
    {
        var code = content.Settings?.Language;
        if (code == null) return null;
        if (string.Equals(code, App.Settings.Language ?? ConfigBackupService.FollowSystemLanguage,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var parsed = ParseLanguageCode(code);
        if (parsed == null) return null;

        if (parsed.Value.setting == null)
        {
            var systemName = App.Loc.S(LocalizationService.SystemLanguage == AppLanguage.Dutch
                ? "settings.language.dutch"
                : "settings.language.english");
            return $"{App.Loc.S("settings.language.followSystem")} — {systemName}";
        }

        return App.Loc.S(parsed.Value.setting == AppLanguage.Dutch
            ? "settings.language.dutch"
            : "settings.language.english");
    }

    // "en" / "nl" / "system" → (wat er in settings.json komt, welke taal dat oplevert).
    private static (AppLanguage? setting, AppLanguage effective)? ParseLanguageCode(string? code) => code switch
    {
        "en" => (AppLanguage.English, AppLanguage.English),
        "nl" => (AppLanguage.Dutch, AppLanguage.Dutch),
        ConfigBackupService.FollowSystemLanguage => (null, LocalizationService.SystemLanguage),
        _ => null
    };

    // Eén melding voor de hele import, met een regel per onderdeel. Bewust niet één
    // samengevat totaal: dan zie je niet meer wélk onderdeel iets moest overslaan,
    // en dat is precies waar de losse import-meldingen voor bestaan.
    private void ShowConfigResult(ConfigApplyResult r)
    {
        var lines = new System.Collections.Generic.List<string>();

        if (r.AppsApplied)
        {
            var appLine = r.AppsSkipped == 0
                ? App.Loc.S("config.result.apps", App.Loc.Plural("common.appCount", r.AppsMatched))
                : App.Loc.S("config.result.appsSkipped",
                    App.Loc.Plural("common.appCount", r.AppsMatched), r.AppsSkipped);
            // Apps buiten de catalogus staan in een eigen sectie op de Apps-pagina;
            // zonder deze regel is niet duidelijk waar ze gebleven zijn.
            if (r.AppsExtra > 0) appLine += App.Loc.S("config.result.appsExtra", r.AppsExtra);
            lines.Add(appLine);
        }

        if (r.TweaksApplied)
        {
            var line = App.Loc.S("config.result.tweaks",
                App.Loc.Plural("common.tweakCount", r.TweaksStaged));
            if (r.TweaksAlreadyGood > 0)
                line += App.Loc.S(r.TweaksAlreadyGood == 1
                    ? "settings.profiles.alreadyGood.one"
                    : "settings.profiles.alreadyGood.other", r.TweaksAlreadyGood);
            if (r.TweaksSkipped > 0)
                line += App.Loc.S("settings.profiles.skipped", r.TweaksSkipped);
            lines.Add(line);
        }

        if (r.SettingsApplied)
            lines.Add(App.Loc.S("config.result.settings", r.SettingsCount));

        if (r.LanguageApplied)
            lines.Add(App.Loc.S("config.result.language"));

        if (lines.Count == 0)
        {
            ShowConfigInfo(InfoBarSeverity.Informational,
                App.Loc.S("config.result.nothing.title"),
                App.Loc.S("config.result.nothing.body"));
            return;
        }

        // Tweaks worden alleen KLAARGEZET, niet geschreven — de Apply (met
        // backup-prompt en één UAC) hoort op de Tweaks-tab, net als bij de
        // profiel-import. Zonder deze regel denkt iedereen dat het al gebeurd is.
        if (r.TweaksStaged > 0) lines.Add(App.Loc.S("config.result.applyHint"));

        var severity = r.AnythingSkipped ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        ShowConfigInfo(severity, App.Loc.S("config.result.title"), string.Join(Environment.NewLine, lines));
    }

    private void SetConfigButtonsEnabled(bool enabled)
    {
        ConfigExportButton.IsEnabled = enabled;
        ConfigImportButton.IsEnabled = enabled;
    }

    private void ShowConfigInfo(InfoBarSeverity severity, string title, string message)
    {
        ConfigResultBar.Severity = severity;
        ConfigResultBar.Title = title;
        ConfigResultBar.Message = message;
        ConfigResultBar.IsOpen = true;
    }

    // Toasts rond de geplande winget auto-update (v1.0.13). ToastHelper leest deze
    // gate voor élke toast, dus uit = geen enkele melding meer van de geplande run.
    private void UpdateNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        App.Settings.UpdateNotificationsEnabled = UpdateNotificationsToggle.IsOn;
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
        CheckUpdatesButtonText.Text = App.Loc.S("settings.checkNow.busy");
        UpdateCheckResultBar.IsOpen = false;
        try
        {
            var result = await App.GitHub.CheckForUpdateAsync();
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Update != null:
                    ShowUpdateCheckInfo(InfoBarSeverity.Success,
                        App.Loc.S("settings.checkNow.available.title"),
                        App.Loc.S("settings.checkNow.available.body", result.Update.Version));
                    App.Window?.ShowUpdate(result.Update);
                    break;
                case UpdateCheckStatus.UpToDate:
                    ShowUpdateCheckInfo(InfoBarSeverity.Success,
                        App.Loc.S("settings.checkNow.upToDate.title"),
                        App.Loc.S("settings.checkNow.upToDate.body", App.GitHub.CurrentVersion));
                    break;
                default:
                    ShowUpdateCheckInfo(InfoBarSeverity.Error,
                        App.Loc.S("settings.checkNow.failed.title"),
                        result.Error ?? App.Loc.S("settings.checkNow.failed.body"));
                    break;
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButtonText.Text = App.Loc.S("settings.checkNow.button");
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
