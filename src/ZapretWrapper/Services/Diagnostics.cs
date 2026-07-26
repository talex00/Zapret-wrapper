using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretWrapper.Services;

/// <summary>
/// Проверки окружения. Самый неприятный сценарий на Windows — когда winws запущен,
/// не ругается, а обход не работает. Практически всегда это одно из двух:
///
/// 1. В системе уже есть другой winws — служба zapret, ручной запуск general*.bat
///    или наш же процесс, оставшийся после падения приложения. Два инстанса
///    ставят конкурирующие фильтры WinDivert, и результат непредсказуем.
/// 2. DNS. Если имя не резолвится или резолвится в адрес заглушки, DPI-обход
///    бессилен: модифицировать пакеты к серверу, к которому мы даже не пошли,
///    нечего. Именно этим чаще всего объясняется «на линуксе рядом работает,
///    на винде нет»: там другой резолвер (DoH или свой DNS), а Windows берёт DNS роутера.
/// </summary>
public static class Diagnostics
{
    private static readonly string[] WinwsNames = { "winws", "winws2" };

    /// <summary>
    /// DoH по IP-литералу: так запрос не зависит от системного DNS вообще —
    /// только так можно узнать настоящие адреса, когда резолвер роутера врёт.
    /// Сертификат Cloudflare содержит IP SAN 1.1.1.1, проверка такого URL проходит.
    /// </summary>
    private const string DohUrl = "https://1.1.1.1/dns-query";

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
    /// Сверяет системный DNS с DoH и пишет вывод в журнал. Запускается фоном:
    /// это диагностика, тест из-за неё ждать не должен.
    /// </summary>
    public static void LogDns(IEnumerable<string> hosts)
    {
        var list = hosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0) return;

        _ = Task.Run(() => CheckDnsAsync(list));
    }

    /// <summary>
    /// Для каждого домена: что ответил системный резолвер и что ответил DoH.
    /// Расхождение — готовый диагноз: дело не в стратегиях, а в DNS.
    /// </summary>
    private static async Task CheckDnsAsync(List<string> hosts)
    {
        var systemBroken = new List<string>();
        var mismatched = new List<string>();

        foreach (var host in hosts)
        {
            var system = await ResolveSystemAsync(host);
            var doh = await ResolveDohAsync(host);

            if (system.Count > 0)
            {
                LogService.Debug($"DNS: {host} → {Format(system)} (системный резолвер)");
            }
            else
            {
                LogService.Warn($"DNS: {host} — системный резолвер не дал адреса.");
            }

            if (doh is null)
            {
                // Сам DoH мог не пройти (блокировка 1.1.1.1) — тогда сверять нечем.
                continue;
            }

            if (doh.Count == 0) continue;

            if (system.Count == 0)
            {
                systemBroken.Add(host);
                LogService.Debug($"DNS: {host} → {Format(doh)} (DoH 1.1.1.1)");
                continue;
            }

            // Сети доставки отдают разные адреса в разных ответах, поэтому сравниваем
            // не списки целиком, а /16-префиксы: подмена всегда уводит в чужую сеть.
            var systemPrefixes = system.Select(Prefix16).ToHashSet(StringComparer.Ordinal);
            var dohPrefixes = doh.Select(Prefix16).ToHashSet(StringComparer.Ordinal);

            if (!systemPrefixes.Overlaps(dohPrefixes))
            {
                mismatched.Add(host);
                LogService.Warn(
                    $"DNS: {host} — системный резолвер даёт {Format(system)}, а DoH — {Format(doh)}.");
            }
        }

        if (systemBroken.Count > 0)
        {
            LogService.Error(
                "DNS не работает для: " + string.Join(", ", systemBroken)
                + ". Адреса при этом спокойно получаются через DoH, то есть резолвер "
                + "роутера/провайдера отвечает «домена не существует». Обход DPI тут "
                + "бессилен: соединение даже не начинается. Пропишите DNS вручную "
                + "(1.1.1.1 / 8.8.8.8) или включите шифрование DNS в параметрах адаптера.");
        }
        else if (mismatched.Count > 0)
        {
            LogService.Error(
                "Подмена DNS для: " + string.Join(", ", mismatched)
                + ". Системный резолвер ведёт в другую сеть, чем DoH. Ни одна стратегия "
                + "не поможет, пока DNS не исправлен.");
        }
    }

    private static async Task<List<string>> ResolveSystemAsync(string host)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            return addresses
                .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                .Select(a => a.ToString())
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// DNS over HTTPS (JSON API Cloudflare). Возвращает null, если сам DoH недоступен,
    /// и пустой список, если домена действительно нет.
    /// </summary>
    private static async Task<List<string>?> ResolveDohAsync(string host)
    {
        try
        {
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(4),
                PooledConnectionLifetime = TimeSpan.Zero,
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{DohUrl}?name={Uri.EscapeDataString(host)}&type=A");
            request.Headers.Add("accept", "application/dns-json");

            using var response = await client.SendAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            var result = new List<string>();
            if (document.RootElement.TryGetProperty("Answer", out var answers)
                && answers.ValueKind == JsonValueKind.Array)
            {
                foreach (var answer in answers.EnumerateArray())
                {
                    // type 1 — A-запись; CNAME (5) пропускаем.
                    if (!answer.TryGetProperty("type", out var type) || type.GetInt32() != 1) continue;
                    if (!answer.TryGetProperty("data", out var data)) continue;

                    var value = data.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value!);
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string Format(List<string> addresses) =>
        string.Join(", ", addresses.Take(4))
        + (addresses.Count > 4 ? $" (всего {addresses.Count})" : string.Empty);

    /// <summary>Первые два октета IPv4; для IPv6 — сам адрес (сравнивать его с A-записями нет смысла).</summary>
    private static string Prefix16(string address)
    {
        var parts = address.Split('.');
        return parts.Length == 4 ? parts[0] + "." + parts[1] : address;
    }
}
