using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SetupToolbox.Helpers;

// First-time vraag aan de user: wil je apps parallel installeren voor extra
// snelheid? Toggle bestaat in Settings maar de meeste users navigeren daar
// nooit heen — deze prompt zorgt dat ze 1x bewust kiezen. Resultaat wordt
// opgeslagen (ParallelInstalls + ParallelInstallsAsked) en de prompt komt
// nooit meer terug. User kan in Settings altijd nog wisselen.
internal static class ParallelInstallsPrompt
{
    public static async Task MaybeShowAsync(XamlRoot xamlRoot)
    {
        if (App.Settings.ParallelInstallsAsked) return;

        var resources = Application.Current.Resources;
        var dialog = new ContentDialog
        {
            Title = "Install apps in parallel?",
            Content = "Run up to 2 installs at the same time — roughly halves install time. Some installers (especially MSI-based) may fail when run concurrently. You can change this any time in Settings.",
            PrimaryButtonText = "Yes, install faster",
            CloseButtonText = "No, one at a time",
            DefaultButton = ContentDialogButton.Primary,
            CornerRadius = new CornerRadius(8),
            PrimaryButtonStyle = (Style)resources["DialogPrimaryButtonStyle"],
            CloseButtonStyle = (Style)resources["DialogDefaultButtonStyle"],
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();

        // Beide knoppen tellen als "answered" zodat de prompt niet terugkomt.
        // Yes → 2 parallel (default speed-up), No → 1 (sequentieel). Fijnafstemming
        // (1-4) kan altijd nog via Settings.
        App.Settings.MaxParallelInstalls = result == ContentDialogResult.Primary ? 2 : 1;
        App.Settings.ParallelInstallsAsked = true;
    }
}
