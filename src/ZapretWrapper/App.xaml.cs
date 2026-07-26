using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
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

        InstallCrashHandlers();

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

    /// <summary>
    /// Приложение молча вылетало: исключение в фоновом потоке убивает процесс без следов.
    /// Теперь любая необработанная ошибка попадает в журнал и показывается пользователю.
    /// </summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Report(args.Exception);
            args.Handled = true;  // UI-поток не роняем
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report(args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Report(args.Exception);
            args.SetObserved();
        };
    }

    private static void Report(Exception? ex)
    {
        if (ex is null) return;

        try
        {
            LogService.Error("Необработанная ошибка: " + ex);
        }
        catch { /* логгер не должен мешать */ }

        try
        {
            var text = "Произошла ошибка:\n\n" + ex.Message +
                       "\n\nПодробности в журнале:\n" + LogService.LogPath;

            void Show() => MessageBox.Show(text, "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Error);

            var app = Current;
            if (app is null) return;
            if (app.Dispatcher.CheckAccess()) Show();
            else app.Dispatcher.BeginInvoke(new Action(Show));
        }
        catch { /* ignore */ }
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
