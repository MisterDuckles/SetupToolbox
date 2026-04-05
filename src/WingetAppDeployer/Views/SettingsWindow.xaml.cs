using System.Windows;
using System.Windows.Controls;
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
    }

    private void LoadSettings()
    {
        ThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            AppTheme.Google => 0,
            AppTheme.Windows => 1,
            AppTheme.Sunset => 2,
            AppTheme.OceanBreeze => 3,
            AppTheme.Aurora => 4,
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
            1 => AppTheme.Windows,
            2 => AppTheme.Sunset,
            3 => AppTheme.OceanBreeze,
            4 => AppTheme.Aurora,
            _ => AppTheme.Google
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        App.ApplyTheme(GetSelectedTheme(), DarkModeCheckBox.IsChecked ?? false);
    }

    private void DarkModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        App.ApplyTheme(GetSelectedTheme(), DarkModeCheckBox.IsChecked ?? false);
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
