using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Прогоняет одну стратегию через единственный ZapretRunner и пробует список тестовых
/// доменов. Так как winws2 — один процесс на всё приложение, вызывающий код должен
/// тестировать стратегии последовательно (один RunAsync за раз).
/// </summary>
public class StrategyTester
{
    public class StrategyTestOutcome
    {
        public Strategy Strategy { get; init; } = null!;
        public int Successes { get; init; }
        public int Total { get; init; }
        public TimeSpan AverageLatency { get; init; }
        public string? Error { get; init; }
    }

    public readonly record struct StrategyTestProgress(int Completed, int Total, string Domain, bool Success);

    private readonly ZapretRunner _runner;

    public StrategyTester(ZapretRunner runner)
    {
        _runner = runner;
    }

    public async Task<StrategyTestOutcome> RunAsync(
        Strategy strategy,
        IEnumerable<string> domains,
        int attemptsPerDomain = 2,
        int timeoutSeconds = 5,
        IProgress<StrategyTestProgress>? progress = null,
        CancellationToken ct = default)
    {
        var domainList = domains as IList<string> ?? domains.ToList();
        var totalAttempts = domainList.Count * attemptsPerDomain;

        var started = _runner.Start(strategy);
        if (!started)
        {
            return new StrategyTestOutcome
            {
                Strategy = strategy,
                Successes = 0,
                Total = totalAttempts,
                AverageLatency = TimeSpan.Zero,
                Error = _runner.LastError ?? "Не удалось запустить стратегию",
            };
        }

        var latencies = new List<double>();
        int successes = 0;
        int total = 0;
        string? error = null;

        try
        {
            // Даём WinDivert время поставить фильтр перед первым запросом.
            await _runner.WaitUntilReadyAsync(SettingsService.Current.StartupDelayMs, ct);

            foreach (var domain in domainList)
            {
                for (int attempt = 0; attempt < attemptsPerDomain; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    total++;

                    var (ok, ms) = await ProbeAsync(domain, timeoutSeconds, ct);
                    if (ok)
                    {
                        successes++;
                        latencies.Add(ms);
                    }

                    progress?.Report(new StrategyTestProgress(total, totalAttempts, domain, ok));
                }
            }
        }
        catch (OperationCanceledException)
        {
            error = "Отменено";
        }
        finally
        {
            _runner.Stop();
        }

        return new StrategyTestOutcome
        {
            Strategy = strategy,
            Successes = successes,
            Total = total,
            AverageLatency = latencies.Count > 0 ? TimeSpan.FromMilliseconds(latencies.Average()) : TimeSpan.Zero,
            Error = error,
        };
    }

    private static async Task<(bool ok, double ms)> ProbeAsync(string domain, int timeoutSeconds, CancellationToken ct)
    {
        // Баг: раньше здесь было $"{{https://{domain}}}/", что давало буквально строку
        // "{https://youtube.com}/" и падало с UriFormatException на каждом запросе.
        var url = domain.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? domain
            : $"https://{domain}/";

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            AllowAutoRedirect = true,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            var ok = (int)response.StatusCode < 500;
            return (ok, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            return (false, sw.Elapsed.TotalMilliseconds);
        }
    }
}
