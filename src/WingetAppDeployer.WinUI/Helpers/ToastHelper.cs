using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace WingetAppDeployer_WinUI.Helpers;

// Toast notificaties voor unpackaged WinUI 3 apps via Microsoft.Toolkit.Uwp.Notifications.
// Onderwater gebruikt dit ToastNotificationManagerCompat dat bij eerste Show()
// automatisch een Start Menu shortcut + AUMID aanmaakt — daardoor accepteert
// het OS de toast zonder dat we zelf een COM activator class hoeven implementeren.
// (WinAppSDK's eigen AppNotificationManager faalt op unpackaged met "Class not registered".)
internal static class ToastHelper
{
    public static void ShowAutoUpdateResult(bool success)
    {
        try
        {
            new ToastContentBuilder()
                .AddText("WingetAppDeployer")
                .AddText(success
                    ? "All apps have been updated."
                    : "Update finished with errors. Open the app for details.")
                .Show();

            Diagnostics.Log("WingetAppDeployer_toast.log", $"Show() OK (success={success})");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("WingetAppDeployer_toast.log", $"Show() FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
