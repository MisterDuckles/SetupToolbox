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
    // Gedeelde cache voor zowel rich entries als ID-set. Vroeger waren dit twee
    // aparte caches met twee aparte `winget list` calls — debloat-pagina deed
    // 2× ~7s = ~15s wachten. Nu één call die beide caches vult.
    private List<WingetListEntry>? _appsListCache;
    private HashSet<string>? _installedIdsCache;
    private readonly SemaphoreSlim _appsListLock = new(1, 1);

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
    /// HashSet van geïnstalleerde winget-IDs. Hergebruikt de cache van
    /// GetInstalledAppsListAsync zodat we geen tweede winget-call doen.
    /// </summary>
    public async Task<HashSet<string>> GetInstalledAppIdsAsync(bool forceRefresh = false)
    {
        await GetInstalledAppsListAsync(forceRefresh);
        return _installedIdsCache ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returnt rich `winget list` entries — name + id + version per geïnstalleerde
    /// app. Cached — pass forceRefresh=true na install/uninstall om opnieuw te scannen.
    /// Locale-fallback ingebouwd: als de rich parser faalt (bv. Nederlandse Windows
    /// met "Naam Id Versie" header) valt 'ie terug op een whitespace-tokenizer die
    /// alleen IDs extraheert. In dat fallback-geval is Name = Id.
    /// </summary>
    public async Task<List<WingetListEntry>> GetInstalledAppsListAsync(bool forceRefresh = false)
    {
        await _appsListLock.WaitAsync();
        try
        {
            if (_appsListCache != null && !forceRefresh) return _appsListCache;

            var list = new List<WingetListEntry>();
            try
            {
                // --disable-interactivity slaat winget's progress-bars over die anders
                // tussen de output-regels gemixed kunnen raken en de parser confusen.
                // Geeft ook iets snellere output op trage systemen.
                var (exitCode, output, _) = await RunWingetCommandAsync(
                    "list --accept-source-agreements --disable-interactivity");
                if (exitCode == 0 || !string.IsNullOrWhiteSpace(output))
                {
                    // Rich parser eerst (volledige Name + Id + Version op Engelse Windows).
                    // Als die 0 entries returnt fallen we terug op de simple ID-tokenizer
                    // — die is locale-agnostic en werkt zelfs op Nederlandse winget output.
                    var rich = ParseListOutput(output);
                    list = rich.Count > 0 ? rich : ParseSimpleIds(output);
                }
            }
            catch
            {
                // swallow — return lege lijst, andere bronnen vullen aan
            }

            _appsListCache = list;
            _installedIdsCache = new HashSet<string>(
                list.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            return _appsListCache;
        }
        finally
        {
            _appsListLock.Release();
        }
    }

    /// <summary>
    /// Locale-onafhankelijke fallback wanneer ParseListOutput 0 entries returnt
    /// (header-detectie faalde door non-Engelse Windows). Pakt elke regel met een
    /// dotted token = winget ID. Geen Name of Version.
    /// </summary>
    private static List<WingetListEntry> ParseSimpleIds(string output)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<WingetListEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                // Skip:
                //   - geen dot (geen winget id)
                //   - tokens met '/' of '\\' (paths, ARP\ / MSIX\ prefixes)
                //   - korte fragments / version-numbers (bv. "147.0.2" zou anders matchen)
                //   - tokens zonder letter (alleen digits + dots = version, niet ID)
                if (!part.Contains('.')) continue;
                if (part.Contains('/') || part.Contains('\\')) continue;
                if (part.Length <= 3) continue;
                if (!part.Any(char.IsLetter)) continue;

                if (seen.Add(part))
                    // Fallback parser kent geen Source kolom — leeg laten betekent
                    // dat InstalledAppsService deze entries minder strict zal taggen.
                    results.Add(new WingetListEntry(part, part, "", ""));
                break;
            }
        }
        return results;
    }

    private static List<WingetListEntry> ParseListOutput(string output)
    {
        var results = new List<WingetListEntry>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.TrimEnd('\r'))
                          .ToList();

        // Header line bevat altijd "Name" en "Id". Daaronder een separator met
        // dashes. Column-position parsing — app-namen mogen spaties bevatten dus
        // whitespace-split werkt niet. Same patroon als ParseSearchOutput.
        var headerIdx = lines.FindIndex(l => l.StartsWith("Name", StringComparison.Ordinal)
                                          && l.Contains("Id", StringComparison.Ordinal));
        if (headerIdx < 0 || headerIdx + 2 >= lines.Count) return results;

        var header = lines[headerIdx];
        var idPos = header.IndexOf("Id", StringComparison.Ordinal);
        var versionPos = header.IndexOf("Version", StringComparison.Ordinal);
        var availablePos = header.IndexOf("Available", StringComparison.Ordinal);
        var sourcePos = header.IndexOf("Source", StringComparison.Ordinal);
        if (idPos < 0 || versionPos < 0 || versionPos <= idPos) return results;

        // Eindkolom voor Version: Available komt voor Source, en Available is
        // optioneel (alleen apps met een upgrade pending). Pak Available als 'ie
        // er staat, anders Source, anders einde regel.
        var versionEnd = availablePos > versionPos ? availablePos
                       : sourcePos > versionPos ? sourcePos
                       : -1;

        for (int i = headerIdx + 2; i < lines.Count; i++)
        {
            // Per-regel try/catch zodat één rare regel (bv. een progress-update
            // tussen entries die door de char-by-char reader als dataregel binnenkomt)
            // niet de hele parse afbreekt. Zonder deze safety net gaf de hele lijst
            // 0 entries terug zodra ÉÉN regel out-of-range Substring veroorzaakte.
            try
            {
                var line = lines[i];
                if (line.Length < versionPos) continue;

                var name = line.Substring(0, idPos).Trim();
                var idEnd = versionPos;
                var id = line.Substring(idPos, idEnd - idPos).Trim();

                string version;
                if (versionEnd > versionPos && line.Length >= versionEnd)
                    version = line.Substring(versionPos, versionEnd - versionPos).Trim();
                else if (line.Length > versionPos)
                    version = line.Substring(versionPos).Trim();
                else
                    version = string.Empty;

                // Source column: "winget" = echt in winget repo, "msstore" = MS Store,
                // leeg = pure registry / unmatched. Cruciaal onderscheid: alleen
                // Source=winget rechtvaardigt de Winget badge in onze UI; msstore en
                // leeg moeten doorvallen naar Store / Web detectie zodat user de
                // juiste tag ziet.
                var source = (sourcePos > 0 && line.Length > sourcePos)
                    ? line.Substring(sourcePos).Trim()
                    : string.Empty;

                // Filter rows zonder echte ID (separators, totals, header-noise).
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                // Skip backslash-prefix IDs:
                //   ARP\Machine\X64\... = pure registry uninstall entry, geen winget package
                //   MSIX\Name_Version_... = Microsoft Store/AppX in disguise (Get-AppxPackage
                //     vindt 'm gewoon, dus laat die detection het pakken, anders krijgen we
                //     dubbel met andere DisplayName en faalt dedup).
                if (id.Contains('\\')) continue;
                // ID bevat een dot voor reguliere winget IDs (Publisher.AppName) of is
                // een Microsoft Store productID (formaat 9XXXX of XPXXX). Beide accepteren.
                var isWingetId = id.Contains('.') && !id.Contains(' ') && id.Length >= 3;
                var isStoreId = (id.StartsWith("9", StringComparison.Ordinal) || id.StartsWith("XP", StringComparison.OrdinalIgnoreCase))
                                && id.Length >= 10 && !id.Contains(' ');
                if (!isWingetId && !isStoreId) continue;

                results.Add(new WingetListEntry(name, id, version, source));
            }
            catch
            {
                // Skip deze regel — defensief, mag in praktijk nooit gebeuren met de
                // length-check hierboven, maar als winget z'n output-format wijzigt
                // willen we niet dat de hele lijst sneuvelt.
                continue;
            }
        }
        return results;
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

    /// <summary>
    /// Uninstall een batch apps sequentieel. Mirror van InstallAppsAsync's API zodat
    /// de UI hetzelfde Progress-pattern kan gebruiken. Sequential omdat parallel
    /// uninstall meer kans geeft op Windows Installer locks (MSI engine = single-instance)
    /// zonder noemenswaardig snelheidsvoordeel — uninstall zelf is snel.
    /// </summary>
    public async Task<Dictionary<string, (bool success, string message)>> UninstallAppsAsync(
        IReadOnlyList<AppModel> apps,
        IProgress<UninstallProgress>? overall = null)
    {
        var results = new Dictionary<string, (bool, string)>();
        var total = apps.Count;

        for (var i = 0; i < total; i++)
        {
            var app = apps[i];
            var index = i + 1;

            overall?.Report(new UninstallProgress(index, total, app, UninstallPhase.Running, $"Uninstalling {app.Name}"));

            var (success, message) = await UninstallAppAsync(app.WingetId);
            results[app.WingetId] = (success, message);

            overall?.Report(new UninstallProgress(
                index, total, app,
                success ? UninstallPhase.Success : UninstallPhase.Failed,
                message));
        }

        return results;
    }

    public async Task<(bool success, string message)> UninstallAppAsync(string wingetId)
    {
        try
        {
            // --silent: vraagt het package om silent uninstall (afhankelijk van of het
            //   uninstaller binary die flag respecteert — niet alle installers doen dat).
            // --disable-interactivity: zet winget's eigen interactive prompts uit. Sommige
            //   uninstallers tonen alsnog hun eigen UI (Antigravity, Adobe Acrobat, etc.) —
            //   dat is een per-installer-respect ding waar we niets aan kunnen doen.
            // Restant-opruiming (--purge) wordt in v0.8.5 als expliciete user-keuze gebouwd.
            var (exitCode, output, error) = await RunWingetCommandAsync(
                $"uninstall --id {wingetId} --exact --silent --disable-interactivity --accept-source-agreements");

            if (exitCode == 0)
            {
                // Invalidate beide caches zodat de volgende GetInstalledAppsListAsync /
                // GetInstalledAppIdsAsync call een verse winget list scan triggert.
                await _appsListLock.WaitAsync();
                try
                {
                    _appsListCache = null;
                    _installedIdsCache = null;
                }
                finally { _appsListLock.Release(); }
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

public enum UninstallPhase
{
    Pending,
    Running,
    Success,
    Failed
}

public readonly record struct UninstallProgress(
    int CurrentIndex,
    int Total,
    AppModel App,
    UninstallPhase Phase,
    string Message);

// Source = "winget" (echt in winget repo), "msstore" (Microsoft Store), of leeg/onbekend.
// Belangrijk onderscheid: `winget list` toont álle installed apps maar markeert per
// row of winget de package kent. Apps met Source=winget zijn die `winget search`
// terugvindt; apps met Source=msstore staan alleen in de Store; lege Source = pure
// registry entry waarvoor winget geen match vond.
public sealed record WingetListEntry(string Name, string Id, string Version, string Source);
