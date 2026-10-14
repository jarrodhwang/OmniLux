using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace OmniLux.Services;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static void Apply(string preference)
    {
        bool systemDark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            systemDark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException) { }
        IsDark = preference == "Dark" || (preference == "System" && systemDark);
        var values = IsDark
            ? new[] { "#16191E", "#1C2026", "#23282F", "#F0F3F6", "#B0B8C3", "#383F49", "#313944", "#68D8C5", "#122B27", "#253D39" }
            : new[] { "#F5F6F8", "#ECEEF2", "#FFFFFF", "#20242A", "#626973", "#DCDFE5", "#E4E8EE", "#007F78", "#FFFFFF", "#DCEEEB" };
        var names = new[] { "BackgroundBrush", "SidebarBrush", "CardBrush", "TextBrush", "MutedBrush", "StrokeBrush", "HoverBrush", "AccentBrush", "AccentTextBrush", "SelectionBrush" };
        for (int i = 0; i < names.Length; i++)
            Application.Current.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
        foreach (Window window in Application.Current.Windows) ApplyWindow(window);
    }

    public static void ApplyWindow(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        int dark = IsDark ? 1 : 0, corners = 2;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
