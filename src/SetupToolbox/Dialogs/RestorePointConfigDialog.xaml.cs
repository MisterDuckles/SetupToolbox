using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SetupToolbox.Services;

namespace SetupToolbox.Dialogs;

// First-run popup voor Deep Clean / Debloat: vraagt user éénmalig of we een
// System Restore Point willen maken voor elke delete/uninstall-batch. Toont
// ook de huidige status van System Protection + laatste restore point zodat
// user weet wat te verwachten.
//
// Twee outcomes:
//   Primary   = ja, restore points aanmaken (setting → true)
//   Secondary = nee, overslaan (setting → false)
//
// Caller markt de "Configured" setting op true zodat deze dialog niet meer
// triggert. User kan altijd later via Settings > System Restore Points de
// toggle aanpassen.
public sealed partial class RestorePointConfigDialog : ContentDialog
{
    /// <summary>
    /// "Deep Clean" of "Debloat" — wordt in de dialoog-tekst gebruikt.
    /// </summary>
    public string OperationName { get; set; } = App.Loc.S("restorePoint.operationDefault");

    public RestorePointConfigDialog()
    {
        InitializeComponent();
        Opened += async (s, e) => await OnOpenedAsync();
    }

    private async System.Threading.Tasks.Task OnOpenedAsync()
    {
        // restorePoint.body bestond al sinds v1.2.3 maar deze call-site was toen
        // niet omgezet, waardoor de key drie versies lang ongebruikt bleef staan.
        ContextText.Text = App.Loc.S("restorePoint.body", OperationName);

        var status = await App.RestorePoint.GetStatusAsync();
        if (status.BlockedReason != null && status.BlockedReason.Contains("System Protection"))
        {
            StatusGlyph.Glyph = "";  // warning
            StatusGlyph.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            StatusText.Text = status.BlockedReason + App.Loc.S("restorePoint.protectionOffSuffix");
        }
        else if (status.HoursSinceLast.HasValue)
        {
            var ago = RestorePointService.FormatAgo(TimeSpan.FromHours(status.HoursSinceLast.Value));
            StatusGlyph.Glyph = "";  // checkmark
            StatusGlyph.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            StatusText.Text = App.Loc.S("restorePoint.lastWas", ago);
        }
        else
        {
            StatusGlyph.Glyph = "";
            StatusGlyph.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            StatusText.Text = App.Loc.S("restorePoint.noneYet");
        }
    }
}
