using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DoorsHandler.Desktop.Theming;

public enum AppTheme
{
    Light,
    Dark,
}

/// <summary>Switches the application's colour palette and the window title bars between light and dark.</summary>
public static class ThemeManager
{
    private static readonly Uri LightPalette = new("/DoorsHandler;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("/DoorsHandler;component/Themes/Dark.xaml", UriKind.Relative);

    private static ResourceDictionary? _palette;

    public static AppTheme Selected { get; private set; } = AppTheme.Light;

    public static bool IsDark => Selected == AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        Selected = theme;
        SetPalette(theme == AppTheme.Dark);
    }

    // DWM window attributes (dwmapi.h).
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;   // Windows 11+
    private const int DwmwaCaptionColor = 35;  // Windows 11+
    private const int DwmwaTextColor = 36;     // Windows 11+

    /// <summary>
    /// Makes the window's title bar match the theme: dark with light text in the dark theme,
    /// white with dark text in the light theme.
    /// </summary>
    /// <remarks>
    /// Windows 11 gets the exact colours from the palette (TitleBarBrush, TitleBarTextBrush,
    /// TitleBarBorderBrush), which also overrides an accent-coloured title bar. Windows 10
    /// (20H1 and later) only supports switching between the system light and dark title bars.
    /// The immersive dark mode flag is set on both, because it also controls the colour of the
    /// minimise/maximise/close buttons. Other Windows versions keep the default title bar.
    /// </remarks>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        try
        {
            var useDark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref useDark, sizeof(int));
            }

            // These fail harmlessly (E_INVALIDARG) before Windows 11.
            SetColorAttribute(window, handle, DwmwaCaptionColor, "TitleBarBrush");
            SetColorAttribute(window, handle, DwmwaTextColor, "TitleBarTextBrush");
            SetColorAttribute(window, handle, DwmwaBorderColor, "TitleBarBorderBrush");

            RepaintFrame(window, handle);
        }
        catch (Exception)
        {
            // dwmapi missing or attribute unsupported: keep the default title bar.
        }
    }

    private static void SetColorAttribute(Window window, IntPtr handle, int attribute, string resourceKey)
    {
        if (window.TryFindResource(resourceKey) is not System.Windows.Media.SolidColorBrush brush) return;

        // COLORREF is 0x00BBGGRR.
        var c = brush.Color;
        var colorRef = c.R | (c.G << 8) | (c.B << 16);
        DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    /// <summary>
    /// Windows does not always repaint the title bar after its attributes change, so a theme
    /// switch would only show once the window is deactivated or resized. Force a repaint now.
    /// </summary>
    private static void RepaintFrame(Window window, IntPtr handle)
    {
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        if (!window.IsVisible) return;

        // Windows 10 ignores the frame change for the caption; toggling the non-client
        // active state redraws it. The second message restores the real state.
        var active = window.IsActive ? 1 : 0;
        SendMessage(handle, WmNcActivate, (IntPtr)(1 - active), IntPtr.Zero);
        SendMessage(handle, WmNcActivate, (IntPtr)active, IntPtr.Zero);
    }

    private static void SetPalette(bool dark)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = dark ? DarkPalette : LightPalette };
        var index = _palette is null ? FindPaletteIndex(merged) : merged.IndexOf(_palette);
        if (index >= 0)
        {
            merged[index] = next;
        }
        else
        {
            merged.Insert(0, next);
        }

        _palette = next;

        foreach (Window window in app.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    private static int FindPaletteIndex(IList<ResourceDictionary> merged)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reads the Windows "default app mode" (light/dark). Only used to pick the theme on the
    /// first run, before the user has chosen one. Assumes light if it cannot be read.
    /// </summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int WmNcActivate = 0x0086;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
