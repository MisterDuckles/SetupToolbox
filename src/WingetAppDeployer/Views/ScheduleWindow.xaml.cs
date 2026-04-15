using System.Windows;
using WingetAppDeployer.Helpers;
using WingetAppDeployer.Models;
using WingetAppDeployer.Services;

namespace WingetAppDeployer.Views;

public partial class ScheduleWindow : Window
{
    public ScheduleWindow()
    {
        InitializeComponent();

        SourceInitialized += (s, e) =>
        {
            var settings = App.SettingsService?.LoadSettings();
            if (settings?.Theme == AppTheme.Windows)
                MicaHelper.SetTitleBarTheme(this, settings.DarkMode);
        };
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var scheduleType = DailyRadio.IsChecked == true
            ? UpdateScheduleType.Daily
            : WeeklyRadio.IsChecked == true
                ? UpdateScheduleType.Weekly
                : UpdateScheduleType.OnStartup;

        var time = TimeTextBox.Text;

        var success = await App.TaskSchedulerService!.CreateUpdateTaskAsync(scheduleType, time);

        if (success)
        {
            // Save setting
            var settings = App.SettingsService!.CurrentSettings;
            settings.AutoUpdateEnabled = true;
            settings.AutoUpdateSchedule = scheduleType.ToString();
            App.SettingsService.SaveSettings(settings);

            MessageBox.Show(
                "Auto-update task created successfully!",
                "Success",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            Close();
        }
        else
        {
            MessageBox.Show(
                "Failed to create scheduled task. Make sure you have administrator privileges.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
