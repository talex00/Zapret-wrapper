using System;
using System.Diagnostics;
using System.Threading;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Запускает и останавливает winws2.exe. Поддерживает одновременно только один инстанс.
/// При первом запуске запрашивает UAC (Verb=runas) и кэширует elevation до закрытия процесса.
/// </summary>
public class ZapretRunner : IDisposable
{
    private Process? _process;
    private readonly object _lock = new();

    public bool IsRunning
    {
        get { lock (_lock) return _process is { HasExited: false }; }
    }

    public string? LastError { get; private set; }

    public bool IsValid => SettingsService.Current.ZapretPath is { } p
        && ZapretLocator.Validate(p).IsValid;

    public event EventHandler? ProcessExited;

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
            if (path is null || !ZapretLocator.Validate(path).IsValid)
            {
                LastError = "zapret2 не настроен. Укажите путь в Настройках.";
                return false;
            }

            var paths = ZapretLocator.ResolvePaths(path);
            var args = ZapretLocator.ResolveArgs(strategy.Args.ToArray(), path);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = paths.WinwsExe,
                    WorkingDirectory = path,
                    UseShellExecute = true,
                    Verb = "runas", // Запросить UAC.
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                _process = Process.Start(psi);
                if (_process is null)
                {
                    LastError = "Process.Start вернул null";
                    return false;
                }
                _process.EnableRaisingEvents = true;
                _process.Exited += (_, _) => ProcessExited?.Invoke(this, EventArgs.Empty);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    /// <summary>Остановить запущенный winws2.exe.</summary>
    public bool Stop()
    {
        lock (_lock)
        {
            if (_process is null || _process.HasExited) return true;
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    public void Dispose()
    {
        try { Stop(); } catch { /* ignore */ }
        _process?.Dispose();
    }
}
