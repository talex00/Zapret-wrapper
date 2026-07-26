using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void CopyAll_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(LogService.Entries.Select(entry => entry.Text));

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = LogView.SelectedItems
            .OfType<LogEntry>()
            .Select(entry => entry.Text)
            .ToList();

        // Ничего не выделено — логичнее скопировать всё, чем молча ничего не сделать.
        if (selected.Count == 0)
        {
            CopyAll_Click(sender, e);
            return;
        }

        CopyToClipboard(selected);
    }

    private void LogView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Если курсор внутри строки и текст выделен мышью — пусть работает обычное
            // копирование выделенного фрагмента.
            if (Keyboard.FocusedElement is TextBox { SelectionLength: > 0 }) return;

            CopySelected_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = LogService.LogPath;
            if (!File.Exists(path))
            {
                MessageBox.Show("Файл журнала пока не создан.", "ZapretWrapper",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // /select открывает папку и выделяет файл — открывать .log сторонним
            // приложением надёжно не получится, ассоциации может не быть.
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось открыть файл журнала: " + ex.Message, "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Буфер обмена в Windows может быть занят другим процессом, и Clipboard.SetText
    /// бросает COM-исключение. Приложение из-за этого падать не должно.
    /// </summary>
    private static void CopyToClipboard(IEnumerable<string> lines)
    {
        var text = string.Join(Environment.NewLine, lines);
        if (text.Length == 0) return;

        try
        {
            Clipboard.SetDataObject(text, true);
            LogService.Debug("Журнал скопирован в буфер обмена.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось скопировать: " + ex.Message, "ZapretWrapper",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
