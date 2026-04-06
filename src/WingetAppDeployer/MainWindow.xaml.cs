using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WingetAppDeployer.Models;
using WingetAppDeployer.Services;
using WingetAppDeployer.Views;
using AppModel = WingetAppDeployer.Models.App;

namespace WingetAppDeployer;

public partial class MainWindow : Window
{
    private AppDatabase? _appDatabase;
    private readonly List<AppModel> _allApps = new();
    private readonly Dictionary<AppModel, bool> _selectedApps = new();
    private readonly Dictionary<AppModel, Border> _appCards = new();
    private readonly Dictionary<AppModel, Border> _checkMarks = new();
    private readonly Dictionary<string, TextBlock> _installedLabels = new();
    private Category? _currentCategory;
    private string _currentCategoryColor = "#607D8B";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = App.SettingsService?.LoadSettings();
        if (settings?.ShowWelcomeScreen == true)
            WelcomeBanner.Visibility = Visibility.Visible;

        var wingetAvailable = await App.WingetService!.IsWingetAvailableAsync();
        if (!wingetAvailable)
        {
            MessageBox.Show(
                "Winget is not installed or not available. Please install Windows App Installer from the Microsoft Store.",
                "Winget Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        if (settings?.CheckForUpdatesOnStartup == true)
            await CheckForAppUpdatesAsync();

        await LoadAppDatabaseAsync();

        // Smooth scroll
        MainScrollViewer.PreviewMouseWheel += (s, ev) =>
        {
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - (ev.Delta * 0.6));
            ev.Handled = true;
        };
    }

    private async Task CheckForAppUpdatesAsync()
    {
        try
        {
            var (updateAvailable, latestVersion, downloadUrl) = await App.GitHubService!.CheckForUpdatesAsync();
            if (updateAvailable && !string.IsNullOrEmpty(downloadUrl))
            {
                var result = MessageBox.Show(
                    $"A new version ({latestVersion}) is available! Would you like to download and install it?",
                    "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    await App.GitHubService.DownloadAndInstallUpdateAsync(downloadUrl, new Progress<int>());
                    Application.Current.Shutdown();
                }
            }
        }
        catch { }
    }

    private async Task LoadAppDatabaseAsync()
    {
        try
        {
            LoadingPanel.Visibility = Visibility.Visible;
            CategoryGridPanel.Visibility = Visibility.Collapsed;

            _appDatabase = await App.GitHubService!.DownloadAppDatabaseAsync();

            if (_appDatabase == null)
            {
                MessageBox.Show("Failed to load app database. Please check your internet connection.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadingPanel.Visibility = Visibility.Collapsed;

            foreach (var cat in _appDatabase.Categories)
            {
                if (cat.Apps != null) _allApps.AddRange(cat.Apps);
                if (cat.Subcategories != null)
                    foreach (var sub in cat.Subcategories)
                        _allApps.AddRange(sub.Apps);
            }

            RenderCategoryGrid();
            _ = CheckInstalledStatusAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading apps: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ========== CATEGORY GRID ==========

    private void RenderCategoryGrid()
    {
        CategoryGridPanel.Children.Clear();
        CategoryGridPanel.Visibility = Visibility.Visible;
        AppListPanel.Visibility = Visibility.Collapsed;
        _currentCategory = null;

        if (_appDatabase == null) return;

        var cornerRadius = GetThemeDouble("CategoryCornerRadius", 16);

        foreach (var category in _appDatabase.Categories)
        {
            var appCount = GetCategoryAppCount(category);

            var catCard = new Border
            {
                Width = 220, Height = 260,
                Padding = new Thickness(24),
                Margin = new Thickness(10),
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(cornerRadius),
                BorderThickness = new Thickness(0),
                Background = GetCategoryGradient(category.Id),
                ClipToBounds = true,
                Effect = new DropShadowEffect
                {
                    ShadowDepth = 3, BlurRadius = 16, Opacity = 0.15, Color = Colors.Black
                }
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bgIcon = new TextBlock
            {
                Text = category.Icon ?? "📦", FontSize = 60, Opacity = 0.25,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };
            Grid.SetRow(bgIcon, 0);
            grid.Children.Add(bgIcon);

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock
            {
                Text = category.Name, FontSize = 18, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = $"{appCount} apps", FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(textPanel, 1);
            grid.Children.Add(textPanel);

            var arrow = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            arrow.Child = new TextBlock
            {
                Text = "→", FontSize = 14, Foreground = Brushes.White, FontWeight = FontWeights.Bold
            };
            Grid.SetRow(arrow, 2);
            grid.Children.Add(arrow);

            catCard.Child = grid;

            catCard.MouseEnter += (s, args) =>
            {
                if (catCard.Effect is DropShadowEffect shadow)
                { shadow.BlurRadius = 24; shadow.Opacity = 0.25; shadow.ShadowDepth = 5; }
                catCard.RenderTransform = new TranslateTransform(0, -3);
            };
            catCard.MouseLeave += (s, args) =>
            {
                if (catCard.Effect is DropShadowEffect shadow)
                { shadow.BlurRadius = 16; shadow.Opacity = 0.15; shadow.ShadowDepth = 3; }
                catCard.RenderTransform = null;
            };

            catCard.MouseLeftButtonDown += (s, args) => NavigateToCategory(category);
            CategoryGridPanel.Children.Add(catCard);
        }
    }

    private void NavigateToCategory(Category category)
    {
        _currentCategory = category;
        _currentCategoryColor = GetCategoryColorHex(category.Id);
        CategoryGridPanel.Visibility = Visibility.Collapsed;
        AppListPanel.Visibility = Visibility.Visible;
        AppListPanel.Children.Clear();

        // Back button
        var backButton = new Button { Content = "← Back to Categories", Margin = new Thickness(0, 0, 0, 16) };
        backButton.SetResourceReference(StyleProperty, "BackButtonStyle");
        backButton.Click += (s, args) => NavigateBack();
        AppListPanel.Children.Add(backButton);

        // Category header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var catTitle = new TextBlock
        {
            Text = $"{category.Icon} {category.Name}",
            FontSize = 24, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center
        };
        catTitle.SetResourceReference(ForegroundProperty, "TextPrimaryColor");
        Grid.SetColumn(catTitle, 0);
        headerGrid.Children.Add(catTitle);

        var selectAllBtn = new Button
        {
            Content = "Select All", Tag = category, VerticalAlignment = VerticalAlignment.Center
        };
        selectAllBtn.SetResourceReference(StyleProperty, "SelectAllButtonStyle");
        selectAllBtn.Click += SelectAllCategory_Click;
        Grid.SetColumn(selectAllBtn, 1);
        headerGrid.Children.Add(selectAllBtn);

        AppListPanel.Children.Add(headerGrid);

        var separator = new Border { Height = 2, Margin = new Thickness(0, 0, 0, 16) };
        separator.SetResourceReference(Border.BackgroundProperty, "PrimaryColor");
        AppListPanel.Children.Add(separator);

        if (category.Apps != null && category.Apps.Any())
            RenderAppList(AppListPanel, category.Apps);

        if (category.Subcategories != null)
        {
            foreach (var subcat in category.Subcategories)
            {
                var subcatHeader = new TextBlock
                {
                    Text = $"💻 {subcat.Name}", FontSize = 17,
                    FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8)
                };
                subcatHeader.SetResourceReference(ForegroundProperty, "TextPrimaryColor");
                AppListPanel.Children.Add(subcatHeader);

                RenderAppList(AppListPanel, subcat.Apps);
            }
        }
    }

    private void NavigateBack()
    {
        _currentCategory = null;
        AppListPanel.Visibility = Visibility.Collapsed;
        CategoryGridPanel.Visibility = Visibility.Visible;
    }

    // ========== APP LIST (Optie A: Card Highlight + Groen Vinkje) ==========

    private void RenderAppList(StackPanel parent, List<AppModel> apps)
    {
        var appsGrid = new WrapPanel();
        var cornerRadius = GetThemeDouble("CardCornerRadius", 12);
        var iconColor = (Color)ColorConverter.ConvertFromString(_currentCategoryColor);

        foreach (var app in apps)
        {
            if (!_allApps.Contains(app)) _allApps.Add(app);

            var isSelected = _selectedApps.GetValueOrDefault(app, false);

            var appCard = new Border
            {
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 300,
                CornerRadius = new CornerRadius(cornerRadius),
                BorderThickness = new Thickness(isSelected ? 2 : 0),
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect
                {
                    ShadowDepth = 2, BlurRadius = 14, Opacity = 0.10, Color = Colors.Black
                }
            };
            appCard.SetResourceReference(Border.BackgroundProperty, "SurfaceColor");
            if (isSelected)
                appCard.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));

            // Grid: icon left, text right, checkmark top-right
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Icon (Apple-style rounded square)
            var iconBorder = new Border
            {
                Width = 44, Height = 44,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(iconColor),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0)
            };
            iconBorder.Child = new TextBlock
            {
                Text = _currentCategory?.Icon ?? "📦",
                FontSize = 22, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBorder, 0);
            grid.Children.Add(iconBorder);

            // Text content
            var textPanel = new StackPanel();

            var nameText = new TextBlock
            {
                Text = app.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            };
            nameText.SetResourceReference(ForegroundProperty, "TextPrimaryColor");

            var descText = new TextBlock
            {
                Text = app.Description, FontSize = 12,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            };
            descText.SetResourceReference(ForegroundProperty, "TextSecondaryColor");

            textPanel.Children.Add(nameText);
            textPanel.Children.Add(descText);

            var badgesPanel = new StackPanel { Orientation = Orientation.Horizontal };

            if (app.Popular)
            {
                var popularBadge = new TextBlock
                {
                    Text = "⭐ Popular", FontSize = 11, Margin = new Thickness(0, 0, 10, 0)
                };
                popularBadge.SetResourceReference(ForegroundProperty, "AccentColor");
                badgesPanel.Children.Add(popularBadge);
            }

            TextBlock installedLabel;
            if (_installedLabels.TryGetValue(app.WingetId, out var existing))
            {
                if (existing.Parent is Panel oldParent) oldParent.Children.Remove(existing);
                installedLabel = existing;
            }
            else
            {
                installedLabel = new TextBlock
                {
                    Text = "✓ Installed", FontSize = 11, Visibility = Visibility.Collapsed
                };
                installedLabel.SetResourceReference(ForegroundProperty, "PrimaryColor");
                _installedLabels[app.WingetId] = installedLabel;
            }
            badgesPanel.Children.Add(installedLabel);

            if (badgesPanel.Children.Count > 0)
                textPanel.Children.Add(badgesPanel);

            Grid.SetColumn(textPanel, 1);
            grid.Children.Add(textPanel);

            // Green checkmark circle (top-right)
            var checkMark = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0),
                Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
            };
            checkMark.Child = new TextBlock
            {
                Text = "✓", FontSize = 14, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            Grid.SetColumn(checkMark, 1);
            grid.Children.Add(checkMark);

            appCard.Child = grid;

            _appCards[app] = appCard;
            _checkMarks[app] = checkMark;

            // Click to toggle selection
            appCard.MouseLeftButtonDown += (s, args) =>
            {
                _selectedApps[app] = !_selectedApps.GetValueOrDefault(app, false);
                UpdateCardVisual(app);
                UpdateSelectionCount();
                args.Handled = true;
            };

            // Hover
            appCard.MouseEnter += (s, args) =>
            {
                if (appCard.Effect is DropShadowEffect shadow)
                { shadow.BlurRadius = 20; shadow.Opacity = 0.20; shadow.ShadowDepth = 3; }
                appCard.RenderTransform = new TranslateTransform(0, -2);
            };
            appCard.MouseLeave += (s, args) =>
            {
                if (appCard.Effect is DropShadowEffect shadow)
                { shadow.BlurRadius = 14; shadow.Opacity = 0.10; shadow.ShadowDepth = 2; }
                appCard.RenderTransform = null;
            };

            appsGrid.Children.Add(appCard);
        }

        parent.Children.Add(appsGrid);
    }

    private void UpdateCardVisual(AppModel app)
    {
        var isSelected = _selectedApps.GetValueOrDefault(app, false);
        var green = new SolidColorBrush(Color.FromRgb(76, 175, 80));

        if (_appCards.TryGetValue(app, out var card))
        {
            card.BorderThickness = new Thickness(isSelected ? 2 : 0);
            card.BorderBrush = isSelected ? green : null;
        }

        if (_checkMarks.TryGetValue(app, out var check))
        {
            check.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ========== INSTALLED STATUS CHECK ==========

    private async Task CheckInstalledStatusAsync()
    {
        try
        {
            var installedIds = await App.WingetService!.GetInstalledAppIdsAsync();
            foreach (var app in _allApps)
            {
                if (installedIds.Contains(app.WingetId))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_installedLabels.TryGetValue(app.WingetId, out var label))
                            label.Visibility = Visibility.Visible;
                    });
                }
            }
        }
        catch { }
    }

    // ========== SELECTION & INSTALL ==========

    private void SelectAllCategory_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var category = button?.Tag as Category;
        if (category == null) return;

        var appsToSelect = GetAllAppsInCategory(category);

        foreach (var app in appsToSelect)
        {
            _selectedApps[app] = true;
            UpdateCardVisual(app);
        }
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var selectedCount = _selectedApps.Count(kv => kv.Value);
        SelectionCountText.Text = $"{selectedCount} app{(selectedCount != 1 ? "s" : "")} selected";
        InstallButton.IsEnabled = selectedCount > 0;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedApps = _selectedApps
            .Where(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        if (!selectedApps.Any()) return;

        var installWindow = new InstallWindow(selectedApps);
        installWindow.Owner = this;
        installWindow.ShowDialog();

        if (App.SettingsService?.CurrentSettings.AutoUpdateEnabled != true)
        {
            var result = MessageBox.Show(
                "Would you like to set up automatic updates for your installed apps?",
                "Auto-Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var scheduleWindow = new ScheduleWindow();
                scheduleWindow.Owner = this;
                scheduleWindow.ShowDialog();
            }
        }
    }

    // ========== SEARCH ==========

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text.ToLower();
        if (_currentCategory == null)
            SearchCategoryGrid(searchText);
        else
            SearchAppList(searchText);
    }

    private void SearchCategoryGrid(string searchText)
    {
        if (_appDatabase == null) return;

        var catIndex = 0;
        foreach (var child in CategoryGridPanel.Children)
        {
            if (child is Border catCard && catIndex < _appDatabase.Categories.Count)
            {
                var category = _appDatabase.Categories[catIndex];
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    catCard.Visibility = Visibility.Visible;
                }
                else
                {
                    var hasMatch = category.Name.ToLower().Contains(searchText) ||
                                   GetAllAppsInCategory(category)
                                       .Any(a => a.Name.ToLower().Contains(searchText) ||
                                                 a.Description.ToLower().Contains(searchText));
                    catCard.Visibility = hasMatch ? Visibility.Visible : Visibility.Collapsed;
                }
                catIndex++;
            }
        }
    }

    private void SearchAppList(string searchText)
    {
        foreach (var kv in _appCards)
        {
            var app = kv.Key;
            var card = kv.Value;
            if (card.Parent is WrapPanel)
            {
                card.Visibility = string.IsNullOrWhiteSpace(searchText) ||
                                  app.Name.ToLower().Contains(searchText) ||
                                  app.Description.ToLower().Contains(searchText)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
    }

    // ========== SETTINGS & WELCOME ==========

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }

    private void CloseWelcome_Click(object sender, RoutedEventArgs e)
    {
        WelcomeBanner.Visibility = Visibility.Collapsed;
        var settings = App.SettingsService?.CurrentSettings;
        if (settings != null)
        {
            settings.ShowWelcomeScreen = false;
            App.SettingsService?.SaveSettings(settings);
        }
    }

    // ========== HELPERS ==========

    private double GetThemeDouble(string key, double fallback)
    {
        try { return (double)FindResource(key); }
        catch { return fallback; }
    }

    private int GetCategoryAppCount(Category category)
    {
        var count = category.Apps?.Count ?? 0;
        if (category.Subcategories != null)
            count += category.Subcategories.Sum(s => s.Apps?.Count ?? 0);
        return count;
    }

    private List<AppModel> GetAllAppsInCategory(Category category)
    {
        var apps = new List<AppModel>();
        if (category.Apps != null) apps.AddRange(category.Apps);
        if (category.Subcategories != null)
            foreach (var sub in category.Subcategories)
                apps.AddRange(sub.Apps);
        return apps;
    }

    private LinearGradientBrush GetCategoryGradient(string categoryId)
    {
        return categoryId?.ToLower() switch
        {
            "browsers" => MakeGradient("#4285F4", "#2B5FCC"),
            "development" => MakeGradient("#7C4DFF", "#5B2ECC"),
            "security" or "security-privacy" => MakeGradient("#00BFA5", "#008C7A"),
            "productivity" => MakeGradient("#FF6D00", "#CC5500"),
            "communication" => MakeGradient("#E91E63", "#B8144D"),
            "media" or "media-entertainment" => MakeGradient("#FF5252", "#CC3333"),
            "utilities" => MakeGradient("#FFC107", "#CC9A00"),
            "creative" or "creative-design" => MakeGradient("#26C6DA", "#1A9DAE"),
            _ => MakeGradient("#607D8B", "#455A64")
        };
    }

    private string GetCategoryColorHex(string categoryId)
    {
        return categoryId?.ToLower() switch
        {
            "browsers" => "#4285F4",
            "development" => "#7C4DFF",
            "security" or "security-privacy" => "#00BFA5",
            "productivity" => "#FF6D00",
            "communication" => "#E91E63",
            "media" or "media-entertainment" => "#FF5252",
            "utilities" => "#FFC107",
            "creative" or "creative-design" => "#26C6DA",
            _ => "#607D8B"
        };
    }

    private static LinearGradientBrush MakeGradient(string startHex, string endHex)
    {
        var start = (Color)ColorConverter.ConvertFromString(startHex);
        var end = (Color)ColorConverter.ConvertFromString(endHex);
        return new LinearGradientBrush(start, end, new Point(0, 0), new Point(1, 1));
    }
}
