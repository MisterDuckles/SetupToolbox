using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
        OverallProgress.Maximum = apps.Count;

        Opened += InstallDialog_Opened;
        Closing += InstallDialog_Closing;
    }

    private async void InstallDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        ProgressHeader.Text = $"Installing {_apps.Count} app{(_apps.Count == 1 ? "" : "s")}";
        ProgressDetail.Text = "Starting...";

        var progress = new Progress<InstallProgress>(OnProgress);
        var results = await App.Winget.InstallAppsAsync(_apps, progress);

        var successCount = results.Count(kv => kv.Value.success);
        var failCount = results.Count - successCount;

        ProgressHeader.Text = failCount == 0
            ? $"Installed {successCount} app{(successCount == 1 ? "" : "s")} successfully"
            : $"{successCount} succeeded, {failCount} failed";
        ProgressDetail.Text = string.Empty;
        OverallProgress.Value = _apps.Count;

        _installFinished = true;
        IsPrimaryButtonEnabled = true;
    }

    private void OnProgress(InstallProgress p)
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

        // Keep running log messages out of the ticker — only show terminal state
        // messages (success/fail) under the name. Preparing messages are noisy.
        item.Message = p.Phase is InstallPhase.Success or InstallPhase.Failed
            ? p.Message
            : string.Empty;

        ProgressHeader.Text = $"Installing {p.CurrentIndex} of {p.Total}: {p.App.Name}";
        ProgressDetail.Text = p.Message;

        // Count completed items for the bar
        var completed = _items.Count(i => i.State is InstallItemState.Success or InstallItemState.Failed);
        OverallProgress.Value = completed;
    }

    private void InstallDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Prevent closing while the install is still running. Primary button
        // is disabled during install so the only way in is Esc.
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
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            if (_message == value) return;
            _message = value ?? string.Empty;
            OnChanged();
            OnChanged(nameof(HasMessage));
        }
    }

    public Visibility HasMessage => string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible;

    // E10D = CircleRing (pending), E768 = ProgressRingDots/Play, E73E = Checkmark, EA39 = ErrorBadge
    public string Glyph => _state switch
    {
        InstallItemState.Pending => "\uE10D",
        InstallItemState.Installing => "\uEC4A",
        InstallItemState.Success => "\uE73E",
        InstallItemState.Failed => "\uEA39",
        _ => "\uE10D"
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
