using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TerminalLauncher.Interop;

/// <summary>
/// حواف منحنية لنافذة معتِمة (بلا AllowsTransparency، حتى يعمل الكونسول المُحتضَن):
/// ويندوز 11 عبر DWM corner preference (ناعمة)، ويندوز 10 عبر Region (SetWindowRgn).
/// </summary>
public static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseW, int ellipseH);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_DONOTROUND = 1;

    private static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>يطبّق حواف منحنية بنصف قطر معيّن (DIP).</summary>
    public static void ApplyRoundedCorners(Window window, int radius)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (IsWindows11)
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            return;
        }

        // ويندوز 10: Region يدوي بوحدات بكسل الجهاز.
        double scale = 1.0;
        var src = PresentationSource.FromVisual(window);
        if (src?.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;

        int w = (int)Math.Round(window.ActualWidth * scale);
        int h = (int)Math.Round(window.ActualHeight * scale);
        if (w <= 0 || h <= 0) return;

        int d = (int)Math.Round(radius * 2 * scale);
        IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d);
        SetWindowRgn(hwnd, rgn, true); // النظام يملك الـ region بعدها.
    }

    /// <summary>يلغي التدوير (عند التكبير) فتملأ النافذة الشاشة بزوايا قائمة.</summary>
    public static void ClearRoundedCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (IsWindows11)
        {
            int pref = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        else
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }
    }
}
