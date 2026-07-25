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
/// Прогоняет стратегию: запускает winws2.exe с её аргументами, делает N HEAD-запросов
/// к списку доменов, замеряет время и считает процент успеха.
/// </summary>
public class StrategyTester
{
    private readonly ZapretRunner _runner;

    public StrategyTester(ZapretRunner runner)
    {
        _runner = runner;
    }

    public class TestProgress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public string CurrentDomain { get; set; } = "";
    }

    public class StrategyTestOutcome
    {
        public Strategy Strategy { get; set; } = null!;
        public int Successes { get; set; }
        public int Total { get; set; }
        public TimeSpan AverageLatency { get; set; }
        public string? Error { get; set; }
    }

    public bool IsSuccess(StrategyTestOutcome o) =>
        o.Error is null && o.Total > 0 && o.Successes == o.Total;

    /// <summary>Полный прогон стратегии. Блокирующий — вызывать в фоне.</summary>
    public async Task<StrategyTestOutcome> RunAsync(
        Strategy strategy,
        IReadOnlyList<string> domains,
        int attemptsPerDomain = 2,
        int timeoutSeconds = 5,
        IProgress<TestProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_runner.IsValid)
            return new StrategyTestOutcome { Strategy = strategy, Error = "zapret2 не настроен" };

        // Запускаем winws2 с аргументами стратегии.
        try { _runner.Stop(); } catch { /* ignore */ }
        var started = _runner.Start(strategy);
        if (!started)
            return new StrategyTestOutcome { Strategy = strategy, Error = "Не удалось запустить winws2" };

        try
        {
            return await ProbeAsync(strategy, domains, attemptsPerDomain, timeoutSeconds, progress, ct);
        }
        finally
        {
            try { _runner.Stop(); } catch { /* ignore */ }
        }
    }

    private async Task<StrategyTestOutcome> ProbeAsync(
        Strategy strategy,
        IReadOnlyList<string> domains,
        int attempts,
        int timeout,
        IProgress<TestProgress>? progress,
        CancellationToken ct)
    {
        int total = domains.Count * attempts;
        int done = 0;
        int success = 0;
        var latencies = new List<double>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        if (!http.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0) ZapretWrapper/0.1"))
        {
            http.DefaultRequestHeaders.Add("User-Agent", "ZapretWrapper/0.1");
        }

        foreach (var domain in domains)
        {
            for (int i = 0; i < attempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                progress?.Report(new TestProgress { Done = done, Total = total, CurrentDomain = domain });

                var sw = Stopwatch.StartNew();
                try
                {
                    var url = domain.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? domain
                        : $"https://{domain}/";
                    using var resp = await http.SendAsync(
                        new HttpRequestMessage(HttpMethod.Head, url),
                        HttpCompletionOption.ResponseHeadersRead, ct);
                    sw.Stop();
                    if ((int)resp.StatusCode < 400)
                    {
                        success++;
                        latencies.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch
                {
                    sw.Stop();
                    // Провал — не считаем за успех.
                }
            }
        }

        var avg = latencies.Count > 0
            ? TimeSpan.FromMilliseconds(latencies.Average())
            : TimeSpan.Zero;
        return new StrategyTestOutcome
        {
            Strategy = strategy,
            Successes = success,
            Total = total,
            AverageLatency = avg,
            Error = null,
        };
    }
}
