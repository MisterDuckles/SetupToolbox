using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WingetAppDeployer_WinUI.Models;

// Categorieën voor de deep-clean scan. Twee top-level groepen:
//   1. Windows caches — predefined system/user paths waarvan we wéten dat ze
//      veilig (Temp / Update cache / Recycle Bin / Prefetch) of acceptabel
//      (browser caches) op te ruimen zijn. Geen detection nodig — de paden
//      zijn altijd dezelfde op Win10/11.
//   2. Orphaned folders — folders in Program Files / AppData die niet matchen
//      met enige geïnstalleerde app. Kandidaten voor "achtergebleven" data van
//      apps die ooit geïnstalleerd waren maar nu weg zijn.
public enum DeepCleanCategory
{
    UserTemp,           // %TEMP% — altijd veilig
    SystemTemp,         // %WINDIR%\Temp — admin nodig, altijd veilig
    UpdateCache,        // %WINDIR%\SoftwareDistribution\Download — admin, veilig
    Prefetch,           // %WINDIR%\Prefetch — admin, veilig (regenereert vanzelf)
    RecycleBin,         // Shell Recycle Bin — speciaal: niet via Directory.Delete
    WindowsOld,         // %WINDIR%.old — admin, caution (rollback-restpunt)
    BrowserCache,       // Edge / Chrome / Firefox / Brave caches — caution
    OrphanedFolder,     // Geen matchende installed app gevonden
    OrphanedRegistry,   // Uninstall registry-key waarvan alle paden dood zijn
    OrphanedAppPath,    // App Paths\<exe> entry waarvan exe niet meer bestaat
    OrphanedMuiCache,   // MUIcache value (recently-used) met dood pad
    OrphanedClassHandler, // File-extension/ProgID class waarvan shell-handler exe weg is
    OrphanedShortcut    // Start Menu .lnk met dood target
}

// Eén cleanup-target. Voor caches zijn DisplayName en Path vooraf bekend; voor
// orphaned folders detecteert DeepCleanService ze runtime. Size wordt lazy
// berekend tijdens de scan zodat de dialog meteen weet hoeveel ruimte vrijkomt.
public sealed class DeepCleanItem : INotifyPropertyChanged
{
    public string DisplayName { get; }
    public string Path { get; }
    public DeepCleanCategory Category { get; }
    public long SizeBytes { get; }
    public DateTime? LastModified { get; }
    public bool RequiresElevation { get; }
    // True wanneer regelmatig opruimen veilig is (Temp / Recycle / Prefetch /
    // Update cache). False voor categorieën met serieuze caveats — Windows.old
    // is je rollback-restpunt na een Windows-upgrade, browser caches betekenen
    // dat je opnieuw moet inloggen op sommige sites, orphaned folders kunnen
    // soms portable apps zijn die geen installer hebben (Spotify CLI, etc.).
    public bool IsSafe { get; }
    public string? Description { get; }

    // Voor MUIcache items: de value-name die uit de key verwijderd moet worden.
    // Path houdt dan de key zelf, RegistryValueName de specifieke value (kan
    // backslashes en spaties bevatten — daarom apart i.p.v. encoding in Path).
    // Voor andere categorieën null.
    public string? RegistryValueName { get; }

    public DeepCleanItem(
        string displayName,
        string path,
        DeepCleanCategory category,
        long sizeBytes,
        bool requiresElevation,
        bool isSafe,
        DateTime? lastModified = null,
        string? description = null,
        string? registryValueName = null)
    {
        DisplayName = displayName;
        Path = path;
        Category = category;
        SizeBytes = sizeBytes;
        RequiresElevation = requiresElevation;
        IsSafe = isSafe;
        LastModified = lastModified;
        Description = description;
        RegistryValueName = registryValueName;
        // Default checked alleen voor "altijd-veilige" items met content. Lege
        // folders (size 0) niet checken — dan zou user per ongeluk meedoen aan
        // een no-op delete. Caution-items (browser caches / Windows.old /
        // orphaned) altijd default uitgevinkt — user moet expliciet kiezen.
        _isSelected = isSafe && sizeBytes > 0;
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

    public string CategoryLabel => Category switch
    {
        DeepCleanCategory.UserTemp => "User temp",
        DeepCleanCategory.SystemTemp => "System temp",
        DeepCleanCategory.UpdateCache => "Update cache",
        DeepCleanCategory.Prefetch => "Prefetch",
        DeepCleanCategory.RecycleBin => "Recycle Bin",
        DeepCleanCategory.WindowsOld => "Windows.old",
        DeepCleanCategory.BrowserCache => "Browser cache",
        DeepCleanCategory.OrphanedFolder => "Orphaned folder",
        DeepCleanCategory.OrphanedRegistry => "Orphaned registry",
        DeepCleanCategory.OrphanedAppPath => "App Paths",
        DeepCleanCategory.OrphanedMuiCache => "MUIcache",
        DeepCleanCategory.OrphanedClassHandler => "Class handler",
        DeepCleanCategory.OrphanedShortcut => "Shortcut",
        _ => string.Empty
    };

    public Brush CategoryBadgeBrush => IsSafe
        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"]
        : (Brush)Application.Current.Resources["SystemFillColorCautionBackgroundBrush"];

    public string SizeLabel => SizeBytes <= 0
        ? "empty"
        : FormatBytes(SizeBytes);

    public string LastModifiedLabel => LastModified == null
        ? string.Empty
        : LastModified.Value.ToString("yyyy-MM-dd");

    public Visibility LastModifiedVisibility =>
        LastModified == null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DescriptionVisibility =>
        string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility ElevationVisibility =>
        RequiresElevation ? Visibility.Visible : Visibility.Collapsed;

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
