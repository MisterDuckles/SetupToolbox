using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WingetAppDeployer.Models;
using WingetAppDeployer.Services;

namespace WingetAppDeployer;

public partial class App : Application
{
    public static GitHubService? GitHubService { get; private set; }
    public static WingetService? WingetService { get; private set; }
    public static SettingsService? SettingsService { get; private set; }
    public static TaskSchedulerService? TaskSchedulerService { get; private set; }

    public static void ApplyTheme(AppTheme theme, bool darkMode = false)
    {
        var mergedDicts = Current.Resources.MergedDictionaries;

        var existingTheme = mergedDicts.FirstOrDefault(d =>
            d.Source?.ToString().Contains("Theme") == true &&
            d.Source?.ToString().Contains("Themes/") == true);
        if (existingTheme != null)
            mergedDicts.Remove(existingTheme);

        var mode = darkMode ? "Dark" : "Light";
        var themeName = theme switch
        {
            AppTheme.Sunset => "Sunset",
            AppTheme.OceanBreeze => "OceanBreeze",
            AppTheme.Aurora => "Aurora",
            _ => "Windows"
        };

        var uri = new Uri($"Themes/{themeName}{mode}.xaml", UriKind.Relative);
        mergedDicts.Add(new ResourceDictionary { Source = uri });

        // Toggle Mica backdrop for Windows theme
        if (Current.MainWindow is MainWindow mainWindow)
        {
            if (theme == AppTheme.Windows)
                mainWindow.ApplyMica(darkMode);
            else
                mainWindow.RemoveMica();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Dev-only: log received args so we can debug launch-arg issues
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wingetappdeployer-args.log");
            System.IO.File.WriteAllText(logPath,
                $"Args count: {e.Args.Length}\n" +
                string.Join("\n", e.Args.Select((a, i) => $"  [{i}]: {a}")));
        }
        catch { }

        base.OnStartup(e);

        // Initialize services
        GitHubService = new GitHubService();
        WingetService = new WingetService();
        SettingsService = new SettingsService();
        TaskSchedulerService = new TaskSchedulerService();

        // Apply saved theme
        var settings = SettingsService.LoadSettings();
        ApplyTheme(settings.Theme, settings.DarkMode);

        // Check for command line arguments
        if (e.Args.Length > 0)
        {
            if (e.Args[0] == "/autoupdate" || e.Args[0] == "--autoupdate")
            {
                // Run auto-update in background
                Task.Run(async () =>
                {
                    await WingetService.UpdateAllApps();
                });
                Shutdown();
                return;
            }

            if (IsSandboxArg(e.Args[0]))
            {
                var sandbox = new Views.Fluent.SandboxWindow();
                MainWindow = sandbox;
                sandbox.Show();
                return;
            }
        }

        // Default: open the regular MainWindow
        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private static bool IsSandboxArg(string arg)
    {
        // Accept multiple forms: /fluentsandbox, --fluentsandbox, fluentsandbox,
        // and also trailing "/fluentsandbox" appended to a path (msys/git-bash path conversion)
        if (string.IsNullOrEmpty(arg)) return false;
        if (arg.Equals("/fluentsandbox", StringComparison.OrdinalIgnoreCase)) return true;
        if (arg.Equals("--fluentsandbox", StringComparison.OrdinalIgnoreCase)) return true;
        if (arg.Equals("fluentsandbox", StringComparison.OrdinalIgnoreCase)) return true;
        if (arg.EndsWith("fluentsandbox", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
