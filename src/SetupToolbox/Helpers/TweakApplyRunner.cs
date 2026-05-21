using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SetupToolbox.Dialogs;
using SetupToolbox.Models;
using SetupToolbox.Services;

namespace SetupToolbox.Helpers;

// Gedeelde apply-orchestratie voor de Tweaks-tab. Sinds de v0.9.8
// herstructurering staat de Apply-knop in een globale footer die op zowel de
// landing als elke category-detail-pagina zit — daarom de apply-flow centraal
// i.p.v. per-page gedupliceerd.
//
// Verantwoordelijk voor: backup-prompt (BackupPromptDialog / BackupBeforeApply
// setting), het batchen van toggle-tweaks per richting (1 UAC voor de hele
// elevated subset), choice-tweaks, de explorer-restart aan het einde, en het
// re-detecten van states. De caller toont de loading-overlay (via onWorkStarting)
// en de ResultBar (op basis van de Outcome).
public static class TweakApplyRunner
{
    public sealed record Outcome(
        bool DidRun,                            // false = user annuleerde bij de backup-prompt
        int SuccessCount,
        int FailedCount,
        bool Cancelled,                         // UAC geweigerd
        IReadOnlyList<string> FailureMessages,
        bool AnyExplorerRestart,
        bool AnySignOut,
        int ChangeCount);                       // aantal tweaks dat geprobeerd is

    /// <summary>
    /// Voert alle pending changes uit App.TweakPending uit. onWorkStarting wordt
    /// aangeroepen zodra de backup-prompt voorbij is en de echte registry-writes
    /// beginnen — caller toont op dat moment de loading-overlay.
    /// </summary>
    public static async Task<Outcome> RunAsync(XamlRoot xamlRoot, Action? onWorkStarting = null)
    {
        var snapshot = App.TweakPending.Snapshot();
        if (snapshot.Count == 0)
            return new Outcome(false, 0, 0, false, Array.Empty<string>(), false, false, 0);

        var pendingTweaks = snapshot.Select(kv => kv.Key).ToList();

        // ── Backup-flow ────────────────────────────────────────────
        var mode = App.Settings.BackupBeforeApply;
        var captureSnapshot = false;
        var snapshotName = string.Empty;

        if (mode == BackupBeforeApplyMode.Ask)
        {
            var prompt = new BackupPromptDialog { XamlRoot = xamlRoot };
            var result = await prompt.ShowAsync();
            if (result == ContentDialogResult.None)
                return new Outcome(false, 0, 0, false, Array.Empty<string>(), false, false, 0);

            captureSnapshot = result == ContentDialogResult.Primary;
            snapshotName = prompt.CustomName;
            if (prompt.DontAskAgain)
            {
                App.Settings.BackupBeforeApply = captureSnapshot
                    ? BackupBeforeApplyMode.Always
                    : BackupBeforeApplyMode.Never;
            }
        }
        else if (mode == BackupBeforeApplyMode.Always)
        {
            captureSnapshot = true;
        }

        onWorkStarting?.Invoke();

        // Snapshot vóór de eerste write — anders zit de "voor"-state al
        // gepollueerd. Faalt 't, dan gewoon door (vangnet, geen blokkade).
        if (captureSnapshot)
        {
            try { await App.Snapshots.CaptureAsync(pendingTweaks, snapshotName); }
            catch { /* snapshot is best-effort */ }
        }

        var totalSuccess = 0;
        var totalFailed = 0;
        var cancelled = false;
        var anyExplorerRestart = false;
        var anySignOut = false;
        var failureLines = new List<string>();

        // Buckets — toggle-tweaks batchen per richting (ApplyAsync verzamelt
        // alle elevated ops in 1 RunElevatedBatchAsync = 1 UAC).
        var toggleApply = new List<Tweak>();
        var toggleRevert = new List<Tweak>();
        var choiceChanges = new List<(Tweak tweak, int idx)>();
        foreach (var (tweak, desired) in snapshot)
        {
            if (tweak.IsChoice && desired is int idx)
                choiceChanges.Add((tweak, idx));
            else if (desired is bool apply)
                (apply ? toggleApply : toggleRevert).Add(tweak);
        }

        async Task RunToggleBatch(IReadOnlyList<Tweak> tweaks, bool apply)
        {
            if (tweaks.Count == 0) return;
            var r = await App.Tweaks.ApplyAsync(tweaks, apply);
            totalSuccess += r.SuccessCount;
            totalFailed += r.FailedCount;
            if (r.Cancelled) cancelled = true;
            foreach (var t in tweaks)
            {
                if (t.Restart == RestartRequirement.ExplorerRestart) anyExplorerRestart = true;
                if (r.SuccessCount > 0 &&
                    (t.Restart == RestartRequirement.SignOut || t.Restart == RestartRequirement.Reboot))
                    anySignOut = true;
            }
            failureLines.AddRange(r.FailureMessages);
        }

        await RunToggleBatch(toggleApply, apply: true);
        await RunToggleBatch(toggleRevert, apply: false);

        foreach (var (tweak, idx) in choiceChanges)
        {
            var r = await App.Tweaks.ApplyChoiceAsync(tweak, idx);
            totalSuccess += r.SuccessCount;
            totalFailed += r.FailedCount;
            if (r.Cancelled) cancelled = true;
            if (tweak.Restart == RestartRequirement.ExplorerRestart) anyExplorerRestart = true;
            if (r.SuccessCount > 0 &&
                (tweak.Restart == RestartRequirement.SignOut || tweak.Restart == RestartRequirement.Reboot))
                anySignOut = true;
            foreach (var msg in r.FailureMessages)
                failureLines.Add($"{tweak.Name} → {msg}");
        }

        // Eén explorer-restart aan het einde voor shell-cached tweaks.
        if (anyExplorerRestart)
        {
            await Task.Delay(250);
            ShellRefresh.RestartExplorerSilent();
        }

        // States re-detecten zodat de UI de nieuwe waardes toont, dan pending op.
        await App.Tweaks.DetectStatesAsync();
        App.TweakPending.Clear();

        return new Outcome(true, totalSuccess, totalFailed, cancelled, failureLines,
            anyExplorerRestart, anySignOut, snapshot.Count);
    }

    /// <summary>
    /// Vult een ResultBar (InfoBar) op basis van een apply-Outcome. Gedeeld
    /// door de landing en de detail-pagina zodat de feedback consistent is.
    /// </summary>
    public static void ShowOutcome(InfoBar bar, Outcome o)
    {
        if (!o.DidRun) return;

        if (o.Cancelled)
        {
            bar.Severity = InfoBarSeverity.Warning;
            bar.Title = "Apply geannuleerd";
            bar.Message = "UAC werd geweigerd — een deel van de wijzigingen kan al geschreven zijn.";
        }
        else if (o.FailedCount == 0)
        {
            bar.Severity = InfoBarSeverity.Success;
            bar.Title = $"{o.ChangeCount} wijziging{(o.ChangeCount == 1 ? "" : "en")} toegepast";
            bar.Message = o.AnySignOut
                ? "Sommige tweaks hebben pas effect nadat je uitlogt of de PC herstart."
                : o.AnyExplorerRestart
                    ? "Explorer herstart — wacht ~1s tot de taskbar terug is."
                    : "Klaar.";
        }
        else
        {
            bar.Severity = InfoBarSeverity.Error;
            bar.Title = $"{o.FailedCount} wijziging(en) mislukt";
            var preview = o.FailureMessages.Take(4).ToList();
            var more = o.FailureMessages.Count - preview.Count;
            var body = string.Join("\n", preview);
            if (more > 0) body += $"\n…en {more} meer.";
            if (o.SuccessCount > 0) body += $"\n({o.SuccessCount} writes wel succesvol)";
            bar.Message = body;
        }
        bar.IsOpen = true;
    }
}
