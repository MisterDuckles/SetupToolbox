using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AppModel = SetupToolbox.Models.App;

namespace SetupToolbox.Helpers;

// Vraagt vooraf — vóór InstallDialog opent — een installatiepad voor elke app die
// `requiresLocation` heeft (bv. Battle.net). MOET vóór InstallDialog draaien:
// InstallDialog is zelf een ContentDialog, en WinUI 3 staat maar ÉÉN open
// ContentDialog tegelijk toe. Een pad-dialog tónen terwijl InstallDialog al open
// staat gooit "Only a single ContentDialog can be open at any time" → app-crash.
//
// Returnt wingetId → gekozen pad. Apps die de user overslaat (of leeg pad) staan
// NIET in de dict; InstallDialog markeert die als Skipped.
internal static class LocationPrompt
{
    public static async Task<Dictionary<string, string>> CollectAsync(
        IReadOnlyList<AppModel> apps, XamlRoot xamlRoot)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var locationApps = apps.Where(a => !a.IsManualDownload && a.RequiresLocation).ToList();
        if (locationApps.Count == 0) return paths;

        var resources = Application.Current.Resources;

        foreach (var a in locationApps)
        {
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", a.Name);

            var pathBox = new TextBox
            {
                Text = defaultPath,
                Header = App.Loc.S("location.header"),
                Margin = new Thickness(0, 12, 0, 0)
            };

            var content = new StackPanel { Spacing = 0 };
            content.Children.Add(new TextBlock
            {
                Text = App.Loc.S("location.body", a.Name),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(pathBox);

            var dialog = new ContentDialog
            {
                Title = App.Loc.S("location.title", a.Name),
                Content = content,
                PrimaryButtonText = App.Loc.S("location.install"),
                CloseButtonText = App.Loc.S("location.skip"),
                DefaultButton = ContentDialogButton.Primary,
                CornerRadius = new CornerRadius(8),
                PrimaryButtonStyle = (Style)resources["DialogPrimaryButtonStyle"],
                XamlRoot = xamlRoot
            };

            var result = await DialogService.ShowAsync(dialog);
            if (result != ContentDialogResult.Primary) continue;

            var chosen = pathBox.Text.Trim();
            if (!string.IsNullOrEmpty(chosen)) paths[a.WingetId] = chosen;
        }

        return paths;
    }
}
