using System;
using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;

namespace WingetAppDeployer_WinUI.Helpers;

// Toast notificaties voor unpackaged WinUI 3 apps via Microsoft.Toolkit.Uwp.Notifications.
// Onderwater gebruikt dit ToastNotificationManagerCompat dat bij eerste Show()
// automatisch een Start Menu shortcut + AUMID aanmaakt — daardoor accepteert
// het OS de toast zonder dat we zelf een COM activator class hoeven implementeren.
// (WinAppSDK's eigen AppNotificationManager faalt op unpackaged met "Class not registered".)
internal static class ToastHelper
{
    private static string LogPath => Path.Combine(Path.GetTempPath(), "WingetAppDeployer_toast.log");

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

            Log($"Show() OK (success={success})");
        }
        catch (Exception ex)
        {
            Log($"Show() FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { /* swallow — logging is best-effort */ }
    }
}
