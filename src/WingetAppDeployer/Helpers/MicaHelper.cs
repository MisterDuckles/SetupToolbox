using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace WingetAppDeployer.Helpers;

/// <summary>
/// Enables Windows 11 Mica backdrop and WindowChrome for native Fluent Design look.
/// Gracefully degrades on Windows 10 (no-op).
/// </summary>
public static class MicaHelper
{
    // DWM attributes
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    // Backdrop types
    private const int DWMSBT_DISABLE = 1;
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    // Margins for DwmExtendFrameIntoClientArea
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;

        public MARGINS(int left, int right, int top, int bottom)
        {
            Left = left; Right = right; Top = top; Bottom = bottom;
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    /// <summary>
    /// Returns true if running on Windows 11 (build 22000+).
    /// </summary>
    public static bool IsWindows11OrGreater()
    {
        return Environment.OSVersion.Version.Build >= 22000;
    }

    /// <summary>
    /// Enables Mica backdrop on a WPF window. Must be called after window handle is available
    /// (SourceInitialized or later). Returns true if Mica was successfully enabled.
    /// </summary>
    public static bool EnableMica(Window window, bool darkMode)
    {
        if (!IsWindows11OrGreater())
            return false;

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            // Set dark/light mode for title bar
            int isDark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));

            // Enable Mica backdrop
            int backdropType = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // Extend frame into client area (required for Mica to render)
            var margins = new MARGINS(-1, -1, -1, -1);
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // Make window background transparent so Mica shows through
            window.Background = Brushes.Transparent;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets only the dark/light mode for the window's title bar (no Mica, no transparency).
    /// Use this for child windows that should keep an opaque background but match the system theme.
    /// </summary>
    public static void SetTitleBarTheme(Window window, bool darkMode)
    {
        if (!IsWindows11OrGreater())
            return;

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            int isDark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// Disables Mica backdrop and restores the window's opaque background.
    /// </summary>
    public static bool DisableMica(Window window)
    {
        if (!IsWindows11OrGreater())
            return false;

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            // Disable backdrop
            int backdropType = DWMSBT_DISABLE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            // Reset frame margins
            var margins = new MARGINS(0, 0, 0, 0);
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // Restore background from theme resource
            if (window.TryFindResource("BackgroundColor") is SolidColorBrush bg)
                window.Background = bg;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies WindowChrome for title bar integration (content extends into caption area).
    /// </summary>
    public static void ApplyWindowChrome(Window window, double captionHeight = 64)
    {
        var chrome = new WindowChrome
        {
            CaptionHeight = captionHeight,
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(8)
        };
        WindowChrome.SetWindowChrome(window, chrome);
    }

    /// <summary>
    /// Removes WindowChrome, restoring default window decorations.
    /// </summary>
    public static void RemoveWindowChrome(Window window)
    {
        WindowChrome.SetWindowChrome(window, null);
    }
}
