using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WingetAppDeployer_WinUI.Models;
using WingetAppDeployer_WinUI.Services;

namespace WingetAppDeployer_WinUI.Dialogs;

public sealed partial class AllAppsUninstallDialog : ContentDialog
{
    private readonly ObservableCollection<AllAppsUninstallEntry> _entries = new();
    private readonly IReadOnlyList<InstalledAppEntry> _items;
    private readonly MixedSourceUninstaller _service;
    private bool _finished;

    public bool HadSuccessfulUninstall { get; private set; }

    // Items die echt verwijderd zijn — voedt de v0.8.5 leftover-scan in DebloatPage.
    public IReadOnlyList<InstalledAppEntry> SuccessfulItems { get; private set; } = new List<InstalledAppEntry>();

    public AllAppsUninstallDialog(IReadOnlyList<InstalledAppEntry> items, MixedSourceUninstaller service)
    {
        InitializeComponent();
        _items = items;
        _service = service;

        foreach (var item in items)
            _entries.Add(new AllAppsUninstallEntry(item));

        ItemList.ItemsSource = _entries;

        // UAC-hint alleen tonen wanneer er Store of Other items in de batch zitten —
        // pure catalog-batches hebben geen UAC nodig.
        var needsElevation = items.Any(i => i.Source != InstalledSource.Winget);
        UacHint.Visibility = needsElevation ? Visibility.Visible : Visibility.Collapsed;

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        var total = _items.Count;
        ProgressHeader.Text = $"Uninstalling {total} app{(total == 1 ? "" : "s")}";

        var progress = new System.Progress<MixedUninstallProgress>(OnProgress);
        var result = await _service.UninstallBatchAsync(_items, progress);

        if (result.SuccessCount > 0) HadSuccessfulUninstall = true;

        SuccessfulItems = _items
            .Where(i => result.ResultsByIdentifier.TryGetValue(i.Identifier, out var r) && r.success)
            .ToList();

        // Final flush — items zonder progress-event (PS gecrashd halverwege) krijgen
        // hun terminal state hier. UAC-cancelled = neutrale Cancelled state, regular
        // failure = Failed.
        var terminalState = result.Cancelled ? AllAppsUninstallState.Cancelled : AllAppsUninstallState.Failed;
        foreach (var entry in _entries)
        {
            if (result.ResultsByIdentifier.TryGetValue(entry.Identifier, out var r))
            {
                if (entry.State == AllAppsUninstallState.Pending || entry.State == AllAppsUninstallState.Running)
                    entry.State = r.success ? AllAppsUninstallState.Success : terminalState;
                if (string.IsNullOrEmpty(entry.Message)) entry.Message = r.message;
            }
        }

        if (result.Cancelled)
        {
            ProgressHeader.Text = "Cancelled — UAC prompt declined for Store/Other items";
        }
        else
        {
            var parts = new List<string>();
            if (result.SuccessCount > 0) parts.Add($"{result.SuccessCount} uninstalled");
            if (result.FailedCount > 0)  parts.Add($"{result.FailedCount} failed");
            ProgressHeader.Text = parts.Count > 0 ? string.Join(", ", parts) : "Done";
        }
        UacHint.Visibility = Visibility.Collapsed;

        _finished = true;
        IsPrimaryButtonEnabled = true;
    }

    private void OnProgress(MixedUninstallProgress p)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyProgress(p);
        else DispatcherQueue.TryEnqueue(() => ApplyProgress(p));
    }

    private void ApplyProgress(MixedUninstallProgress p)
    {
        var entry = _entries.FirstOrDefault(e => e.Identifier == p.Entry.Identifier);
        if (entry == null) return;

        entry.Message = p.Message ?? string.Empty;
        switch (p.Phase)
        {
            case MixedUninstallPhase.Running:
                entry.State = AllAppsUninstallState.Running;
                break;
            case MixedUninstallPhase.Success:
                entry.State = AllAppsUninstallState.Success;
                break;
            case MixedUninstallPhase.Failed:
                entry.State = AllAppsUninstallState.Failed;
                break;
        }

        var done = _entries.Count(e =>
            e.State is AllAppsUninstallState.Success
                    or AllAppsUninstallState.Failed
                    or AllAppsUninstallState.Cancelled);
        ProgressHeader.Text = $"Uninstalling — {done} of {_items.Count} done";
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!_finished) args.Cancel = true;
    }
}

public enum AllAppsUninstallState { Pending, Running, Success, Failed, Cancelled }

public sealed class AllAppsUninstallEntry : INotifyPropertyChanged
{
    private readonly InstalledAppEntry _source;

    public AllAppsUninstallEntry(InstalledAppEntry source)
    {
        _source = source;
    }

    public string DisplayName => _source.DisplayName;
    public string Identifier => _source.Identifier;
    public string SourceBadgeText => _source.SourceBadgeText;
    public Brush SourceBadgeBrush => _source.SourceBadgeBrush;

    private AllAppsUninstallState _state = AllAppsUninstallState.Pending;
    public AllAppsUninstallState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnChanged();
            OnChanged(nameof(StateLabel));
            OnChanged(nameof(StateLabelBrush));
            OnChanged(nameof(RingVisibility));
            OnChanged(nameof(BarVisibility));
            OnChanged(nameof(CheckVisibility));
            OnChanged(nameof(ErrorVisibility));
            OnChanged(nameof(CancelledVisibility));
            OnChanged(nameof(MessageVisibility));
        }
    }

    private string _message = string.Empty;
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

    public Visibility RingVisibility =>
        _state == AllAppsUninstallState.Pending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BarVisibility =>
        _state == AllAppsUninstallState.Running ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckVisibility =>
        _state == AllAppsUninstallState.Success ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility =>
        _state == AllAppsUninstallState.Failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CancelledVisibility =>
        _state == AllAppsUninstallState.Cancelled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility =>
        _state == AllAppsUninstallState.Running && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        AllAppsUninstallState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        AllAppsUninstallState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        AllAppsUninstallState.Pending => "Waiting",
        AllAppsUninstallState.Running => "Working...",
        AllAppsUninstallState.Success => "Removed",
        AllAppsUninstallState.Failed => "Failed",
        AllAppsUninstallState.Cancelled => "Cancelled",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
