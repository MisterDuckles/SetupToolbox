using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Services;

// Minimal port of the WPF WingetService focused on the install flow. Uses the
// winget.exe CLI, streams stdout as progress, no shared code with the WPF app.
public sealed class WingetService
{
    private HashSet<string>? _installedIdsCache;
    private readonly SemaphoreSlim _installedLock = new(1, 1);

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

    /// <summary>
    /// Parses `winget list` output and returns the set of installed winget IDs.
    /// Cached — pass forceRefresh=true after an install batch to re-detect.
    /// </summary>
    public async Task<HashSet<string>> GetInstalledAppIdsAsync(bool forceRefresh = false)
    {
        await _installedLock.WaitAsync();
        try
        {
            if (_installedIdsCache != null && !forceRefresh)
                return _installedIdsCache;

            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var (exitCode, output, _) = await RunWingetCommandAsync(
                    "list --accept-source-agreements");
                if (exitCode == 0)
                {
                    // winget list output: header line + separator + rows. The ID is the
                    // second column. We split on whitespace; some rows have names with
                    // spaces so we can't rely on position — instead match any token that
                    // looks like a dotted winget ID (Publisher.AppName) per row.
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines.Skip(2))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            if (part.Contains('.') && !part.Contains('/') && part.Length > 3)
                            {
                                installed.Add(part);
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
                // swallow — return empty set
            }

            _installedIdsCache = installed;
            return installed;
        }
        finally
        {
            _installedLock.Release();
        }
    }

    /// <summary>
    /// Upgrades all installed apps via winget. Used by the scheduled auto-update task.
    /// </summary>
    public async Task<bool> UpdateAllAppsAsync()
    {
        try
        {
            var (exitCode, _, _) = await RunWingetCommandAsync(
                "upgrade --all --silent --accept-source-agreements --accept-package-agreements");
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool success, string message)> UninstallAppAsync(string wingetId)
    {
        try
        {
            var (exitCode, output, error) = await RunWingetCommandAsync(
                $"uninstall --id {wingetId} --exact --silent --accept-source-agreements");

            if (exitCode == 0)
            {
                // Invalidate cache so the next GetInstalledAppIdsAsync call re-queries.
                await _installedLock.WaitAsync();
                try { _installedIdsCache = null; }
                finally { _installedLock.Release(); }
                return (true, "Uninstalled");
            }

            var combined = error + output;
            if (combined.Contains("No installed package found", StringComparison.OrdinalIgnoreCase))
                return (false, "Not installed.");
            if (combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
                return (false, "Access denied. Try running as administrator.");
            return (false, "Uninstall failed.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
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

        process.Start();

        // winget updates its download progress bar by overwriting the current
        // terminal line with '\r' instead of emitting a new line. The default
        // OutputDataReceived / BeginOutputReadLine only triggers on '\n', so we'd
        // miss every intermediate "X MB / Y MB" update and only see the final
        // snapshot when the download finishes. Read char-by-char and split on
        // either '\r' or '\n' so each progress tick surfaces as its own line.
        var stdoutTask = ReadStreamAsync(process.StandardOutput, line =>
        {
            outputBuilder.AppendLine(line);
            outputCallback?.Invoke(line);
        });
        var stderrTask = ReadStreamAsync(process.StandardError, line =>
        {
            errorBuilder.AppendLine(line);
        });

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onLine)
    {
        var buf = new char[4096];
        var carry = new StringBuilder();
        while (true)
        {
            int read = await reader.ReadAsync(buf, 0, buf.Length);
            if (read <= 0) break;
            for (int i = 0; i < read; i++)
            {
                var c = buf[i];
                if (c == '\r' || c == '\n')
                {
                    if (carry.Length > 0)
                    {
                        onLine(carry.ToString());
                        carry.Clear();
                    }
                }
                else
                {
                    carry.Append(c);
                }
            }
        }
        if (carry.Length > 0) onLine(carry.ToString());
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
