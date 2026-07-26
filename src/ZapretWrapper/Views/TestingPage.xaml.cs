using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Models;
using ZapretWrapper.Services;
using ZapretWrapper.ViewModels;

namespace ZapretWrapper.Views;

/// <summary>
/// Вся механика тестирования живёт здесь, а не на главной: главная отвечает только
/// за быстрый запуск обхода.
/// </summary>
public partial class TestingPage : UserControl
{
    private readonly ObservableCollection<StrategyTestRow> _rows = new();
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

        var probeCount = GetTargets().Sum(t => t.Probes.Count);
        SourceText.Text =
            $"Стратегий к проверке: {StrategyCatalog.All.Count} — {StrategyCatalog.SourceDescription}. "
            + $"Проверок на каждую стратегию: {probeCount}.";

        BuildRows();
    }

    private void BuildRows()
    {
        _rows.Clear();
        _rows.Add(new StrategyTestRow { Name = "Без обхода (база)" });
        foreach (var s in StrategyCatalog.All)
            _rows.Add(new StrategyTestRow { Name = s.Name });
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

        var strategies = StrategyCatalog.All.ToList();
        if (strategies.Count == 0)
        {
            MessageBox.Show("Стратегий не найдено: проверьте папку с zapret в Настройках.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BuildRows();
        BestStrategyText.Text = "";
        ResetRings();
        SetRunningState(true);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var targets = GetTargets();
        var outcomes = new List<StrategyTester.StrategyTestOutcome>();

        LogService.Info($"Тест: {strategies.Count} стратегий + база.");

        try
        {
            // Строго последовательно: winws2 один на всё приложение.
            for (int idx = 0; idx < _rows.Count; idx++)
            {
                if (ct.IsCancellationRequested) break;

                var row = _rows[idx];
                row.Status = TestStatus.Running;

                var progress = new Progress<StrategyTester.StrategyTestProgress>(p =>
                {
                    row.Details = $"{p.Completed}/{p.Total} — {p.ProbeTitle}";
                    ProgressText.Text = $"{row.Name}: {p.Completed}/{p.Total} — {p.ProbeTitle}";
                });

                StrategyTester.StrategyTestOutcome outcome;
                try
                {
                    outcome = idx == 0
                        ? await tester.RunBaselineAsync(targets, 6, progress, ct)
                        : await tester.RunAsync(strategies[idx - 1], targets, 6, progress, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Сбой одной стратегии не должен обрывать весь прогон.
                    LogService.Error($"«{row.Name}»: {ex.Message}");
                    row.Status = TestStatus.Failure;
                    row.Details = "ошибка: " + ex.Message;
                    continue;
                }

                ApplyOutcome(row, outcome);
                if (idx > 0) outcomes.Add(outcome);

                if (ct.IsCancellationRequested) break;
            }

            if (ct.IsCancellationRequested)
            {
                ProgressText.Text = "Тестирование остановлено. Уже проверенные стратегии остались в таблице.";
                LogService.Info("Тестирование прервано пользователем.");
                ShowBest(outcomes);
            }
            else
            {
                ProgressText.Text = "Тестирование завершено.";
                ShowBest(outcomes);
            }
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

    private void ShowBest(List<StrategyTester.StrategyTestOutcome> outcomes)
    {
        StrategyTester.StrategyTestOutcome? best = null;
        foreach (var o in outcomes)
        {
            if (o.Error is not null || o.Strategy is null) continue;
            if (best is null
                || o.Successes > best.Successes
                || (o.Successes == best.Successes && o.AverageLatency < best.AverageLatency))
                best = o;
        }

        if (best?.Strategy is null)
        {
            BestStrategyText.Text = "Не удалось подобрать стратегию";
            LogService.Warn("Ни одна стратегия не прошла тест.");
            return;
        }

        LastBestStrategyName = best.Strategy.Name;
        BestStrategyText.Text = "Лучшая: " + best.Strategy.Name;

        // Лучшая стратегия сразу становится выбранной — на главной осталось нажать «Запустить».
        SettingsService.Current.SelectedStrategyId = best.Strategy.Id;
        SettingsService.Save();

        LogService.Success(
            $"Лучшая стратегия: {best.Strategy.Name} ({best.Successes}/{best.Total} проверок). Выбрана автоматически.");

        UpdateRings(best);
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
