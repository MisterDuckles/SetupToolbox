using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace WingetAppDeployer_WinUI.Helpers;

// AppNotificationManager (WinAppSDK 1.4+) werkt voor unpackaged WinUI 3 apps,
// mits we vóór de eerste Show() registreren. Register() schrijft een HKCU-entry
// met AUMID + COM activator zodat het OS de toast kan tonen en (theoretisch)
// de app kan launchen op activate. We gebruiken alleen Show — onze /autoupdate
// run exit direct na de toast.
internal static class ToastHelper
{
    private static bool _registered;

    private static void EnsureRegistered()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            // Registratie kan falen wanneer het systeem notificaties geblokt heeft
            // (Focus Assist policy, group policy). Zwijg en skip — geen kritiek.
        }
    }

    public static void ShowAutoUpdateResult(bool success)
    {
        try
        {
            EnsureRegistered();
            if (!_registered) return;

            var notification = new AppNotificationBuilder()
                .AddText("WingetAppDeployer")
                .AddText(success
                    ? "All apps have been updated."
                    : "Update finished with errors. Open the app for details.")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Toast is best-effort — onder het scheduled task in een non-interactive
            // session kan het OS Show() weigeren. We willen sowieso NIET dat een
            // toast-fail de auto-update flow doet crashen.
        }
    }
}
