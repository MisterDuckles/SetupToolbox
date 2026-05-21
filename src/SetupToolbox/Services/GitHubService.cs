using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SetupToolbox.Services;

// Self-update via GitHub Releases (v0.10.1). Checkt de releases van de publieke
// repo, vergelijkt met de draaiende AssemblyVersion, en kan de nieuwe installer
// (setup.exe) downloaden + silent draaien. De Inno Setup installer (v0.10.0,
// per-user) doet de daadwerkelijke in-use file-swap + relaunch via Restart Manager.
//
// Belangrijk: we gebruiken NIET het /releases/latest endpoint maar de volledige
// /releases-lijst, en filteren op de WinUI-installer-assetnaam (*-Setup-v*.exe)
// + stabiele releases (geen draft/pre-release). Zo wordt een eventuele andere
// (hoger-genummerde) release zonder onze installer-asset genegeerd — robuust
// tegen verdwaalde tags.
public sealed class GitHubService
{
    private const string Owner = "MisterDuckles";
    private const string Repo = "SetupToolbox";
    private const string ReleasesApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases";

    // Matcht onze installer-asset: "SetupToolbox-v1.0.0.exe".
    private static readonly Regex SetupAssetRx =
        new(@"^SetupToolbox-v.*\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private static readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub vereist een User-Agent; zonder → 403.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SetupToolbox-WinUI");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // Versie van de draaiende app (Major.Minor.Build; revision genegeerd).
    public Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    // Haalt de releases op en bepaalt of er een nieuwere stabiele WinUI-installer
    // beschikbaar is. Gooit niet — netwerk/parse-fouten worden Status.Error.
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(ReleasesApi, ct);
            if (!resp.IsSuccessStatusCode)
                return UpdateCheckResult.Failure($"GitHub gaf {(int)resp.StatusCode} {resp.ReasonPhrase}.");

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var releases = await JsonSerializer.DeserializeAsync<List<GhRelease>>(stream, _json, ct)
                           ?? new List<GhRelease>();

            UpdateInfo? best = null;
            foreach (var rel in releases)
            {
                if (rel.Draft || rel.Prerelease) continue;
                var asset = rel.Assets?.FirstOrDefault(a => SetupAssetRx.IsMatch(a.Name ?? string.Empty));
                if (asset == null) continue;
                if (!TryParseTag(rel.TagName, out var ver)) continue;
                if (best == null || ver > best.Version)
                    best = new UpdateInfo(ver, rel.TagName ?? string.Empty,
                        rel.Name ?? rel.TagName ?? string.Empty, rel.Body ?? string.Empty,
                        asset.Name ?? string.Empty, asset.BrowserDownloadUrl ?? string.Empty);
            }

            if (best == null || best.Version <= CurrentVersion)
                return UpdateCheckResult.UpToDate(CurrentVersion);
            return UpdateCheckResult.Available(best);
        }
        catch (OperationCanceledException) { return UpdateCheckResult.Failure("Geannuleerd."); }
        catch (Exception ex) { return UpdateCheckResult.Failure(ex.Message); }
    }

    // Download de setup.exe naar %TEMP%. progress rapporteert 0..1 als de
    // Content-Length bekend is. Returnt het lokale pad.
    public async Task<string> DownloadInstallerAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), info.AssetName);
        using var resp = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        return dest;
    }

    // Start de installer silent. Restart Manager sluit DEZE app (die de install-
    // bestanden vergrendelt), vervangt ze, en herstart 'm via /RESTARTAPPLICATIONS.
    // We exiten dus NIET zelf — RM regelt de shutdown + relaunch. UseShellExecute
    // zodat de installer een onafhankelijk proces is en blijft draaien als RM ons
    // afsluit. Per-user install → geen UAC.
    public void LaunchInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NOCANCEL",
            UseShellExecute = true
        });
    }

    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var t = tag.TrimStart('v', 'V').Trim();
        var dash = t.IndexOf('-');               // strip pre-release suffix ("0.10.1-beta")
        if (dash >= 0) t = t.Substring(0, dash);
        if (!Version.TryParse(t, out var v)) return false;
        version = new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
        return true;
    }

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GhAsset>? Assets { get; set; }
    }

    private sealed class GhAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

public sealed record UpdateInfo(
    Version Version, string TagName, string ReleaseName, string Notes, string AssetName, string DownloadUrl);

public enum UpdateCheckStatus { UpdateAvailable, UpToDate, Error }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update, Version? Current, string? Error)
{
    public static UpdateCheckResult Available(UpdateInfo u) => new(UpdateCheckStatus.UpdateAvailable, u, null, null);
    public static UpdateCheckResult UpToDate(Version current) => new(UpdateCheckStatus.UpToDate, null, current, null);
    public static UpdateCheckResult Failure(string error) => new(UpdateCheckStatus.Error, null, null, error);
}
