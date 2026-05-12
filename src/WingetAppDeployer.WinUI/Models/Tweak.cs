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
}

// Een toggle-able item in de Tweaks tab. Definitions worden statisch in
// TweakService.BuildAll() geregistreerd - apps.json-stijl data-driven, geen
// per-tweak UI-code.
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; }                          // stable ID voor presets/profiles later
    public TweakCategory Category { get; }
    public string Name { get; }                        // korte regel, mens-leesbaar
    public string Description { get; }                 // wat doet 't kort (1 zin)
    public string? UseCase { get; }                    // waarom willen power-users dit (optional)
    public IReadOnlyList<TweakOperation> Operations { get; }
    public RestartRequirement Restart { get; }

    public bool RequiresElevation => Operations.Any(o => o.RequiresElevation);

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

    public Tweak(
        string id,
        TweakCategory category,
        string name,
        string description,
        IReadOnlyList<TweakOperation> operations,
        RestartRequirement restart = RestartRequirement.None,
        string? useCase = null)
    {
        Id = id;
        Category = category;
        Name = name;
        Description = description;
        UseCase = useCase;
        Operations = operations;
        Restart = restart;
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
        TweakCategory.AdsBloat => "Ads & Bloat",
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
}
