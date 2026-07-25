using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ZapretWrapper.Styles;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// Управляет активной палитрой приложения.
/// При смене темы пересоздаёт SolidColorBrush-ресурсы (ключи активных *Brush),
/// обновляя их Color из соответствующих Light.* / Dark.* Color-токенов.
/// </summary>
public static class ThemeManager
{
    private const string LightPrefix = "Light.";
    private const string DarkPrefix = "Dark.";

    /// <summary>
    /// Пары: активная кисть (имя в Application.Resources) → токен Color (Light.* / Dark.*).
    /// Имя кисти = "BackgroundBrush" → имя токена = "Light.BackgroundColor" / "Dark.BackgroundColor".
    /// </summary>
    private static readonly Dictionary<string, string> _brushToColorToken = new()
    {
        ["BackgroundBrush"]   = "BackgroundColor",
        ["SidebarBrush"]      = "SidebarColor",
        ["SurfaceBrush"]      = "SurfaceColor",
        ["SurfaceAltBrush"]   = "SurfaceAltColor",
        ["BorderBrush"]       = "BorderColor",
        ["BorderStrongBrush"] = "BorderStrongColor",
        ["TextBrush"]         = "TextColor",
        ["TextMutedBrush"]    = "TextMutedColor",
        ["TextSubtleBrush"]   = "TextSubtleColor",
        ["AccentBrush"]       = "AccentColor",
        ["AccentHoverBrush"]  = "AccentHoverColor",
        ["AccentPressedBrush"] = "AccentPressedColor",
        ["AccentLightBrush"]  = "AccentLightColor",
        ["AccentTextBrush"]   = "AccentTextColor",
        ["SuccessBrush"]      = "SuccessColor",
        ["SuccessLightBrush"] = "SuccessLightColor",
        ["DangerBrush"]       = "DangerColor",
        ["DangerLightBrush"]  = "DangerLightColor",
        ["WarnBrush"]         = "WarnColor",
        ["WarnLightBrush"]    = "WarnLightColor",
        ["NeutralLightBrush"] = "NeutralLightColor",
        ["PingBrush"]         = "PingColor",
        ["LossBrush"]         = "LossColor",
        ["SpeedBrush"]        = "SpeedColor",
        ["TrackBrush"]        = "NeutralLightColor",
    };

    public static AppTheme Current { get; private set; } = AppTheme.System;

    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        var actual = theme == AppTheme.System ? ResolveSystemTheme() : theme;
        ApplyPalette(actual);
        Current = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Toggle()
    {
        var next = Current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        Apply(next);
    }

    public static AppTheme ResolveSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
            {
                return i == 0 ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch
        {
            // Реестр недоступен — фолбэк на Light.
        }
        return AppTheme.Light;
    }

    private static void ApplyPalette(AppTheme theme)
    {
        var prefix = theme == AppTheme.Dark ? DarkPrefix : LightPrefix;
        var resources = Application.Current.Resources;

        foreach (var (brushKey, tokenName) in _brushToColorToken)
        {
            var colorKey = prefix + tokenName;
            if (resources[colorKey] is not Color color) continue;

            // Всегда заменяем кисть целиком — это надёжно триггерит уведомление подписчиков
            // DynamicResource во всех шаблонах и контролах.
            var newBrush = new SolidColorBrush(color);
            newBrush.Freeze();
            resources[brushKey] = newBrush;
        }
    }
}
