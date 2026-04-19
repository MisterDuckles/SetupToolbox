using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

        var msg = string.IsNullOrWhiteSpace(p.Message) ? string.Empty : p.Message.Trim();
        item.Message = msg;

        // Parse "X MB / Y MB" from winget's live output. Once we see a ratio we
        // flip the item's bar from indeterminate to determinate; subsequent lines
        // update the value. Install phase after download doesn't emit percentages,
        // so the last value stays on screen until Success/Failed.
        var ratio = TryParseProgressRatio(msg);
        if (ratio.HasValue) item.Progress = ratio.Value;

        ProgressHeader.Text = $"Installing {p.CurrentIndex} of {p.Total}: {p.App.Name}";
    }

    private static readonly Regex _progressRegex = new(
        @"([\d.,]+)\s*(B|KB|MB|GB)\s*/\s*([\d.,]+)\s*(B|KB|MB|GB)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static double? TryParseProgressRatio(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var m = _progressRegex.Match(line);
        if (!m.Success) return null;

        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cur))
            return null;
        if (!double.TryParse(m.Groups[3].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var tot))
            return null;

        var curBytes = cur * UnitMultiplier(m.Groups[2].Value);
        var totBytes = tot * UnitMultiplier(m.Groups[4].Value);
        if (totBytes <= 0) return null;

        return Math.Clamp(curBytes / totBytes, 0.0, 1.0);
    }

    private static double UnitMultiplier(string unit) => unit.ToUpperInvariant() switch
    {
        "B" => 1,
        "KB" => 1024,
        "MB" => 1024d * 1024,
        "GB" => 1024d * 1024 * 1024,
        _ => 1
    };

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
    private double _progress;
    private bool _hasProgress;

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
            OnChanged(nameof(RingVisibility));
            OnChanged(nameof(IndeterminateVisibility));
            OnChanged(nameof(DeterminateVisibility));
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

    public double Progress
    {
        get => _progress;
        set
        {
            var v = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_progress - v) < 0.0001) return;
            _progress = v;
            OnChanged();

            if (!_hasProgress)
            {
                _hasProgress = true;
                // Once we have a real percentage, swap indeterminate → determinate.
                OnChanged(nameof(IndeterminateVisibility));
                OnChanged(nameof(DeterminateVisibility));
            }
        }
    }

    // Explicit Visibility-typed properties — bool->Visibility implicit conversion
    // via x:Bind can be flaky on re-eval; returning Visibility directly is reliable.
    public Visibility RingVisibility =>
        _state == InstallItemState.Pending ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IndeterminateVisibility =>
        _state == InstallItemState.Installing && !_hasProgress ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DeterminateVisibility =>
        _state == InstallItemState.Installing && _hasProgress ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GlyphVisibility =>
        _state is InstallItemState.Success or InstallItemState.Failed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MessageVisibility =>
        string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible;

    // Glyph is only rendered in terminal states — Pending uses ProgressRing,
    // Installing uses ProgressBar, so no glyph needed there.
    public string Glyph => _state switch
    {
        InstallItemState.Success => "\uE73E",   // CheckMark
        InstallItemState.Failed => "\uEA39",    // ErrorBadge
        _ => string.Empty
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
