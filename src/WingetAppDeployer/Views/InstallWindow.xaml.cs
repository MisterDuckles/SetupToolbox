using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WingetAppDeployer.Helpers;
using WingetAppDeployer.Models;
using AppModel = WingetAppDeployer.Models.App;

namespace WingetAppDeployer.Views;

public partial class InstallWindow : Window
{
    private readonly List<AppModel> _appsToInstall;
    private readonly Dictionary<string, TextBlock> _appStatusTexts = new();

    public InstallWindow(List<AppModel> appsToInstall)
    {
        InitializeComponent();
        _appsToInstall = appsToInstall;
        Loaded += InstallWindow_Loaded;

        SourceInitialized += (s, e) =>
        {
            var settings = App.SettingsService?.LoadSettings();
            if (settings?.Theme == AppTheme.Windows)
                MicaHelper.SetTitleBarTheme(this, settings.DarkMode);
        };
    }

    private async void InstallWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Create status text for each app
        foreach (var app in _appsToInstall)
        {
            var appPanel = new Border
            {
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10)
            };
            appPanel.SetResourceReference(Border.BackgroundProperty, "SurfaceColor");

            var stackPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = app.Name,
                FontSize = 14,
                FontWeight = FontWeights.Medium
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryColor");

            var statusText = new TextBlock
            {
                Text = "⏳ Waiting...",
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0)
            };
            statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryColor");

            _appStatusTexts[app.WingetId] = statusText;

            stackPanel.Children.Add(nameText);
            stackPanel.Children.Add(statusText);
            appPanel.Child = stackPanel;

            InstallLogPanel.Children.Add(appPanel);
        }

        // Start installation
        await InstallAppsAsync();
    }

    private async Task InstallAppsAsync()
    {
        var progress = new Progress<(int current, int total, string appName, string message)>(update =>
        {
            Dispatcher.Invoke(() =>
            {
                CurrentAppText.Text = $"Installing app {update.current} of {update.total}: {update.appName}";
                OverallProgressBar.Value = (update.current * 100.0) / update.total;

                // Update specific app status
                var app = _appsToInstall.FirstOrDefault(a => a.Name == update.appName);
                if (app != null && _appStatusTexts.TryGetValue(app.WingetId, out var statusText))
                {
                    statusText.Text = update.message;

                    if (update.message.Contains("✓"))
                    {
                        statusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentColor");
                    }
                    else if (update.message.Contains("✗"))
                    {
                        statusText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorColor");
                    }
                }
            });
        });

        var results = await App.WingetService!.InstallAppsAsync(_appsToInstall, progress);

        Dispatcher.Invoke(() =>
        {
            var successCount = results.Count(r => r.Value);
            var failCount = results.Count - successCount;

            CurrentAppText.Text = successCount == results.Count
                ? $"✓ All {successCount} apps installed successfully!"
                : $"Installation complete: {successCount} succeeded, {failCount} failed";

            CloseButton.IsEnabled = true;
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
