using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Запускает и останавливает winws2.exe. Поддерживает только один инстанс сразу.
/// Само приложение поднимается с правами администратора (app.manifest), поэтому
/// процесс стартует без UAC-диалога, его вывод можно читать, а его самого — убить.
/// </summary>
public class ZapretRunner : IDisposable
{
    private Process? _process;
    private readonly object _lock = new();
    private volatile bool _stopping;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (_process is null) return false;
                try
                {
                    return !_process.HasExited;
                }
                catch
                {
                    // Процесс уже освобождён — считаем, что не работает.
                    return false;
                }
            }
        }
    }

    public string? LastError { get; private set; }

    /// <summary>Стратегия, с которой запущен текущий процесс.</summary>
    public Strategy? CurrentStrategy { get; private set; }

    public bool IsValid => ZapretLocator.Validate(SettingsService.Current.ZapretPath).IsValid;

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Любая смена состояния: запуск, остановка, неожиданное падение.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Процесс завершился сам, без вызова Stop().</summary>
    public event EventHandler? ProcessExited;

    /// <summary>
    /// События раннера подписывает UI (MainWindow обновляет индикатор состояния), а
    /// Process.Exited приходит из пула потоков. Без этого перехода в поток диспетчера
    /// обработчик падал с InvalidOperationException и ронял всё приложение.
    /// </summary>
    private static void OnUi(Action action)
    {
        var app = Application.Current;
        if (app is null)
        {
            action();
            return;
        }

        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    private void RaiseStateChanged() =>
        OnUi(() => StateChanged?.Invoke(this, EventArgs.Empty));

    /// <summary>Запустить winws2.exe с аргументами стратегии.</summary>
    public bool Start(Strategy strategy)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
            {
                LastError = "winws2 уже запущен";
                return false;
            }

            var path = SettingsService.Current.ZapretPath;
            var layout = ZapretLocator.Validate(path);
            if (!layout.IsValid)
            {
                LastError = layout.Error
                    ?? ("Не найдены файлы: " + string.Join(", ", layout.Missing));
                LogService.Error($"Запуск отменён: {LastError}");
                return false;
            }

            if (!IsElevated)
            {
                LastError = "Приложение должно быть запущено от имени администратора.";
                LogService.Error(LastError);
                return false;
            }

            var paths = ZapretLocator.ResolvePaths(path!);
            var args = ZapretLocator.ResolveArgs(strategy.Args.ToArray(), path!);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = paths.WinwsExe,
                    WorkingDirectory = path,
                    // UseShellExecute=false обязателен для перехвата вывода winws2.
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                LogService.Debug($"{paths.WinwsExe} {string.Join(" ", args)}");

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data)) LogService.Debug("winws2: " + e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data)) LogService.Warn("winws2: " + e.Data);
                };
                process.Exited += OnProcessExited;

                _stopping = false;

                if (!process.Start())
                {
                    LastError = "Не удалось запустить winws2.exe";
                    process.Dispose();
                    LogService.Error(LastError);
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _process = process;
                CurrentStrategy = strategy;
                LastError = null;
                LogService.Success(
                    $"winws2 запущен (PID {process.Id}), стратегия «{strategy.Name}».");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LogService.Error($"Ошибка запуска winws2: {ex.Message}");
                return false;
            }
        }

        RaiseStateChanged();
        return true;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // Штатную остановку обрабатывает Stop(); сюда попадают только падения.
        if (_stopping) return;

        CurrentStrategy = null;
        LogService.Warn("winws2 завершил работу неожиданно.");

        // Вызов приходит из пула потоков — подписчиков дёргаем только в UI-потоке.
        OnUi(() =>
        {
            ProcessExited?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Остановить запущенный winws2.exe.</summary>
    public bool Stop()
    {
        bool changed = false;

        lock (_lock)
        {
            if (_process is null)
            {
                LastError = null;
                return true;
            }

            _stopping = true;
            try
            {
                // Обработчик снимаем до Kill(): событие Exited приходит асинхронно и
                // успевало прилететь уже после сброса _stopping — штатная остановка
                // выглядела как падение winws2 и рвала тест на первой же стратегии.
                _process.Exited -= OnProcessExited;

                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    if (!_process.WaitForExit(5000))
                    {
                        LastError = "winws2 не завершился за 5 секунд.";
                        LogService.Warn(LastError);
                        return false;
                    }

                    LogService.Info("winws2 остановлен.");
                }

                _process.Dispose();
                _process = null;
                CurrentStrategy = null;
                LastError = null;
                changed = true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                LogService.Error($"Ошибка остановки winws2: {ex.Message}");
                return false;
            }
            finally
            {
                _stopping = false;
            }
        }

        if (changed) RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Ждёт, пока WinDivert поставит фильтр. Без этой паузы первые пробы тестера
    /// проваливаются ложно. Возвращает false, если процесс не пережил запуск.
    /// </summary>
    public async Task<bool> WaitUntilReadyAsync(int milliseconds = 1500, CancellationToken ct = default)
    {
        await Task.Delay(milliseconds, ct);
        return IsRunning;
    }

    public void Dispose()
    {
        try { Stop(); } catch { /* ignore */ }
        lock (_lock)
        {
            _process?.Dispose();
            _process = null;
        }
        GC.SuppressFinalize(this);
    }
}
