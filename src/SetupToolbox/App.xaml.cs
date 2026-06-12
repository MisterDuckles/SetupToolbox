using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // When launched by the scheduled task with "/autoupdate", run winget
        // upgrade --all silently, post een toast naar Action Center, en exit —
        // geen window, geen UI behalve de notificatie. Korte sleep na Show()
        // geeft het OS tijd om de toast door te geven voor het proces stopt.
        var cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Length > 1 && IsAutoUpdateArg(cmdArgs[1]))
        {
            _ = Task.Run(async () =>
            {
                var success = false;
                try
                {
                    success = await Winget.UpdateAllAppsAsync();
                }
                finally
                {
                    Helpers.ToastHelper.ShowAutoUpdateResult(success);
                    await Task.Delay(3000);
                    Environment.Exit(0);
                }
            });
            return;
        }

        // Debug switch — toast tonen zonder eerst minuten op winget upgrade --all
        // te wachten. Handig om in dev te verifiëren dat AppNotificationManager
        // registratie + Show effectief landen in Action Center.
        if (cmdArgs.Length > 1 && cmdArgs[1].Equals("/toasttest", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                Helpers.ToastHelper.ShowAutoUpdateResult(true);
                await Task.Delay(3000);
                Environment.Exit(0);
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

    private static bool IsAutoUpdateArg(string arg) =>
        arg.Equals("/autoupdate", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("--autoupdate", StringComparison.OrdinalIgnoreCase)
        || arg.Equals("autoupdate", StringComparison.OrdinalIgnoreCase);
}
