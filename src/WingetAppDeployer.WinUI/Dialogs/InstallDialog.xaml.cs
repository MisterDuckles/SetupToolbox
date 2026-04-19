using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
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
        ProgressHeader.Text = $"Installing {_apps.Count} app{(_apps.Count == 1 ? "" : "s")}";

        var progress = new Progress<InstallProgress>(OnProgress);
        var results = await App.Winget.InstallAppsAsync(_apps, progress);

        var successCount = results.Count(kv => kv.Value.success);
        var failCount = results.Count - successCount;

        ProgressHeader.Text = failCount == 0
            ? $"Installed {successCount} app{(successCount == 1 ? "" : "s")} successfully"
            : $"{successCount} succeeded, {failCount} failed";

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

        item.State = p.Phase switch
        {
            InstallPhase.Starting => InstallItemState.Installing,
            InstallPhase.Running => InstallItemState.Installing,
            InstallPhase.Success => InstallItemState.Success,
            InstallPhase.Failed => InstallItemState.Failed,
            _ => item.State
        };

        // Show winget's live output during install so the user sees progress
        // (e.g. "97.3 MB / 154 MB"). On Success/Failed show the terminal message.
        item.Message = string.IsNullOrWhiteSpace(p.Message) ? string.Empty : p.Message.Trim();

        ProgressHeader.Text = $"Installing {p.CurrentIndex} of {p.Total}: {p.App.Name}";
    }

    private void InstallDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Prevent closing while the install is still running.
        if (!_installFinished) args.Cancel = true;
    }
}

public enum InstallItemState { Pending, Installing, Success, Failed }

public sealed class InstallItem : INotifyPropertyChanged
{
    private InstallItemState _state = InstallItemState.Pending;
    private string _message = string.Empty;

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
            OnChanged(nameof(Glyph));
            OnChanged(nameof(IconBrush));
            OnChanged(nameof(StateLabel));
            OnChanged(nameof(ProgressVisibility));
            OnChanged(nameof(GlyphVisibility));
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

    // Explicit Visibility-typed properties — bool->Visibility implicit conversion
    // via x:Bind can be flaky on re-eval; returning Visibility directly is reliable.
    public Visibility ProgressVisibility =>
        _state == InstallItemState.Installing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlyphVisibility =>
        _state == InstallItemState.Installing ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MessageVisibility =>
        string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible;

    // Segoe Fluent Icons glyphs — reliable code points that render on Win11.
    public string Glyph => _state switch
    {
        InstallItemState.Pending => "\uEA3A",   // CircleRing (outline circle)
        InstallItemState.Installing => string.Empty,
        InstallItemState.Success => "\uE73E",   // CheckMark
        InstallItemState.Failed => "\uEA39",    // ErrorBadge
        _ => "\uEA3A"
    };

    public Brush IconBrush => _state switch
    {
        InstallItemState.Success => new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)),
        InstallItemState.Failed => new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0x1A, 0x3B)),
        InstallItemState.Installing => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
    };

    public string StateLabel => _state switch
    {
        InstallItemState.Pending => "Waiting",
        InstallItemState.Installing => "Installing",
        InstallItemState.Success => "Done",
        InstallItemState.Failed => "Failed",
        _ => string.Empty
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
