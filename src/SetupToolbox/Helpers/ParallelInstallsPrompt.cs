using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SetupToolbox.Helpers;

// First-time vraag aan de user: hoeveel apps tegelijk installeren (1-4)?
// Toggle bestaat in Settings, maar de meeste users navigeren daar nooit
// heen — deze prompt zorgt dat ze 1x bewust kiezen. Resultaat wordt
// opgeslagen (MaxParallelInstalls + ParallelInstallsAsked) en de prompt
// komt nooit meer terug. User kan in Settings altijd nog wisselen.
//
// v1.0.7: was eerder een binaire Yes/No prompt (2 of 1) — nu een NumberBox
// zodat user direct 1-4 kan kiezen i.p.v. te moeten doorklikken naar Settings.
internal static class ParallelInstallsPrompt
{
    public static async Task MaybeShowAsync(XamlRoot xamlRoot)
    {
        if (App.Settings.ParallelInstallsAsked) return;

        var resources = Application.Current.Resources;

        // Integer-formatter zodat de waarde als "2" toont (niet "2,0" — NL-locale
        // zou anders een decimaal-komma kunnen plakken).
        var formatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
        {
            FractionDigits = 0,
            IsGrouped = false
        };

        var numberBox = new NumberBox
        {
            Minimum = 1,
            Maximum = 4,
            Value = 2,                                  // default = 2 (oude Yes-keuze)
            // Compact: spin-buttons alleen op hover/focus → het ingetypte cijfer
            // is altijd vol zichtbaar. Inline + Width=120 gaf op de VM een te
            // krap vak waarin alleen de spin-buttons zichtbaar waren.
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 1,
            LargeChange = 1,
            NumberFormatter = formatter,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 160
        };

        var content = new StackPanel { Spacing = 0 };
        content.Children.Add(new TextBlock
        {
            Text = "Kies hoeveel apps tegelijk geïnstalleerd worden. Hoger = sneller, maar sommige installers (vooral MSI-based) kunnen botsen als ze parallel draaien. Je kunt dit altijd wijzigen in Settings.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(numberBox);

        var dialog = new ContentDialog
        {
            Title = "Parallelisme bij installs",
            Content = content,
            PrimaryButtonText = "OK",
            DefaultButton = ContentDialogButton.Primary,
            CornerRadius = new CornerRadius(8),
            PrimaryButtonStyle = (Style)resources["DialogPrimaryButtonStyle"],
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();

        // Alleen opslaan + asked-flag zetten bij OK; bij dialog-X (None) krijgt
        // user gewoon volgende keer opnieuw de prompt.
        if (result != ContentDialogResult.Primary) return;

        var v = (int)Math.Clamp(numberBox.Value, 1, 4);
        App.Settings.MaxParallelInstalls = v;
        App.Settings.ParallelInstallsAsked = true;
    }
}
