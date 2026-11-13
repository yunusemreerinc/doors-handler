using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DoorsHandler.Desktop.Theming;

public enum AppTheme
{
    /// <summary>Follow the Windows "app mode" setting (Settings → Personalization → Colors).</summary>
    System,
    Light,
    Dark,
}

/// <summary>Switches the application's colour palette and the window title bars between light and dark.</summary>
public static class ThemeManager
{
    private static readonly Uri LightPalette = new("/DoorsHandler;component/Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("/DoorsHandler;component/Themes/Dark.xaml", UriKind.Relative);

    private static ResourceDictionary? _palette;
    private static bool _listeningToSystem;

    public static AppTheme Selected { get; private set; } = AppTheme.System;

    /// <summary>The theme actually shown (after resolving <see cref="AppTheme.System"/>).</summary>
    public static bool IsDark { get; private set; }

    public static void Apply(AppTheme theme)
    {
        Selected = theme;
        var dark = theme == AppTheme.Dark || (theme == AppTheme.System && SystemPrefersDark());
        SetPalette(dark);

        if (theme == AppTheme.System && !_listeningToSystem)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _listeningToSystem = true;
        }
        else if (theme != AppTheme.System && _listeningToSystem)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _listeningToSystem = false;
        }
    }

    /// <summary>Makes the window's title bar match the theme (Windows 10 20H1 and later; ignored elsewhere).</summary>
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var useDark = IsDark ? 1 : 0;
        try
        {
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; builds before Windows 10 20H1 used 19.
            if (DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref useDark, sizeof(int));
            }
        }
        catch (Exception)
        {
            // dwmapi missing or attribute unsupported: keep the default title bar.
        }
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
        IsDark = dark;

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

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;

        // SystemEvents may raise this on another thread.
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (Selected != AppTheme.System) return;
            var dark = SystemPrefersDark();
            if (dark != IsDark) SetPalette(dark);
        });
    }

    /// <summary>Reads the Windows "default app mode" (light/dark). Assumes light if it cannot be read.</summary>
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
