using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WingetAppDeployer_WinUI.Services;
using AppModel = WingetAppDeployer_WinUI.Models.App;

namespace WingetAppDeployer_WinUI.Dialogs;

public sealed partial class InstallDialog : ContentDialog
{
    private readonly ObservableCollection<InstallItem> _items = new();
    private readonly IReadOnlyList<AppModel> _apps;
    private bool _installFinished;

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
        // Manual-download apps eerst afhandelen — geen winget call, gewoon URL
        // openen in default browser zodat user de installer handmatig kan downloaden.
        var manualApps = _apps.Where(a => a.IsManualDownload).ToList();
        var wingetApps = _apps.Where(a => !a.IsManualDownload).ToList();

        foreach (var app in manualApps)
        {
            var item = _items.FirstOrDefault(i => i.WingetId == app.WingetId);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = app.DownloadUrl,
                    UseShellExecute = true   // routes via shell → opent default browser
                });
                if (item != null)
                {
                    item.Message = "Opened vendor download page in browser";
                    item.State = InstallItemState.ManualOpened;
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.Message = $"Could not open URL: {ex.Message}";
                    item.State = InstallItemState.Failed;
                }
            }
        }

        if (wingetApps.Count == 0)
        {
            // Alleen manual downloads geselecteerd — geen winget run nodig
            var manualOk = manualApps.Count(a => _items.FirstOrDefault(i => i.WingetId == a.WingetId)?.State == InstallItemState.ManualOpened);
            ProgressHeader.Text = $"Opened {manualOk} download page{(manualOk == 1 ? "" : "s")} — install manually";
            _installFinished = true;
            IsPrimaryButtonEnabled = true;
            return;
        }

        ProgressHeader.Text = $"Installing {wingetApps.Count} app{(wingetApps.Count == 1 ? "" : "s")}";

        var progress = new Progress<InstallProgress>(OnProgress);
        var results = await App.Winget.InstallAppsAsync(wingetApps, progress);

        var successCount = results.Count(kv => kv.Value.success);
        var failCount = results.Count - successCount;
        var manualCount = manualApps.Count;

        // Final summary text — combineert winget + manual results
        var parts = new List<string>();
        if (successCount > 0) parts.Add($"{successCount} installed");
        if (failCount > 0)    parts.Add($"{failCount} failed");
        if (manualCount > 0)  parts.Add($"{manualCount} manual download{(manualCount == 1 ? "" : "s")} opened");
        ProgressHeader.Text = string.Join(", ", parts);

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
                break;
            case InstallPhase.Failed:
                item.State = InstallItemState.Failed;
                break;
        }

        ProgressHeader.Text = $"Installing {p.CurrentIndex} of {p.Total}: {p.App.Name}";
    }

    private void InstallDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Prevent closing while the install is still running.
        if (!_installFinished) args.Cancel = true;
    }
}

public enum InstallItemState { Pending, Installing, Success, Failed, ManualOpened }

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
        (_state == InstallItemState.Installing || _state == InstallItemState.ManualOpened) && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    // Text state label (right column) only shown outside the Installing phase;
    // during install the stage ring occupies that column instead.
    public Visibility StateLabelVisibility =>
        _state is InstallItemState.Pending or InstallItemState.Success or InstallItemState.Failed or InstallItemState.ManualOpened
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        InstallItemState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        InstallItemState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        InstallItemState.ManualOpened => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        InstallItemState.Pending => "Waiting",
        InstallItemState.Success => "Installed",
        InstallItemState.Failed => "Failed",
        InstallItemState.ManualOpened => "Browser opened",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
