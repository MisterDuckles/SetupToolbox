using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Services;

namespace SetupToolbox.Dialogs;

// Voorbeeld vóór het terugzetten van een config-backup (v1.2.9): wat zit er in het
// bestand, en welke onderdelen wil je daadwerkelijk overnemen. Per ONDERDEEL, niet
// per veld — "wel mijn tweak-profiel, niet mijn logging-voorkeur" is precies de
// korrel waarop mensen die keuze maken, en 15 losse vinkjes leest niemand na.
//
// Deze dialog is meteen de bevestiging die de import tot nu toe miste: het
// terugzetten van de app-selectie wist de huidige selectie.
public sealed partial class ConfigImportDialog : ContentDialog
{
    private readonly bool _hasApps;
    private readonly bool _hasTweaks;
    private readonly bool _hasSettings;

    public ConfigImportDialog(ConfigBackupContent content, string? languageLabel)
    {
        InitializeComponent();

        _hasApps = content.AppIds.Count > 0;
        _hasTweaks = content.Tweaks.Count > 0;
        _hasSettings = content.Settings != null && content.Settings.Count > 0;

        SubtitleText.Text = content.ExportedAt == DateTimeOffset.MinValue
            ? App.Loc.S("config.import.subtitleNoDate")
            : App.Loc.S("config.import.subtitle",
                content.ExportedAt.ToLocalTime().ToString("d MMMM yyyy", App.Loc.Culture));

        AppsLabel.Text = App.Loc.S("config.import.part.apps",
            App.Loc.Plural("common.appCount", content.AppIds.Count));
        AppsCaption.Text = App.Loc.S("config.import.part.appsCaption");
        AppsCheck.IsChecked = _hasApps;
        AppsCheck.IsEnabled = _hasApps;

        TweaksLabel.Text = App.Loc.S("config.import.part.tweaks",
            App.Loc.Plural("common.tweakCount", content.Tweaks.Count));
        TweaksCaption.Text = App.Loc.S("config.import.part.tweaksCaption");
        TweaksCheck.IsChecked = _hasTweaks;
        TweaksCheck.IsEnabled = _hasTweaks;

        SettingsLabel.Text = App.Loc.S("config.import.part.settings",
            content.Settings?.Count ?? 0);
        SettingsCheck.IsChecked = _hasSettings;
        SettingsCheck.IsEnabled = _hasSettings;

        if (languageLabel != null)
        {
            LanguageLabel.Text = App.Loc.S("config.import.part.language", languageLabel);
            LanguageCheck.Visibility = Visibility.Visible;
            LanguageCheck.IsChecked = false;   // apart bevestigen, dus nooit voorgevinkt
        }

        UpdateState();
    }

    public ConfigImportOptions Options => new(
        AppsCheck.IsChecked == true,
        TweaksCheck.IsChecked == true,
        SettingsCheck.IsChecked == true,
        LanguageCheck.IsChecked == true);

    private void Part_Toggled(object sender, RoutedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        var any = AppsCheck.IsChecked == true
            || TweaksCheck.IsChecked == true
            || SettingsCheck.IsChecked == true
            || LanguageCheck.IsChecked == true;
        IsPrimaryButtonEnabled = any;

        // De app-selectie is het enige onderdeel dat iets WEGGOOIT in plaats van
        // toevoegt: de huidige selectie wordt vervangen, niet aangevuld.
        OverwriteBar.IsOpen = AppsCheck.IsChecked == true;
    }
}
