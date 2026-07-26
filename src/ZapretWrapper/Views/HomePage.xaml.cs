using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Models;
using ZapretWrapper.Services;

namespace ZapretWrapper.Views;

/// <summary>
/// Главная страница отвечает только за быстрый запуск обхода: тестирование целиком
/// переехало на вкладку «Тестирование».
/// </summary>
public partial class HomePage : UserControl
{
    private bool _suppressSelection;

    public HomePage()
    {
        InitializeComponent();

        LogList.ItemsSource = LogService.Entries;
        ((INotifyCollectionChanged)LogService.Entries).CollectionChanged += (_, _) =>
            LogScroll.Dispatcher.InvokeAsync(() => LogScroll.ScrollToEnd());

        if (App.Runner is ZapretRunner runner)
            runner.StateChanged += (_, _) => UpdateActionButton();

        RefreshFromSettings();
    }

    /// <summary>Оставлено для совместимости с MainWindow: разметка теперь тянется сама.</summary>
    public void HandleResize(double windowWidth) { }

    /// <summary>Перечитывает список стратегий и настройки при каждом открытии страницы.</summary>
    public void RefreshFromSettings()
    {
        StrategyCatalog.Reload();

        _suppressSelection = true;
        StrategyCombo.ItemsSource = StrategyCatalog.All;
        var selected = StrategyCatalog.FindById(SettingsService.Current.SelectedStrategyId);
        StrategyCombo.SelectedItem = selected ?? (StrategyCatalog.All.Count > 0 ? StrategyCatalog.All[0] : null);
        _suppressSelection = false;

        SourceText.Text = "Источник стратегий: " + StrategyCatalog.SourceDescription + ".";

        UpdateDescription();
        UpdateBestHint();
        UpdateActionButton();
    }

    private void UpdateDescription()
    {
        StrategyDescriptionText.Text = StrategyCombo.SelectedItem is Strategy s
            ? s.Description
            : "Стратегия не выбрана.";
    }

    private void UpdateBestHint()
    {
        var best = TestingPage.LastBestStrategyName;
        BestHintText.Text = string.IsNullOrEmpty(best)
            ? "Автоподбор находится на вкладке «Тестирование»: каждая стратегия проверяется реальными запросами, лучшая выбирается автоматически."
            : $"По итогам последнего теста лучшей оказалась «{best}» — она уже выбрана. Повторить подбор можно на вкладке «Тестирование».";
    }

    /// <summary>
    /// Единая кнопка вместо пары «Запустить»/«Остановить». События раннера могут
    /// прийти из фонового потока, поэтому сначала гарантируем UI-поток.
    /// </summary>
    private void UpdateActionButton()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateActionButton));
            return;
        }

        var runner = App.Runner;
        var running = runner is not null && runner.IsRunning;

        ActionButton.Content = running ? "■  Остановить обход" : "▶  Запустить обход";
        StrategyCombo.IsEnabled = !running;

        if (running)
            StateText.Text = "Обход работает. Повторное нажатие остановит winws2.";
        else if (runner is null || !runner.IsValid)
            StateText.Text = "Папка zapret не указана или неполная — проверьте Настройки.";
        else if (!string.IsNullOrEmpty(runner.LastError))
            StateText.Text = "Обход остановлен. Последняя ошибка: " + runner.LastError;
        else
            StateText.Text = "Обход остановлен.";
    }

    private void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (StrategyCombo.SelectedItem is not Strategy strategy) return;

        SettingsService.Current.SelectedStrategyId = strategy.Id;
        SettingsService.Save();
        UpdateDescription();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        var runner = App.Runner;
        if (runner is null) return;

        ActionButton.IsEnabled = false;
        try
        {
            if (runner.IsRunning)
            {
                runner.Stop();
                if (runner.IsRunning)
                    MessageBox.Show("Не удалось остановить winws2: " + (runner.LastError ?? "причина неизвестна"),
                        "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!runner.IsValid)
            {
                MessageBox.Show("Сначала укажите папку с zapret в Настройках.",
                    "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (StrategyCombo.SelectedItem is not Strategy strategy)
            {
                MessageBox.Show("Стратегии не найдены. Проверьте, что в папке zapret есть .cmd-пресеты.",
                    "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            runner.Start(strategy);
            if (!runner.IsRunning)
                MessageBox.Show("Не удалось запустить winws2: " + (runner.LastError ?? "см. журнал"),
                    "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ActionButton.IsEnabled = true;
            UpdateActionButton();
            if (Window.GetWindow(this) is MainWindow mw) mw.RefreshStatus();
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogService.Clear();
}
