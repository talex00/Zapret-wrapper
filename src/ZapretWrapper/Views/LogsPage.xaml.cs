using System.Windows;
using System.Windows.Controls;
using ZapretWrapper.Services;

namespace ZapretWrapper.Views;

public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        InitializeComponent();

        LogView.ItemsSource = LogService.Entries;
        LogService.EntryAdded += (_, _) => Dispatcher.InvokeAsync(ScrollToEnd);
        Loaded += (_, _) => ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (LogView.Items.Count > 0)
            LogView.ScrollIntoView(LogView.Items[^1]);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ScrollToEnd();

    private void Clear_Click(object sender, RoutedEventArgs e) => LogService.Clear();
}
