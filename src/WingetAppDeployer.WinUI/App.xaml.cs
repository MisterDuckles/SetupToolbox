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
using WingetAppDeployer_WinUI.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WingetAppDeployer_WinUI;

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

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // When launched by the scheduled task with "/autoupdate", run winget
        // upgrade --all silently and exit — no window, no UI.
        var cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Length > 1 && IsAutoUpdateArg(cmdArgs[1]))
        {
            _ = Task.Run(async () =>
            {
                try { await Winget.UpdateAllAppsAsync(); }
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
