using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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

        var manualOpenedCount = _items.Count(i => i.State == InstallItemState.ManualOpened);
        var skippedCount = _items.Count(i => i.State == InstallItemState.Skipped);

        if (wingetApps.Count == 0)
        {
            // Alleen manual downloads geselecteerd — geen winget run nodig
            var parts = new List<string>();
            if (manualOpenedCount > 0) parts.Add($"Opened {manualOpenedCount} download page{(manualOpenedCount == 1 ? "" : "s")}");
            if (skippedCount > 0)      parts.Add($"{skippedCount} skipped");
            ProgressHeader.Text = parts.Count > 0
                ? string.Join(", ", parts)
                : "No installable apps selected";
            _installFinished = true;
            IsPrimaryButtonEnabled = true;
            return;
        }

        var maxParallelism = App.Settings.ParallelInstalls ? 2 : 1;
        _parallelLabel = maxParallelism > 1 ? " (2 in parallel)" : string.Empty;
        _wingetTotal = wingetApps.Count;
        _completedCount = 0;
        ProgressHeader.Text = $"Installing {_wingetTotal} app{(_wingetTotal == 1 ? "" : "s")}{_parallelLabel}";

        var progress = new Progress<InstallProgress>(OnProgress);
        var results = await App.Winget.InstallAppsAsync(wingetApps, progress, maxParallelism);

        var successCount = results.Count(kv => kv.Value.success);
        var failCount = results.Count - successCount;
        if (successCount > 0) HadSuccessfulInstall = true;

        // Final summary text — combineert winget + manual results
        var summaryParts = new List<string>();
        if (successCount > 0)       summaryParts.Add($"{successCount} installed");
        if (failCount > 0)          summaryParts.Add($"{failCount} failed");
        if (manualOpenedCount > 0)  summaryParts.Add($"{manualOpenedCount} manual download{(manualOpenedCount == 1 ? "" : "s")} opened");
        if (skippedCount > 0)       summaryParts.Add($"{skippedCount} skipped");
        ProgressHeader.Text = string.Join(", ", summaryParts);

        _installFinished = true;
        IsPrimaryButtonEnabled = true;
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

public enum InstallItemState { Pending, Installing, Success, Failed, ManualOpened, Skipped }

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
        _state == InstallItemState.Success ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility =>
        _state == InstallItemState.Failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility =>
        (_state == InstallItemState.Installing || _state == InstallItemState.ManualOpened || _state == InstallItemState.Skipped) && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    // Text state label (right column) only shown outside the Installing phase;
    // during install the stage ring occupies that column instead.
    public Visibility StateLabelVisibility =>
        _state is InstallItemState.Pending or InstallItemState.Success or InstallItemState.Failed or InstallItemState.ManualOpened or InstallItemState.Skipped
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        InstallItemState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        InstallItemState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        InstallItemState.ManualOpened => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        InstallItemState.Skipped => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        InstallItemState.Pending => "Waiting",
        InstallItemState.Success => "Installed",
        InstallItemState.Failed => "Failed",
        InstallItemState.ManualOpened => "Browser opened",
        InstallItemState.Skipped => "Skipped",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
