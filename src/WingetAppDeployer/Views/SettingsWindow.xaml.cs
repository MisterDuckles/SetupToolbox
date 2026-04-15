using System;
using System.Windows;
using System.Windows.Controls;
using WingetAppDeployer.Helpers;
using WingetAppDeployer.Models;

namespace WingetAppDeployer.Views;

public partial class SettingsWindow : Window
{
    private AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = App.SettingsService!.LoadSettings();
        LoadSettings();

        SourceInitialized += (s, e) =>
        {
            if (_settings.Theme == AppTheme.Windows)
                MicaHelper.SetTitleBarTheme(this, _settings.DarkMode);
        };
    }

    private void LoadSettings()
    {
        ThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            AppTheme.Windows => 0,
            AppTheme.Sunset => 1,
            AppTheme.OceanBreeze => 2,
            AppTheme.Aurora => 3,
            _ => 0
        };

        DarkModeCheckBox.IsChecked = _settings.DarkMode;
        CheckUpdatesOnStartupCheckBox.IsChecked = _settings.CheckForUpdatesOnStartup;
        ShowWelcomeCheckBox.IsChecked = _settings.ShowWelcomeScreen;
    }

    private AppTheme GetSelectedTheme()
    {
        return ThemeComboBox.SelectedIndex switch
        {
            1 => AppTheme.Sunset,
            2 => AppTheme.OceanBreeze,
            3 => AppTheme.Aurora,
            _ => AppTheme.Windows
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var theme = GetSelectedTheme();
        var dark = DarkModeCheckBox.IsChecked ?? false;
        App.ApplyTheme(theme, dark);
        ApplyMicaToSelf(theme, dark);
    }

    private void DarkModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var theme = GetSelectedTheme();
        var dark = DarkModeCheckBox.IsChecked ?? false;
        App.ApplyTheme(theme, dark);
        ApplyMicaToSelf(theme, dark);
    }

    private void ApplyMicaToSelf(AppTheme theme, bool dark)
    {
        if (theme == AppTheme.Windows)
            MicaHelper.SetTitleBarTheme(this, dark);
    }

    private void ManageScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        var scheduleWindow = new ScheduleWindow();
        scheduleWindow.Owner = this;
        scheduleWindow.ShowDialog();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = GetSelectedTheme();

        // Save other settings
        _settings.DarkMode = DarkModeCheckBox.IsChecked ?? false;
        _settings.CheckForUpdatesOnStartup = CheckUpdatesOnStartupCheckBox.IsChecked ?? true;
        _settings.ShowWelcomeScreen = ShowWelcomeCheckBox.IsChecked ?? true;

        App.SettingsService!.SaveSettings(_settings);

        MessageBox.Show(
            "Settings saved successfully! Some changes may require restarting the app.",
            "Settings Saved",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
