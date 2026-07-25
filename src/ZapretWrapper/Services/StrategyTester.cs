using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

public class StrategyTestOutcome
{
    public string Domain { get; init; } = "";
    public bool IsSuccess { get; init; }
    public long LatencyMs { get; init; }
    public string? Error { get; init; }
}

public class TestProgress
{
    public Strategy Strategy { get; init; } = null!;
    public int Completed { get; init; }
    public int Total { get; init; }
    public StrategyTestOutcome? Last { get; init; }
}

/// <summary>
/// Прогоняет стратегию через единственный ZapretRunner и замеряет, открываются ли
сайты из списка тестовых доменов.
/// </summary>
public class StrategyTester
{
    private readonly ZapretRunner _runner;

    public StrategyTester(ZapretRunner runner)
    {
        _runner = runner;
    }

    public async Task<List<StrategyTestOutcome>> RunAsync(
        Strategy strategy,
        IEnumerable<string> domains,
        IProgress<TestProgress>? progress = null,
        CancellationToken ct = default)
    {
        var domainList = new List<string>(domains);
        var results = new List<StrategyTestOutcome>();

        var started = _runner.Start(strategy);
        if (!started)
        {
            var error = _runner.LastError ?? "Не удалось запустить стратегию";
            foreach (var domain in domainList)
                results.Add(new StrategyTestOutcome { Domain = domain, IsSuccess = false, Error = error });
            return results;
        }

        try
        {
            // Даём WinDivert время поставить фильтр перед первым запросом.
            await _runner.WaitUntilReadyAsync(SettingsService.Current.StartupDelayMs, ct);

            for (int i = 0; i < domainList.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var domain = domainList[i];
                var outcome = await ProbeAsync(domain, ct);
                results.Add(outcome);

                progress?.Report(new TestProgress
                {
                    Strategy = strategy,
                    Completed = i + 1,
                    Total = domainList.Count,
                    Last = outcome,
                });
            }
        }
        finally
        {
            _runner.Stop();
        }

        return results;
    }

    private static async Task<StrategyTestOutcome> ProbeAsync(string domain, CancellationToken ct)
    {
        var url = domain.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? domain
            : $"https://{domain}/";

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AllowAutoRedirect = true,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            return new StrategyTestOutcome
            {
                Domain = domain,
                IsSuccess = response.IsSuccessStatusCode || (int)response.StatusCode < 500,
                LatencyMs = sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new StrategyTestOutcome
            {
                Domain = domain,
                IsSuccess = false,
                LatencyMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }
}
