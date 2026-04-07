using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using WingetAppDeployer.Models;
using Newtonsoft.Json;

namespace WingetAppDeployer.Services;

public class GitHubService
{
    private readonly HttpClient _httpClient;
    private const string RepoOwner = "MisterDuckles";
    private const string RepoName = "WinGetAppDeployer";
    private static string CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public GitHubService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WingetAppDeployer");
    }

    /// <summary>
    /// Downloads the apps.json database from GitHub
    /// </summary>
    public async Task<AppDatabase?> DownloadAppDatabaseAsync()
    {
        try
        {
            var url = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/data/apps.json";
            var json = await _httpClient.GetStringAsync(url);
            return JsonConvert.DeserializeObject<AppDatabase>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download app database: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a new version of the app is available on GitHub
    /// </summary>
    public async Task<(bool updateAvailable, string? latestVersion, string? downloadUrl)> CheckForUpdatesAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);
            var json = JsonNode.Parse(response);

            if (json == null) return (false, null, null);

            var latestVersion = json["tag_name"]?.ToString().TrimStart('v');
            var assets = json["assets"]?.AsArray();

            if (latestVersion == null || assets == null) return (false, null, null);

            // Find the main exe asset
            var exeAsset = assets.FirstOrDefault(a =>
                a?["name"]?.ToString().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

            if (exeAsset == null) return (false, null, null);

            var downloadUrl = exeAsset["browser_download_url"]?.ToString();
            var isNewer = IsVersionNewer(CurrentVersion, latestVersion);

            return (isNewer, latestVersion, downloadUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to check for updates: {ex.Message}");
            return (false, null, null);
        }
    }

    private bool IsVersionNewer(string current, string latest)
    {
        try
        {
            var currentParts = current.Split('.').Select(int.Parse).ToArray();
            var latestParts = latest.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Min(currentParts.Length, latestParts.Length); i++)
            {
                if (latestParts[i] > currentParts[i]) return true;
                if (latestParts[i] < currentParts[i]) return false;
            }

            return latestParts.Length > currentParts.Length;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Downloads and installs the app update
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<int>? progress = null)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "WingetAppDeployer_Update.exe");

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progressPercent = (int)((downloadedBytes * 100) / totalBytes);
                    progress?.Report(progressPercent);
                }
            }

            // Start the updater and exit current app
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true,
                Arguments = "/update"
            });

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download update: {ex.Message}");
            return false;
        }
    }
}
