using Microsoft.UI.Xaml.Controls;

namespace WingetAppDeployer_WinUI.Dialogs;

// Pre-Apply dialog die vraagt of er een snapshot moet worden gemaakt. Drie
// outcomes via ContentDialogResult:
//   Primary   = Backup maken en Apply (CustomName eventueel ingevuld)
//   Secondary = Apply zonder backup
//   None      = Annuleer (gebruiker klikte Close button of Esc)
//
// Plus DontAskAgain checkbox: als aangevinkt EN result == Primary, switcht
// BackupBeforeApply naar Always. Wanneer aangevinkt EN result == Secondary,
// switcht naar Never. Geeft de power-user een snelle escape om de dialog
// te skippen na de eerste keer.
public sealed partial class BackupPromptDialog : ContentDialog
{
    public BackupPromptDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// De door user opgegeven custom naam, of empty string als 't veld leeg
    /// bleef. SnapshotService genereert dan een auto-description.
    /// </summary>
    public string CustomName => NameBox.Text?.Trim() ?? string.Empty;

    /// <summary>
    /// Wanneer aangevinkt, slaat de gebruikers-keuze (backup vs no-backup) op
    /// als de nieuwe default (Always/Never) zodat de dialog niet meer komt.
    /// </summary>
    public bool DontAskAgain => DontAskCheckBox.IsChecked == true;
}
