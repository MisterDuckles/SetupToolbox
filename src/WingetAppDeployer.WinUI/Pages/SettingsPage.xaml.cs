using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WingetAppDeployer_WinUI.Dialogs;

namespace WingetAppDeployer_WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await RefreshScheduleStatusAsync();
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
        var confirm = new ContentDialog
        {
            Title = "Disable auto-updates?",
            Content = "This removes the Windows scheduled task. You can re-create it any time.",
            PrimaryButtonText = "Disable",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(8),
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
            XamlRoot = this.XamlRoot
        };
        await result.ShowAsync();

        await RefreshScheduleStatusAsync();
    }
}
