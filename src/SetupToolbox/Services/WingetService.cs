using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AppModel = SetupToolbox.Models.App;

namespace SetupToolbox.Services;

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
            var args = $"uninstall --id {wingetId} --exact --silent --disable-interactivity --accept-source-agreements";
            var (exitCode, output, error) = await RunWingetCommandAsync(args);

            if (exitCode == 0)
            {
                InvalidateInstalledCache();
                return (true, "Uninstalled");
            }

            var combined = error + output;
            if (combined.Contains("No installed package found", StringComparison.OrdinalIgnoreCase))
                return (false, "Not installed.");

            // Common failure mode: app installed in HKLM (Program Files) needs admin
            // to remove. Winget zelf draait als user → underlying uninstaller faalt
            // op file-permissions. Auto-retry met UAC elevation zodat user 1 prompt
            // ziet en de uninstaller alsnog kan zijn werk doen.
            var needsElevation =
                combined.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("administrator", StringComparison.OrdinalIgnoreCase) ||
                exitCode == unchecked((int)0x80073D02) ||       // ERROR_INSTALL_PACKAGE_DOWNGRADE
                exitCode == unchecked((int)0x80070005);         // E_ACCESSDENIED

            // Veel uninstallers (zoals CCleaner / Inno Setup) returneren een niet-
            // descriptive exit code zonder "access denied" in stderr — toch is de
            // root cause meestal admin nodig. Retryen-met-UAC is altijd veilig
            // omdat het opnieuw winget aanroept met dezelfde args.
            var (elevatedOk, elevatedMsg) = await UninstallAppElevatedAsync(wingetId);
            if (elevatedOk)
            {
                InvalidateInstalledCache();
                return (true, "Uninstalled (elevated)");
            }

            // Beide attempts faalden. Geef de meest informatieve message terug.
            return (false, needsElevation
                ? "Uninstall failed (also when elevated)."
                : $"Uninstall failed. {elevatedMsg}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Run winget uninstall via Process.Start met `verb=runas` zodat de
    /// underlying uninstaller admin rights heeft. Gebruikt als fallback wanneer
    /// de user-context uninstall faalt — typisch voor HKLM-registered apps
    /// (Program Files installs) waarvan de uninstaller delete permissions nodig
    /// heeft. Eén UAC prompt voor de hele retry.
    /// </summary>
    public async Task<(bool success, string message)> UninstallAppElevatedAsync(string wingetId)
    {
        try
        {
            var args = $"uninstall --id {wingetId} --exact --silent --disable-interactivity --accept-source-agreements";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "winget.exe",
                Arguments = args,
                UseShellExecute = true,      // verplicht voor Verb=runas
                Verb = "runas",
                CreateNoWindow = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return (false, "Could not start elevated winget");
            await proc.WaitForExitAsync();

            InvalidateInstalledCache();

            if (proc.ExitCode == 0) return (true, "Uninstalled (elevated)");
            return (false, $"Elevated winget exit {proc.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User klikte "No" op UAC prompt
            return (false, "Cancelled — UAC prompt declined");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void InvalidateInstalledCache()
    {
        // Invalidate beide caches zodat de volgende GetInstalledAppsListAsync /
        // GetInstalledAppIdsAsync call een verse winget list scan triggert.
        _appsListLock.Wait();
        try
        {
            _appsListCache = null;
            _installedIdsCache = null;
        }
        finally { _appsListLock.Release(); }
    }

    // Sentinel-bericht voor de "zit er al"-uitkomst. Gedeeld met de UI (InstallDialog)
    // zodat die de al-aanwezig-case kan onderscheiden van een verse install zonder op
    // een losse magic string te matchen.
    public const string AlreadyInstalledMessage = "Al geïnstalleerd";

    // Sentinel (v1.0.7): installer weigert elevated context (typisch Spotify met
    // exit 0x8A150056 "The installer cannot be run from an administrator context").
    // De InstallDialog detecteert deze message na een elevated batch en draait die
    // apps automatisch nog een keer in-process (onge-eleveerd).
    public const string RequiresUnelevatedMessage = "Vereist onge-eleveerde install";

    // Sentinel (v1.0.7): user heeft de batch geannuleerd via de Annuleren-knop.
    public const string CancelledMessage = "Geannuleerd";

    // Sentinel (v1.0.7 D): winget kon de msstore-install niet via z'n eigen IPC
    // doen (cert-error 0x8A15005E — gebroken cert-pin laag tussen winget en
    // Microsoft Store, zien we typisch op stale Windows-installs / corporate
    // proxies / AV-interferentie, niet alleen op VMs). Fallback: Store-app
    // rechtstreeks openen op de productpagina (`ms-windows-store://pdp/?productid=…`).
    // User moet daar 1× op "Halen" klikken; updates lopen daarna via winget
    // (healthy) of via de Store-app zelf in de achtergrond (broken).
    public const string MsStoreOpenedMessage = "Geopend in Microsoft Store";

    // Sentinel (E1): winget geeft aan dat de installer een installatielocatie
    // vereist. InstallDialog toont een pad-dialog en herprobeert in-process
    // met --location <gekozen pad>.
    public const string RequiresLocationMessage = "Installatielocatie vereist";

    public async Task<(bool success, string message)> InstallAppAsync(string wingetId, IProgress<string>? progress = null, string source = "winget", CancellationToken ct = default, string? location = null)
    {
        try
        {
            progress?.Report($"Installing {wingetId}...");

            // Source-bewust install:
            //  - msstore apps (productID 9XXX/XPXXX): `winget install <id>` zonder
            //    --silent/--source — die combinatie gaat via de native Store backend
            //    en is veel sneller dan een geforceerd --source msstore COM-pad.
            //  - reguliere winget apps: PIN op `--source winget`. Zonder --source
            //    doorzoekt winget óók de msstore-bron; als die op een (schone)
            //    machine een certificaat-fout geeft (0x8a15005e "server certificate
            //    did not match"), weigert winget vervolgens de winget-match auto te
            //    kiezen en stopt met "specify --source". Pinnen op winget vermijdt
            //    de msstore-bron volledig.
            string args;
            if (string.Equals(source, "msstore", StringComparison.OrdinalIgnoreCase))
            {
                args = $"install {wingetId} --accept-source-agreements --accept-package-agreements";
            }
            else
            {
                args = $"install --id {wingetId} --exact --silent --source winget --accept-source-agreements --accept-package-agreements";
                if (!string.IsNullOrEmpty(location))
                    args += $" --location \"{location.Replace("\"", "\\\"")}\"";
            }

            LogInstall($"START {wingetId} (source={source}) -> winget {args}");
            var (exitCode, output, error) = await RunWingetCommandAsync(args, line => progress?.Report(line), ct);

            // Cancellation gewonnen: het winget-proces is gekild via de ct.Register-hook;
            // de exit-code die we terugkrijgen is willekeurig (kill-signaal). Géén
            // FriendlyError mapping — gewoon als "Geannuleerd" rapporteren.
            if (ct.IsCancellationRequested)
            {
                LogInstall($"CANCEL {wingetId}");
                progress?.Report(CancelledMessage);
                return (false, CancelledMessage);
            }

            if (exitCode == 0)
            {
                LogInstall($"OK    {wingetId}");
                progress?.Report("Installed");
                return (true, "Installed");
            }

            // "Already installed" / "niets nieuwers": geen echte fout. NIET gehardcode
            // op Edge — gebaseerd op winget's eigen response (zie IsAlreadyInstalled):
            // taal-onafhankelijke exit codes + de Engelstalige output-tekst als fallback.
            // Edge is preinstalled op Windows → winget probeert te upgraden, vindt niets
            // nieuwers en geeft een van die codes. Behandel als succes/overslaan zodat het
            // niet als rode failure in de UI komt.
            if (IsAlreadyInstalled(exitCode, error + output))
            {
                LogInstall($"SKIP  {wingetId} (already installed, exit=0x{exitCode:X8})");
                progress?.Report(AlreadyInstalledMessage);
                return (true, AlreadyInstalledMessage);
            }

            // v1.0.7: installer-weigert-admin (typisch Spotify exit 0x8A150056). Gemarkeerd
            // met de sentinel-message zodat InstallDialog na een elevated batch deze app
            // automatisch onge-eleveerd opnieuw kan proberen. Géén failure-status hier —
            // we hebben 'm geprobeerd maar de installer zelf wil deze context niet.
            if (IsAdminContextRefused(exitCode, error + output))
            {
                LogInstall($"REJECT-ADMIN {wingetId} exit=0x{exitCode:X8} — needs unelevated retry");
                progress?.Report(RequiresUnelevatedMessage);
                return (false, RequiresUnelevatedMessage);
            }

            // E1: installer vereist een expliciete installatielocatie. Sentinel geeft
            // InstallDialog het sein een pad-dialog te tonen en in-process te herproberen
            // met --location <gekozen pad>.
            if (string.IsNullOrEmpty(location) && IsLocationRequired(exitCode, error + output))
            {
                LogInstall($"LOCATION-REQUIRED {wingetId} exit=0x{exitCode:X8}");
                progress?.Report(RequiresLocationMessage);
                return (false, RequiresLocationMessage);
            }

            // v1.0.7 D: msstore cert-error (0x8A15005E) — winget's IPC-pad naar de
            // Store-app is stuk, maar de Store-app zélf werkt meestal nog. Open de
            // productpagina rechtstreeks zodat user 1× op "Halen" kan klikken. Alleen
            // voor msstore-source apps; voor winget-community apps zou deze code geen
            // zin hebben (geen Store-productID). Bij Process.Start-fout (geen Store-app
            // geïnstalleerd) vallen we door op de gewone FriendlyError hieronder.
            if (string.Equals(source, "msstore", StringComparison.OrdinalIgnoreCase)
                && IsMsStoreCertError(exitCode, error + output)
                && TryOpenInMicrosoftStore(wingetId))
            {
                LogInstall($"STORE-OPEN {wingetId} (msstore cert-error fallback)");
                progress?.Report(MsStoreOpenedMessage);
                return (true, MsStoreOpenedMessage);
            }

            LogInstall($"FAIL  {wingetId} exit=0x{exitCode:X8} | stderr: {Trim1k(error)} | stdout: {Trim1k(output)}");
            var friendly = FriendlyError(error, output, exitCode, wingetId);
            progress?.Report(friendly);
            return (false, friendly);
        }
        catch (OperationCanceledException)
        {
            LogInstall($"CANCEL {wingetId} (token)");
            progress?.Report(CancelledMessage);
            return (false, CancelledMessage);
        }
        catch (Exception ex)
        {
            LogInstall($"EXCEPTION {wingetId}: {ex.Message}");
            progress?.Report(ex.Message);
            return (false, ex.Message);
        }
    }

    public async Task<Dictionary<string, (bool success, string message)>> InstallAppsAsync(
        IReadOnlyList<AppModel> apps,
        IProgress<InstallProgress>? overall = null,
        int maxParallelism = 1,
        CancellationToken ct = default)
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
            tasks.Add(InstallOneInBatchAsync(app, index, total, sem, results, overall, ct));
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
        IProgress<InstallProgress>? overall,
        CancellationToken ct)
    {
        // Cancellation kan al getriggerd zijn vóór we überhaupt aan de semaphore
        // komen (bv. user kliket Annuleren tijdens een lange MSI-wachtrij).
        try { await sem.WaitAsync(ct); }
        catch (OperationCanceledException)
        {
            results[app.WingetId] = (false, CancelledMessage);
            overall?.Report(new InstallProgress(index, total, app, InstallPhase.Failed, CancelledMessage));
            return;
        }
        try
        {
            if (ct.IsCancellationRequested)
            {
                results[app.WingetId] = (false, CancelledMessage);
                overall?.Report(new InstallProgress(index, total, app, InstallPhase.Failed, CancelledMessage));
                return;
            }

            overall?.Report(new InstallProgress(index, total, app, InstallPhase.Starting, $"Starting {app.Name}"));

            var perApp = new Progress<string>(msg =>
                overall?.Report(new InstallProgress(index, total, app, InstallPhase.Running, msg)));

            var (success, message) = await InstallAppAsync(app.WingetId, perApp, app.Source, ct);
            results[app.WingetId] = (success, message);

            var phase = !success ? InstallPhase.Failed
                : message == AlreadyInstalledMessage ? InstallPhase.AlreadyInstalled
                : message == MsStoreOpenedMessage ? InstallPhase.OpenedInStore
                : InstallPhase.Success;
            overall?.Report(new InstallProgress(index, total, app, phase, message));
        }
        finally
        {
            sem.Release();
        }
    }

    // "Zit er al / niets nieuwers" — geen echte install-fout. Primair op winget's
    // gedocumenteerde exit codes (taal-onafhankelijk), met de Engelstalige output-tekst
    // als extra vangnet. Bewust GEEN hardcoded app-namen (zoals Edge): elke app die
    // winget als reeds-aanwezig rapporteert valt hieronder.
    private static bool IsAlreadyInstalled(int exitCode, string combined)
    {
        switch (unchecked((uint)exitCode))
        {
            case 0x8A150061: // PACKAGE_ALREADY_INSTALLED  ("Found at least one version installed")
            case 0x8A15010D: // INSTALL_ALREADY_INSTALLED  (installer meldt al aanwezig)
            case 0x8A15002B: // UPDATE_NOT_APPLICABLE       (al op nieuwste versie — bv. Edge preinstalled)
                return true;
        }
        return combined.Contains("already installed", StringComparison.OrdinalIgnoreCase);
    }

    // v1.0.7: installer weigert in elevated context te draaien (typisch Spotify).
    // Exit-code primair (taal-onafhankelijk); Engelstalige output-tekst als vangnet.
    // InstallDialog gebruikt deze signal om de app in een tweede pass onge-eleveerd
    // opnieuw te proberen — winget zelf draait nog steeds via z'n alias.
    private static bool IsAdminContextRefused(int exitCode, string combined)
    {
        if (unchecked((uint)exitCode) == 0x8A150056) // INSTALLER_PROHIBITS_ELEVATION
            return true;
        return combined.Contains("cannot be run from an administrator context", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("must not be run as administrator", StringComparison.OrdinalIgnoreCase);
    }

    // E1: installer vereist een installatielocatie. Tekst-detectie primair (taal-
    // onafhankelijke exit-code voor dit geval is niet gedocumenteerd in winget-cli).
    private static bool IsLocationRequired(int exitCode, string combined) =>
        combined.Contains("requires install location", StringComparison.OrdinalIgnoreCase)
        || combined.Contains("install location is required", StringComparison.OrdinalIgnoreCase)
        || combined.Contains("requires an installation location", StringComparison.OrdinalIgnoreCase)
        || combined.Contains("location is required", StringComparison.OrdinalIgnoreCase);

    // v1.0.7 D: msstore cert-pin error. winget's `source update` werkt vaak nog
    // (CDN-laag oké), maar de install-IPC naar de Store-app valt op een aparte
    // cert-check. Op gezonde machines is dit zeldzaam; op stale Windows-installs
    // / AV-proxies / corporate setups regelmatig. Detectie via exit-code +
    // tekst-fallback.
    private static bool IsMsStoreCertError(int exitCode, string combined) =>
        unchecked((uint)exitCode) == 0x8A15005E
        || combined.Contains("server certificate did not match", StringComparison.OrdinalIgnoreCase);

    // v1.0.7 D: opent de Microsoft Store-app direct op de productpagina van de
    // gegeven Store-productID (formaat "9XXXXXX" of "XPXXX…"). ShellExecute via
    // het `ms-windows-store:`-protocol. Faalt netjes (false) als de Store-app
    // niet geïnstalleerd is of het protocol om een andere reden niet handled
    // wordt — caller valt dan door op de gewone FriendlyError.
    private static bool TryOpenInMicrosoftStore(string productId)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"ms-windows-store://pdp/?productid={productId}",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FriendlyError(string error, string output, int exitCode, string wingetId)
    {
        var combined = error + output;

        // Exit-code eerst — taal-onafhankelijk én betrouwbaarder dan output-tekst.
        // Codes geobserveerd in echte install-logs (zie winget returnCodes.md). Veel
        // hiervan zijn TIJDELIJK (UAC geweigerd, app in gebruik, parallel-botsing,
        // onderbroken download) → "Probeer opnieuw", géén misleidende "niet compatibel".
        switch (unchecked((uint)exitCode))
        {
            case 0x800704C7: // ERROR_CANCELLED — UAC-prompt niet (op tijd) geaccepteerd
            case 0x8A15010C: // INSTALL_CANCELLED_BY_USER
                return "Geannuleerd — UAC niet (op tijd) geaccepteerd. Probeer opnieuw.";
            case 0x8A150006: // SHELLEXEC_INSTALL_FAILED — installer gaf een fout terug
                return "Installer mislukte (afgebroken, UAC geweigerd of al bezig). Probeer opnieuw.";
            case 0x8A150011: // INSTALLER_HASH_MISMATCH — download corrupt of manifest-hash verouderd
                return "Download-verificatie faalde (hash). Probeer later opnieuw.";
            case 0x8A150101: // INSTALL_PACKAGE_IN_USE
            case 0x8A150103: // INSTALL_FILE_IN_USE
            case 0x8A150111: // INSTALL_PACKAGE_IN_USE_BY_APPLICATION
                return "App of bestand is in gebruik — sluit 'm en probeer opnieuw.";
            case 0x8A150102: // INSTALL_INSTALL_IN_PROGRESS
                return "Er loopt al een installatie — even wachten en opnieuw proberen.";
            case 0x8A150105: // INSTALL_DISK_FULL
                return "Schijf vol — maak ruimte vrij en probeer opnieuw.";
            case 0x8A150001: // INTERNAL_ERROR (o.a. na VM-pauze / onverwachte winget-staat)
                return "Winget interne fout. Probeer opnieuw.";
            case 0x8A15005E: // PINNED_CERTIFICATE_MISMATCH — msstore-bronfout op verse machines
                return "Winget-bronfout (certificaat). Draai in een terminal: winget source reset --force";
            case 0x80070005: // E_ACCESSDENIED
                return "Toegang geweigerd — vereist mogelijk admin-rechten.";
        }

        // Tekst-fallback voor codes die we niet expliciet mappen.
        if (combined.Contains("No applicable installer", StringComparison.OrdinalIgnoreCase))
            return "Geen geschikte installer voor dit systeem (architectuur/scope).";
        if (combined.Contains("No package found", StringComparison.OrdinalIgnoreCase))
            return "Pakket niet gevonden in winget.";
        if (combined.Contains("server certificate did not match", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("Failed when searching source", StringComparison.OrdinalIgnoreCase))
            return "Winget-bronfout (certificaat). Draai in een terminal: winget source reset --force";

        return $"Install mislukt (winget exit 0x{exitCode:X8}). Details in de log (Settings → Diagnostiek → Open logmap).";
    }

    // Install-logging via de gedeelde Diagnostics-gate (Settings-toggle
    // ErrorLoggingEnabled). Landt in %LocalAppData%\SetupToolbox\logs\install.log.
    private static void LogInstall(string message) => Helpers.Diagnostics.Log("install.log", message);

    private static string Trim1k(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length > 1000 ? t.Substring(0, 1000) + "…" : t;
    }

    private static async Task<(int exitCode, string output, string error)> RunWingetCommandAsync(
        string arguments,
        Action<string>? outputCallback = null,
        CancellationToken ct = default)
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

        // Cancellation (v1.0.7): bij Annuleren moet niet alleen winget zelf sterven
        // maar óók de installer die winget zelf spawnt (msi / setup.exe).
        // entireProcessTree:true zorgt voor de hele boom. Try/catch want het proces
        // kan al normaal geëindigd zijn (race) en dan throwt Kill InvalidOperation.
        using var ctReg = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

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
    AlreadyInstalled,
    OpenedInStore,   // v1.0.7 D: msstore cert-fallback opende de Store-app
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
