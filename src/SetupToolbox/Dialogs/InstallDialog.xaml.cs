using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
    private bool _installFinished;
    private int _completedCount;
    private int _wingetTotal;
    private string _parallelLabel = string.Empty;

    // True wanneer minstens één winget install slaagde. Calling page gebruikt
    // dit om de post-install "Schedule auto-updates?" prompt alleen te tonen
    // als er ook iets te schedulen valt (geen prompt na 0 successes / alle
    // failed of alle skipped).
    public bool HadSuccessfulInstall { get; private set; }

    public InstallDialog(IReadOnlyList<AppModel> apps)
    {
        InitializeComponent();
        _apps = apps;

        foreach (var app in apps)
            _items.Add(new InstallItem(app.Name, app.WingetId));

        AppItemList.ItemsSource = _items;

        Opened += InstallDialog_Opened;
        Closing += InstallDialog_Closing;
        SecondaryButtonClick += InstallDialog_SecondaryButtonClick;
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
                item.Message = "Manual downloads disabled in Settings";
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
                item.Message = "Opened vendor download page in browser";
                item.State = InstallItemState.ManualOpened;
            }
            catch (Exception ex)
            {
                item.Message = $"Could not open URL: {ex.Message}";
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
        _parallelLabel = maxParallelism > 1 ? $" ({maxParallelism} in parallel)" : string.Empty;
        _wingetTotal = apps.Count;
        _completedCount = 0;

        _installFinished = false;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        ProgressHeader.Text = $"Installing {_wingetTotal} app{(_wingetTotal == 1 ? "" : "s")}{_parallelLabel}";

        var progress = new Progress<InstallProgress>(OnProgress);
        await App.Winget.InstallAppsAsync(apps, progress, maxParallelism);

        UpdateSummaryAndButtons();
    }

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
        var skipped = _items.Count(i => i.State == InstallItemState.Skipped);

        HadSuccessfulInstall = installed > 0 || already > 0;

        var parts = new List<string>();
        if (installed > 0)    parts.Add($"{installed} installed");
        if (already > 0)      parts.Add($"{already} already installed");
        if (failed > 0)       parts.Add($"{failed} failed");
        if (manualOpened > 0) parts.Add($"{manualOpened} manual download{(manualOpened == 1 ? "" : "s")} opened");
        if (skipped > 0)      parts.Add($"{skipped} skipped");
        ProgressHeader.Text = parts.Count > 0 ? string.Join(", ", parts) : "No installable apps selected";

        _installFinished = true;
        IsPrimaryButtonEnabled = true;

        if (failedWinget > 0)
        {
            SecondaryButtonText = failedWinget == 1 ? "Retry failed app" : $"Retry {failedWinget} failed apps";
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
        var item = _items.FirstOrDefault(i => i.WingetId == p.App.WingetId);
        if (item == null) return;

        var msg = string.IsNullOrWhiteSpace(p.Message) ? string.Empty : p.Message.Trim();
        item.Message = msg;

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
            case InstallPhase.Failed:
                item.State = InstallItemState.Failed;
                _completedCount++;
                break;
        }

        // Bij parallel mode is "X of Y: Name" verwarrend (meerdere apps lopen
        // tegelijk, name flikkert tussen winners). Tonen we count-based:
        // "Installing 3 of 8 done (2 in parallel)". Bij sequential is het
        // gedrag effectief identiek aan de oude per-app header.
        ProgressHeader.Text = $"Installing — {_completedCount} of {_wingetTotal} done{_parallelLabel}";
    }

    private void InstallDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Prevent closing while the install is still running.
        if (!_installFinished) args.Cancel = true;
    }
}

public enum InstallItemState { Pending, Installing, Success, AlreadyInstalled, Failed, ManualOpened, Skipped }

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
        1 => "Downloading",
        2 => "Verifying",
        3 => "Installing",
        4 => "Done",
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
        (_state == InstallItemState.Installing || _state == InstallItemState.ManualOpened || _state == InstallItemState.Skipped) && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    // Text state label (right column) only shown outside the Installing phase;
    // during install the stage ring occupies that column instead.
    public Visibility StateLabelVisibility =>
        _state is InstallItemState.Pending or InstallItemState.Success or InstallItemState.AlreadyInstalled or InstallItemState.Failed or InstallItemState.ManualOpened or InstallItemState.Skipped
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        InstallItemState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        InstallItemState.AlreadyInstalled => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        InstallItemState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        InstallItemState.ManualOpened => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        InstallItemState.Skipped => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        InstallItemState.Pending => "Waiting",
        InstallItemState.Success => "Installed",
        InstallItemState.AlreadyInstalled => "Al geïnstalleerd",
        InstallItemState.Failed => "Failed",
        InstallItemState.ManualOpened => "Browser opened",
        InstallItemState.Skipped => "Skipped",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
