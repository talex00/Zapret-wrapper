using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Прогоняет стратегии по набору целей. winws2 — один процесс на всё приложение, поэтому
/// стратегии тестируются строго по очереди: запуск → пробы → остановка.
/// </summary>
public class StrategyTester
{
    public class StrategyTestOutcome
    {
        public Strategy? Strategy { get; init; }
        public string Name { get; init; } = "";
        public int Successes { get; init; }
        public int Total { get; init; }
        public TimeSpan AverageLatency { get; init; }
        public string? Error { get; init; }
        public IReadOnlyList<ProbeResult> Results { get; init; } = Array.Empty<ProbeResult>();

        public double SuccessRate => Total > 0 ? 100.0 * Successes / Total : 0;

        /// <summary>Короткий список того, что не прошло — это и показывается в таблице.</summary>
        public string FailedSummary
        {
            get
            {
                if (Error is not null) return Error;
                var failed = Results.Where(r => !r.Ok).Select(r => r.Spec.Title).ToList();
                return failed.Count == 0 ? "Все проверки пройдены" : string.Join("; ", failed);
            }
        }
    }

    public readonly record struct StrategyTestProgress(
        string StrategyName, string ProbeTitle, int Completed, int Total, bool Ok);

    private readonly ZapretRunner _runner;

    public StrategyTester(ZapretRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Замер без обхода. Нужен как база сравнения: если ресурс недоступен и без zapret, и с ним —
    /// проблема не в стратегии, а в сети или в самом ресурсе.
    /// </summary>
    public Task<StrategyTestOutcome> RunBaselineAsync(
        IReadOnlyList<CheckTarget> targets,
        int timeoutSeconds = 6,
        IProgress<StrategyTestProgress>? progress = null,
        CancellationToken ct = default)
        => ProbeAllAsync(null, "Без обхода", targets, timeoutSeconds, progress, ct);

    public async Task<StrategyTestOutcome> RunAsync(
        Strategy strategy,
        IReadOnlyList<CheckTarget> targets,
        int timeoutSeconds = 6,
        IProgress<StrategyTestProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!_runner.Start(strategy))
        {
            return new StrategyTestOutcome
            {
                Strategy = strategy,
                Name = strategy.Name,
                Successes = 0,
                Total = CountCritical(targets),
                Error = _runner.LastError ?? "не удалось запустить winws2",
            };
        }

        try
        {
            await _runner.WaitUntilReadyAsync(SettingsService.Current.StartupDelayMs, ct);

            if (!_runner.IsRunning)
            {
                return new StrategyTestOutcome
                {
                    Strategy = strategy,
                    Name = strategy.Name,
                    Successes = 0,
                    Total = CountCritical(targets),
                    Error = "winws2 завершился сразу после запуска (см. журнал)",
                };
            }

            return await ProbeAllAsync(strategy, strategy.Name, targets, timeoutSeconds, progress, ct);
        }
        finally
        {
            _runner.Stop();
        }
    }

    private static int CountCritical(IReadOnlyList<CheckTarget> targets) =>
        targets.SelectMany(t => t.Probes).Count(p => p.Critical);

    private static async Task<StrategyTestOutcome> ProbeAllAsync(
        Strategy? strategy,
        string name,
        IReadOnlyList<CheckTarget> targets,
        int timeoutSeconds,
        IProgress<StrategyTestProgress>? progress,
        CancellationToken ct)
    {
        var specs = targets.SelectMany(t => t.Probes).ToList();
        var results = new List<ProbeResult>();
        string? error = null;

        try
        {
            foreach (var spec in specs)
            {
                ct.ThrowIfCancellationRequested();

                var result = await NetProbe.RunAsync(spec, timeoutSeconds, ct);
                results.Add(result);

                if (result.Ok)
                    LogService.Debug($"[{name}] {spec.Title}: OK, {result.Ms:0} мс");
                else
                    LogService.Warn($"[{name}] {spec.Title}: {result.Error}");

                progress?.Report(new StrategyTestProgress(name, spec.Title, results.Count, specs.Count, result.Ok));
            }
        }
        catch (OperationCanceledException)
        {
            error = "отменено";
        }

        var critical = results.Where(r => r.Spec.Critical).ToList();
        var latencies = results.Where(r => r.Ok).Select(r => r.Ms).ToList();

        return new StrategyTestOutcome
        {
            Strategy = strategy,
            Name = name,
            Successes = critical.Count(r => r.Ok),
            Total = critical.Count,
            AverageLatency = latencies.Count > 0
                ? TimeSpan.FromMilliseconds(latencies.Average())
                : TimeSpan.Zero,
            Error = error,
            Results = results,
        };
    }
}
