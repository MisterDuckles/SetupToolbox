using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace WingetAppDeployer_WinUI.Models;

// Categorie-buckets voor de Tweaks tab. UI groept tweaks per categorie als
// SettingsCard-rijen onder een header. Volgorde hier = volgorde op de page.
public enum TweakCategory
{
    Explorer,
    Taskbar,
    StartMenu,
    AdsBloat,
    AiCopilot,
    Privacy,
    UiTheme,
    Performance,
    ContextMenu,
    NotificationsLock,
    Updates,
    Gaming
}

// Wat moet de user doen na een tweak-apply voor het effect zichtbaar wordt.
// Volgorde van impact: None < ExplorerRestart < SignOut < Reboot. Voor multi-op
// tweaks pakt TweakService de hoogste eis van alle ops.
public enum RestartRequirement
{
    None,
    ExplorerRestart,
    SignOut,
    Reboot
}

// "Is deze tweak nu actief op het systeem?" Door bij page-load de actuele
// registry-waardes te lezen weet de UI exact wat aan/uit/half-aan staat.
//   Enabled  = ALLE ops staan in de EnabledValue-state
//   Disabled = ALLE ops staan in de DisabledValue-state
//   Partial  = mix - sommige ops al gedaan, andere nog niet (typisch bij
//              bundles als OFGB met 22 keys waar user er handmatig wat van had)
//   Unknown  = read mislukt (permissions / corrupt key) - UI grayed-out
public enum TweakState
{
    Disabled,
    Enabled,
    Partial,
    Unknown
}

// Read-only alternatief signaal dat ALLEEN voor state-detectie wordt gebruikt
// (nooit voor write/apply). Sommige tweaks kunnen via meerdere registry-paden
// effectief actief gemaakt worden — bv. Win11 Home heeft geen Settings UI
// voor HideRecommendedSection policy, maar wel een Start_IrisRecommendations
// toggle die hetzelfde visuele effect heeft. Als ÉÉN AlternateEnabledPath
// matcht → tweak telt als Enabled. Per-tweak research-driven (zie commit-log
// v0.9.4 "Multi-path detection" voor de bronnen).
//
// Value == null betekent "value MOET absent zijn voor deze signal-hit".
public sealed record TweakAlternateSignal
{
    public string Path { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;
    public object? EnabledValue { get; init; }
}

// Een registry-operatie binnen een tweak. Een tweak heeft 1..N ops; allemaal
// moeten in EnabledValue-state staan voor "tweak actief".
//
// Semantiek van EnabledValue / DisabledValue:
//   != null -> schrijf deze waarde naar (Path, ValueName)
//   == null -> de value of key MOET ABSENT zijn voor deze state (delete
//              tijdens apply, of detecteer als not-present tijdens read)
//
// DeleteKeyOnAbsent regelt: als we naar absent gaan, delete dan de hele key
// (DeleteSubKeyTree) i.p.v. alleen de value. Nodig voor tweaks zoals classic
// context menu - daar maken we een hele CLSID-subkey aan om de tweak te
// activeren, en bij revert willen we die parent-key opruimen.
//
// AlternateEnabledPaths: read-only signalen die ÓÓK als "tweak actief"
// gerekend worden. Zie TweakAlternateSignal. Schrijf-logica gebruikt nooit
// deze paden — alleen Path/ValueName.
//
// AbsenceMeans: wat moet detectie returnen als de primary value absent is en
// noch EnabledValue noch DisabledValue null is? Default Disabled (= de tweak
// is niet door ons aangezet en Windows zit in default-gedrag). Voor zeldzame
// tweaks waar Windows-default exact ONZE EnabledValue is, zet je dit op
// Enabled.
public sealed record TweakOperation
{
    public string Path { get; init; } = string.Empty;       // "HKCU\\Software\\..." of "HKLM\\..."
    public string ValueName { get; init; } = string.Empty;  // "" = (Default) value
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;
    public object? EnabledValue { get; init; }
    public object? DisabledValue { get; init; }
    public bool DeleteKeyOnAbsent { get; init; }
    // True wanneer HKLM (machine-scope) - moet via elevated batch.
    public bool RequiresElevation { get; init; }
    public IReadOnlyList<TweakAlternateSignal> AlternateEnabledPaths { get; init; }
        = Array.Empty<TweakAlternateSignal>();
    public TweakState AbsenceMeans { get; init; } = TweakState.Disabled;
}

// Een registry-set die hoort bij één gekozen optie in een multi-state tweak.
// Voorbeeld: bij Search-mode "Icon only" hoort Mode=1, Cache=1, ShowSearchBox=1.
// Value == null betekent "delete deze value" (Windows fallback default).
public sealed record TweakChoiceValue
{
    public string Path { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;
    public object? Value { get; init; }
    public bool RequiresElevation { get; init; }
}

// Een keuze in een multi-state tweak. Bijv. Search heeft 4 modes (Hide /
// Icon only / Search box / Icon + label) — elk een TweakChoice met een set
// (Path, ValueName, Value) writes die gezamenlijk die mode realiseren.
public sealed class TweakChoice
{
    public string Label { get; }            // displayed in ComboBox
    public string? Description { get; }     // optional caption onder de label
    public IReadOnlyList<TweakChoiceValue> Values { get; }

    public TweakChoice(string label, IReadOnlyList<TweakChoiceValue> values, string? description = null)
    {
        Label = label;
        Description = description;
        Values = values;
    }
}

// Een toggle-able item in de Tweaks tab. Definitions worden statisch in
// TweakService.BuildAll() geregistreerd - apps.json-stijl data-driven, geen
// per-tweak UI-code.
//
// Twee varianten:
//   1. Toggle (default): Operations met EnabledValue/DisabledValue.
//      TweaksPage rendert een ToggleSwitch.
//   2. Multi-choice: Choices met een TweakChoice per optie. TweaksPage
//      rendert een ComboBox die mirror't wat Windows Settings doet voor
//      multi-state opties (Search mode, Light/Dark, etc.).
// Een Tweak is óf toggle (Operations.Count > 0, Choices == null) óf choice
// (Operations is empty, Choices != null). Beide hebben dezelfde live state-
// detect + restart-handling lifecycle.
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; }                          // stable ID voor presets/profiles later
    public TweakCategory Category { get; }
    public string Name { get; }                        // korte regel, mens-leesbaar
    public string Description { get; }                 // wat doet 't kort (1 zin)
    public string? UseCase { get; }                    // waarom willen power-users dit (optional)
    public IReadOnlyList<TweakOperation> Operations { get; }
    public IReadOnlyList<TweakChoice>? Choices { get; }
    public RestartRequirement Restart { get; }
    // Optionele sub-groep BINNEN een categorie — puur cosmetisch voor de
    // TweaksPage-rendering (sub-headers in een grote categorie zoals UI/Theme).
    // null = geen sub-groep, tweak rendert plat. Apply/detect-logica leest dit
    // veld NOOIT — toevoegen verandert niets aan tweak-gedrag.
    public string? Group { get; }

    public bool IsChoice => Choices != null;
    public bool RequiresElevation =>
        Operations.Any(o => o.RequiresElevation) ||
        (Choices?.SelectMany(c => c.Values).Any(v => v.RequiresElevation) ?? false);

    // Voor choice-tweaks: de huidige geselecteerde index. -1 = "geen match"
    // (= user heeft een waarde die niet bij één van onze choices hoort —
    //  typisch direct na install of na een handmatige regedit-tweak).
    private int _selectedChoiceIndex = -1;
    public int SelectedChoiceIndex
    {
        get => _selectedChoiceIndex;
        set
        {
            if (_selectedChoiceIndex == value) return;
            _selectedChoiceIndex = value;
            OnChanged();
        }
    }

    // Backing voor live state - TweakService schrijft hier bij page-load.
    private TweakState _state = TweakState.Unknown;
    public TweakState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnChanged();
            OnChanged(nameof(IsToggleOn));
            OnChanged(nameof(IsPartial));
            OnChanged(nameof(StateLabel));
        }
    }

    // ToggleSwitch.IsOn binding-target. Partial telt visueel als "AAN" met
    // partial-indicator ernaast - user ziet dan dat het deels al staat.
    public bool IsToggleOn => _state == TweakState.Enabled || _state == TweakState.Partial;
    public bool IsPartial => _state == TweakState.Partial;
    public string StateLabel => _state switch
    {
        TweakState.Enabled => "Active",
        TweakState.Disabled => "Default",
        TweakState.Partial => "Partial",
        TweakState.Unknown => "Unknown",
        _ => string.Empty
    };

    public string AdminGlyph => RequiresElevation ? "" : string.Empty;  // Lock
    public string AdminTooltip => RequiresElevation ? "Vereist administrator (UAC)" : string.Empty;

    // Constructor voor toggle-tweaks (Operations met EnabledValue/DisabledValue).
    public Tweak(
        string id,
        TweakCategory category,
        string name,
        string description,
        IReadOnlyList<TweakOperation> operations,
        RestartRequirement restart = RestartRequirement.None,
        string? useCase = null,
        string? group = null)
    {
        Id = id;
        Category = category;
        Name = name;
        Description = description;
        UseCase = useCase;
        Operations = operations;
        Choices = null;
        Restart = restart;
        Group = group;
    }

    // Constructor voor multi-choice tweaks (ComboBox in UI, mirror van Windows
    // Settings options). Operations blijft leeg; alle writes komen uit de
    // geselecteerde Choice.Values.
    public Tweak(
        string id,
        TweakCategory category,
        string name,
        string description,
        IReadOnlyList<TweakChoice> choices,
        RestartRequirement restart = RestartRequirement.None,
        string? useCase = null,
        string? group = null)
    {
        Id = id;
        Category = category;
        Name = name;
        Description = description;
        UseCase = useCase;
        Operations = Array.Empty<TweakOperation>();
        Choices = choices;
        Restart = restart;
        Group = group;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// UI-helper voor category-grouping op de TweaksPage.
public static class TweakCategoryExtensions
{
    public static string DisplayName(this TweakCategory cat) => cat switch
    {
        TweakCategory.Explorer => "Explorer",
        TweakCategory.Taskbar => "Taskbar",
        TweakCategory.StartMenu => "Start Menu",
        TweakCategory.AdsBloat => "Ads & Tracking",
        TweakCategory.AiCopilot => "AI / Copilot",
        TweakCategory.Privacy => "Privacy",
        TweakCategory.UiTheme => "UI / Theme",
        TweakCategory.Performance => "Performance",
        TweakCategory.ContextMenu => "Context Menu",
        TweakCategory.NotificationsLock => "Notifications & Lock Screen",
        TweakCategory.Updates => "Updates",
        TweakCategory.Gaming => "Gaming",
        _ => cat.ToString()
    };

    // Emoji-icoon per categorie — gebruikt als tile-icoon op de Tweaks-landing,
    // consistent met de emoji-iconen van de Apps-tab category-tiles.
    public static string Icon(this TweakCategory cat) => cat switch
    {
        TweakCategory.Explorer => "\U0001F4C1",          // file folder
        TweakCategory.Taskbar => "\U0001F4CC",           // pushpin
        TweakCategory.StartMenu => "\U0001FA9F",         // window
        TweakCategory.AdsBloat => "\U0001F4E2",          // loudspeaker
        TweakCategory.AiCopilot => "\U0001F916",         // robot
        TweakCategory.Privacy => "\U0001F512",           // lock
        TweakCategory.UiTheme => "\U0001F3A8",           // artist palette
        TweakCategory.Performance => "⚡",           // high voltage
        TweakCategory.ContextMenu => "\U0001F5B1",       // computer mouse
        TweakCategory.NotificationsLock => "\U0001F514", // bell
        TweakCategory.Updates => "\U0001F504",           // counterclockwise arrows
        TweakCategory.Gaming => "\U0001F3AE",            // video game
        _ => "⚙"                                    // gear
    };

    // Korte één-regel omschrijving per categorie voor de landing-tiles.
    public static string Blurb(this TweakCategory cat) => cat switch
    {
        TweakCategory.Explorer => "File Explorer weergave & gedrag",
        TweakCategory.Taskbar => "Taskbar knoppen, zoekbalk & gedrag",
        TweakCategory.StartMenu => "Start menu layout & aanbevelingen",
        TweakCategory.AdsBloat => "Suggesties, ads & tracking-toggles",
        TweakCategory.AiCopilot => "Recall, Click to Do & AI-features",
        TweakCategory.Privacy => "Telemetrie, activity history & meer",
        TweakCategory.UiTheme => "Thema, kleuren, animaties & login",
        TweakCategory.Performance => "Opstart, opslag, netwerk & snelheid",
        TweakCategory.ContextMenu => "Rechtermuisknop-menu aanpassingen",
        TweakCategory.NotificationsLock => "Meldingen & vergrendelscherm",
        TweakCategory.Updates => "Windows Update gedrag",
        TweakCategory.Gaming => "Game DVR, Game Bar & Xbox-services",
        _ => string.Empty
    };
}

// View-model voor één category-tile op de Tweaks-landing. Wordt in code
// opgebouwd (counts zijn dynamisch — actief / pending) en aan de ItemsRepeater
// gebonden. x:Bind in de DataTemplate leest deze read-only properties.
public sealed class TweakCategoryTile
{
    public TweakCategory Category { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Blurb { get; init; } = string.Empty;
    // "3 / 9 actief" — getoond wanneer NIET alles is toegepast.
    public string CountLabel { get; init; } = string.Empty;
    // "2 pending" — leeg + collapsed wanneer geen pending changes.
    public string PendingLabel { get; init; } = string.Empty;
    public Microsoft.UI.Xaml.Visibility PendingVisible =>
        string.IsNullOrEmpty(PendingLabel)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    // Volledig-toegepast indicator: bij 100% een groene "Volledig actief"-pill,
    // anders de gewone CountLabel-tekst.
    public bool IsFullyApplied { get; init; }
    public Microsoft.UI.Xaml.Visibility FullPillVisible =>
        IsFullyApplied ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility CountVisible =>
        IsFullyApplied ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
}
