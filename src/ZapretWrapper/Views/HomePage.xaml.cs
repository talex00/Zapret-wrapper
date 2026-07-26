using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ZapretWrapper.Models;
using ZapretWrapper.Services;
using ZapretWrapper.ViewModels;

namespace ZapretWrapper.Views;

public partial class HomePage : UserControl
{
    private readonly ObservableCollection<StrategyTestRow> _rows = new();
    private CancellationTokenSource? _testCts;

    public HomePage()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _rows;

        // Первая строка — замер без обхода. Без неё нельзя понять, что именно чинит стратегия.
        _rows.Add(new StrategyTestRow { Name = "Без обхода (база)" });
        foreach (var s in StrategyCatalog.All)
            _rows.Add(new StrategyTestRow { Name = s.Name });

        RefreshFromSettings();

        Loaded += (_, _) => AppendLog("INFO", "Главная загружена. Выберите стратегию или нажмите «Тест всех».");
    }

    public void HandleResize(double windowWidth) { }

    /// <summary>Перечитывает цели и выбранную стратегию из настроек (страницы переиспользуются).</summary>
    public void RefreshFromSettings()
    {
        TargetResourcesPanel.Children.Clear();
        foreach (var target in GetTargets())
        {
            var border = new Border { Style = (Style)FindResource("PillBorderStyle") };
            border.Child = new TextBlock { Text = target.Name };
            TargetResourcesPanel.Children.Add(border);
        }

        var saved = SettingsService.Current.SelectedStrategyId;
        if (!string.IsNullOrEmpty(saved)) SelectStrategyInCombo(saved);
    }

    private void SelectStrategyInCombo(string id)
    {
        foreach (var item in StrategyCombo.Items)
        {
            if (item is ComboBoxItem cbi && (cbi.Tag as string) == id)
            {
                StrategyCombo.SelectedItem = cbi;
                return;
            }
        }
    }

    private void RefreshHeader()
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.RefreshStatus();
    }

    private static IReadOnlyList<CheckTarget> GetTargets() =>
        TargetCatalog.Resolve(SettingsService.Current.TestDomains);

    private void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StrategyCombo.SelectedItem is ComboBoxItem cbi)
        {
            SettingsService.Current.SelectedStrategyId = cbi.Tag as string;
            SettingsService.Save();
        }
    }

    private Strategy? GetSelectedStrategy()
    {
        if (StrategyCombo.SelectedItem is not ComboBoxItem cbi) return null;
        return StrategyCatalog.FindById(cbi.Tag as string);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var runner = App.Runner;
        if (runner is null) return;

        if (!runner.IsValid)
        {
            MessageBox.Show("Сначала укажите путь к zapret2 в Настройках.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var strategy = GetSelectedStrategy();
        if (strategy is null)
        {
            MessageBox.Show("Выберите стратегию.", "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!runner.Start(strategy))
        {
            MessageBox.Show($"Не удалось запустить: {runner.LastError}",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog("ERROR", $"Запуск не удался: {runner.LastError}");
            return;
        }

        AppendLog("SUCCESS", $"Запущена стратегия «{strategy.Name}».");
        RefreshHeader();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        var runner = App.Runner;
        if (runner is null) return;

        if (!runner.Stop())
        {
            MessageBox.Show($"Не удалось остановить: {runner.LastError}",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        AppendLog("INFO", "winws2 остановлен.");
        RefreshHeader();
    }

    private async void TestAll_Click(object sender, RoutedEventArgs e)
    {
        var runner = App.Runner;
        var tester = App.Tester;
        if (runner is null || tester is null) return;

        if (!runner.IsValid)
        {
            MessageBox.Show("Сначала укажите путь к zapret2 в Настройках.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (runner.IsRunning)
        {
            MessageBox.Show("Сначала остановите текущий запуск: тест сам управляет winws2.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        BestStrategyText.Text = "";
        ResetRings();

        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        var targets = GetTargets();
        var strategies = StrategyCatalog.All.ToList();
        foreach (var row in _rows) row.Reset();

        var probeCount = targets.Sum(t => t.Probes.Count);
        AppendLog("INFO",
            $"Тест: {strategies.Count} стратегий + база, по {probeCount} проверок на каждую.");

        var outcomes = new List<StrategyTester.StrategyTestOutcome>();

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
                    throw;
                }
                catch (Exception ex)
                {
                    // Сбой одной стратегии не должен обрывать весь прогон.
                    AppendLog("ERROR", $"«{row.Name}»: {ex.Message}");
                    row.Status = TestStatus.Failure;
                    row.Details = "ошибка: " + ex.Message;
                    continue;
                }

                if (ct.IsCancellationRequested) break;

                ApplyOutcome(row, outcome);
                if (idx > 0) outcomes.Add(outcome);
            }

            ShowBest(outcomes);
        }
        catch (OperationCanceledException)
        {
            AppendLog("INFO", "Тестирование прервано.");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Ошибка тестирования: {ex.Message}");
        }
        finally
        {
            // На всякий случай: тест сам управляет процессом и не должен оставлять его висеть.
            try { runner.Stop(); } catch { /* ignore */ }

            TestButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            _testCts?.Dispose();
            _testCts = null;
            RefreshHeader();
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
            AppendLog("WARN", "Ни одна стратегия не прошла тест.");
            BestStrategyText.Text = "Не удалось подобрать стратегию";
            return;
        }

        AppendLog("SUCCESS",
            $"Лучшая стратегия: {best.Strategy.Name} ({best.Successes}/{best.Total} проверок).");
        BestStrategyText.Text = $"Лучшая: {best.Strategy.Name}";
        SelectStrategyInCombo(best.Strategy.Id);
        UpdateRings(best);
    }

    private void ResetRings()
    {
        PingRing.Value = "—"; PingRing.Progress = 0;
        SuccessRing.Value = "—"; SuccessRing.Progress = 0;
        ChecksRing.Value = "—"; ChecksRing.Progress = 0;
    }

    /// <summary>Кольца показывают реальные цифры лучшей стратегии, а не заглушки «—».</summary>
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

    /// <summary>
    /// Без этого DataGrid «съедал» колёсико мыши и страница переставала скроллиться,
    /// когда курсор оказывался над таблицей.
    /// </summary>
    private void ResultsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;

        var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        };

        if (sender is FrameworkElement { Parent: UIElement parent })
            parent.RaiseEvent(forwarded);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogPanel.Children.Clear();

    private void AppendLog(string level, string msg)
    {
        switch (level)
        {
            case "WARN": LogService.Warn(msg); break;
            case "ERROR": LogService.Error(msg); break;
            case "SUCCESS": LogService.Success(msg); break;
            default: LogService.Info(msg); break;
        }

        var color = level switch
        {
            "WARN" => (Brush)FindResource("WarnBrush"),
            "ERROR" => (Brush)FindResource("DangerBrush"),
            "SUCCESS" => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("AccentBrush"),
        };
        var label = level switch
        {
            "WARN" => "[WARN]   ",
            "ERROR" => "[ERROR]  ",
            "SUCCESS" => "[SUCCESS]",
            _ => "[INFO]   ",
        };

        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        tb.Inlines.Add(new Run(label) { Foreground = color });
        tb.Inlines.Add(new Run(" " + msg) { Foreground = (Brush)FindResource("TextBrush") });
        LogPanel.Children.Add(tb);

        LogScroll?.Dispatcher.InvokeAsync(() => LogScroll.ScrollToEnd());
    }
}
