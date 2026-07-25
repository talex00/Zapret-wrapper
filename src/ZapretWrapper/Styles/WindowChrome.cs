using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZapretWrapper.Styles;

/// <summary>
/// Позволяет управлять оформлением неклиентской области окна: цвет заголовка и фрейма.
/// В Win11 22H2+ (build 22621) поддерживаются DWMWA_CAPTION_COLOR и DWMWA_BORDER_COLOR.
/// В более старых версиях вызовы тихо игнорируются.
/// </summary>
public static class WindowChrome
{
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero) return;

        // Цвет caption и рамки должен совпадать с цветом собственного header-бара
        // MainWindow (SidebarBrush), чтобы системный title bar визуально сливался с приложением.
        var bg = TryFindBrushColor(window, "SidebarBrush")
                 ?? TryFindBrushColor(window, "BackgroundBrush")
                 ?? Colors.Black;

        var colorRef = ToColorRef(bg);
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));

        // Тёмная тема — заголовок тоже в тёмном режиме (белый текст/иконки).
        var useDark = (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) < 128 ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
    }

    private static Color? TryFindBrushColor(Window window, string key)
    {
        if (window.TryFindResource(key) is SolidColorBrush brush)
            return brush.Color;
        return null;
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
