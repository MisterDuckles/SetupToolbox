using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Toolkit.Uwp.Notifications;
using SetupToolbox.Services;

namespace SetupToolbox.Helpers;

// Toast notificaties voor unpackaged WinUI 3 apps via Microsoft.Toolkit.Uwp.Notifications.
// Onderwater gebruikt dit ToastNotificationManagerCompat dat bij eerste Show()
// automatisch een Start Menu shortcut + AUMID aanmaakt — daardoor accepteert
// het OS de toast zonder dat we zelf een COM activator class hoeven implementeren.
// (WinAppSDK's eigen AppNotificationManager faalt op unpackaged met "Class not registered".)
//
// v1.0.13: de auto-update-run gebruikt ÉÉN toast die zichzelf overschrijft. Windows
// vervangt een bestaande toast zodra er een nieuwe met dezelfde Tag + Group binnenkomt,
// dus "Zoeken naar updates…" wórdt het resultaat i.p.v. dat er een tweede melding
// naast komt. Bij een dagelijkse run blijft het Action Center daardoor schoon.
internal static class ToastHelper
{
    private const string AutoUpdateTag = "autoupdate";
    private const string ScheduleTag = "schedule";
    private const string ToastGroup = "SetupToolbox";

    // Toasts tonen maar een paar regels — een run van 30 apps zou de melding
    // onleesbaar maken. Noem de eerste paar bij naam en tel de rest op.
    private const int MaxNames = 5;

    public static void ShowAutoUpdateSearching() =>
        Show(AutoUpdateTag, b => b
            .AddText("Setup Toolbox")
            .AddText("Zoeken naar updates…"));

    public static void ShowAutoUpdateResult(AutoUpdateResult result) =>
        Show(AutoUpdateTag, b => b
            .AddText("Setup Toolbox")
            .AddText(BuildResultText(result)));

    public static void ShowAutoUpdateFailed(string reason) =>
        Show(AutoUpdateTag, b => b
            .AddText("Setup Toolbox")
            .AddText($"Bijwerken is mislukt. {reason}"));

    public static void ShowScheduleEnabled(string scheduleDescription) =>
        Show(ScheduleTag, b => b
            .AddText("Automatische updates staan ingeschakeld")
            .AddText(scheduleDescription));

    private static string BuildResultText(AutoUpdateResult result)
    {
        if (result.HasListError)
            return $"Kon niet naar updates zoeken. {result.ListError}";

        if (result.NothingToDo)
            return "Alles is up-to-date.";

        var parts = new List<string>();

        if (result.Updated.Count > 0)
            parts.Add(result.Updated.Count == 1
                ? $"{result.Updated[0]} is bijgewerkt."
                : $"{Summarize(result.Updated)} zijn bijgewerkt.");

        // Bij één mislukking noemen we de reden erbij — dat is het geval waar de
        // reden nog leesbaar in een toast past. Bij meerdere alleen de namen; de
        // details staan in install.log en de toast opent bij klik de app.
        if (result.Failed.Count == 1)
            parts.Add($"{result.Failed[0].Name} is mislukt — {result.Failed[0].Reason}");
        else if (result.Failed.Count > 1)
            parts.Add($"Mislukt: {Summarize(result.Failed.Select(f => f.Name).ToList())}.");

        return string.Join(" ", parts);
    }

    private static string Summarize(IReadOnlyList<string> names)
    {
        if (names.Count <= MaxNames) return JoinDutch(names);
        return JoinDutch(names.Take(MaxNames).ToList()) + $" en {names.Count - MaxNames} andere";
    }

    // "Vivaldi, VLC en Notion" — Nederlandse opsomming met "en" voor het laatste item.
    private static string JoinDutch(IReadOnlyList<string> names) =>
        names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " en " + names[^1]
        };

    // Eén plek voor de gate, de Tag/Group, de klik-actie en de logging, zodat elke
    // toast hetzelfde gedrag heeft. Best-effort: een geweigerde notificatie mag de
    // auto-update-flow nooit omvergooien.
    private static void Show(string tag, Action<ToastContentBuilder> build)
    {
        if (!App.Settings.UpdateNotificationsEnabled)
        {
            Diagnostics.Log("SetupToolbox_toast.log", $"skipped (notificaties uit) tag={tag}");
            return;
        }

        try
        {
            // Klik-actie via ons eigen URI-protocol i.p.v. de COM-activator — zie
            // ToastProtocol voor waarom die op unpackaged apps niet aanslaat.
            ToastProtocol.EnsureRegistered();

            var builder = new ToastContentBuilder();
            build(builder);
            builder.SetProtocolActivation(new Uri(ToastProtocol.OpenUri));

            builder.Show(toast =>
            {
                // Zelfde Tag + Group => Windows vervangt de vorige toast in plaats
                // van er een tweede naast te zetten.
                toast.Tag = tag;
                toast.Group = ToastGroup;
            });

            Diagnostics.Log("SetupToolbox_toast.log", $"Show() OK tag={tag}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SetupToolbox_toast.log", $"Show() FAILED tag={tag}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
