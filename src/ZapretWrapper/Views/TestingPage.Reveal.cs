using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using ZapretWrapper.ViewModels;

namespace ZapretWrapper.Views;

/// <summary>
/// Раскрытие таблицы результатов по мере появления данных.
///
/// Вынесено в отдельный файл, чтобы не трогать логику подбора в TestingPage.xaml.cs:
/// это чистое представление. Кольца метрик сюда не входят — ими управляет привязка
/// в разметке (пока в них «—», карточка свёрнута).
///
/// Почему не по нажатию кнопки: клик может закончиться предупреждением о том, что путь
/// к zapret не указан, и тогда пустая таблица раскрылась бы зря. Уведомление от строки
/// приходит только когда прогон реально пошёл.
/// </summary>
public partial class TestingPage
{
    private bool _revealHooked;

    private void Reveal_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded срабатывает при каждом возврате на вкладку — подписываемся один раз.
        if (_revealHooked) return;
        _revealHooked = true;

        foreach (var row in _rows) HookRow(row);

        // BuildRows пересобирает список при каждом открытии вкладки и перед стартом теста,
        // так что новые строки нужно подхватывать.
        _rows.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is null) return;
            foreach (StrategyTestRow row in args.NewItems) HookRow(row);
        };
    }

    private void HookRow(StrategyTestRow row) => row.PropertyChanged += Row_PropertyChanged;

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Имя выставляется в инициализаторе объекта, до подписки, поэтому сюда
        // долетают только изменения по ходу прогона: статус, отклик, детали.
        ResultsCard.Visibility = Visibility.Visible;
        ProgressText.Visibility = Visibility.Visible;
    }
}
