using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

        // Заполняем список стратегий дефолтным набором (UI для тестов).
        foreach (var s in StrategyCatalog.All)
        {
            _rows.Add(new StrategyTestRow
            {
                Name = s.Name,
                Status = TestStatus.Pending,
            });
        }

        // Целевые ресурсы — из настроек или дефолт.
        var domains = GetTargetDomains();
        TargetResourcesPanel.Children.Clear();
        foreach (var d in domains)
        {
            var border = new Border { Style = (Style)FindResource("PillBorderStyle") };
            border.Child = new TextBlock { Text = d };
            TargetResourcesPanel.Children.Add(border);
        }

        // Восстанавливаем выбор стратегии из настроек.
        var saved = SettingsService.Current.SelectedStrategyId;
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (var item in StrategyCombo.Items)
            {
                if (item is ComboBoxItem cbi && (cbi.Tag as string) == saved)
                {
                    StrategyCombo.SelectedItem = cbi;
                    break;
                }
            }
        }

        Loaded += (_, _) => AppendLog("INFO", "Главная загружена. Выберите стратегию и нажмите «Запустить обход».");
    }

    public void HandleResize(double windowWidth) { }

    private void RefreshHeader()
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.RefreshStatus();
    }

    private static List<string> GetTargetDomains()
    {
        var s = SettingsService.Current.TestDomains;
        if (!string.IsNullOrWhiteSpace(s))
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
        return new() { "youtube.com", "discord.com", "instagram.com", "twitch.tv" };
    }

    // =================== Стратегия ===================

    private void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StrategyCombo.SelectedItem is ComboBoxItem cbi)
        {
            var id = cbi.Tag as string;
            SettingsService.Current.SelectedStrategyId = id;
            SettingsService.Save();
        }
    }

    private Strategy? GetSelectedStrategy()
    {
        if (StrategyCombo.SelectedItem is not ComboBoxItem cbi) return null;
        var id = cbi.Tag as string;
        return StrategyCatalog.FindById(id);
    }

    // =================== Запуск/остановка ===================

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

        var ok = runner.Start(strategy);
        if (!ok)
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

    // =================== Тест ===================

    private async void TestAll_Click(object sender, RoutedEventArgs e)
    {
        var runner = App.Runner;
        if (runner is null) return;

        if (!runner.IsValid)
        {
            MessageBox.Show("Сначала укажите путь к zapret2 в Настройках.",
                "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TestButton.IsEnabled = false;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        BestStrategyText.Text = "";

        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;
        var domains = GetTargetDomains();
        var strategies = StrategyCatalog.All.ToList();

        // Сбрасываем строки.
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Status = i < strategies.Count ? TestStatus.Pending : TestStatus.Pending;
        }

        var progress = new Progress<(int idx, TestStatus s, int? ping, double? loss, double? speed)>(p =>
        {
            _rows[p.idx].Status = p.s;
            _rows[p.idx].Ping = p.ping;
            _rows[p.idx].Loss = p.loss;
            _rows[p.idx].Speed = p.speed;
        });

        AppendLog("INFO", $"Запуск тестирования {strategies.Count} стратегий по {domains.Count} доменам...");

        try
        {
            var tasks = strategies.Select(async (s, idx) =>
            {
                if (ct.IsCancellationRequested) return null;
                _ = Dispatcher.Invoke(() => _rows[idx].Status = TestStatus.Running);
                AppendLog("INFO", $"→ {s.Name}");

                var outcome = await App.Tester!.RunAsync(s, domains,
                    attemptsPerDomain: 2,
                    timeoutSeconds: 5,
                    progress: null,
                    ct: ct);

                if (ct.IsCancellationRequested) return null;

                Dispatcher.Invoke(() =>
                {
                    var row = _rows[idx];
                    row.Ping = outcome.AverageLatency == TimeSpan.Zero ? null : (int?)outcome.AverageLatency.TotalMilliseconds;
                    row.Loss = outcome.Total > 0 ? 100.0 * outcome.Successes / outcome.Total : 100;
                    row.Speed = outcome.AverageLatency == TimeSpan.Zero ? 0 : outcome.AverageLatency.TotalMilliseconds;
                    row.Status = outcome.Error is null
                        ? (outcome.Successes == outcome.Total ? TestStatus.Success : TestStatus.Failure)
                        : TestStatus.Failure;
                });
                return outcome;
            }).ToList();

            var outcomes = await Task.WhenAll(tasks);

            // Выбираем лучшую: максимум Successes, потом минимум latency.
            StrategyTester.StrategyTestOutcome? best = null;
            foreach (var o in outcomes)
            {
                if (o is null) continue;
                if (o.Error is not null) continue;
                if (best is null
                    || o.Successes > best.Successes
                    || (o.Successes == best.Successes && o.AverageLatency < best.AverageLatency))
                    best = o;
            }

            if (best is not null)
            {
                AppendLog("SUCCESS", $"Лучшая стратегия: {best.Strategy.Name} ({best.Successes}/{best.Total}).");
                BestStrategyText.Text = $"Лучшая: {best.Strategy.Name}";

                // Подсветим её в ComboBox.
                foreach (var item in StrategyCombo.Items)
                {
                    if (item is ComboBoxItem cbi && (cbi.Tag as string) == best.Strategy.Id)
                    {
                        StrategyCombo.SelectedItem = cbi;
                        break;
                    }
                }
            }
            else
            {
                AppendLog("WARN", "Ни одна стратегия не прошла тест.");
                BestStrategyText.Text = "Не удалось подобрать стратегию";
            }
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
            TestButton.IsEnabled = true;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    // =================== Лог ===================

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogPanel.Children.Clear();
    }

    private enum LogLevel { Info, Warn, Error, Success }
    private void Log(LogLevel level, string msg)
    {
        var color = level switch
        {
            LogLevel.Info => (Brush)FindResource("AccentBrush"),
            LogLevel.Warn => (Brush)FindResource("WarnBrush"),
            LogLevel.Error => (Brush)FindResource("DangerBrush"),
            LogLevel.Success => (Brush)FindResource("SuccessBrush"),
            _ => (Brush)FindResource("TextBrush"),
        };
        var label = level switch
        {
            LogLevel.Info => "[INFO]   ",
            LogLevel.Warn => "[WARN]   ",
            LogLevel.Error => "[ERROR]  ",
            LogLevel.Success => "[SUCCESS]",
            _ => "         ",
        };
        AppendLogRaw(color, label, msg);
    }

    private void AppendLog(string level, string msg)
    {
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
        AppendLogRaw(color, label, msg);
    }

    private void AppendLogRaw(Brush color, string label, string msg)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
        };
        tb.Inlines.Add(new Run(label) { Foreground = color });
        tb.Inlines.Add(new Run(" " + msg) { Foreground = (Brush)FindResource("TextBrush") });
        LogPanel.Children.Add(tb);
        // Прокрутить вниз
        if (LogScroll is not null)
        {
            LogScroll.Dispatcher.InvokeAsync(() =>
            {
                LogScroll.ScrollToEnd();
            });
        }
    }
}
