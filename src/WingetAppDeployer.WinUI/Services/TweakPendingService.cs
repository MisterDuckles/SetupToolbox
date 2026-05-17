using System;
using System.Collections.Generic;
using System.Linq;
using WingetAppDeployer_WinUI.Models;

namespace WingetAppDeployer_WinUI.Services;

// Cross-page pending-changes store voor de Tweaks-tab. Sinds de v0.9.8
// herstructurering is de Tweaks-tab een category-grid → detail-pagina flow
// (zoals de Apps-tab), dus pending changes moeten navigatie tussen de landing
// en de detail-pagina's overleven. Singleton via App.TweakPending.
//
// Value-semantiek per entry:
//   bool → toggle-tweak desired state (true = apply, false = revert)
//   int  → choice-tweak desired index
//
// Pages abonneren op Changed om hun footer (count / Apply-knop) te verversen.
public sealed class TweakPendingService
{
    private readonly Dictionary<Tweak, object> _pending = new();

    /// <summary>Aantal tweaks met een pending wijziging.</summary>
    public int Count => _pending.Count;

    /// <summary>Vuurt na elke mutatie (Set / Remove / Clear).</summary>
    public event Action? Changed;

    public void Set(Tweak tweak, object desired)
    {
        _pending[tweak] = desired;
        Changed?.Invoke();
    }

    public void Remove(Tweak tweak)
    {
        if (_pending.Remove(tweak)) Changed?.Invoke();
    }

    public void Clear()
    {
        if (_pending.Count == 0) return;
        _pending.Clear();
        Changed?.Invoke();
    }

    public bool TryGet(Tweak tweak, out object? desired)
    {
        var found = _pending.TryGetValue(tweak, out var v);
        desired = v;
        return found;
    }

    public bool Contains(Tweak tweak) => _pending.ContainsKey(tweak);

    /// <summary>Momentopname voor de Apply-runner — kopie zodat mutaties tijdens
    /// apply de iteratie niet breken.</summary>
    public IReadOnlyList<KeyValuePair<Tweak, object>> Snapshot() => _pending.ToList();

    /// <summary>Hoeveel pending changes binnen één categorie — voor de
    /// per-tile teller op de landing.</summary>
    public int CountInCategory(TweakCategory category) =>
        _pending.Keys.Count(t => t.Category == category);
}
