using System.Windows.Controls;
using ZapretWrapper.Models;

namespace ZapretWrapper.Views;

public partial class StrategiesPage : UserControl
{
    public StrategiesPage()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>Список зависит от папки zapret из настроек, поэтому обновляется при открытии.</summary>
    public void Refresh()
    {
        StrategyCatalog.Reload();

        StrategyList.ItemsSource = null;
        StrategyList.ItemsSource = StrategyCatalog.All;

        SourceText.Text = StrategyCatalog.IsFromFolder
            ? $"Стратегии прочитаны из папки zapret ({StrategyCatalog.SourceDescription}). Чтобы добавить свою — положите ещё один .cmd-пресет рядом с остальными."
            : $"Используются встроенные пресеты: {StrategyCatalog.SourceDescription}.";
    }
}
