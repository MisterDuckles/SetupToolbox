using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SetupToolbox.Services;

namespace SetupToolbox.Dialogs;

// Dialog die alle bewaarde snapshots toont met per-snapshot Herstel + Verwijder
// knoppen. Herstel triggert SnapshotService.RestoreAsync — kan UAC vragen voor
// elevated ops (HKLM + HKCU Policies). Verwijder is permanent (snapshot-bestand
// op disk weg).
//
// Geen "Apply alles" knop — restore is per-snapshot beslissing zodat user
// bewust kiest welke staat 'ie terug wil. Bij selecteren uit lijst krijg je
// een bevestigings-dialog zodat een misklik niet meteen schrijfacties triggert.
public sealed partial class SnapshotBrowserDialog : ContentDialog
{
    public SnapshotBrowserDialog()
    {
        InitializeComponent();
        Loaded += (s, e) => RefreshList();
    }

    private void RefreshList()
    {
        SnapshotList.Children.Clear();
        var snapshots = App.Snapshots.List();

        if (snapshots.Count == 0)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            return;
        }
        EmptyPanel.Visibility = Visibility.Collapsed;

        foreach (var snap in snapshots)
        {
            SnapshotList.Children.Add(BuildSnapshotCard(snap));
        }
    }

    private FrameworkElement BuildSnapshotCard(SnapshotMetadata snap)
    {
        var border = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12)
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var content = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = snap.Description,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        var localTime = snap.CreatedAt.ToLocalTime();
        content.Children.Add(new TextBlock
        {
            Text = $"{localTime:dd MMM yyyy 'om' HH:mm} · {snap.Entries.Count} registry-waardes",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        Grid.SetColumn(content, 0);
        grid.Children.Add(content);

        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        var restoreBtn = new Button
        {
            Content = App.Loc.S("snapshot.restore"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            Flyout = BuildConfirmFlyout(
                title: App.Loc.S("snapshot.restore.title"),
                body: App.Loc.S("snapshot.restore.body", snap.CreatedAt.ToLocalTime().ToString("dd MMM HH:mm", App.Loc.Culture)),
                confirmText: App.Loc.S("snapshot.restore.confirm"),
                onConfirm: async () => await OnRestoreConfirmed(snap))
        };
        var deleteBtn = new Button
        {
            Content = App.Loc.S("snapshot.delete"),
            Flyout = BuildConfirmFlyout(
                title: App.Loc.S("snapshot.delete.title"),
                body: $"\"{snap.Description}\" wordt permanent van schijf verwijderd.",
                confirmText: App.Loc.S("snapshot.delete.confirm"),
                onConfirm: () => { OnDeleteConfirmed(snap); return Task.CompletedTask; })
        };
        buttonStack.Children.Add(restoreBtn);
        buttonStack.Children.Add(deleteBtn);
        Grid.SetColumn(buttonStack, 1);
        grid.Children.Add(buttonStack);

        border.Child = grid;
        return border;
    }

    // Bouwt een Flyout met confirm-tekst + Ja/Annuleer buttons. WinUI verbiedt
    // nested ContentDialogs (eerste poging via een binnenshuis-ContentDialog
    // crashte de app) — Flyouts werken wel binnen een ContentDialog omdat ze
    // niet modal-by-design zijn. De Flyout sluit automatisch wanneer user
    // ergens anders klikt; de Ja-knop sluit 'm expliciet via Hide().
    private static Flyout BuildConfirmFlyout(string title, string body, string confirmText, Func<Task> onConfirm)
    {
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 10, MaxWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        panel.Children.Add(new TextBlock
        {
            Text = body,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancelBtn = new Button { Content = App.Loc.S("common.cancel") };
        cancelBtn.Click += (s, e) => flyout.Hide();
        var confirmBtn = new Button
        {
            Content = confirmText,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        confirmBtn.Click += async (s, e) =>
        {
            flyout.Hide();
            await onConfirm();
        };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(confirmBtn);
        panel.Children.Add(buttons);
        flyout.Content = panel;
        return flyout;
    }

    private async Task OnRestoreConfirmed(SnapshotMetadata snap)
    {
        var result = await App.Snapshots.RestoreAsync(snap.Id);

        if (result.Cancelled)
        {
            ShowResult(InfoBarSeverity.Warning, App.Loc.S("snapshot.restore.interrupted"),
                "UAC werd geweigerd voor de elevated registry-keys. De HKCU-keys zijn wel teruggezet.");
        }
        else if (result.FailedCount == 0)
        {
            ShowResult(InfoBarSeverity.Success, App.Loc.S("snapshot.restore.ok.title", result.SuccessCount),
                App.Loc.S("snapshot.restore.ok.body"));
            // Tweaks tab re-detect gebeurt automatisch bij volgende OnNavigatedTo.
        }
        else
        {
            var preview = string.Join("\n", result.FailureMessages);
            if (preview.Length > 240) preview = preview.Substring(0, 240) + "…";
            ShowResult(InfoBarSeverity.Error, App.Loc.S("snapshot.restore.failed.title", result.FailedCount),
                App.Loc.S("snapshot.restore.failed.body", result.SuccessCount, preview));
        }
    }

    private void OnDeleteConfirmed(SnapshotMetadata snap)
    {
        App.Snapshots.Delete(snap.Id);
        ShowResult(InfoBarSeverity.Success, App.Loc.S("snapshot.deleted.title"),
            $"\"{snap.Description}\" is van schijf verwijderd.");
        RefreshList();
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
