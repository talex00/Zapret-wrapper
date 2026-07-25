using System;
using System.Windows;
using ZapretWrapper.Styles;

namespace ZapretWrapper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // TEMP: принудительно тёмная тема для скриншот-теста.
        var theme = Environment.GetEnvironmentVariable("ZW_THEME") switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => ThemeManager.ResolveSystemTheme()
        };
        ThemeManager.Apply(theme);

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }
}
