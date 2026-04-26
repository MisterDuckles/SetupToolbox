using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Services;

// Read-only service voor de curated apps.json. Leest het bundled bestand
// data/apps.json dat naast de exe staat (via csproj <Content> CopyToOutputDirectory).
//
// Distributie-model: repo blijft private, gebundelde exe wordt verspreid via
// public GitHub Releases. Geen remote fetch — apps.json updates komen mee met
// nieuwe exe-releases, niet via een live HTTP-call. Werkt offline en heeft
// geen auth-tokens nodig.
public sealed class AppDatabaseService
{
    private AppDatabase? _cached;

    public Task<AppDatabase?> GetAppDatabaseAsync(bool forceRefresh = false)
    {
        if (_cached != null && !forceRefresh)
            return Task.FromResult<AppDatabase?>(_cached);

        var path = Path.Combine(AppContext.BaseDirectory, "data", "apps.json");
        if (!File.Exists(path))
            return Task.FromResult<AppDatabase?>(null);

        try
        {
            var json = File.ReadAllText(path);
            _cached = JsonSerializer.Deserialize<AppDatabase>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return Task.FromResult(_cached);
        }
        catch
        {
            return Task.FromResult<AppDatabase?>(null);
        }
    }
}
