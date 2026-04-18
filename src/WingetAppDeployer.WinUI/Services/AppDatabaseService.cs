using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Services;

// Minimal read-only service that pulls apps.json from the shared repository.
// Mirrors the WPF WingetAppDeployer.Services.GitHubService.DownloadAppDatabaseAsync
// but keeps the WinUI project self-contained (no cross-project reference).
public sealed class AppDatabaseService
{
    private const string AppsJsonUrl =
        "https://raw.githubusercontent.com/MisterDuckles/WinGetAppDeployer/main/data/apps.json";

    private readonly HttpClient _httpClient;
    private AppDatabase? _cached;

    public AppDatabaseService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WingetAppDeployer.WinUI");
    }

    public async Task<AppDatabase?> GetAppDatabaseAsync(bool forceRefresh = false)
    {
        if (_cached != null && !forceRefresh)
            return _cached;

        try
        {
            var json = await _httpClient.GetStringAsync(AppsJsonUrl);
            _cached = JsonSerializer.Deserialize<AppDatabase>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return _cached;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
