using Microsoft.UI.Xaml.Markup;

namespace SetupToolbox.Helpers;

// XAML-markup-extension die x:Uid vervangt:
//
//     xmlns:loc="using:SetupToolbox.Helpers"
//     <TextBlock Text="{loc:Localize Key=settings.title}" />
//
// x:Uid is op deze app onbruikbaar — zie de uitleg in LocalizationService:
// op een unpackaged WinUI 3-app volgt x:Uid altijd de Windows-weergavetaal en
// is die niet om te buigen, dus de taalkeuze zou er stil langs lopen.
//
// De klasse heet bewust géén "LocalizeExtension": UWP/WinUI-XAML kent de
// WPF-conventie van het weglaten van het "Extension"-achtervoegsel niet
// betrouwbaar, dus de naam is precies wat je in XAML typt.
//
// LET OP: een markup-extension wordt één keer geëvalueerd tijdens het parsen van
// de XAML. Bij een taalwissel her-evalueert 'ie zichzelf dus NIET — daarvoor
// bouwt MainWindow de huidige page opnieuw op (zie MainWindow.ReloadForLanguage).
public sealed class Localize : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => App.Loc.S(Key);
}
