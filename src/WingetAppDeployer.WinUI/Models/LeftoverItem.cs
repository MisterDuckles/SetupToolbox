using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WingetAppDeployer_WinUI.Models;

// Categorie van een leftover. Bepaalt het delete-mechanisme én de UI-styling
// (icon + caption). Volgorde matcht hoe we ze in de dialog groeperen.
public enum LeftoverType
{
    // Orphaned uninstall key in HKLM/HKCU\\...\\Uninstall — installer heeft de
    // key niet opgeruimd. Hoog vertrouwen, klein risico bij delete (alleen UI
    // in Programs and Features blijft verwijst, geen runtime impact).
    RegistryKey,

    // Map in Program Files (x86)/Program Files met overgebleven binaries / data.
    // Vereist admin rechten om te verwijderen.
    ProgramFilesFolder,

    // Map in %LOCALAPPDATA% / %APPDATA% / %PROGRAMDATA%. User-data, configs,
    // caches. Lager vertrouwen — sommige users willen settings bewaren voor
    // herinstallatie. Default uitgevinkt.
    AppDataFolder,

    // App Paths registry-entry (HKLM/HKCU\...\App Paths\<exe>) voor de
    // verwijderde app's exe. Window's "where is this exe" register.
    AppPath,

    // MUIcache value (HKCU shell cache) — Windows onthoudt de friendly-name
    // van laatst-gestarte exes. Per app vaak meerdere values (per exe één).
    MuiCache,

    // File-extension class handler in `HKLM/HKCU\Software\Classes\Applications`
    // — broken file-association registry leftover.
    ClassHandler,

    // Start Menu / Desktop .lnk shortcut die naar verwijderde exe wijst.
    Shortcut
}

// Hoe zeker zijn we dat dit echt leftover is van de zojuist verwijderde app?
// Bepaalt of het item default aangevinkt staat in de cleanup-dialog.
public enum LeftoverConfidence
{
    // Exact-match op DisplayName / publisher path. Hoge zekerheid dat dit van
    // de verwijderde app is. Default checked.
    High,

    // Substring of fuzzy match (bv. publisher folder bevat app-naam). Default
    // unchecked — user moet expliciet kiezen.
    Medium,

    // Brede match (alleen publisher folder match, niet app-specifiek). Default
    // unchecked. Vooral relevant voor "vendor" folders die meerdere apps bevatten.
    Low
}

// Een individueel leftover item dat de scanner heeft gevonden. Wordt in de
// cleanup-dialog getoond met checkbox + path + size, gegroepeerd per
// LeftoverType.
public sealed class LeftoverItem : INotifyPropertyChanged
{
    public string Path { get; }
    public LeftoverType Type { get; }
    public LeftoverConfidence Confidence { get; }
    public string SourceAppName { get; }
    public long SizeBytes { get; }
    public bool RequiresElevation { get; }

    // Voor MUIcache-items: de specifieke value-name die uit de key verwijderd
    // moet worden. Path houdt de key zelf, RegistryValueName de value (kan
    // backslashes/spaties bevatten; daarom apart). Voor andere types null.
    public string? RegistryValueName { get; }

    public LeftoverItem(
        string path,
        LeftoverType type,
        LeftoverConfidence confidence,
        string sourceAppName,
        long sizeBytes,
        bool requiresElevation,
        string? registryValueName = null)
    {
        Path = path;
        Type = type;
        Confidence = confidence;
        SourceAppName = sourceAppName;
        SizeBytes = sizeBytes;
        RequiresElevation = requiresElevation;
        RegistryValueName = registryValueName;
        // High-confidence orphans default checked; medium/low laat user kiezen.
        _isSelected = confidence == LeftoverConfidence.High;
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnChanged();
        }
    }

    // ── UI helpers ─────────────────────────────────────────────────

    public string TypeBadgeText => Type switch
    {
        LeftoverType.RegistryKey => "Registry",
        LeftoverType.ProgramFilesFolder => "Program Files",
        LeftoverType.AppDataFolder => "AppData",
        LeftoverType.AppPath => "App Paths",
        LeftoverType.MuiCache => "MUIcache",
        LeftoverType.ClassHandler => "Class handler",
        LeftoverType.Shortcut => "Shortcut",
        _ => string.Empty
    };

    public Brush TypeBadgeBrush => Type switch
    {
        LeftoverType.RegistryKey => (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
        LeftoverType.ProgramFilesFolder => (Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"],
        LeftoverType.AppDataFolder => (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"],
        LeftoverType.AppPath => (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
        LeftoverType.MuiCache => (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
        LeftoverType.ClassHandler => (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
        LeftoverType.Shortcut => (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"],
        _ => (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"]
    };

    public string ConfidenceLabel => Confidence switch
    {
        LeftoverConfidence.High => "High match",
        LeftoverConfidence.Medium => "Possible match",
        LeftoverConfidence.Low => "Loose match",
        _ => string.Empty
    };

    public string SizeLabel => SizeBytes <= 0
        ? string.Empty
        : FormatBytes(SizeBytes);

    public Visibility SizeVisibility => SizeBytes > 0 ? Visibility.Visible : Visibility.Collapsed;

    // Voor de hint-tekst bij de footer ("X of Y require admin rights").
    public Visibility ElevationBadgeVisibility => RequiresElevation ? Visibility.Visible : Visibility.Collapsed;

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int unitIdx = -1;
        do { v /= 1024; unitIdx++; } while (v >= 1024 && unitIdx < units.Length - 1);
        return v >= 100 ? $"{v:0} {units[unitIdx]}" : $"{v:0.#} {units[unitIdx]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Lichtgewicht referentie naar een zojuist-verwijderde app, gebruikt door de
// LeftoverScanner om te weten waar te zoeken. Houdt bewust niet de zware
// InstalledAppEntry / BloatwareItem / App vast — die hebben een UI-binding-leven
// en passen niet bij een puur scan-input record.
public sealed record UninstalledAppRef(
    string DisplayName,
    string? Publisher,
    string? PackageName,    // AppX Name uit Get-AppxPackage (zonder version-suffix)
    string? WingetId);      // Voor winget/catalog uninstalls (bv. "Mozilla.Firefox")
