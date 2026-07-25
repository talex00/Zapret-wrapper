using System.Windows.Controls;
using ZapretWrapper.Models;

namespace ZapretWrapper.Views;

public partial class StrategiesPage : UserControl
{
    public StrategiesPage()
    {
        InitializeComponent();
        StrategyList.ItemsSource = StrategyCatalog.All;
    }
}
