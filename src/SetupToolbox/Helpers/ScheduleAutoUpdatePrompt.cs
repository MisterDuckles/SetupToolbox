using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Dialogs;

namespace SetupToolbox.Helpers;

// Post-install ContentDialog die user vraagt of `winget upgrade --all` op een
// schedule moet komen. Heuristiek: alleen tonen als er nog GEEN scheduled task
// bestaat én user heeft de prompt niet eerder dismissed via "Don't ask again".
// Calling code is verantwoordelijk voor de "had successful install" check —
// we willen niet promoten als er niks geslaagd is.
internal static class ScheduleAutoUpdatePrompt
{
    public static async Task MaybeShowAsync(XamlRoot xamlRoot)
    {
        if (App.Settings.DontAskAboutScheduling) return;
        if (await App.TaskScheduler.TaskExistsAsync()) return;

        var resources = Application.Current.Resources;
        var dialog = new ContentDialog
        {
            Title = App.Loc.S("schedulePrompt.title"),
            Content = App.Loc.S("schedulePrompt.body"),
            PrimaryButtonText = App.Loc.S("schedulePrompt.schedule"),
            SecondaryButtonText = App.Loc.S("schedulePrompt.dontAsk"),
            CloseButtonText = App.Loc.S("schedulePrompt.notNow"),
            DefaultButton = ContentDialogButton.Primary,
            CornerRadius = new CornerRadius(8),
            PrimaryButtonStyle = (Style)resources["DialogPrimaryButtonStyle"],
            SecondaryButtonStyle = (Style)resources["DialogDefaultButtonStyle"],
            CloseButtonStyle = (Style)resources["DialogDefaultButtonStyle"],
            XamlRoot = xamlRoot
        };

        var result = await DialogService.ShowAsync(dialog);

        switch (result)
        {
            case ContentDialogResult.Primary:
                var schedule = new ScheduleDialog { XamlRoot = xamlRoot };
                await DialogService.ShowAsync(schedule);
                break;
            case ContentDialogResult.Secondary:
                App.Settings.DontAskAboutScheduling = true;
                break;
            // ContentDialogResult.None ("Not now") → niks doen, vraagt volgende
            // succesvolle install opnieuw.
        }
    }
}
