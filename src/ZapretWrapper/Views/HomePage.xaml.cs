using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ZapretWrapper.Models;
using ZapretWrapper.Services;

namespace ZapretWrapper.Views;

/// <summary>
/// Главный экран построен как последовательность шагов, а не как набор карточек:
/// каждый следующий блок появляется только тогда, когда для него есть данные.
///
///   нет папки        → только выбор папки;
///   папка неполная   → выбор папки + причина;
///   папка валидна    → стратегия + акцентная кнопка запуска, ниже автоподбор;
///   обход работает   → состояние и остановка, автоподбор скрыт (он всё равно убьёт winws).
///
/// Тексты держим короткими: подпись, которую пользователь читает один раз в жизни,
/// он потом ещё сто раз пролистывает глазами. Всё объясняющее живёт в «Подробностях».
/// Пустые подписи не просто очищаются, а прячутся — иначе пустая строка
/// продолжает занимать высоту и карточка выглядит дырявой.
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
            runner.StateChanged += (_, _) => RefreshFlow();

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
        RefreshFlow();
    }

    /// <summary>
    /// Единственное место, которое решает, что видно на экране. События раннера
    /// приходят из фонового потока, поэтому сначала гарантируем UI-поток.
    /// </summary>
    public void RefreshFlow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(RefreshFlow));
            return;
        }

        var runner = App.Runner;
        var running = runner is not null && runner.IsRunning;

        var path = SettingsService.Current.ZapretPath;
        var layout = ZapretBackend.Validate(path);

        PathDetailText.Text = string.IsNullOrWhiteSpace(path) ? "" : "Папка: " + path;

        // ---- Шаг 1: без рабочей папки остальное не имеет смысла. ----
        if (!layout.IsValid && !running)
        {
            PathCard.Visibility = Visibility.Visible;
            LaunchCard.Visibility = Visibility.Collapsed;
            DetailsExpander.Visibility = string.IsNullOrWhiteSpace(path)
                ? Visibility.Collapsed
                : Visibility.Visible;

            SetText(PathText, string.IsNullOrWhiteSpace(path) ? "" : path);

            var problem = layout.Error
                ?? (layout.Missing.Count > 0 ? "Не хватает файлов: " + string.Join(", ", layout.Missing) : null);

            SetText(PathProblemText, string.IsNullOrWhiteSpace(path) ? "" : problem ?? "");
            return;
        }

        // ---- Шаг 2: запуск. Главный сценарий — стратегия уже известна. ----
        PathCard.Visibility = Visibility.Collapsed;
        LaunchCard.Visibility = Visibility.Visible;
        DetailsExpander.Visibility = Visibility.Visible;

        // Во время работы обхода автоподбор скрыт: тест всё равно остановит winws.
        AutoPickCard.Visibility = running ? Visibility.Collapsed : Visibility.Visible;

        var hasStrategies = StrategyCatalog.All.Count > 0;

        DetectedText.Text = running
            ? "Обход работает"
            : hasStrategies
                ? $"{layout.Label} · стратегий: {StrategyCatalog.All.Count}"
                : $"{layout.Label} · стратегий не найдено";

        StrategyCombo.IsEnabled = !running && hasStrategies;
        ActionButton.Content = running ? "■  Остановить обход" : "▶  Запустить обход";
        ActionButton.IsEnabled = running || hasStrategies;

        if (running)
        {
            var current = runner?.CurrentStrategy;
            SetText(StateText, current is null ? "" : $"Стратегия «{current.Name}»");
        }
        else if (!hasStrategies)
        {
            SetText(StateText, "В папке нет ни пресетов, ни general*.bat.");
        }
        else if (!string.IsNullOrEmpty(runner?.LastError))
        {
            SetText(StateText, "Ошибка: " + runner!.LastError);
        }
        else
        {
            SetText(StateText, "");
        }

        UpdateArgs();
        UpdateBestHint();
    }

    /// <summary>Пустой текст прячет сам TextBlock: иначе пустая строка держит высоту.</summary>
    private static void SetText(TextBlock target, string? text)
    {
        target.Text = text ?? "";
        target.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateDescription()
    {
        StrategyDescriptionText.Text = StrategyCombo.SelectedItem is Strategy s
            ? s.Description
            : "";
    }

    private void UpdateArgs()
    {
        ArgsText.Text = StrategyCombo.SelectedItem is Strategy s
            ? string.Join(" ", s.Args)
            : "—";
    }

    private void UpdateBestHint()
    {
        var best = TestingPage.LastBestStrategyName;
        SetText(BestHintText, string.IsNullOrEmpty(best)
            ? ""
            : $"Прошлая проверка выбрала «{best}».");
    }

    /// <summary>Выбор папки прямо на главной: раньше за этим приходилось идти в Настройки.</summary>
    private void ChoosePath_Click(object sender, RoutedEventArgs e)
    {
        var current = SettingsService.Current.ZapretPath;

        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку zapret (zapret2 или zapret-discord-youtube)",
        };
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
            dlg.InitialDirectory = current;

        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        var path = dlg.FolderName;
        var layout = ZapretBackend.Validate(path);

        // Путь сохраняем даже неподходящий: иначе пользователь не увидит, что именно не так.
        SettingsService.Current.ZapretPath = path;
        SettingsService.Save();

        if (layout.IsValid)
        {
            // Сборка могла стать другой — аргументы прошлой сборки несовместимы.
            StrategyCatalog.Reload(force: true);

            var selected = SettingsService.Current.SelectedStrategyId;
            if (!string.IsNullOrEmpty(selected) && StrategyCatalog.FindById(selected) is null)
            {
                SettingsService.Current.SelectedStrategyId = null;
                SettingsService.Save();
                LogService.Info("Выбранная стратегия сброшена: в новой папке её нет.");
            }

            LogService.Success($"Папка принята: {layout.Label}. {StrategyCatalog.SourceDescription}");
        }

        RefreshFromSettings();
    }

    private void StrategyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (StrategyCombo.SelectedItem is not Strategy strategy) return;

        SettingsService.Current.SelectedStrategyId = strategy.Id;
        SettingsService.Save();
        UpdateDescription();
        UpdateArgs();
    }

    private void AutoPick_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mw) mw.GoToAutoPick();
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
                    MessageBox.Show("Не удалось остановить winws: " + (runner.LastError ?? "причина неизвестна"),
                        "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!runner.IsValid)
            {
                MessageBox.Show("Сначала укажите папку с zapret.",
                    "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (StrategyCombo.SelectedItem is not Strategy strategy)
            {
                MessageBox.Show("Стратегии не найдены. Проверьте, что в папке zapret есть пресеты или general*.bat.",
                    "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            runner.Start(strategy);
            if (!runner.IsRunning)
                MessageBox.Show("Не удалось запустить winws: " + (runner.LastError ?? "см. журнал в «Подробностях»"),
                    "ZapretWrapper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ActionButton.IsEnabled = true;
            RefreshFlow();
            if (Window.GetWindow(this) is MainWindow mw) mw.RefreshStatus();
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogService.Clear();
}
