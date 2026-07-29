using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZapretWrapper.Views;

/// <summary>
/// В простом режиме окно размером с единственную карточку, и тогда её рамка
/// теряет смысл: карточка нужна, чтобы отделять себя от фона и соседних блоков, а здесь
/// нет ни того, ни другого — видна только обводка по краю окна.
///
/// В расширенном режиме карточки возвращаются: там они живут на широком сером
/// полотне рядом с другими блоками, и границы снова работают.
/// </summary>
public partial class HomePage
{
    /// <summary>Совпадает с Padding карточек в HomePage.xaml.</summary>
    private const double CardPadding = 18;

    private bool _chromeless;

    /// <summary>
    /// Включает безрамочный вид. Вызывается из MainWindow при каждом переходе на главную
    /// и при смене режима: страница кешируется, поэтому одного раза при создании не хватает.
    /// </summary>
    public void SetChromeless(bool on)
    {
        _chromeless = on;

        ApplyCardChrome(PathCard, on);
        ApplyCardChrome(LaunchCard, on);

        // Без рамок вертикальные отступы карточек пропадают, и «Подробности» прилипают
        // к кнопке запуска — добавляем их вручную.
        DetailsExpander.Margin = on
            ? new Thickness(0, 14, 0, 0)
            : new Thickness(0, 10, 0, 0);
    }

    private static void ApplyCardChrome(Border? card, bool chromeless)
    {
        if (card is null) return;

        if (chromeless)
        {
            card.Background = Brushes.Transparent;
            card.BorderThickness = new Thickness(0);
            card.Padding = new Thickness(0);
            return;
        }

        // Локальные значения перекрывают Style, поэтому возврат — именно ClearValue,
        // иначе карточка осталась бы прозрачной и перестала следить за темой.
        card.ClearValue(Border.BackgroundProperty);
        card.ClearValue(Border.BorderThicknessProperty);
        card.Padding = new Thickness(CardPadding);
    }
}
