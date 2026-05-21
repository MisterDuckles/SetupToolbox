using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Services;

namespace SetupToolbox.Dialogs;

public sealed partial class ScheduleDialog : ContentDialog
{
    public ScheduleDialog()
    {
        InitializeComponent();

        // Default time 09:00.
        TimePickerCtrl.Time = new TimeSpan(9, 0, 0);

        // Let the user hit the primary button and handle creation ourselves so
        // we can keep the dialog open to show InfoBar feedback / errors.
        PrimaryButtonClick += ScheduleDialog_PrimaryButtonClick;
    }

    private async void ScheduleDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Defer closing until we've reported the result.
        var deferral = args.GetDeferral();
        try
        {
            var scheduleType = DailyRadio.IsChecked == true
                ? UpdateScheduleType.Daily
                : WeeklyRadio.IsChecked == true
                    ? UpdateScheduleType.Weekly
                    : UpdateScheduleType.OnStartup;

            var timeString = scheduleType == UpdateScheduleType.OnStartup
                ? null
                : $"{TimePickerCtrl.Time.Hours:D2}:{TimePickerCtrl.Time.Minutes:D2}";

            var outcome = await App.TaskScheduler.CreateUpdateTaskAsync(scheduleType, timeString);

            switch (outcome.Result)
            {
                case CreateTaskResult.Success:
                    // Dialog open houden zodat user de bevestiging ziet. Primary
                    // wordt disabled, Close-tekst wordt "Done" zodat de exit-knop
                    // duidelijk is.
                    args.Cancel = true;
                    ShowInfo(InfoBarSeverity.Success,
                        "Scheduled task created",
                        BuildScheduleDescription(scheduleType, timeString));
                    IsPrimaryButtonEnabled = false;
                    CloseButtonText = "Done";
                    break;

                case CreateTaskResult.UserCancelled:
                    args.Cancel = true;
                    ShowInfo(InfoBarSeverity.Warning,
                        "Admin rights denied",
                        "De UAC-prompt is geannuleerd. Er is geen scheduled task aangemaakt.");
                    break;

                case CreateTaskResult.Failed:
                default:
                    args.Cancel = true;
                    ShowInfo(InfoBarSeverity.Error,
                        "Could not create scheduled task",
                        outcome.ErrorOutput ?? "schtasks.exe gaf een fout terug zonder output.");
                    break;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ScheduleRadio_Changed(object sender, RoutedEventArgs e)
    {
        if (TimePanel == null) return;
        // On-logon has no time; hide the picker row.
        TimePanel.Visibility = OnStartupRadio.IsChecked == true
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ShowInfo(InfoBarSeverity severity, string title, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    private static string BuildScheduleDescription(UpdateScheduleType scheduleType, string? time) =>
        scheduleType switch
        {
            UpdateScheduleType.Daily => $"'winget upgrade --all' draait elke dag om {time ?? "09:00"}.",
            UpdateScheduleType.Weekly => $"'winget upgrade --all' draait elke maandag om {time ?? "09:00"}.",
            UpdateScheduleType.OnStartup => "'winget upgrade --all' draait bij elke gebruikers-login.",
            _ => "Scheduled task is aangemaakt."
        };
}
