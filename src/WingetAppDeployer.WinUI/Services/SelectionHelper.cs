using System.Collections.Generic;
using System.Linq;
using WingetAppDeployer_WinUI.Models;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Services;

// Centraal punt voor "welke apps zijn globaal geselecteerd". IsSelected zit op
// het App-model zelf (cached via AppDatabaseService), dus selection overleeft
// page-navigatie vanzelf. Daarnaast tracken we synthetische App-objecten die
// via winget-search door de user zijn toegevoegd (niet in onze apps.json),
// zodat die ook meetellen en meegaan met "Install selected apps".
internal static class SelectionHelper
{
    private static readonly List<AppModel> _extraSelectedApps = new();

    public static IReadOnlyList<AppModel> ExtraSelectedApps => _extraSelectedApps;

    public static IEnumerable<AppModel> EnumerateAllApps(AppDatabase? db)
    {
        if (db == null) yield break;
        foreach (var cat in db.Categories)
        {
            if (cat.Apps != null)
                foreach (var app in cat.Apps) yield return app;
            if (cat.Subcategories != null)
                foreach (var sub in cat.Subcategories)
                    foreach (var app in sub.Apps) yield return app;
        }
    }

    public static bool IsInCatalog(AppDatabase? db, string wingetId) =>
        EnumerateAllApps(db).Any(a => string.Equals(a.WingetId, wingetId, System.StringComparison.OrdinalIgnoreCase));

    public static AppModel? FindExtra(string wingetId) =>
        _extraSelectedApps.FirstOrDefault(a =>
            string.Equals(a.WingetId, wingetId, System.StringComparison.OrdinalIgnoreCase));

    public static void AddExtraSelected(AppModel app)
    {
        if (FindExtra(app.WingetId) != null) return;
        app.IsSelected = true;
        _extraSelectedApps.Add(app);
    }

    public static void RemoveExtraSelected(string wingetId)
    {
        _extraSelectedApps.RemoveAll(a =>
            string.Equals(a.WingetId, wingetId, System.StringComparison.OrdinalIgnoreCase));
    }

    public static int GetSelectedCount(AppDatabase? db) =>
        EnumerateAllApps(db).Count(a => a.IsSelected)
        + _extraSelectedApps.Count(a => a.IsSelected);

    public static List<AppModel> GetSelectedApps(AppDatabase? db)
    {
        var list = EnumerateAllApps(db).Where(a => a.IsSelected).ToList();
        list.AddRange(_extraSelectedApps.Where(a => a.IsSelected));
        return list;
    }

    public static void ClearSelection(AppDatabase? db)
    {
        foreach (var app in EnumerateAllApps(db))
            app.IsSelected = false;
        _extraSelectedApps.Clear();
    }
}
