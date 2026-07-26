using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Models;
using ZapretWrapper.Services;
using ZapretWrapper.ViewModels;

namespace ZapretWrapper.Views;

/// <summary>
/// Подбор стратегии в три шага — так же, как это делает blockcheck2:
///
/// 0. База без обхода. По её результатам видно, что именно заблокировано: TCP/TLS, HTTP или QUIC.
///    Методы для протоколов, которые и так работают, не перебираются вообще.
/// 1. Быстрый прогон: каждый кандидат проверяется двумя пробами, которые упали на базе.
/// 2. Полная проверка выживших — только по своему протоколу.
/// 3. Победители каждого протокола склеиваются в один профиль через --new и проверяются целиком.
/// </summary>
public partial class TestingPage : UserControl
{
    private const int ProbeTimeoutSeconds = 6;

    private readonly ObservableCollection<StrategyTestRow> _rows = new();
    private readonly List<Strategy> _strategies = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>Итог последнего теста — главная страница показывает его как подсказку.</summary>
    public static string? LastBestStrategyName { get; private set; }

    public TestingPage()
    {
        InitializeComponent();

        ResultsList.ItemsSource = _rows;
        LogList.ItemsSource = LogService.Entries;

        // Журнал должен сам ездить вниз: иначе прогресс теста не виден.
        ((INotifyCollectionChanged)LogService.Entries).CollectionChanged += (_, _) =>
            LogScroll.Dispatcher.InvokeAsync(() => LogScroll.ScrollToEnd());

        RefreshFromSettings();
    }

    /// <summary>Вызывается при каждом открытии вкладки: путь и цели могли поменяться.</summary>
    public void RefreshFromSettings()
    {
        if (_isRunning) return;

        StrategyCatalog.Reload();

        TargetsPanel.Children.Clear();
        foreach (var target in GetTargets())
        {
            var pill = new Border { Style = (Style)FindResource("PillBorderStyle") };
            pill.Child = new TextBlock { Text = target.Name };
            TargetsPanel.Children.Add(pill);
        }

        BuildRows();

        var probeCount = GetTargets().Sum(t => t.Probes.Count);
        SourceText.Text =
            $"Кандидатов: {_strategies.Count} — {StrategyCatalog.SourceDescription}. "
            + $"Проверок в полном прогоне: {probeCount}. "
            + "Сначала идёт база без обхода, затем быстрый отбор, затем полная проверка выживших.";
    }

    private void BuildRows()
    {
        _rows.Clear();
        _strategies.Clear();

        _rows.Add(new StrategyTestRow { Name = "Без обхода (база)" });

        foreach (var strategy in StrategyCatalog.TestCandidates)
        {
            _strategies.Add(strategy);
            _rows.Add(new StrategyTestRow { Name = strategy.Name });
        }
    }

    private static IReadOnlyList<CheckTarget> GetTargets() =>
        TargetCatalog.Resolve(SettingsService.Current.TestDomains);

    private void SetRunningState(bool running)
    {
        _isRunning = running;
        TestToggleButton.Content = running
            ? "■  Остановить тестирование"
            : "🔍  Запустить тестирование";
        TestToggleButton.IsEnabled = true;
    }

    private async void TestToggle_Click(object sender, RoutedEventArgs e)
    {
        // Кнопка двухрежимная: второе нажатие отменяет прогон.
        if (_isRunning)
        {
            ProgressText.Text = "Останавливаю — ждём завершения текущей проверки…";
            TestToggleButton.IsEnabled = false;
            _cts?.Cancel();
            return;
        }

        var runner = App.Runner;
        var tester = App.Tester;
        if (runner is null || tester is null) return;

        if (!runner.IsValid)
        {
            MessageBox.Show("Сначала укажите путь к zapret в Настройках.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (runner.IsRunning)
        {
            var answer = MessageBox.Show(
                "Сейчас запущен обход. Тест сам управляет winws2, поэтому обход будет остановлен. Продолжить?",
                "ZapretWrapper", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
            runner.Stop();
        }

        BuildRows();

        if (_strategies.Count == 0)
        {
            MessageBox.Show("Стратегий не найдено: проверьте папку с zapret в Настройках.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BestStrategyText.Text = "";
        ResetRings();
        SetRunningState(true);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var targets = GetTargets();

        try
        {
            // ---- Фаза 0: база без обхода ----
            var baseRow = _rows[0];
            baseRow.Status = TestStatus.Running;
            ProgressText.Text = "Проверяю, что не работает без обхода…";

            var baseline = await tester.RunBaselineAsync(targets, ProbeTimeoutSeconds, MakeProgress(baseRow, "база"), ct);
            ApplyOutcome(baseRow, baseline);

            var blocked = StrategyPlan.BlockedProtocols(baseline.Results);

            if (ct.IsCancellationRequested) return;

            if (blocked.Count == 0)
            {
                ProgressText.Text = "Всё работает и без обхода — подбирать нечего.";
                BestStrategyText.Text = "Обход не требуется";
                LogService.Success("База прошла полностью: блокировки не обнаружено.");
                MarkRest("пропущено: блокировки не обнаружено");
                UpdateRings(baseline);
                return;
            }

            LogService.Info("Заблокировано: " +
                string.Join(", ", blocked.Select(StrategyPlan.Label)));

            // ---- Фаза 1: быстрый отбор ----
            var survivors = new List<(Strategy Strategy, StrategyProtocol Protocol, int Index)>();

            for (int i = 0; i < _strategies.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var strategy = _strategies[i];
                var row = _rows[i + 1];
                var protocol = StrategyPlan.Detect(strategy);

                if (protocol != StrategyProtocol.Mixed && !blocked.Contains(protocol))
                {
                    row.Details = $"пропущено: {StrategyPlan.Label(protocol)} не заблокирован";
                    continue;
                }

                var quick = StrategyPlan.QuickTargets(targets, baseline.Results, protocol);
                if (quick.Count == 0)
                {
                    row.Details = "пропущено: нет подходящих проверок";
                    continue;
                }

                row.Status = TestStatus.Running;
                var outcome = await RunSafeAsync(tester, strategy, quick, row, "быстрый прогон", ct);
                if (outcome is null) continue;

                ApplyOutcome(row, outcome);
                row.Details = "быстрый прогон: " + outcome.FailedSummary;

                if (outcome.Error is null && outcome.Total > 0 && outcome.Successes == outcome.Total)
                    survivors.Add((strategy, protocol, i));
            }

            LogService.Info($"Быстрый отбор прошли: {survivors.Count}.");

            // ---- Фаза 2: полная проверка выживших ----
            var best = new Dictionary<StrategyProtocol, StrategyTester.StrategyTestOutcome>();

            foreach (var (strategy, protocol, index) in survivors)
            {
                if (ct.IsCancellationRequested) break;

                var row = _rows[index + 1];
                var full = StrategyPlan.ProtocolTargets(targets, protocol);
                if (full.Count == 0) continue;

                row.Status = TestStatus.Running;
                var outcome = await RunSafeAsync(tester, strategy, full, row, "полная проверка", ct);
                if (outcome is null) continue;

                ApplyOutcome(row, outcome);

                if (outcome.Error is not null || outcome.Total == 0 || outcome.Successes != outcome.Total)
                    continue;

                if (!best.TryGetValue(protocol, out var current)
                    || outcome.AverageLatency < current.AverageLatency)
                    best[protocol] = outcome;
            }

            if (ct.IsCancellationRequested)
            {
                ProgressText.Text = "Тестирование остановлено. Уже проверенные стратегии остались в таблице.";
                LogService.Info("Тестирование прервано пользователем.";
            }

            // ---- Фаза 3: итоговый профиль ----
            await FinishAsync(tester, best, blocked, targets, ct);
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Тестирование остановлено.";
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка тестирования: " + ex.Message);
            ProgressText.Text = "Ошибка тестирования: " + ex.Message;
        }
        finally
        {
            try { runner.Stop(); } catch { /* ignore */ }

            foreach (var row in _rows)
            {
                if (row.Status == TestStatus.Running)
                {
                    row.Status = TestStatus.Pending;
                    row.Details = "не проверено";
                }
            }

            _cts?.Dispose();
            _cts = null;
            SetRunningState(false);

            if (Window.GetWindow(this) is MainWindow mw) mw.RefreshStatus();
        }
    }

    /// <summary>Склеивает победителей в один профиль и проверяет его на всех целях.</summary>
    private async Task FinishAsync(
        StrategyTester tester,
        Dictionary<StrategyProtocol, StrategyTester.StrategyTestOutcome> best,
        HashSet<StrategyProtocol> blocked,
        IReadOnlyList<CheckTarget> targets,
        CancellationToken ct)
    {
        Strategy? final = null;

        // Стратегия из config проверялась сразу на всё: если она вытянула — склеивать нечего.
        if (best.TryGetValue(StrategyProtocol.Mixed, out var mixed) && mixed.Strategy is not null)
        {
            final = mixed.Strategy;
        }
        else
        {
            var parts = new List<Strategy>();
            foreach (var protocol in new[] { StrategyProtocol.Tls, StrategyProtocol.Http, StrategyProtocol.Quic })
            {
                if (!blocked.Contains(protocol)) continue;
                if (best.TryGetValue(protocol, out var outcome) && outcome.Strategy is not null)
                    parts.Add(outcome.Strategy);
                else
                    LogService.Warn($"Для {StrategyPlan.Label(protocol)} рабочего метода не нашлось.");
            }

            if (parts.Count > 0) final = StrategyProfileBuilder.Build(parts);
        }

        if (final is null)
        {
            BestStrategyText.Text = "Не удалось подобрать стратегию";
            ProgressText.Text = "Ни один метод не закрыл проверки целиком.";
            LogService.Warn("Ни одна стратегия не прошла тест.");
            return;
        }

        var isCombined = StrategyProfileBuilder.IsCombined(final.Id);
        if (isCombined) StrategyCatalog.SetCombined(final);

        // Итоговая проверка целиком: склейка могла повести себя иначе, чем каждая часть отдельно.
        StrategyTestRow row;
        var existing = _strategies.FindIndex(s => s.Id == final.Id);
        if (existing >= 0)
        {
            row = _rows[existing + 1];
        }
        else
        {
            row = new StrategyTestRow { Name = final.Name };
            _rows.Add(row);
        }

        row.Reset();
        row.Status = TestStatus.Running;

        var verify = await RunSafeAsync(tester, final, targets, row, "итоговая проверка", ct);
        if (verify is not null)
        {
            ApplyOutcome(row, verify);
            UpdateRings(verify);
        }

        LastBestStrategyName = final.Name;
        BestStrategyText.Text = "Итог: " + final.Name;

        // Итоговый профиль сразу становится выбранным — на главной осталось нажать «Запустить».
        SettingsService.Current.SelectedStrategyId = final.Id;
        SettingsService.Save();

        ProgressText.Text = "Тестирование завершено.";
        LogService.Success("Итоговая стратегия: " + final.Name + ". Аргументы: " + string.Join(" ", final.Args));
    }

    private Progress<StrategyTester.StrategyTestProgress> MakeProgress(StrategyTestRow row, string phase)
    {
        return new Progress<StrategyTester.StrategyTestProgress>(p =>
        {
            row.Details = $"{phase}: {p.Completed}/{p.Total} — {p.ProbeTitle}";
            ProgressText.Text = $"{row.Name} — {phase}: {p.Completed}/{p.Total} — {p.ProbeTitle}";
        });
    }

    /// <summary>Сбой одной стратегии не должен обрывать весь прогон.</summary>
    private async Task<StrategyTester.StrategyTestOutcome?> RunSafeAsync(
        StrategyTester tester,
        Strategy strategy,
        IReadOnlyList<CheckTarget> targets,
        StrategyTestRow row,
        string phase,
        CancellationToken ct)
    {
        try
        {
            return await tester.RunAsync(strategy, targets, ProbeTimeoutSeconds, MakeProgress(row, phase), ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error($"«{row.Name}»: {ex.Message}");
            row.Status = TestStatus.Failure;
            row.Details = "ошибка: " + ex.Message;
            return null;
        }
    }

    private void MarkRest(string details)
    {
        for (int i = 1; i < _rows.Count; i++)
        {
            _rows[i].Status = TestStatus.Pending;
            _rows[i].Details = details;
        }
    }

    private static void ApplyOutcome(StrategyTestRow row, StrategyTester.StrategyTestOutcome outcome)
    {
        row.Ping = outcome.AverageLatency == TimeSpan.Zero
            ? null
            : (int)outcome.AverageLatency.TotalMilliseconds;
        row.SuccessRate = outcome.Total > 0 ? outcome.SuccessRate : null;
        row.Details = outcome.FailedSummary;
        row.Status = outcome.Error is not null
            ? TestStatus.Failure
            : outcome.Successes == outcome.Total && outcome.Total > 0
                ? TestStatus.Success
                : outcome.Successes > 0
                    ? TestStatus.Partial
                    : TestStatus.Failure;
    }

    private void ResetRings()
    {
        PingRing.Value = "—"; PingRing.Progress = 0;
        SuccessRing.Value = "—"; SuccessRing.Progress = 0;
        ChecksRing.Value = "—"; ChecksRing.Progress = 0;
    }

    private void UpdateRings(StrategyTester.StrategyTestOutcome outcome)
    {
        var ms = outcome.AverageLatency.TotalMilliseconds;
        PingRing.Value = ms > 0 ? $"{ms:0} мс" : "—";
        PingRing.Progress = ms > 0 ? Math.Max(0, 1 - Math.Min(ms / 1000.0, 1)) : 0;

        SuccessRing.Value = $"{outcome.SuccessRate:0}%";
        SuccessRing.Progress = outcome.SuccessRate / 100.0;

        var passed = outcome.Results.Count(r => r.Ok);
        var total = outcome.Results.Count;
        ChecksRing.Value = $"{passed}/{total}";
        ChecksRing.Progress = total > 0 ? (double)passed / total : 0;
    }
}
