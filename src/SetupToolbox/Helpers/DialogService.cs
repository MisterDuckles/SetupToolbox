using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SetupToolbox.Helpers;

// WinUI 3 staat per thread maar ÉÉN open ContentDialog toe. Een tweede ShowAsync
// gooit COMException "Only a single ContentDialog can be open at any time" — niet
// alleen bij écht geneste dialogs, maar ook bij twee dialogs vlák ná elkaar: zodra
// de eerste ShowAsync-await voltooit ben je terug in je code, maar WinUI heeft de
// popup van die dialog dan nog niet uit de visual tree gehaald, dus de volgende
// ShowAsync botst erop.
//
// Dat is precies de crash uit crash.log/install.log (09:13:10): de post-install
// "Schedule auto-updates?"-prompt vuurde direct na het sluiten van de InstallDialog.
// Vóór v1.0.11 nekte dat de app; sinds het globale vangnet wordt het gelogd, maar de
// prompt verscheen alsnog niet. Deze gate lost de oorzaak op.
//
// Werking: elke dialog die hier doorheen gaat wordt geserialiseerd (één tegelijk),
// en de teardown-race wordt opgevangen — bij de COMException geven we de UI-thread
// een paar ticks om de vorige popup op te ruimen en proberen we opnieuw.
//
// Sinds 2026-08-23 ook het thema-vangnet. Een ContentDialog met alleen een XamlRoot
// leeft in een losse Popup onder de PopupRoot, en die krijgt de thema-walk van een
// Windows-themawissel tijdens runtime niet mee (microsoft-ui-xaml #6577, #8077 —
// open sinds 2022; WinUI Gallery werkt er zelf omheen). Het venster kleurt dus live
// mee, maar elke dialog die je daarna opent staat nog in het thema van app-start:
// een donkere planning-dialog op een lichte Settings-pagina. Daarom pinnen we de
// dialog hier op het live ActualTheme van de root en volgen we dat zolang hij open
// staat. ALLE dialogs horen daarom via deze methode te gaan, niet via dialog.ShowAsync().
internal static class DialogService
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync();
        try
        {
            // XamlRoot.Content is de root van MainWindow — het element dat bij een
            // themawissel aantoonbaar wél mee-kleurt. Fallback op het venster voor
            // het geval een caller de XamlRoot vergat.
            var root = dialog.XamlRoot?.Content as FrameworkElement
                    ?? App.Window?.Content as FrameworkElement;

            TypedEventHandler<FrameworkElement, object>? follow = null;
            if (root != null)
            {
                dialog.RequestedTheme = root.ActualTheme;
                follow = (sender, _) => dialog.RequestedTheme = sender.ActualTheme;
                root.ActualThemeChanged += follow;
            }

            try
            {
                const int maxAttempts = 10;
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await dialog.ShowAsync();
                    }
                    catch (COMException) when (attempt < maxAttempts)
                    {
                        // Vorige dialog nog niet volledig opgeruimd. Geef de UI-thread een
                        // low-priority tick (draait ná pending layout/render) en herprobeer.
                        await YieldToUiAsync(dialog.DispatcherQueue);
                    }
                }
            }
            finally
            {
                if (root != null && follow != null)
                    root.ActualThemeChanged -= follow;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Task YieldToUiAsync(DispatcherQueue? queue)
    {
        var tcs = new TaskCompletionSource();
        if (queue == null || !queue.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.SetResult()))
            tcs.SetResult();
        return tcs.Task;
    }
}
