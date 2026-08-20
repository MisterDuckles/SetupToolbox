using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SetupToolbox.Services;
using AppModel = SetupToolbox.Models.App;

namespace SetupToolbox.Dialogs;

public sealed partial class InstallDialog : ContentDialog
{
    private readonly ObservableCollection<InstallItem> _items = new();
    private readonly IReadOnlyList<AppModel> _apps;

    // Vooraf gekozen installatiepaden voor requiresLocation-apps (Battle.net),
    // verzameld door LocationPrompt.CollectAsync vóór deze dialog opende. WinUI
    // staat geen geneste ContentDialog toe, dus we kunnen het pad niet hier vragen.
    private readonly IReadOnlyDictionary<string, string>? _locationPaths;
    private bool _installFinished;
    private int _completedCount;
    private int _wingetTotal;
    private string _parallelLabel = string.Empty;

    // v1.0.7: per-batch cancellation. Tijdens een lopende install draait de
    // Primary-knop als "Annuleren" en cancelt deze CTS → propageert door de hele
    // install-stack (kind/parent via cancel.flag, en in beide procesden naar
    // WingetService die z'n actieve winget-processen + installer-tree killt).
    private CancellationTokenSource? _cancelCts;

    // True wanneer minstens één winget install slaagde. Calling page gebruikt
    // dit om de post-install "Schedule auto-updates?" prompt alleen te tonen
    // als er ook iets te schedulen valt (geen prompt na 0 successes / alle
    // failed of alle skipped).
    public bool HadSuccessfulInstall { get; private set; }

    public InstallDialog(IReadOnlyList<AppModel> apps, IReadOnlyDictionary<string, string>? locationPaths = null)
    {
        InitializeComponent();
        _apps = apps;
        _locationPaths = locationPaths;

        foreach (var app in apps)
            _items.Add(new InstallItem(app.Name, app.WingetId));

        AppItemList.ItemsSource = _items;

        Opened += InstallDialog_Opened;
        Closing += InstallDialog_Closing;
        SecondaryButtonClick += InstallDialog_SecondaryButtonClick;
        PrimaryButtonClick += InstallDialog_PrimaryButtonClick;
    }

    // Primary-knop:
    //  - tijdens batch (!_installFinished) = "Annuleren" → cancel CTS, dialog open houden.
    //  - na batch = "Sluiten" → default-gedrag (dialog sluit).
    private void InstallDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_installFinished) return;            // Sluiten — laat de default-close door
        args.Cancel = true;                      // dialog open houden tijdens cancel-wind-down
        _cancelCts?.Cancel();
        PrimaryButtonText = App.Loc.S("common.cancelling");
        IsPrimaryButtonEnabled = false;
    }

    private async void InstallDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Manual-download apps (downloadUrl) eerst afhandelen — geen winget call,
        // gewoon URL openen. msstore apps gaan WEL via winget, maar met aangepaste
        // flags (zie WingetService.InstallAppAsync) — equivalent met handmatig
        // `winget install <productID>` runnen, wat dramatisch sneller is dan
        // `winget install --silent --source msstore`.
        var manualApps = _apps.Where(a => a.IsManualDownload).ToList();
        var wingetApps = _apps.Where(a => !a.IsManualDownload).ToList();
        var fallbackEnabled = App.Settings.FallbackToDownloadPage;

        foreach (var app in manualApps)
        {
            var item = _items.FirstOrDefault(i => i.WingetId == app.WingetId);
            if (item == null) continue;

            if (!fallbackEnabled)
            {
                item.Message = App.Loc.S("install.manualDisabled");
                item.State = InstallItemState.Skipped;
                continue;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = app.DownloadUrl,
                    UseShellExecute = true   // routes via shell → opent default browser
                });
                item.Message = App.Loc.S("install.openedVendorPage");
                item.State = InstallItemState.ManualOpened;
            }
            catch (Exception ex)
            {
                item.Message = App.Loc.S("install.couldNotOpenUrl", ex.Message);
                item.State = InstallItemState.Failed;
            }
        }

        if (wingetApps.Count > 0)
            await RunWingetInstallsAsync(wingetApps);
        else
            UpdateSummaryAndButtons();
    }

    // Draait een winget-install-batch (initieel of retry) en werkt daarna de
    // samenvatting + knoppen bij. Herbruikbaar zodat "Retry failed" exact dezelfde
    // weg volgt voor alléén de gefaalde apps.
    private async Task RunWingetInstallsAsync(IReadOnlyList<AppModel> apps)
    {
        var maxParallelism = App.Settings.MaxParallelInstalls;
        _parallelLabel = maxParallelism > 1 ? App.Loc.S("progress.parallelSuffix", maxParallelism) : string.Empty;
        _wingetTotal = apps.Count;
        _completedCount = 0;

        _installFinished = false;
        IsSecondaryButtonEnabled = false;

        // v1.0.7: Primary-knop tijdens batch = "Annuleren" (PrimaryButtonClick-handler
        // cancelt de CTS en houdt de dialog open). Na de batch zet UpdateSummaryAndButtons
        // de tekst weer terug naar "Sluiten".
        _cancelCts = new CancellationTokenSource();
        PrimaryButtonText = App.Loc.S("common.cancel");
        IsPrimaryButtonEnabled = true;

        ProgressHeader.Text = App.Loc.S("progress.installing", App.Loc.Plural("common.appCount", _wingetTotal), _parallelLabel);

        var progress = new Progress<InstallProgress>(OnProgress);

        // Splitsing (v1.0.10):
        //  - requiresUnelevated apps (Spotify) → PROACTIEF in-process onge-eleveerd.
        //    Hun installer weigert admin-context en faalt dan met een generieke/hash-
        //    fout i.p.v. een nette 0x8A150056 "prohibits elevation"-code, dus detectie-
        //    achteraf is onbetrouwbaar. Daarom nooit via de batch; meteen onge-eleveerd.
        //  - requiresLocation apps (Battle.net) → PROACTIEF pad-dialog + --location.
        //    Winget eerst laten falen en de fouttekst parsen is te fragiel (taal-/
        //    manifest-afhankelijk), dus we vragen de locatie vooraf.
        //  - overige community-winget-apps → ge-eleveerde runner: ÉÉN UAC voor de hele
        //    batch i.p.v. een prompt per machine-scope app. Bij geweigerde UAC vallen
        //    we terug op de in-process per-app flow zodat de user niet vastzit.
        //  - msstore-apps → blijven onge-eleveerd in-process: de Store-backend werkt
        //    niet betrouwbaar onder elevatie en prompt toch zelden om UAC.
        var msstore = apps.Where(IsMsStore).ToList();
        var unelevated = apps.Where(a => !IsMsStore(a) && a.RequiresUnelevated).ToList();
        var locationRequired = apps.Where(a => !IsMsStore(a) && !a.RequiresUnelevated && a.RequiresLocation).ToList();
        var community = apps.Where(a => !IsMsStore(a) && !a.RequiresUnelevated && !a.RequiresLocation).ToList();

        if (community.Count > 0)
        {
            // finalMessages bevat de definitieve uitkomst per app, gesynchroniseerd
            // geschreven in DrainProgress vóór de DispatcherQueue-dispatch. Gebruik
            // dit voor retry-scans i.p.v. _items: die worden bijgewerkt via
            // TryEnqueue (async) en kunnen nog pending zijn bij snelle batches.
            var (result, finalMessages) = await ElevatedInstallRunner.InstallAppsElevatedAsync(community, progress, maxParallelism, _cancelCts.Token);
            if (result == ElevatedRunResult.Completed)
            {
                // Vangnet-B: een community-app die de batch tóch met de prohibits-
                // elevation sentinel (0x8A150056) verliet → onge-eleveerd herproberen.
                // De bekende user-scope apps zitten al in `unelevated`; dit dekt de rest.
                var retryApps = community.Where(a =>
                    finalMessages.TryGetValue(a.WingetId, out var msg) &&
                    msg == WingetService.RequiresUnelevatedMessage
                ).ToList();

                if (retryApps.Count > 0 && !_cancelCts.IsCancellationRequested)
                {
                    foreach (var a in retryApps)
                    {
                        var it = _items.First(i => i.WingetId == a.WingetId);
                        it.Reset();
                        it.State = InstallItemState.Installing;
                        it.Message = App.Loc.S("install.retryUnelevated");
                    }
                    _completedCount -= retryApps.Count;   // worden opnieuw geteld in OnProgress
                    await App.Winget.InstallAppsAsync(retryApps, progress, maxParallelism, _cancelCts.Token);
                }

                // NB: géén location-vangnet ná de batch — een pad-dialog tonen terwijl
                // deze ContentDialog open staat crasht WinUI ("only a single ContentDialog
                // can be open"). Apps die een locatie nodig hebben moeten in apps.json
                // `requiresLocation: true` krijgen; LocationPrompt vraagt het pad dan vooraf
                // (vóór deze dialog opent) en geeft het door via `_locationPaths`.
            }
            else
            {
                // UAC geweigerd / elevatie mislukt → terugvallen op in-process flow.
                await App.Winget.InstallAppsAsync(community, progress, maxParallelism, _cancelCts.Token);
            }
        }

        // Spotify e.a.: proactief in-process onge-eleveerd installeren.
        if (unelevated.Count > 0 && !_cancelCts.IsCancellationRequested)
            await App.Winget.InstallAppsAsync(unelevated, progress, maxParallelism, _cancelCts.Token);

        // Battle.net e.a.: installeren met het vooraf gekozen pad (LocationPrompt
        // draaide vóór deze dialog open ging). Geen pad gekozen → app overslaan.
        foreach (var a in locationRequired)
        {
            if (_cancelCts.IsCancellationRequested) break;
            await InstallWithLocationAsync(a);
        }

        if (msstore.Count > 0)
            await App.Winget.InstallAppsAsync(msstore, progress, maxParallelism, _cancelCts.Token);

        _cancelCts.Dispose();
        _cancelCts = null;

        // Knop terug naar "Sluiten" (default close-gedrag via de Primary-handler).
        PrimaryButtonText = App.Loc.S("common.close");

        UpdateSummaryAndButtons();
    }

    // E1: installeert een requiresLocation-app in-process met `--location <pad>`.
    // Het pad is vooraf gekozen via LocationPrompt (vóór deze dialog open ging — WinUI
    // staat geen geneste ContentDialog toe). Geen pad in _locationPaths = user heeft
    // overgeslagen → app als Skipped markeren. Deze apps gaan nooit door de elevated
    // batch, dus ze zijn nog niet geteld; we tellen ze hier exact één keer.
    private async Task InstallWithLocationAsync(AppModel a)
    {
        var item = _items.First(i => i.WingetId == a.WingetId);

        if (_locationPaths == null
            || !_locationPaths.TryGetValue(a.WingetId, out var chosenPath)
            || string.IsNullOrWhiteSpace(chosenPath))
        {
            item.State = InstallItemState.Skipped;
            item.Message = App.Loc.S("install.skippedNoLocation");
            _completedCount++;
            ProgressHeader.Text = App.Loc.S("progress.installingOf", _completedCount, _wingetTotal, _parallelLabel);
            return;
        }

        item.State = InstallItemState.Installing;
        item.AdvanceStage(1);
        item.Message = App.Loc.S("install.installingToLocation");

        var locationProgress = new Progress<string>(msg =>
            OnProgress(new InstallProgress(0, _wingetTotal, a, InstallPhase.Running, msg)));

        var (locSuccess, locMessage) = await App.Winget.InstallAppAsync(
            a.WingetId, locationProgress, a.Source, _cancelCts!.Token, chosenPath, a.Name);

        var locPhase = !locSuccess ? InstallPhase.Failed
            : locMessage == WingetService.AlreadyInstalledMessage ? InstallPhase.AlreadyInstalled
            : InstallPhase.Success;
        OnProgress(new InstallProgress(0, _wingetTotal, a, locPhase, locMessage));
    }

    private static bool IsMsStore(AppModel a) =>
        string.Equals(a.Source, "msstore", StringComparison.OrdinalIgnoreCase);

    // Samenvatting + knopstaat afgeleid uit _items (cumulatief over de initiële run
    // én retries). "Retry failed" verschijnt alleen als er nog winget-apps gefaald zijn.
    private void UpdateSummaryAndButtons()
    {
        var manualIds = _apps.Where(a => a.IsManualDownload).Select(a => a.WingetId).ToHashSet();
        var installed = _items.Count(i => i.State == InstallItemState.Success);
        var already = _items.Count(i => i.State == InstallItemState.AlreadyInstalled);
        var failed = _items.Count(i => i.State == InstallItemState.Failed);
        var failedWinget = _items.Count(i => i.State == InstallItemState.Failed && !manualIds.Contains(i.WingetId));
        var manualOpened = _items.Count(i => i.State == InstallItemState.ManualOpened);
        var openedInStore = _items.Count(i => i.State == InstallItemState.OpenedInStore);
        var skipped = _items.Count(i => i.State == InstallItemState.Skipped);

        // openedInStore telt mee voor HadSuccessfulInstall: we hebben de install
        // succesvol getriggerd via de Store-app; user moet alleen nog op "Halen"
        // klikken. De "Schedule auto-updates?"-prompt is dan ook relevant.
        HadSuccessfulInstall = installed > 0 || already > 0 || openedInStore > 0;

        var parts = new List<string>();
        if (installed > 0)     parts.Add(App.Loc.S("summary.installed", installed));
        if (already > 0)       parts.Add(App.Loc.S("summary.alreadyInstalled", already));
        if (failed > 0)        parts.Add(App.Loc.S("summary.failed", failed));
        if (manualOpened > 0)  parts.Add(App.Loc.S("summary.manualOpened", manualOpened));
        if (openedInStore > 0) parts.Add(App.Loc.S("summary.openedInStore", openedInStore));
        if (skipped > 0)       parts.Add(App.Loc.S("summary.skipped", skipped));
        ProgressHeader.Text = parts.Count > 0 ? string.Join(", ", parts) : App.Loc.S("progress.noInstallable");

        _installFinished = true;
        IsPrimaryButtonEnabled = true;

        if (failedWinget > 0)
        {
            SecondaryButtonText = failedWinget == 1 ? App.Loc.S("install.retryOne") : App.Loc.S("install.retryMany", failedWinget);
            IsSecondaryButtonEnabled = true;
        }
        else
        {
            SecondaryButtonText = string.Empty; // lege tekst verbergt de Secondary-knop
        }
    }

    // "Retry failed": reset de gefaalde winget-items en draai alléén die opnieuw,
    // zonder de dialog te sluiten.
    private async void InstallDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // dialog open houden — we herproberen in-place

        var failedIds = _items.Where(i => i.State == InstallItemState.Failed)
                              .Select(i => i.WingetId).ToHashSet();
        var failedApps = _apps.Where(a => !a.IsManualDownload && failedIds.Contains(a.WingetId)).ToList();
        if (failedApps.Count == 0) return;

        foreach (var item in _items.Where(i => failedIds.Contains(i.WingetId)))
            item.Reset();

        await RunWingetInstallsAsync(failedApps);
    }

    private void OnProgress(InstallProgress p)
    {
        // Progress<T> captures SyncContext at construction, but WinUI 3 Desktop
        // doesn't always have a SyncContext on the UI thread. Marshal explicitly
        // via DispatcherQueue to be safe — INPC must fire on the UI thread.
        if (DispatcherQueue.HasThreadAccess)
            ApplyProgress(p);
        else
            DispatcherQueue.TryEnqueue(() => ApplyProgress(p));
    }

    private void ApplyProgress(InstallProgress p)
    {
        // Progress-callbacks lopen via Progress<T>.Post op de UI-thread. Een exception
        // hier (transiente WinUI binding-/layout-fout bij snelle property-updates) is
        // anders ONGEVANGEN: ze propageert niet terug naar de drain-loop maar kilt de
        // app — precies het TreeSize-crash-patroon (install slaagde, app verdween vóór
        // RUNNER EXIT). Inkapselen + loggen zodat een UI-hik nooit een install nekt.
        try
        {
            ApplyProgressCore(p);
        }
        catch (Exception ex)
        {
            Helpers.Diagnostics.Log("install.log",
                $"APPLYPROGRESS-EX {p.App.WingetId} phase={p.Phase}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ApplyProgressCore(InstallProgress p)
    {
        var item = _items.FirstOrDefault(i => i.WingetId == p.App.WingetId);
        if (item == null) return;

        var msg = string.IsNullOrWhiteSpace(p.Message) ? string.Empty : p.Message.Trim();

        // v1.2.8.1: WAT JE ZIET is de vertaalde regel, WAAROP GEMATCHT WORDT is de
        // ruwe. Die regels komen tijdens de install rechtstreeks uit winget's stdout
        // en zijn dus Engels ongeacht onze taalkeuze. De stage-detectie hieronder
        // matcht bewust nog op `msg` — vertalen vóór die switch laat de ring staan.
        item.Message = WingetService.FriendlyOutputLine(msg);

        switch (p.Phase)
        {
            case InstallPhase.Starting:
                // Preparing is too fast to visualize — start the ring straight at
                // Downloading (1/4). If winget's "Found / license headers" flash
                // by quickly the user still sees a consistent stage-1 marker.
                item.State = InstallItemState.Installing;
                item.AdvanceStage(1); // Downloading
                break;
            case InstallPhase.Running:
                // Stages: 1 Downloading → 2 Verifying → 3 Installing → 4 Done.
                // AdvanceStage never moves the stage backwards.
                if (msg.Contains("Starting package install", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Installing package", StringComparison.OrdinalIgnoreCase))
                    item.AdvanceStage(3);
                else if (msg.Contains("installer hash", StringComparison.OrdinalIgnoreCase))
                    item.AdvanceStage(2);
                // else: stay at current stage (1 = Downloading) — the "X MB / Y MB"
                // lines flow by here and don't advance, they just confirm we're
                // still downloading.
                break;
            case InstallPhase.Success:
                item.State = InstallItemState.Success;
                item.AdvanceStage(4); // Done — ring is hidden anyway, checkmark takes over
                _completedCount++;
                break;
            case InstallPhase.AlreadyInstalled:
                item.State = InstallItemState.AlreadyInstalled;
                item.AdvanceStage(4);
                _completedCount++;
                break;
            case InstallPhase.OpenedInStore:
                item.State = InstallItemState.OpenedInStore;
                item.AdvanceStage(4);
                _completedCount++;
                break;
            case InstallPhase.Failed:
                item.State = InstallItemState.Failed;
                _completedCount++;
                break;
        }

        // Bij parallel mode is "X of Y: Name" verwarrend (meerdere apps lopen
        // tegelijk, name flikkert tussen winners). Tonen we count-based:
        // "Installing 3 of 8 done (2 in parallel)". Bij sequential is het
        // gedrag effectief identiek aan de oude per-app header.
        ProgressHeader.Text = App.Loc.S("progress.installingOf", _completedCount, _wingetTotal, _parallelLabel);
    }

    private void InstallDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Prevent closing while the install is still running.
        if (!_installFinished) args.Cancel = true;
    }
}

public enum InstallItemState { Pending, Installing, Success, AlreadyInstalled, Failed, ManualOpened, OpenedInStore, Skipped }

public sealed class InstallItem : INotifyPropertyChanged
{
    public const int TotalStages = 4;

    private InstallItemState _state = InstallItemState.Pending;
    private string _message = string.Empty;
    private int _stage;

    public InstallItem(string name, string wingetId)
    {
        Name = name;
        WingetId = wingetId;
    }

    public string Name { get; }
    public string WingetId { get; }

    public InstallItemState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnChanged();
            OnChanged(nameof(StateLabel));
            OnChanged(nameof(StateLabelBrush));
            OnChanged(nameof(PendingRingVisibility));
            OnChanged(nameof(BarVisibility));
            OnChanged(nameof(StageRingVisibility));
            OnChanged(nameof(CheckVisibility));
            OnChanged(nameof(ErrorVisibility));
            OnChanged(nameof(MessageVisibility));
            OnChanged(nameof(StateLabelVisibility));
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            var v = value ?? string.Empty;
            if (_message == v) return;
            _message = v;
            OnChanged();
            OnChanged(nameof(MessageVisibility));
        }
    }

    public int Stage
    {
        get => _stage;
        private set
        {
            var v = Math.Clamp(value, 0, TotalStages);
            if (_stage == v) return;
            _stage = v;
            OnChanged();
            OnChanged(nameof(StageValue));
            OnChanged(nameof(StageLabel));
            OnChanged(nameof(StageText));
        }
    }

    // Ladder-style advance: stage only moves forward, never backwards.
    public void AdvanceStage(int target) => Stage = Math.Max(_stage, target);

    // Reset naar begintoestand zodat een retry de kaart opnieuw door de fases laat lopen.
    public void Reset()
    {
        Message = string.Empty;
        Stage = 0;
        State = InstallItemState.Pending;
    }

    // ProgressRing value — Maximum is TotalStages so the ring fills fully at stage 4.
    public double StageValue => _stage;

    public string StageText => _stage == 0 ? string.Empty : $"{_stage}/{TotalStages}";

    public string StageLabel => _stage switch
    {
        1 => App.Loc.S("install.stage.downloading"),
        2 => App.Loc.S("install.stage.verifying"),
        3 => App.Loc.S("install.stage.installing"),
        4 => App.Loc.S("install.stage.done"),
        _ => string.Empty
    };

    // One visibility per visual element so x:Bind always has a clean signal.
    public Visibility PendingRingVisibility =>
        _state == InstallItemState.Pending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BarVisibility =>
        _state == InstallItemState.Installing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StageRingVisibility =>
        _state == InstallItemState.Installing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckVisibility =>
        _state is InstallItemState.Success or InstallItemState.AlreadyInstalled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility =>
        _state == InstallItemState.Failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility =>
        (_state == InstallItemState.Installing || _state == InstallItemState.ManualOpened || _state == InstallItemState.OpenedInStore || _state == InstallItemState.Skipped) && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    // Text state label (right column) only shown outside the Installing phase;
    // during install the stage ring occupies that column instead.
    public Visibility StateLabelVisibility =>
        _state is InstallItemState.Pending or InstallItemState.Success or InstallItemState.AlreadyInstalled or InstallItemState.Failed or InstallItemState.ManualOpened or InstallItemState.OpenedInStore or InstallItemState.Skipped
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        InstallItemState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        InstallItemState.AlreadyInstalled => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        InstallItemState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        InstallItemState.ManualOpened => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        InstallItemState.OpenedInStore => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        InstallItemState.Skipped => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    // v1.2.7: stond hier als hardcoded switch en mengde beide talen — vijf armen
    // Engels, twee Nederlands. Losstaand van WingetService.AlreadyInstalledMessage:
    // die blijft een const, want dat is een sentinel die vergeleken wordt en niet
    // getoond (MessageVisibility staat uit voor de AlreadyInstalled-state).
    public string StateLabel => _state switch
    {
        InstallItemState.Pending => App.Loc.S("install.state.waiting"),
        InstallItemState.Success => App.Loc.S("install.state.installed"),
        InstallItemState.AlreadyInstalled => App.Loc.S("install.state.alreadyInstalled"),
        InstallItemState.Failed => App.Loc.S("install.state.failed"),
        InstallItemState.ManualOpened => App.Loc.S("install.state.browserOpened"),
        InstallItemState.OpenedInStore => App.Loc.S("install.state.openedInStore"),
        InstallItemState.Skipped => App.Loc.S("install.state.skipped"),
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
