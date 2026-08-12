using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

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
internal static class DialogService
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync();
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
