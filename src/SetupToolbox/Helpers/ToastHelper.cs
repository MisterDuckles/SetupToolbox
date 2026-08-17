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
        Show(AutoUpdateTag, App.Loc.S("toast.appName"), App.Loc.S("toast.searching"));

    public static void ShowAutoUpdateResult(AutoUpdateResult result) =>
        Show(AutoUpdateTag, App.Loc.S("toast.appName"), BuildResultText(result));

    public static void ShowAutoUpdateFailed(string reason) =>
        Show(AutoUpdateTag, App.Loc.S("toast.appName"), App.Loc.S("toast.updateFailed", reason));

    public static void ShowScheduleEnabled(string scheduleDescription) =>
        Show(ScheduleTag, App.Loc.S("toast.schedule.title"), scheduleDescription);

    private static string BuildResultText(AutoUpdateResult result)
    {
        if (result.HasListError)
            return App.Loc.S("toast.listFailed", result.ListError);

        if (result.NothingToDo)
            return App.Loc.S("toast.upToDate");

        var parts = new List<string>();

        if (result.Updated.Count > 0)
            parts.Add(result.Updated.Count == 1
                ? App.Loc.S("toast.updatedOne", result.Updated[0])
                : App.Loc.S("toast.updatedMany", Summarize(result.Updated)));

        // Bij één mislukking noemen we de reden erbij — dat is het geval waar de
        // reden nog leesbaar in een toast past. Bij meerdere alleen de namen; de
        // details staan in install.log en de toast opent bij klik de app.
        if (result.Failed.Count == 1)
            parts.Add(App.Loc.S("toast.failedOne", result.Failed[0].Name, result.Failed[0].Reason));
        else if (result.Failed.Count > 1)
            parts.Add(App.Loc.S("toast.failedMany", Summarize(result.Failed.Select(f => f.Name).ToList())));

        return string.Join(" ", parts);
    }

    // De opsomming zelf ("A, B en C" / "A, B and C") is grammatica en zit daarom
    // in LocalizationService.JoinList, niet in een vertaalde losse string.
    private static string Summarize(IReadOnlyList<string> names)
    {
        if (names.Count <= MaxNames) return App.Loc.JoinList(names);
        return App.Loc.S("toast.andOthers",
            App.Loc.JoinList(names.Take(MaxNames).ToList()),
            names.Count - MaxNames);
    }

    // Eén plek voor de gate, de Tag/Group, de klik-actie en de logging, zodat elke
    // toast hetzelfde gedrag heeft. Best-effort: een geweigerde notificatie mag de
    // auto-update-flow nooit omvergooien.
    private static void Show(string tag, string title, string body)
    {
        if (!App.Settings.UpdateNotificationsEnabled)
        {
            Diagnostics.Log("SetupToolbox_toast.log", $"skipped (notifications off) tag={tag}");
            return;
        }

        try
        {
            // Klik-actie via ons eigen URI-protocol i.p.v. de COM-activator — zie
            // ToastProtocol voor waarom die op unpackaged apps niet aanslaat.
            ToastProtocol.EnsureRegistered();

            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body);
            builder.SetProtocolActivation(new Uri(ToastProtocol.OpenUri));

            builder.Show(toast =>
            {
                // Zelfde Tag + Group => Windows vervangt de vorige toast in plaats
                // van er een tweede naast te zetten.
                toast.Tag = tag;
                toast.Group = ToastGroup;
            });

            // Ook de tekst loggen, niet alleen "OK". /toasttest bestaat om de
            // toast-tekst te kunnen verifiëren, maar zonder dit moest je daarvoor
            // het scherm in de gaten houden — en met twee talen erbij is precies
            // die tekst wat je wilt narekenen.
            Diagnostics.Log("SetupToolbox_toast.log",
                $"Show() OK tag={tag} lang={App.Loc.Current} text=\"{body}\"");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SetupToolbox_toast.log", $"Show() FAILED tag={tag}: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
