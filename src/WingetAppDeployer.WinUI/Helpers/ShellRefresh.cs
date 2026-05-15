using System;
using System.Runtime.InteropServices;

namespace WingetAppDeployer_WinUI.Helpers;

// Broadcast WM_SETTINGCHANGE op dezelfde manier als Windows Settings dat doet
// voor taskbar / personalization / policy tweaks. Per research mei 2026
// (zie NEXT-STEPS v0.9.2 entry): Settings gebruikt geen private API — gewoon
// registry-write + WM_SETTINGCHANGE broadcast met meerdere bekende lParams.
// Voor ~3 tweaks (Show seconds in clock, Never combine buttons, Classic
// context menu CLSID) is een explorer-restart genuinely nodig omdat de shell
// die waardes alleen bij startup leest — TweaksPage detecteert dat via
// tweak.Restart == RestartRequirement.ExplorerRestart en biedt de manual
// "Restart Explorer" knop top-right aan.
internal static class ShellRefresh
{
    private const uint WM_SETTINGCHANGE = 0x001A;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    // SMTO_ABORTIFHUNG: skip hung windows direct (geen full-timeout wait).
    // SMTO_NOTIMEOUTIFNOTHUNG: well-behaved windows krijgen unlimited tijd
    // (antwoorden in microseconds). Identiek aan wat SHSetSettings intern doet.
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_NOTIMEOUTIFNOTHUNG = 0x0008;
    private const uint SMTO_FLAGS = SMTO_ABORTIFHUNG | SMTO_NOTIMEOUTIFNOTHUNG;

    // SHChangeNotify constants — voor App Paths / association / icon-cache tweaks.
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, UIntPtr wParam, string? lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>
    /// Broadcast WM_SETTINGCHANGE met alle lParam-strings waar Windows shell-
    /// componenten op filteren. Op Win11 22H2+ pakt de taskbar live op via
    /// "TraySettings" voor: TaskbarAl / TaskbarMn / ShowTaskViewButton /
    /// SearchboxTaskbarMode. NULL eerste voor legacy listeners die alleen op
    /// NULL filteren. Total worst-case ~50ms door SMTO_ABORTIFHUNG.
    /// </summary>
    public static void NotifySettingsChanged()
    {
        // Volgorde: NULL eerst (catch-all voor legacy), dan specifieke categorieën.
        // Elke broadcast bounded door SMTO_ABORTIFHUNG zodat een enkele frozen
        // window ons nooit kan blokkeren.
        string?[] lParams =
        {
            null,                          // empty — legacy components filteren alleen hierop
            "TraySettings",                // Explorer CTray (taskbar Advanced keys)
            "Policy",                      // PolicyManager — HKLM/HKCU Policies paden
            "ImmersiveColorSet",           // DWM accent / titlebar tint
            "WindowsThemeElement",         // Light/dark + UI elements
            "ShellState",                  // Hidden files, super-hidden, file extensions
            "Environment",                 // Env vars (cheap, included voor completeness)
            "WindowsSearchSettingChanged", // Nieuw in 24H2/25H2 — SearchHost / start menu search
            "SearchSettingsChanged",       // Alias/complement van WindowsSearchSettingChanged
        };

        foreach (var lp in lParams)
        {
            try
            {
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE,
                    UIntPtr.Zero, lp, SMTO_FLAGS, 100, out _);
            }
            catch
            {
                // Best-effort. Een failed broadcast mag tweak-apply niet kapot maken.
            }
        }
    }

    /// <summary>
    /// Notify de shell dat file associations / App Paths / icon overlays
    /// veranderden. Te gebruiken na tweaks die HKCR, App Paths, of shell
    /// extensions raken. Cheap — Explorer negeert 'm als er niets relevants
    /// veranderde. Gebruikt voor v0.9.1 ClassicContextMenu CLSID-tweak.
    /// </summary>
    public static void NotifyAssociationsChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Kill SearchHost.exe en StartMenuExperienceHost.exe — Windows respawnt
    /// beide processen automatisch. Triggert een full taskbar-rebind: de
    /// Win11 25H2 XAML-taskbar negeert WM_SETTINGCHANGE voor de meeste
    /// taskbar-keys (TaskbarAl / TaskbarDa / SearchboxTaskbarMode / etc.) en
    /// pickt nieuwe waardes pas op wanneer een gehoste shell-component
    /// respawnt — dan herleest Taskbar.View.dll via een interne IPC-call
    /// (twinui.pcshell.dll ITrayUI) de complete config uit registry.
    ///
    /// Settings.exe doet hetzelfde via een private WinRT API in
    /// `twinui.appcore.dll` (`TaskbarSettingsHelper`) die niet redistributable
    /// is voor third-party apps. Process-restart is de pragmatische alternatief
    /// dat tools als winutil / Winaero ook gebruiken.
    ///
    /// Cost: ~1s Win+S offline (SearchHost respawnt), ~300ms Start menu sluit
    /// (StartMenuExperienceHost respawnt on-demand bij eerste klik). Geen
    /// visible flicker zoals een full explorer-restart.
    /// </summary>
    public static void RestartSearchHost()
    {
        var names = new[] { "SearchHost", "StartMenuExperienceHost" };
        foreach (var name in names)
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    try { p.Kill(); } catch { /* best-effort */ }
                }
            }
            catch { /* permissions / no-process — fine */ }
        }
    }

    /// <summary>
    /// Kill explorer.exe — Windows respawnt 'm automatisch via de Shell Watchdog
    /// (UserInit-mechanisme in winlogon.exe). Voor tweaks waarvan de waarde
    /// gecached wordt bij shell-startup (TaskbarAl / Never combine / Show
    /// seconds in clock / Classic context menu) is dit de enige manier om
    /// live-effect te krijgen.
    ///
    /// BELANGRIJK: we doen GEEN expliciete Process.Start("explorer.exe") als
    /// fallback. Dat zou een File Explorer venster openen (naar Documenten /
    /// Home) want manueel-gestarte explorer.exe gedraagt zich anders dan de
    /// shell-watchdog-spawn. We vertrouwen op de Watchdog die op alle moderne
    /// Win11-installs binnen ~1s reageert. Als 't ooit faalt: user opent Task
    /// Manager (Ctrl+Shift+Esc) en doet Run new task → explorer.
    /// </summary>
    public static void RestartExplorerSilent()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { /* best-effort */ }
            }
        }
        catch
        {
            // best-effort
        }
    }
}
