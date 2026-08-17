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

public sealed partial class UninstallDialog : ContentDialog
{
    private readonly ObservableCollection<UninstallItem> _items = new();
    private readonly IReadOnlyList<AppModel> _apps;
    private bool _finished;
    private int _completedCount;

    public bool HadSuccessfulUninstall { get; private set; }

    public UninstallDialog(IReadOnlyList<AppModel> apps)
    {
        InitializeComponent();
        _apps = apps;

        foreach (var app in apps)
            _items.Add(new UninstallItem(app.Name, app.WingetId));

        AppItemList.ItemsSource = _items;

        Opened += UninstallDialog_Opened;
        Closing += UninstallDialog_Closing;
    }

    private async void UninstallDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        var total = _apps.Count;
        if (total == 0)
        {
            ProgressHeader.Text = App.Loc.S("progress.noApps");
            _finished = true;
            IsPrimaryButtonEnabled = true;
            return;
        }

        _completedCount = 0;
        ProgressHeader.Text = App.Loc.S("progress.uninstalling", App.Loc.Plural("common.appCount", total));

        var progress = new System.Progress<UninstallProgress>(OnProgress);
        var results = await App.Winget.UninstallAppsAsync(_apps, progress);

        var successCount = results.Count(kv => kv.Value.success);
        var failCount = results.Count - successCount;
        if (successCount > 0) HadSuccessfulUninstall = true;

        var parts = new List<string>();
        if (successCount > 0) parts.Add(App.Loc.S("summary.uninstalled", successCount));
        if (failCount > 0)    parts.Add(App.Loc.S("summary.failed", failCount));
        ProgressHeader.Text = parts.Count > 0 ? string.Join(", ", parts) : App.Loc.S("common.done");

        _finished = true;
        IsPrimaryButtonEnabled = true;
    }

    private void OnProgress(UninstallProgress p)
    {
        if (DispatcherQueue.HasThreadAccess)
            ApplyProgress(p);
        else
            DispatcherQueue.TryEnqueue(() => ApplyProgress(p));
    }

    private void ApplyProgress(UninstallProgress p)
    {
        var item = _items.FirstOrDefault(i => i.WingetId == p.App.WingetId);
        if (item == null) return;

        item.Message = string.IsNullOrWhiteSpace(p.Message) ? string.Empty : p.Message.Trim();

        switch (p.Phase)
        {
            case UninstallPhase.Running:
                item.State = UninstallItemState.Running;
                break;
            case UninstallPhase.Success:
                item.State = UninstallItemState.Success;
                _completedCount++;
                break;
            case UninstallPhase.Failed:
                item.State = UninstallItemState.Failed;
                _completedCount++;
                break;
        }

        ProgressHeader.Text = App.Loc.S("progress.uninstallingOf", _completedCount, _apps.Count);
    }

    private void UninstallDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!_finished) args.Cancel = true;
    }
}

public enum UninstallItemState { Pending, Running, Success, Failed }

public sealed class UninstallItem : INotifyPropertyChanged
{
    private UninstallItemState _state = UninstallItemState.Pending;
    private string _message = string.Empty;

    public UninstallItem(string name, string wingetId)
    {
        Name = name;
        WingetId = wingetId;
    }

    public string Name { get; }
    public string WingetId { get; }

    public UninstallItemState State
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
            OnChanged(nameof(MessageVisibility));
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

    public Visibility RingVisibility =>
        _state == UninstallItemState.Pending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BarVisibility =>
        _state == UninstallItemState.Running ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckVisibility =>
        _state == UninstallItemState.Success ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility =>
        _state == UninstallItemState.Failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility =>
        _state == UninstallItemState.Running && !string.IsNullOrEmpty(_message)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Brush StateLabelBrush => _state switch
    {
        UninstallItemState.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
        UninstallItemState.Failed => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        UninstallItemState.Pending => App.Loc.S("common.waiting"),
        UninstallItemState.Running => App.Loc.S("common.working"),
        UninstallItemState.Success => App.Loc.S("common.uninstalled"),
        UninstallItemState.Failed => App.Loc.S("common.failed"),
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
