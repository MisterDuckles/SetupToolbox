using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Services;

// Minimal port of the WPF WingetService focused on the install flow. Uses the
// winget.exe CLI, streams stdout as progress, no shared code with the WPF app.
public sealed class WingetService
{
    public async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var (exitCode, _, _) = await RunWingetCommandAsync("--version");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool success, string message)> InstallAppAsync(string wingetId, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Installing {wingetId}...");

            var (exitCode, output, error) = await RunWingetCommandAsync(
                $"install --id {wingetId} --exact --silent --accept-source-agreements --accept-package-agreements",
                line => progress?.Report(line));

            if (exitCode == 0)
            {
                progress?.Report("Installed");
                return (true, "Installed");
            }

            var friendly = FriendlyError(error, output, wingetId);
            progress?.Report(friendly);
            return (false, friendly);
        }
        catch (Exception ex)
        {
            progress?.Report(ex.Message);
            return (false, ex.Message);
        }
    }

    public async Task<Dictionary<string, (bool success, string message)>> InstallAppsAsync(
        IReadOnlyList<AppModel> apps,
        IProgress<InstallProgress>? overall = null)
    {
        var results = new Dictionary<string, (bool, string)>();
        var total = apps.Count;

        for (var i = 0; i < total; i++)
        {
            var app = apps[i];
            var index = i + 1;

            overall?.Report(new InstallProgress(index, total, app, InstallPhase.Starting, $"Starting {app.Name}"));

            var perApp = new Progress<string>(msg =>
                overall?.Report(new InstallProgress(index, total, app, InstallPhase.Running, msg)));

            var (success, message) = await InstallAppAsync(app.WingetId, perApp);
            results[app.WingetId] = (success, message);

            overall?.Report(new InstallProgress(
                index, total, app,
                success ? InstallPhase.Success : InstallPhase.Failed,
                message));

            await Task.Delay(200);
        }

        return results;
    }

    private static string FriendlyError(string error, string output, string wingetId)
    {
        var combined = error + output;
        if (combined.Contains("No applicable installer", StringComparison.OrdinalIgnoreCase))
            return "No compatible installer for this system.";
        if (combined.Contains("already installed", StringComparison.OrdinalIgnoreCase))
            return "Already installed.";
        if (combined.Contains("No package found", StringComparison.OrdinalIgnoreCase))
            return "Package not found.";
        if (combined.Contains("installer hash does not match", StringComparison.OrdinalIgnoreCase))
            return "Download verification failed.";
        if (combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("0x80070005", StringComparison.OrdinalIgnoreCase))
            return "Access denied. Try running as administrator.";
        return "Install failed.";
    }

    private static async Task<(int exitCode, string output, string error)> RunWingetCommandAsync(
        string arguments,
        Action<string>? outputCallback = null)
    {
        using var process = new Process
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

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
                outputCallback?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}

public enum InstallPhase
{
    Starting,
    Running,
    Success,
    Failed
}

public readonly record struct InstallProgress(
    int CurrentIndex,
    int Total,
    AppModel App,
    InstallPhase Phase,
    string Message);
