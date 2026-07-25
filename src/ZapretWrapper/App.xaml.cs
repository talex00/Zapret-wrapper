using System;
using System.ComponentModel;
using System.Diagnostics;
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

        // Приложение должно быть админом целиком. Тогда winws2.exe наследует токен и стартует
        // без UAC — раньше подтверждение прав всплывало на КАЖДУЮ тестируемую стратегию.
        if (!ZapretRunner.IsElevated && !TryRestartElevated())
        {
            MessageBox.Show(
                "Без прав администратора winws2.exe не сможет перехватывать трафик: " +
                "запуск и тестирование стратегий работать не будут.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        try
        {
            _ = SettingsService.Current;  // триггер загрузки настроек
        }
        catch { /* ignore */ }

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

    /// <summary>Перезапускает себя с повышением прав: один UAC-запрос на всё приложение.</summary>
    private bool TryRestartElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
            });
            Shutdown();
            return true;
        }
        catch (Win32Exception)
        {
            // Пользователь отказался в диалоге UAC.
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Runner?.Dispose();
        base.OnExit(e);
    }
}
