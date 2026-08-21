using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MIDImunger.W;

public static class ThemeResources
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void Apply(ResourceDictionary resources)
    {
        var palette = IsDarkModeEnabled() ? ThemePalette.Dark : ThemePalette.Light;
        SetBrush(resources, "WindowBackgroundBrush", palette.WindowBackground);
        SetBrush(resources, "SurfaceBackgroundBrush", palette.SurfaceBackground);
        SetBrush(resources, "ControlBackgroundBrush", palette.ControlBackground);
        SetBrush(resources, "HeaderBackgroundBrush", palette.HeaderBackground);
        SetBrush(resources, "ControlForegroundBrush", palette.ControlForeground);
        SetBrush(resources, "SubtleForegroundBrush", palette.SubtleForeground);
        SetBrush(resources, "HeadingForegroundBrush", palette.HeadingForeground);
        SetBrush(resources, "BorderBrush", palette.Border);
        SetBrush(resources, "AlternateRowBackgroundBrush", palette.AlternateRowBackground);
        SetBrush(resources, "SelectionBackgroundBrush", palette.SelectionBackground);
        SetBrush(resources, "ButtonHoverBackgroundBrush", palette.ButtonHoverBackground);
        SetBrush(resources, "ButtonHoverForegroundBrush", palette.ButtonHoverForeground);
        SetBrush(resources, "ButtonPressedBackgroundBrush", palette.ButtonPressedBackground);
        SetBrush(resources, "ButtonPressedForegroundBrush", palette.ButtonPressedForeground);
        SetBrush(resources, "LedBackgroundBrush", palette.LedBackground);
        SetBrush(resources, "LedForegroundBrush", palette.LedForeground);
        SetBrush(resources, "LedBorderBrush", palette.LedBorder);
    }

    public static void ApplyWindowTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var useDarkMode = IsDarkModeEnabled() ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref useDarkMode, Marshal.SizeOf<int>());

        var palette = IsDarkModeEnabled() ? ThemePalette.Dark : ThemePalette.Light;
        var borderColor = ToColorRef(palette.WindowBackground);
        var captionColor = ToColorRef(palette.WindowBackground);
        var textColor = ToColorRef(palette.ControlForeground);
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, Marshal.SizeOf<int>());
    }

    private static bool IsDarkModeEnabled()
    {
        using var personalizeKey = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
        return personalizeKey?.GetValue("AppsUseLightTheme") switch
        {
            int value => value == 0,
            _ => false
        };
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private sealed record ThemePalette(
        Color WindowBackground,
        Color SurfaceBackground,
        Color ControlBackground,
        Color HeaderBackground,
        Color ControlForeground,
        Color SubtleForeground,
        Color HeadingForeground,
        Color Border,
        Color AlternateRowBackground,
        Color SelectionBackground,
        Color ButtonHoverBackground,
        Color ButtonHoverForeground,
        Color ButtonPressedBackground,
        Color ButtonPressedForeground,
        Color LedBackground,
        Color LedForeground,
        Color LedBorder)
    {
        public static ThemePalette Light { get; } = new(
            Color.FromRgb(0xF7, 0xF7, 0xF7),
            Colors.White,
            Colors.White,
            Color.FromRgb(0xF0, 0xF0, 0xF0),
            Color.FromRgb(0x1F, 0x1F, 0x1F),
            Color.FromRgb(0x66, 0x66, 0x66),
            Color.FromRgb(0x0B, 0x53, 0x94),
            Color.FromRgb(0xD0, 0xD0, 0xD0),
            Color.FromRgb(0xF7, 0xF7, 0xF7),
            Color.FromRgb(0xCC, 0xE8, 0xFF),
            Color.FromRgb(0xE5, 0xF1, 0xFB),
            Color.FromRgb(0x1F, 0x1F, 0x1F),
            Color.FromRgb(0xCC, 0xE4, 0xF7),
            Color.FromRgb(0x1F, 0x1F, 0x1F),
            Color.FromRgb(0x08, 0x11, 0x08),
            Color.FromRgb(0x3C, 0xF5, 0x63),
            Color.FromRgb(0x1E, 0x4A, 0x24));

        public static ThemePalette Dark { get; } = new(
            Color.FromRgb(0x1E, 0x1E, 0x1E),
            Color.FromRgb(0x25, 0x25, 0x26),
            Color.FromRgb(0x2D, 0x2D, 0x30),
            Color.FromRgb(0x33, 0x33, 0x37),
            Color.FromRgb(0xF3, 0xF3, 0xF3),
            Color.FromRgb(0xB8, 0xB8, 0xB8),
            Color.FromRgb(0x7D, 0xC3, 0xFF),
            Color.FromRgb(0x3F, 0x3F, 0x46),
            Color.FromRgb(0x20, 0x20, 0x22),
            Color.FromRgb(0x09, 0x47, 0x71),
            Color.FromRgb(0x4C, 0x89, 0xC8),
            Color.FromRgb(0x0F, 0x0F, 0x10),
            Color.FromRgb(0x68, 0xA0, 0xD6),
            Color.FromRgb(0x0F, 0x0F, 0x10),
            Color.FromRgb(0x08, 0x11, 0x08),
            Color.FromRgb(0x5C, 0xFF, 0x72),
            Color.FromRgb(0x1E, 0x4A, 0x24));
    }
}
