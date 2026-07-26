using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Models;
using ZapretWrapper.Services;

namespace ZapretWrapper.Views;

/// <summary>
/// Главная страница отвечает только за быстрый запуск обхода.
/// Тестирование целиком живёт на вкладке «Тестирование».
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

    public void HandleResize(double windowWidth) { }

    /// <summary>Перечитывает список стратегий и настройки — страницы переиспользуются.</summary>
    public void RefreshFromSettings()
    {
        StrategyCatalog.Reload();

        _suppressSelection = true;
        StrategyCombo.ItemsSource = StrategyCatalog.All;
        StrategyCombo.SelectedItem =
            StrategyCatalog.FindById(