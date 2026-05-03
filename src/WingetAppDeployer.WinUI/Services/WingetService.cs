using System;
using System.Collections.Concurrent;
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
    /// Zoekt in de volledige winget repository naar apps die matchen op de query.
    /// Returned synthetische App-objecten (zonder category-koppeling) zodat ze in
    /// dezelfde install-flow meegenomen kunnen worden als catalog-apps.
    /// </summary>
    public async Task<List<AppModel>> SearchWingetRepoAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<AppModel>();

        try
        {
            // --source winget forceert alleen de community repo (niet msstore), wat
            // scheelt in output-ruis en dubbele hits. --accept-source-agreements
            // voorkomt dat een eerstkeer-prompt de exit code 0x8A150049 geeft.
            var escaped = query.Replace("\"", "");
            var (exitCode, output, _) = await RunWingetCommandAsync(
                $"search \"{escaped}\" --source winget --accept-source-agreements");

            if (exitCode != 0 && string.IsNullOrWhiteSpace(output))
                return new List<AppModel>();

            return ParseSearchOutput(output);
        }
        catch
        {
            return new List<AppModel>();
        }
    }

    private static List<AppModel> ParseSearchOutput(string output)
    {
        var results = new List<AppModel>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.TrimEnd('\r'))
                          .ToList();

        // Header line bevat altijd "Name" en "Id". Daaronder staat een separator
        // met dashes. Gebruik de kolom-posities uit de header om correct te parsen;
        // app-namen mogen spaties bevatten dus whitespace-split werkt niet.
        var headerIdx = lines.FindIndex(l => l.StartsWith("Name", StringComparison.Ordinal)
                                          && l.Contains("Id", StringComparison.Ordinal));
        if (headerIdx < 0 || headerIdx + 2 >= lines.Count) return results;

        var header = lines[headerIdx];
        var idPos = header.IndexOf("Id", StringComparison.Ordinal);
        var versionPos = header.IndexOf("Version", StringComparison.Ordinal);
        var sourcePos = header.IndexOf("Source", StringComparison.Ordinal);
        if (idPos < 0 || versionPos < 0 || versionPos <= idPos) return results;

        for (int i = headerIdx + 2; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length < versionPos) continue;

            var name = line.Substring(0, idPos).Trim();

            var idEnd = versionPos;
            var id = line.Substring(idPos, idEnd - idPos).Trim();

            var versionEnd = sourcePos > versionPos ? sourcePos : line.Length;
            var version = line.Substring(versionPos, versionEnd - versionPos).Trim();

            // Alleen entries die eruit zien als een echte winget-ID. Filter header
            // noise ("Match", "Moniker", ">") en regels zonder dot in de ID.
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
            if (!id.Contains('.')) continue;
            if (id.Length < 3 || id.Contains(' ')) continue;

            results.Add(new AppModel
            {
                Name = name,
                WingetId = id,
                Description = string.IsNullOrEmpty(version)
                    ? "Available via winget"
                    : $"Available via winget (v{version})"
            });
        }

        return results;
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

    public async Task<(bool success, string message)> InstallAppAsync(string wingetId, IProgress<string>? progress = null, string source = "winget")
    {
        try
        {
            progress?.Report($"Installing {wingetId}...");

            // Source-bewust install. msstore apps (productID-formaat 9XXX of XPXXX)
            // installeren TANTOE traag wanneer we --silent + --source msstore
            // forceren — die combinatie gebruikt onder water een COM-pad dat de
            // download-stream throttlet. `winget install <id>` zonder die flags
            // (zoals user kan reproduceren in PowerShell) gaat direct via de
            // native Microsoft Store backend en is meervoudig sneller.
            //
            // Voor reguliere winget apps blijven we wel --silent --exact gebruiken
            // — daar werkt het wel goed en hebben we de stille install nodig.
            string args;
            if (string.Equals(source, "msstore", StringComparison.OrdinalIgnoreCase))
            {
                args = $"install {wingetId} --accept-source-agreements --accept-package-agreements";
            }
            else
            {
                args = $"install --id {wingetId} --exact --silent --accept-source-agreements --accept-package-agreements";
            }

            var (exitCode, output, error) = await RunWingetCommandAsync(args, line => progress?.Report(line));

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
        IProgress<InstallProgress>? overall = null,
        int maxParallelism = 1)
    {
        var results = new ConcurrentDictionary<string, (bool, string)>();
        var total = apps.Count;
        var degree = Math.Max(1, Math.Min(maxParallelism, 4));

        // SemaphoreSlim begrenst hoeveel apps tegelijk in de winget-call mogen.
        // Bij maxParallelism=1 blijft het effectief sequentieel (zelfde gedrag
        // als de oude for-loop), bij 2+ draaien meerdere installs concurrent.
        // Hard-cap op 4 — meer dan dat is sowieso nooit veilig op typische
        // Windows-machines i.v.m. MSI-engine locks.
        using var sem = new SemaphoreSlim(degree);
        var tasks = new List<Task>(total);

        for (var i = 0; i < total; i++)
        {
            var app = apps[i];
            var index = i + 1;
            tasks.Add(InstallOneInBatchAsync(app, index, total, sem, results, overall));
        }

        await Task.WhenAll(tasks);

        // Preserve input order in returned dict zodat UI summary deterministic blijft.
        var ordered = new Dictionary<string, (bool, string)>();
        foreach (var app in apps)
            if (results.TryGetValue(app.WingetId, out var v))
                ordered[app.WingetId] = v;
        return ordered;
    }

    private async Task InstallOneInBatchAsync(
        AppModel app,
        int index,
        int total,
        SemaphoreSlim sem,
        ConcurrentDictionary<string, (bool, string)> results,
        IProgress<InstallProgress>? overall)
    {
        await sem.WaitAsync();
        try
        {
            overall?.Report(new InstallProgress(index, total, app, InstallPhase.Starting, $"Starting {app.Name}"));

            var perApp = new Progress<string>(msg =>
                overall?.Report(new InstallProgress(index, total, app, InstallPhase.Running, msg)));

            var (success, message) = await InstallAppAsync(app.WingetId, perApp, app.Source);
            results[app.WingetId] = (success, message);

            overall?.Report(new InstallProgress(
                index, total, app,
                success ? InstallPhase.Success : InstallPhase.Failed,
                message));
        }
        finally
        {
            sem.Release();
        }
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
