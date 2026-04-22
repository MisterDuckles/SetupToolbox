using System.Collections.Generic;
using System.Linq;
using WingetAppDeployer_WinUI.Models;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Services;

// Centraal punt voor "welke apps zijn globaal geselecteerd". IsSelected zit op
// het App-model zelf (cached via AppDatabaseService), dus selection overleeft
// page-navigatie vanzelf. Deze helper geeft de twee gangbare queries.
internal static class SelectionHelper
{
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

    public static int GetSelectedCount(AppDatabase? db) =>
        EnumerateAllApps(db).Count(a => a.IsSelected);

    public static List<AppModel> GetSelectedApps(AppDatabase? db) =>
        EnumerateAllApps(db).Where(a => a.IsSelected).ToList();

    public static void ClearSelection(AppDatabase? db)
    {
        foreach (var app in EnumerateAllApps(db))
            app.IsSelected = false;
    }
}
