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
            AppTheme.Windows => "Windows",
            AppTheme.Sunset => "Sunset",
            AppTheme.OceanBreeze => "OceanBreeze",
            AppTheme.Aurora => "Aurora",
            _ => "Google"
        };

        var uri = new Uri($"Themes/{themeName}{mode}.xaml", UriKind.Relative);
        mergedDicts.Add(new ResourceDictionary { Source = uri });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
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
            if (e.Args[0] == "/autoupdate")
            {
                // Run auto-update in background
                Task.Run(async () =>
                {
                    await WingetService.UpdateAllApps();
                });
                Shutdown();
                return;
            }
        }
    }
}
