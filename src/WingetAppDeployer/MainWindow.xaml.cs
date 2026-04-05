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
    private readonly List<CheckBox> _appCheckBoxes = new();
    private readonly Dictionary<string, TextBlock> _installedLabels = new();
    private Category? _currentCategory;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = App.SettingsService?.LoadSettings();
        if (settings?.ShowWelcomeScreen == true)
        {
            WelcomeBanner.Visibility = Visibility.Visible;
        }

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
        {
            await CheckForAppUpdatesAsync();
        }

        await LoadAppDatabaseAsync();
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

            // Pre-register all apps for selection tracking
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

        var cornerRadius = GetThemeDouble("CategoryCornerRadius", 10);

        foreach (var category in _appDatabase.Categories)
        {
            var appCount = GetCategoryAppCount(category);

            var catShadowDepth = GetThemeDouble("CardShadowDepth", 2);
            var catShadowBlur = GetThemeDouble("CardShadowBlur", 16);
            var catShadowOpacity = GetThemeDouble("CardShadowOpacity", 0.12);

            var catCard = new Border
            {
                Width = 240,
                Height = 180,
                Padding = new Thickness(24),
                Margin = new Thickness(10),
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(cornerRadius),
                BorderThickness = new Thickness(0),
                Effect = new DropShadowEffect
                {
                    ShadowDepth = catShadowDepth,
                    BlurRadius = catShadowBlur,
                    Opacity = catShadowOpacity,
                    Color = Colors.Black
                }
            };
            catCard.SetResourceReference(Border.BackgroundProperty, "CategoryCardBg");

            var content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var icon = new TextBlock
            {
                Text = category.Icon ?? "📦",
                FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            icon.SetResourceReference(ForegroundProperty, "TextPrimaryColor");

            var name = new TextBlock
            {
                Text = category.Name,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            name.SetResourceReference(ForegroundProperty, "TextPrimaryColor");

            var count = new TextBlock
            {
                Text = $"{appCount} apps",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            count.SetResourceReference(ForegroundProperty, "TextSecondaryColor");

            content.Children.Add(icon);
            content.Children.Add(name);
            content.Children.Add(count);
            catCard.Child = content;

            // Hover: shadow strengthens, subtle lift
            catCard.MouseEnter += (s, args) =>
            {
                if (catCard.Effect is DropShadowEffect shadow)
                {
                    shadow.BlurRadius = 24;
                    shadow.Opacity = 0.22;
                    shadow.ShadowDepth = 4;
                }
                catCard.RenderTransform = new TranslateTransform(0, -3);
            };
            catCard.MouseLeave += (s, args) =>
            {
                if (catCard.Effect is DropShadowEffect shadow)
                {
                    shadow.BlurRadius = catShadowBlur;
                    shadow.Opacity = catShadowOpacity;
                    shadow.ShadowDepth = catShadowDepth;
                }
                catCard.RenderTransform = null;
            };

            // Click to navigate
            catCard.MouseLeftButtonDown += (s, args) => NavigateToCategory(category);

            CategoryGridPanel.Children.Add(catCard);
        }
    }

    private void NavigateToCategory(Category category)
    {
        _currentCategory = category;
        CategoryGridPanel.Visibility = Visibility.Collapsed;
        AppListPanel.Visibility = Visibility.Visible;
        AppListPanel.Children.Clear();

        var cornerRadius = GetThemeDouble("CardCornerRadius", 10);

        // Back button
        var backButton = new Button
        {
            Content = "← Back to Categories",
            Margin = new Thickness(0, 0, 0, 16)
        };
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
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        catTitle.SetResourceReference(ForegroundProperty, "TextPrimaryColor");
        Grid.SetColumn(catTitle, 0);
        headerGrid.Children.Add(catTitle);

        var selectAllBtn = new Button
        {
            Content = "Select All",
            Tag = category,
            VerticalAlignment = VerticalAlignment.Center
        };
        selectAllBtn.SetResourceReference(StyleProperty, "SelectAllButtonStyle");
        selectAllBtn.Click += SelectAllCategory_Click;
        Grid.SetColumn(selectAllBtn, 1);
        headerGrid.Children.Add(selectAllBtn);

        AppListPanel.Children.Add(headerGrid);

        // Separator
        var separator = new Border
        {
            Height = 2,
            Margin = new Thickness(0, 0, 0, 16)
        };
        separator.SetResourceReference(Border.BackgroundProperty, "PrimaryColor");
        AppListPanel.Children.Add(separator);

        // Apps in category
        if (category.Apps != null && category.Apps.Any())
        {
            RenderAppList(AppListPanel, category.Apps);
        }

        // Subcategories
        if (category.Subcategories != null)
        {
            foreach (var subcat in category.Subcategories)
            {
                var subcatHeader = new TextBlock
                {
                    Text = $"💻 {subcat.Name}",
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 16, 0, 8)
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

    // ========== APP LIST ==========

    private void RenderAppList(StackPanel parent, List<AppModel> apps)
    {
        var appsGrid = new WrapPanel();

        var cornerRadius = GetThemeDouble("CardCornerRadius", 12);
        var shadowDepth = GetThemeDouble("CardShadowDepth", 2);
        var shadowBlur = GetThemeDouble("CardShadowBlur", 14);
        var shadowOpacity = GetThemeDouble("CardShadowOpacity", 0.10);

        foreach (var app in apps)
        {
            // Don't re-add if already tracked (navigating back and forth)
            if (!_allApps.Contains(app))
                _allApps.Add(app);

            var appCard = new Border
            {
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 12, 12),
                Width = 300,
                CornerRadius = new CornerRadius(cornerRadius),
                BorderThickness = new Thickness(0),
                Effect = new DropShadowEffect
                {
                    ShadowDepth = shadowDepth,
                    BlurRadius = shadowBlur,
                    Opacity = shadowOpacity,
                    Color = Colors.Black
                }
            };
            appCard.SetResourceReference(Border.BackgroundProperty, "SurfaceColor");

            var appPanel = new StackPanel();

            // Check if we already have a checkbox for this app (preserves selection)
            var existingCheckbox = _appCheckBoxes.FirstOrDefault(cb => cb.Tag == app);
            CheckBox checkbox;

            if (existingCheckbox != null)
            {
                // Detach from old parent
                if (existingCheckbox.Parent is Panel oldParent)
                    oldParent.Children.Remove(existingCheckbox);
                checkbox = existingCheckbox;
            }
            else
            {
                checkbox = new CheckBox
                {
                    Tag = app,
                    FontSize = 14,
                    FontWeight = FontWeights.Medium
                };
                checkbox.Content = app.Name;
                checkbox.Checked += AppCheckBox_Changed;
                checkbox.Unchecked += AppCheckBox_Changed;
                _appCheckBoxes.Add(checkbox);
            }
            checkbox.SetResourceReference(ForegroundProperty, "TextPrimaryColor");

            var description = new TextBlock
            {
                Text = app.Description,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            description.SetResourceReference(ForegroundProperty, "TextSecondaryColor");

            appPanel.Children.Add(checkbox);
            appPanel.Children.Add(description);

            if (app.Popular)
            {
                var popularBadge = new TextBlock
                {
                    Text = "⭐ Popular",
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                popularBadge.SetResourceReference(ForegroundProperty, "AccentColor");
                appPanel.Children.Add(popularBadge);
            }

            // Installed label
            TextBlock installedLabel;
            if (_installedLabels.TryGetValue(app.WingetId, out var existing))
            {
                if (existing.Parent is Panel oldParent)
                    oldParent.Children.Remove(existing);
                installedLabel = existing;
            }
            else
            {
                installedLabel = new TextBlock
                {
                    Text = "✓ Installed",
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                installedLabel.SetResourceReference(ForegroundProperty, "PrimaryColor");
                _installedLabels[app.WingetId] = installedLabel;
            }
            appPanel.Children.Add(installedLabel);

            appCard.Child = appPanel;

            // Card clickable
            appCard.Cursor = Cursors.Hand;
            appCard.MouseLeftButtonDown += (s, args) =>
            {
                checkbox.IsChecked = !checkbox.IsChecked;
                args.Handled = true;
            };

            // Hover: shadow strengthens, subtle lift
            appCard.MouseEnter += (s, args) =>
            {
                if (appCard.Effect is DropShadowEffect shadow)
                {
                    shadow.BlurRadius = 20;
                    shadow.Opacity = 0.20;
                    shadow.ShadowDepth = 3;
                }
                appCard.RenderTransform = new TranslateTransform(0, -2);
            };
            appCard.MouseLeave += (s, args) =>
            {
                if (appCard.Effect is DropShadowEffect shadow)
                {
                    shadow.BlurRadius = shadowBlur;
                    shadow.Opacity = shadowOpacity;
                    shadow.ShadowDepth = shadowDepth;
                }
                appCard.RenderTransform = null;
            };

            appsGrid.Children.Add(appCard);
        }

        parent.Children.Add(appsGrid);
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

        var appsToSelect = new List<AppModel>();
        if (category.Apps != null) appsToSelect.AddRange(category.Apps);
        if (category.Subcategories != null)
            foreach (var subcat in category.Subcategories)
                appsToSelect.AddRange(subcat.Apps);

        foreach (var checkbox in _appCheckBoxes)
        {
            if (checkbox.Tag is AppModel app && appsToSelect.Contains(app))
                checkbox.IsChecked = true;
        }
    }

    private void AppCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var selectedCount = _appCheckBoxes.Count(cb => cb.IsChecked == true);
        SelectionCountText.Text = $"{selectedCount} app{(selectedCount != 1 ? "s" : "")} selected";
        InstallButton.IsEnabled = selectedCount > 0;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedApps = _appCheckBoxes
            .Where(cb => cb.IsChecked == true)
            .Select(cb => cb.Tag as AppModel)
            .Where(app => app != null)
            .Cast<AppModel>()
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
        {
            // On category grid: filter category cards
            SearchCategoryGrid(searchText);
        }
        else
        {
            // On app list: filter app cards
            SearchAppList(searchText);
        }
    }

    private void SearchCategoryGrid(string searchText)
    {
        if (_appDatabase == null) return;

        foreach (var child in CategoryGridPanel.Children)
        {
            if (child is Border catCard)
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    catCard.Visibility = Visibility.Visible;
                    continue;
                }

                // Find the category this card represents by matching the name text
                var content = catCard.Child as StackPanel;
                var nameBlock = content?.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.FontSize == 16);

                if (nameBlock != null)
                {
                    var catName = nameBlock.Text.ToLower();
                    // Also check if any apps in this category match the search
                    var category = _appDatabase.Categories
                        .FirstOrDefault(c => c.Name.ToLower() == catName);

                    var hasMatch = catName.Contains(searchText);
                    if (!hasMatch && category != null)
                    {
                        hasMatch = GetAllAppsInCategory(category)
                            .Any(a => a.Name.ToLower().Contains(searchText) ||
                                      a.Description.ToLower().Contains(searchText));
                    }

                    catCard.Visibility = hasMatch ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void SearchAppList(string searchText)
    {
        foreach (var checkbox in _appCheckBoxes)
        {
            if (checkbox.Tag is AppModel app)
            {
                var parent = checkbox.Parent as StackPanel;
                var card = parent?.Parent as Border;
                if (card != null)
                {
                    card.Visibility = string.IsNullOrWhiteSpace(searchText) ||
                                     app.Name.ToLower().Contains(searchText) ||
                                     app.Description.ToLower().Contains(searchText)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
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
}
