using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WingetAppDeployer.Models;
using AppModel = WingetAppDeployer.Models.App;

namespace WingetAppDeployer.Services;

public class WingetService
{
    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;

    /// <summary>
    /// Checks if winget is installed and available
    /// </summary>
    public async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var result = await RunWingetCommandAsync("--version");
            return result.exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if an app is already installed
    /// </summary>
    public async Task<bool> IsAppInstalledAsync(string wingetId)
    {
        try
        {
            var result = await RunWingetCommandAsync($"list --id {wingetId} --exact");
            return result.exitCode == 0 && result.output.Contains(wingetId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets all installed app IDs in a single batch call
    /// </summary>
    public async Task<HashSet<string>> GetInstalledAppIdsAsync()
    {
        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = await RunWingetCommandAsync("list --accept-source-agreements");
            if (result.exitCode == 0)
            {
                var lines = result.output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                // Skip header lines (first 2 lines are header + separator)
                foreach (var line in lines.Skip(2))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        installedIds.Add(parts[1]); // Second column is the ID
                    }
                }
            }
        }
        catch
        {
            // Return empty set on failure
        }
        return installedIds;
    }

    /// <summary>
    /// Installs an app using winget
    /// </summary>
    public async Task<(bool success, string output)> InstallAppAsync(string wingetId, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Installing {wingetId}...");

            var result = await RunWingetCommandAsync(
                $"install --id {wingetId} --exact --silent --accept-source-agreements --accept-package-agreements",
                (output) => progress?.Report(output)
            );

            if (result.exitCode == 0)
            {
                progress?.Report($"✓ Successfully installed {wingetId}");
                return (true, result.output);
            }
            else
            {
                var friendlyError = GetFriendlyErrorMessage(result.error, result.output, wingetId);
                progress?.Report($"✗ {friendlyError}");
                return (false, result.error);
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"✗ Error installing {wingetId}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    private static string GetFriendlyErrorMessage(string error, string output, string wingetId)
    {
        var combined = error + output;
        if (combined.Contains("No applicable installer", StringComparison.OrdinalIgnoreCase))
            return $"{wingetId}: No compatible installer found for this system.";
        if (combined.Contains("already installed", StringComparison.OrdinalIgnoreCase))
            return $"{wingetId}: This app is already installed.";
        if (combined.Contains("No package found", StringComparison.OrdinalIgnoreCase))
            return $"{wingetId}: Package not found in winget repository.";
        if (combined.Contains("installer hash does not match", StringComparison.OrdinalIgnoreCase))
            return $"{wingetId}: Download verification failed. Try again later.";
        if (combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            return $"{wingetId}: Access denied. Try running as administrator.";
        if (combined.Contains("0x80070005", StringComparison.OrdinalIgnoreCase))
            return $"{wingetId}: Permission error. Try running as administrator.";
        return $"Failed to install {wingetId}. Check your internet connection and try again.";
    }

    /// <summary>
    /// Installs multiple apps sequentially
    /// </summary>
    public async Task<Dictionary<string, bool>> InstallAppsAsync(
        List<AppModel> apps,
        IProgress<(int current, int total, string appName, string message)>? progress = null)
    {
        var results = new Dictionary<string, bool>();
        var current = 0;
        var total = apps.Count;

        foreach (var app in apps)
        {
            current++;
            progress?.Report((current, total, app.Name, $"Installing {app.Name}..."));

            var localProgress = new Progress<string>(msg =>
                progress?.Report((current, total, app.Name, msg)));

            var (success, _) = await InstallAppAsync(app.WingetId, localProgress);
            results[app.WingetId] = success;

            // Small delay between installs
            await Task.Delay(500);
        }

        return results;
    }

    /// <summary>
    /// Updates all installed apps
    /// </summary>
    public async Task<bool> UpdateAllApps()
    {
        try
        {
            var result = await RunWingetCommandAsync("upgrade --all --silent --accept-source-agreements --accept-package-agreements");
            return result.exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets list of apps with available updates
    /// </summary>
    public async Task<List<string>> GetAvailableUpdatesAsync()
    {
        try
        {
            var result = await RunWingetCommandAsync("upgrade");
            var updates = new List<string>();

            if (result.exitCode == 0)
            {
                var lines = result.output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    // Parse winget upgrade output
                    // Format: Name  Id  Version  Available
                    if (line.Contains("Available"))
                        continue; // Skip header

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        updates.Add(parts[1]); // Add ID
                    }
                }
            }

            return updates;
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<(int exitCode, string output, string error)> RunWingetCommandAsync(
        string arguments,
        Action<string>? outputCallback = null)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "winget.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                outputCallback?.Invoke(e.Data);
                OutputReceived?.Invoke(this, e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
                ErrorReceived?.Invoke(this, e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}
