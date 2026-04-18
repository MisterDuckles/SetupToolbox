using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;

namespace WingetAppDeployer_WinUI;

public sealed partial class MainWindow : Window
{
    private WindowsSystemDispatcherQueueHelper? _wsdqHelper;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configurationSource;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Enable Desktop Acrylic backdrop (Thin variant = stronger see-through)
        TrySetAcrylicBackdrop(useAcrylicThin: true);
    }

    private bool TrySetAcrylicBackdrop(bool useAcrylicThin)
    {
        if (!DesktopAcrylicController.IsSupported())
            return false;

        _wsdqHelper = new WindowsSystemDispatcherQueueHelper();
        _wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

        // Hook up the policy object so the backdrop reacts to window activation
        // and theme changes correctly.
        _configurationSource = new SystemBackdropConfiguration();
        Activated += Window_Activated;
        Closed += Window_Closed;
        if (Content is FrameworkElement root)
            root.ActualThemeChanged += Window_ThemeChanged;

        _configurationSource.IsInputActive = true;
        SetConfigurationSourceTheme();

        _acrylicController = new DesktopAcrylicController
        {
            Kind = useAcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base
        };

        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_configurationSource);
        return true;
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
            _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_acrylicController != null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }
        Activated -= Window_Activated;
        _configurationSource = null;
    }

    private void Window_ThemeChanged(FrameworkElement sender, object args)
    {
        if (_configurationSource != null)
            SetConfigurationSourceTheme();
    }

    private void SetConfigurationSourceTheme()
    {
        if (_configurationSource == null || Content is not FrameworkElement root) return;
        _configurationSource.Theme = root.ActualTheme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }
}
