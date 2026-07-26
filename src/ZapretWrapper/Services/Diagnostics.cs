using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ZapretWrapper.Services;

/// <summary>
/// Проверки окружения. Самый неприятный сценарий на Windows — когда winws запущен,
/// не ругается, а обход не работает. Практически всегда это одно из двух:
///
/// 1. В системе уже есть другой winws — служба zapret, ручной запуск general*.bat
///    или наш же процесс, оставшийся после падения приложения. Два инстанса
///    ставят конкурирующие фильтры WinDivert, и результат непредсказуем: пакеты
///    модифицируются дважды либо не модифицируются вовсе.
/// 2. Подмена DNS. DPI-обход против неё бессилен: соединение уходит на адрес
///    заглушки провайдера, а там никакого YouTube нет. Именно этим чаще всего
///    объясняется «на линуксе рядом работает, на винде нет»: там другой резолвер.
/// </summary>
public static class Diagnostics
{
    private static readonly string[] WinwsNames = { "winws", "winws2" };

    /// <summary>
    /// Ищет посторонние процессы winws/winws2 и завершает их. Возвращает число
    /// завершённых. Мы всегда работаем от админа, так что прав для Kill достаточно.
    /// </summary>
    public static int StopForeignWinws(int ownPid = 0)
    {
        var killed = 0;

        foreach (var name in WinwsNames)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in found)
            {
                try
                {
                    if (process.Id == ownPid) continue;

                    LogService.Warn(
                        $"Найдён посторонний {name} (PID {process.Id}): он держит свой фильтр WinDivert "
                        + "и мешает обходу. Завершаю его.");

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                    killed++;
                }
                catch (Exception ex)
                {
                    LogService.Warn(
                        $"Не удалось завершить {name}: {ex.Message}. Если установлена служба zapret, "
                        + "снимите её через service.bat — иначе два winws будут мешать друг другу.");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return killed;
    }

    /// <summary>
    /// Резолвит проверяемые домены и пишет в журнал адреса. Запускается фоном:
    /// это справочная информация, тест из-за неё ждать не должен.
    /// </summary>
    public static void LogDns(IEnumerable<string> hosts)
    {
        var list = hosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0) return;

        _ = Task.Run(async () =>
        {
            var byAddress = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var host in list)
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(host);
                    if (addresses.Length == 0)
                    {
                        LogService.Warn($"DNS: {host} не резолвится вовсе.");
                        continue;
                    }

                    var v4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
                    var v6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();

                    LogService.Debug(
                        $"DNS: {host} → {string.Join(", ", addresses.Take(4).Select(a => a.ToString()))}"
                        + (addresses.Length > 4 ? $" (всего {addresses.Length})" : string.Empty)
                        + (v6.Count > 0 && v4.Count == 0 ? " — только IPv6" : string.Empty));

                    foreach (var address in addresses)
                    {
                        var key = address.ToString();
                        if (!byAddress.TryGetValue(key, out var owners))
                        {
                            owners = new List<string>();
                            byAddress[key] = owners;
                        }

                        if (!owners.Contains(host, StringComparer.OrdinalIgnoreCase)) owners.Add(host);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warn($"DNS: {host} — ошибка разрешения имени: {ex.Message}");
                }
            }

            // Один IP на домены разных сервисов (YouTube и Discord вместе) — почти всегда
            // заглушка провайдера. С таким DNS ни одна стратегия не заработает.
            var suspicious = byAddress
                .Where(kv => kv.Value.Select(RootDomain).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
                .ToList();

            foreach (var kv in suspicious)
            {
                LogService.Warn(
                    $"DNS подозрителен: адрес {kv.Key} отдаётся сразу для "
                    + string.Join(", ", kv.Value)
                    + ". Похоже на подмену DNS провайдером — DPI-обход тут не поможет, "
                    + "нужен другой DNS (DoH или 8.8.8.8/1.1.1.1).");
            }
        });
    }

    /// <summary>Грубое «domain.tld» из имени хоста — только для группировки в журнале.</summary>
    private static string RootDomain(string host)
    {
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? host : string.Join('.', parts.Skip(parts.Length - 2));
    }
}
