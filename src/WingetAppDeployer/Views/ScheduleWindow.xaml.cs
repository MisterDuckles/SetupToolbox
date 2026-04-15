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

        var result = await App.TaskSchedulerService!.CreateUpdateTaskAsync(scheduleType, time);

        switch (result)
        {
            case CreateTaskResult.Success:
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
                break;

            case CreateTaskResult.UserCancelled:
                MessageBox.Show(
                    "Admin-rechten geweigerd. De taak is niet aangemaakt.",
                    "Geannuleerd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                break;

            case CreateTaskResult.Failed:
            default:
                MessageBox.Show(
                    "Kon de scheduled task niet aanmaken. Probeer de app opnieuw te starten.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                break;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
