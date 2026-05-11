using System;
using System.IO;

namespace WingetAppDeployer_WinUI.Helpers;

// Centrale gate voor diagnostic-logging dat in productie geen disk-IO doet.
// Load-bearing IPC logs (elevated PS-batch progress + results, schtasks stderr
// capture) lopen NIET via deze gate — die hebben hun eigen lifecycle en zijn
// nodig voor de UI om progress te tonen of errors te reporten.
//
// Voor dev: flip Enabled naar true om de scan-diagnostics weer naar
// %TEMP%\WingetAppDeployer_*.log te laten schrijven. In productie blijft 'ie
// false zodat we geen ruis op de user z'n temp-folder achterlaten.
internal static class Diagnostics
{
    // static readonly i.p.v. const zodat de compiler de body niet als
    // unreachable code flagt (CS0162) wanneer Enabled=false. Runtime overhead
    // is verwaarloosbaar (één bool-compare per call).
    public static readonly bool Enabled = false;

    /// <summary>
    /// Append een regel aan een diagnostic-logfile in %TEMP%. No-op wanneer
    /// Diagnostics.Enabled = false (productie default). Gebruik via:
    ///   Diagnostics.Log("WingetAppDeployer_deepclean.log", $"scan started");
    /// Of via een lokale Action<string>:
    ///   Action<string> log = msg => Diagnostics.Log("filename.log", msg);
    /// </summary>
    public static void Log(string fileName, string message)
    {
        if (!Enabled) return;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), fileName);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort logging — nooit een failure laten cascaderen naar de UI.
        }
    }
}
