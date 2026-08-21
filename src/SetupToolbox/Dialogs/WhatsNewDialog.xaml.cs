using System;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Services;

namespace SetupToolbox.Dialogs;

// De "wat is er nieuw"-melding (v1.2.10). Verschijnt bij de eerste start op een
// nieuw versienummer, en is daarnaast op te roepen via de link in Instellingen.
//
// De inhoud komt LIVE uit de GitHub-release - keuze van user. Dat heeft een
// gevolg dat je moet weten: de release-body is in EEN taal geschreven, dus die
// tekst volgt de taalkeuze in de app niet. Alles eromheen (titel, intro, knop,
// link) is wel vertaald. De alternatieve route was een meegebakken
// data/whatsnew.json met loc-keys per bullet; die is bewust niet gekozen.
public sealed partial class WhatsNewDialog : ContentDialog
{
    public WhatsNewDialog(ReleaseNotes notes)
    {
        InitializeComponent();

        Title = App.Loc.S("whatsnew.title", notes.Version);
        PrimaryButtonText = App.Loc.S("whatsnew.close");
        IntroText.Text = App.Loc.S("whatsnew.intro");
        NotesText.Text = FormatNotes(notes.Body);
        NotesLink.Content = App.Loc.S("whatsnew.releaseNotes");
        NotesLink.NavigateUri = new Uri(notes.Url);
    }

    /// <summary>
    /// De release-body is markdown en wordt hier als platte tekst getoond. Een
    /// volwaardige markdown-renderer is voor een handvol bullets niet te
    /// rechtvaardigen - dat zou een dependency of een eigen parser kosten voor
    /// tekst die de gebruiker een keer per release leest.
    ///
    /// Opgeruimd worden alleen de vormen die anders als ruwe tekens in beeld
    /// staan: kop-hekjes, vette sterretjes, backticks, de horizontale streep en
    /// de streepjes-opsomming. De rest blijft letterlijk staan; half interpreteren
    /// is slechter dan niet interpreteren.
    /// </summary>
    private static string FormatNotes(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return App.Loc.S("whatsnew.empty");

        var sb = new StringBuilder();
        var blanks = 0;

        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            // Horizontale streep: in platte tekst betekenisloos.
            if (line.Trim().Length >= 3 && line.Trim().TrimStart('-').Length == 0) continue;

            line = line.TrimStart('#').TrimStart();
            line = line.Replace("**", string.Empty).Replace("`", string.Empty);

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal)
                || trimmed.StartsWith("* ", StringComparison.Ordinal))
                line = "\u2022 " + trimmed[2..];

            // Meer dan een lege regel achter elkaar levert een gat in de dialog op.
            if (line.Length == 0)
            {
                if (++blanks > 1) continue;
            }
            else
            {
                blanks = 0;
            }

            sb.AppendLine(line);
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? App.Loc.S("whatsnew.empty") : text;
    }
}
