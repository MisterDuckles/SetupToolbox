using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Toolkit.Uwp.Notifications;
using SetupToolbox.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SetupToolbox;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    // Exposed so Pages can tweak window-level things (e.g. SystemBackdrop swap
    // from a sub-view). Set during OnLaunched.
    public static MainWindow? Window { get; private set; }

    // App-wide singleton services. Pages reach them via App.Database / App.Winget.
    public static AppDatabaseService Database { get; } = new();
    public static WingetService Winget { get; } = new();
    public static TaskSchedulerService TaskScheduler { get; } = new();
    public static SettingsService Settings { get; } = new();
    // Vertalingen (v1.2.2). MOET ná Settings gedeclareerd blijven: de service
    // leest de taalkeuze uit Settings, en static field initializers draaien in
    // declaratie-volgorde.
    public static LocalizationService Loc { get; } = new();
    public static SelectionImportExportService SelectionIO { get; } = new();
    public static BloatwareService Bloatware { get; } = new();
    public static InstalledAppsService InstalledApps { get; } = new();
    public static MixedSourceUninstaller MixedUninstaller { get; } = new();
    public static LeftoverScannerService LeftoverScanner { get; } = new();
    public static DeepCleanService DeepClean { get; } = new();
    public static TweakService Tweaks { get; } = new();
    public static SnapshotService Snapshots { get; } = new();
    public static RestorePointService RestorePoint { get; } = new();
    public static TweakPendingService TweakPending { get; } = new();
    // Profiel-bouwer (v0.9.20). ProfileSelection is een aparte selectie-store
    // (los van TweakPending om mode-bleed te voorkomen); TweakProfileIO doet de
    // file-IO. ProfileMode is de globale flag die de Tweaks-tab in clean-slate
    // selectie-modus zet — zie TweaksPage / MainWindow.EnterTweakProfileMode.
    public static TweakPendingService ProfileSelection { get; } = new();
    public static TweakProfileService TweakProfileIO { get; } = new();
    // Gebundelde config-backup (v1.2.9): apps + tweaks + voorkeuren in één bestand.
    // Leest ook de twee losse formats, zodat één Importeren-knop alles aankan.
    public static ConfigBackupService ConfigIO { get; } = new();
    public static bool ProfileMode { get; set; }
    // Self-update (v0.10.1): GitHub release-check + installer-download/-launch.
    public static GitHubService GitHub { get; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();

        // Globale vangnet: zonder dit kill een onbehandelde exception op de UI-thread
        // de héle app stilzwijgend (geen window, geen log). Dat zagen we bij een
        // install die wél slaagde maar waar een transiente WinUI binding-/layout-fout
        // in een Progress-callback de app deed verdwijnen vóór de runner z'n EXIT-regel
        // kon loggen. We loggen de volledige stack en houden de app in leven.
        UnhandledException += OnUnhandledException;

        // Houdt de toolkit-registratie in stand (AUMID + weergavenaam + icoon van de
        // toast). Onze toasts activeren via het URI-protocol, niet via deze COM-route
        // — zie Helpers/ToastProtocol — dus in de praktijk vuurt deze handler niet.
        // Blijft staan als vangnet voor een toast die ooit zónder protocol-activatie
        // verstuurd wordt.
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            // Headless modes (/autoupdate, --install-runner) hebben geen window —
            // dan valt er niets te activeren.
            var window = Window;
            if (window == null) return;

            // OnActivated komt van een achtergrond-thread; Activate() moet op de UI-thread.
            window.DispatcherQueue.TryEnqueue(() => window.Activate());
        }
        catch (Exception ex)
        {
            Helpers.Diagnostics.Log("SetupToolbox_toast.log",
                $"OnActivated FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            Helpers.Diagnostics.Log("crash.log",
                $"UNHANDLED {e.Message}{Environment.NewLine}{e.Exception}");
            Helpers.Diagnostics.Log("install.log",
                $"UNHANDLED-UI {e.Exception?.GetType().Name}: {e.Message}");
        }
        catch { /* logging mag nooit zelf crashen */ }

        // Handled=true voorkomt de fatale app-terminatie voor herstelbare managed
        // exceptions (bv. een binding-update tijdens layout). Niet elke WinUI-fout is
        // herstelbaar, maar voor de progress-callback-categorie wel — en we hebben nu
        // sowieso de stack in crash.log voor diagnose.
        e.Handled = true;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Gestart door de scheduled task met "/autoupdate": draai de winget-upgrades
        // stil, post de toasts naar Action Center, en exit — geen window, geen UI
        // behalve de notificatie. Korte sleep na Show() geeft het OS tijd om de toast
        // door te geven voordat het proces stopt.
        //
        // Klik op een toast terwijl de app niet draait → Windows start deze exe met
        // "-ToastActivated". Dat matcht geen enkele tak hieronder en valt dus door
        // naar de normale window-creatie: precies het gewenste "open de app"-gedrag.
        var cmdArgs = Environment.GetCommandLineArgs();

        // Launch-trace: laat in de log zien met welke argumenten dit proces start.
        // Onmisbaar bij het diagnosticeren van toast-activatie (start Windows de exe
        // überhaupt bij een klik?) en van de headless takken hieronder.
        Helpers.Diagnostics.Log("install.log",
            $"LAUNCH args=[{string.Join(" ", cmdArgs.Skip(1))}]");

        // Klik op een toast start ons via "setuptoolbox:open" (v1.0.13). Draait er al
        // een instantie met een venster, breng die dan naar voren en stop — anders
        // krijgt user een tweede venster. Bewust een gerichte check alleen voor deze
        // tak: de app doet elders géén AppInstance-redirectie, omdat de ge-eleveerde
        // install-runner zichzelf juist als tweede proces moet kunnen starten.
        if (cmdArgs.Length > 1 && Helpers.ToastProtocol.IsOpenArg(cmdArgs[1]) && TryFocusExistingInstance())
        {
            Environment.Exit(0);
            return;
        }

        if (cmdArgs.Length > 1 && IsAutoUpdateArg(cmdArgs[1]))
        {
            _ = Task.Run(async () =>
            {
                // v1.0.13: "Zoeken naar updates…" eerst, daarna vervangt het resultaat
                // diezelfde toast (gelijke Tag/Group) — één melding per run.
                Helpers.ToastHelper.ShowAutoUpdateSearching();
                try
                {
                    var result = await Winget.UpdateAllAppsAsync();
                    Helpers.ToastHelper.ShowAutoUpdateResult(result);
                }
                catch (Exception ex)
                {
                    // UpdateAllAppsAsync vangt per-app fouten zelf al af; dit is het
                    // vangnet zodat er sowieso een melding komt i.p.v. een stille run.
                    Helpers.Diagnostics.Log("install.log", $"AUTOUPDATE-EX {ex.GetType().Name}: {ex.Message}");
                    Helpers.ToastHelper.ShowAutoUpdateFailed(ex.Message);
                }
                finally
                {
                    await Task.Delay(3000);
                    Environment.Exit(0);
                }
            });
            return;
        }

        // Debug switch — de volledige toast-flow tonen zonder eerst minuten op winget
        // te wachten. Verifieert ook dat de tweede toast de eerste vervángt i.p.v.
        // er een tweede melding naast te zetten.
        if (cmdArgs.Length > 1 && cmdArgs[1].Equals("/toasttest", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                Helpers.ToastHelper.ShowAutoUpdateSearching();
                await Task.Delay(2500);
                // Nepdata in de brontaal (Engels). De échte reden-teksten komen uit
                // WingetService en zijn nog niet vertaald — dat is v1.2.3.
                Helpers.ToastHelper.ShowAutoUpdateResult(new AutoUpdateResult(
                    new[] { "Vivaldi", "VLC media player" },
                    new[] { new AutoUpdateFailure("Notion", "The installer failed. Try again.") },
                    null));
                await Task.Delay(3000);
                Environment.Exit(0);
            });
            return;
        }

        // Debug switch — alléén de inventarisatie-fase van de auto-update draaien en
        // tonen wát er bijgewerkt zóu worden. Installeert niets. Bedoeld om de parser
        // en de toast-tekst te verifiëren zonder een echte run van tientallen apps
        // los te laten op de machine.
        if (cmdArgs.Length > 1 && cmdArgs[1].Equals("/updatecheck", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                Helpers.ToastHelper.ShowAutoUpdateSearching();
                try
                {
                    var pending = await Winget.GetUpgradableAppsAsync();
                    foreach (var entry in pending)
                        Helpers.Diagnostics.Log("install.log",
                            $"UPDATECHECK {entry.Id} name=\"{entry.Name}\" source={entry.Source}");
                    Helpers.Diagnostics.Log("install.log", $"UPDATECHECK total={pending.Count}");

                    // Zelfde tekst-opbouw als een echte run, met de gevonden apps als
                    // "bijgewerkt" — zo zie je exact hoe de melding eruit gaat zien.
                    var names = pending
                        .Select(e => string.IsNullOrWhiteSpace(e.Name) ? e.Id : e.Name)
                        .ToList();
                    Helpers.ToastHelper.ShowAutoUpdateResult(
                        new AutoUpdateResult(names, Array.Empty<AutoUpdateFailure>(), null));
                }
                catch (Exception ex)
                {
                    Helpers.Diagnostics.Log("install.log", $"UPDATECHECK-EX {ex.GetType().Name}: {ex.Message}");
                    Helpers.ToastHelper.ShowAutoUpdateFailed(ex.Message);
                }
                finally
                {
                    await Task.Delay(3000);
                    Environment.Exit(0);
                }
            });
            return;
        }

        // Ge-eleveerde install-runner (v1.0.5): wanneer de app relaunched wordt met
        // "--install-runner <jobPath>" draaien we de winget-batch headless (ÉÉN UAC
        // voor de hele batch) en streamen we de voortgang naar een JSONL-bestand dat
        // het ouder-proces tailt. Geen window — net als de /autoupdate-tak.
        if (cmdArgs.Length > 2 && ElevatedInstallRunner.IsRunnerArg(cmdArgs[1]))
        {
            _ = Task.Run(async () =>
            {
                try { await ElevatedInstallRunner.RunChildAsync(cmdArgs[2]); }
                finally { Environment.Exit(0); }
            });
            return;
        }

        Window = new MainWindow();
        Window.Activate();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    // Zoekt een al draaiende instantie met een venster en brengt die naar voren.
    // Processen zonder MainWindowHandle worden overgeslagen: dat zijn de headless
    // takken (/autoupdate, --install-runner) of een instantie die z'n venster nog
    // aan het opbouwen is. False => caller opent gewoon een nieuw venster.
    private static bool TryFocusExistingInstance()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(me.ProcessName))
            {
                using (other)
                {
                    if (other.Id == me.Id) continue;

                    var hwnd = other.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;

                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort: bij twijfel liever een tweede venster dan geen venster.
            Helpers.Diagnostics.Log("SetupToolbox_toast.log",
                $"focus bestaande instantie FAILED: {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    private static bool IsAutoUpdateArg(string arg) =>
        arg.Equals("/autoupdate", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--autoupdate", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("autoupdate", StringComparison.OrdinalIgnoreCase);
}
