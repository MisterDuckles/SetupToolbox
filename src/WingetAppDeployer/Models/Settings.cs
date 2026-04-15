namespace WingetAppDeployer.Models;

public enum AppTheme
{
    Windows,
    Sunset,
    OceanBreeze,
    Aurora
}

public class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Windows;
    public bool DarkMode { get; set; }
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool ShowWelcomeScreen { get; set; } = true;
    public bool AutoUpdateEnabled { get; set; }
    public string? AutoUpdateSchedule { get; set; }
}
