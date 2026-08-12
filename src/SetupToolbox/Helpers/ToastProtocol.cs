using System;
using Microsoft.Win32;

namespace SetupToolbox.Helpers;

// URI-protocol voor toast-activatie (v1.0.13).
//
// Waarom niet de COM-activator van Microsoft.Toolkit.Uwp.Notifications: die
// registreert zichzelf keurig (AUMID + CustomActivator + LocalServer32), maar op
// een unpackaged app routeert Windows de klik er niet naartoe zolang er geen Start
// Menu-snelkoppeling met de `System.AppUserModel.ToastActivatorCLSID`-eigenschap
// bestaat. Gemeten gedrag: de toast verdween bij een klik, maar de exe werd niet
// gestart (geen LAUNCH-regel in install.log).
//
// Protocol-activatie omzeilt dat volledig: Windows doet simpelweg een ShellExecute
// op de URI, wat een gewone proces-start is. Werkt identiek in de dev-build en de
// geïnstalleerde build, en vereist geen snelkoppeling.
internal static class ToastProtocol
{
    public const string Scheme = "setuptoolbox";
    public const string OpenUri = Scheme + ":open";

    /// <summary>
    /// Schrijft de URI-handler naar HKCU (geen admin nodig). Idempotent: het
    /// commando wordt alleen weggeschreven als het afwijkt, zodat de dev-build en
    /// de geïnstalleerde build elkaar netjes overnemen i.p.v. elke start te schrijven.
    /// Best-effort — een mislukte registratie mag nooit een toast tegenhouden.
    /// </summary>
    public static void EnsureRegistered()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var command = $"\"{exePath}\" \"%1\"";

            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            if (root == null) return;

            // De lege naam is de (Default)-waarde; "URL Protocol" (zonder data) is
            // wat een sleutel voor Windows tot URI-handler maakt.
            root.SetValue(string.Empty, "URL:Setup Toolbox");
            root.SetValue("URL Protocol", string.Empty);

            using var cmd = root.CreateSubKey(@"shell\open\command");
            if (cmd == null) return;

            if (cmd.GetValue(string.Empty) as string != command)
            {
                cmd.SetValue(string.Empty, command);
                Diagnostics.Log("SetupToolbox_toast.log", $"protocol geregistreerd -> {command}");
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log("SetupToolbox_toast.log",
                $"protocol registratie FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Is dit command-line argument onze activatie-URI?</summary>
    public static bool IsOpenArg(string arg) =>
        arg.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);
}
