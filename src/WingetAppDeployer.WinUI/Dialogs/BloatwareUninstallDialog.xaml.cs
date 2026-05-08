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

public sealed partial class BloatwareUninstallDialog : ContentDialog
{
    private readonly ObservableCollection<BloatwareUninstallEntry> _entries = new();
    private readonly IReadOnlyList<BloatwareItem> _items;
    private readonly BloatwareService _service;
    private bool _finished;

    public bool HadSuccessfulRemoval { get; private set; }

    // Lijst van items die daadwerkelijk verwijderd zijn (successful RESULT-line in
    // de batch-log). Gebruikt door DebloatPage om de v0.8.5 leftover-scan ná
    // uninstall te voeden — alleen items die echt weg zijn willen we naar de
    // scanner sturen.
    public IReadOnlyList<BloatwareItem> SuccessfulItems { get; private set; } = new List<BloatwareItem>();

    public BloatwareUninstallDialog(IReadOnlyList<BloatwareItem> items, BloatwareService service)
    {
        InitializeComponent();
        _items = items;
        _service = service;

        foreach (var item in items)
            _entries.Add(new BloatwareUninstallEntry(item.DisplayName));

        ItemList.ItemsSource = _entries;

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        var total = _items.Count;
        ProgressHeader.Text = $"Removing {total} bloatware item{(total == 1 ? "" : "s")}";

        var progress = new System.Progress<BloatwareProgress>(OnProgress);
        var result = await _service.UninstallBatchAsync(_items, progress);

        if (result.SuccessCount > 0) HadSuccessfulRemoval = true;

        SuccessfulItems = _items
            .Where(i => result.ResultsByDisplayName.TryGetValue(i.DisplayName, out var r) && r.success)
            .ToList();

        // Final flush — items die geen RESULT-line in de log kregen krijgen hier
        // hun terminal state. UAC-cancelled = neutrale Cancelled state (geen rode
        // error glyph), regular failure = Failed.
        var terminalState = result.Cancelled ? BloatwareUninstallState.Cancelled : BloatwareUninstallState.Failed;
        foreach (var entry in _entries)
        {
            if (result.ResultsByDisplayName.TryGetValue(entry.DisplayName, out var r))
            {
                if (entry.State == BloatwareUninstallState.Pending || entry.State == BloatwareUninstallState.Running)
                    entry.State = r.success ? BloatwareUninstallState.Success : terminalState;
                if (string.IsNullOrEmpty(entry.Message)) entry.Message = r.message;
            }
        }

        if (result.Cancelled)
        {
            ProgressHeader.Text = "Cancelled — UAC prompt declined";
        }
        else
        {
            var parts = new List<string>();
            if (result.SuccessCount > 0) parts.Add($"{result.SuccessCount} removed");
            if (result.FailedCount > 0)  parts.Add($"{result.FailedCount} failed");
            ProgressHeader.Text = parts.Count > 0 ? string.Join(", ", parts) : "Done";
        }
        UacHint.Visibility = Visibility.Collapsed;

        _finished = true;
        IsPrimaryButtonEnabled = true;
    }

    private void OnProgress(BloatwareProgress p)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyProgress(p);
        else DispatcherQueue.TryEnqueue(() => ApplyProgress(p));
    }

    private void ApplyProgress(BloatwareProgress p)
    {
        var entry = _entries.FirstOrDefault(e => e.DisplayName == p.Item.DisplayName);
        if (entry == null) return;

        entry.Message = p.Message ?? string.Empty;
        switch (p.Phase)
        {
            case BloatwarePhase.Running:
                entry.State = BloatwareUninstallState.Running;
                break;
            case BloatwarePhase.Success:
                entry.State = BloatwareUninstallState.Success;
                break;
            case BloatwarePhase.Failed:
                entry.State = BloatwareUninstallState.Failed;
                break;
        }

        var done = _entries.Count(e => e.State is BloatwareUninstallState.Success or BloatwareUninstallState.Failed);
        ProgressHeader.Text = $"Removing — {done} of {_items.Count} done";
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!_finished) args.Cancel = true;
    }
}

public enum BloatwareUninstallState { Pending, Running, Success, Failed, Cancelled }

public sealed class BloatwareUninstallEntry : INotifyPropertyChanged
{
    public BloatwareUninstallEntry(string displayName) => DisplayName = displayName;

    public string DisplayName { get; }

    private BloatwareUninstallState _state = BloatwareUninstallState.Pending;
    public BloatwareUninstallState State
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
        _state == BloatwareUninstallState.Pending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BarVisibility =>
        _state == BloatwareUninstallState.Running ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckVisibility =>
        _state == BloatwareUninstallState.Success ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility =>
        _state == BloatwareUninstallState.Failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CancelledVisibility =>
        _state == BloatwareUninstallState.Cancelled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility =>
        _state == BloatwareUninstallState.Running && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        BloatwareUninstallState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        BloatwareUninstallState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        BloatwareUninstallState.Pending => "Waiting",
        BloatwareUninstallState.Running => "Working...",
        BloatwareUninstallState.Success => "Removed",
        BloatwareUninstallState.Failed => "Failed",
        BloatwareUninstallState.Cancelled => "Cancelled",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
