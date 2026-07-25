using System;
using System.Windows;
using ZapretWrapper.Services;
using ZapretWrapper.Styles;

namespace ZapretWrapper;

public partial class App : Application
{
    public static ZapretRunner? Runner { get; private set; }
    public static StrategyTester? Tester { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Загружаем настройки ДО первого обращения к ним.
        try
        {
            _ = SettingsService.Current; // триггер загрузки
        }
        catch { /* ignore */ }

        // Применяем тему из настроек.
        var theme = SettingsService.Current.Theme switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.System
        };
        ThemeManager.Apply(theme);

        Runner = new ZapretRunner();
        Tester = new StrategyTester(Runner);

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        main.RefreshStatus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Runner?.Dispose();
        base.OnExit(e);
    }
}
