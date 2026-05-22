using System;
using System.IO;

namespace SetupToolbox.Helpers;

// Centrale gate voor diagnostische / install-logging. Aan/uit via de
// Settings-toggle (App.Settings.ErrorLoggingEnabled). Logs gaan naar
// %LocalAppData%\SetupToolbox\logs — één eigen map, te openen via de
// "Open logmap"-knop in Settings (i.p.v. ruis in %TEMP%).
//
// Load-bearing IPC logs (elevated PS-batch progress + results, schtasks stderr
// capture) lopen NIET via deze gate — die hebben hun eigen lifecycle en zijn
// nodig voor de UI om progress te tonen of errors te reporten.
internal static class Diagnostics
{
    // Runtime-toggle: leest de Settings-flag (default aan). App.Settings is een
    // static singleton, dus geen extra dependency-injection nodig.
    public static bool Enabled => App.Settings.ErrorLoggingEnabled;

    /// <summary>Map met de logbestanden — geopend via de "Open logmap"-knop in Settings.</summary>
    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SetupToolbox", "logs");

    /// <summary>
    /// Append een regel aan een diagnostic-logfile in %TEMP%. No-op wanneer
    /// Diagnostics.Enabled = false (productie default). Gebruik via:
    ///   Diagnostics.Log("SetupToolbox_deepclean.log", $"scan started");
    /// Of via een lokale Action<string>:
    ///   Action<string> log = msg => Diagnostics.Log("filename.log", msg);
    /// </summary>
    public static void Log(string fileName, string message)
    {
        if (!Enabled) return;
        try
        {
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, fileName);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort logging — nooit een failure laten cascaderen naar de UI.
        }
    }
}
