using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZapretWrapper.Services;

namespace ZapretWrapper.Views;

public partial class LogsPage : UserControl
{
    private readonly ObservableCollection<LogLine> _lines = new();

    public LogsPage()
    {
        InitializeComponent();
        LogView.ItemsSource = _lines;

        // Подписка на события Runner
        if (App.Runner is ZapretRunner runner)
        {
            runner.ProcessExited += (_, _) => AddLine("[INFO]", "winws2 завершил работу.");
        }

        // Очистим файл лога при первом открытии? Нет — оставим.
        Loaded += (_, _) => RefreshFromFile();
    }

    private void RefreshFromFile()
    {
        _lines.Clear();
        var logPath = GetLogPath();
        if (logPath is null || !File.Exists(logPath)) return;
        try
        {
            foreach (var line in File.ReadAllLines(logPath))
            {
                _lines.Add(new LogLine { Text = line, Level = GuessLevel(line) });
            }
        }
        catch { /* ignore */ }
    }

    private static string? GetLogPath()
    {
        var path = SettingsService.Current.ZapretPath;
        if (path is null) return null;
        var candidate = System.IO.Path.Combine(path, "zapret-wrapper.log");
        return candidate;
    }

    private static string GuessLevel(string line)
    {
        if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) return "ERROR";
        if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase)) return "WARN";
        if (line.Contains("INFO", StringComparison.OrdinalIgnoreCase)) return "INFO";
        return "DEBUG";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshFromFile();
    private void Clear_Click(object sender, RoutedEventArgs e) => _lines.Clear();

    public void AddLine(string level, string text)
    {
        Dispatcher.Invoke(() =>
        {
            _lines.Add(new LogLine { Text = $"{level} {text}", Level = level });
            while (_lines.Count > 1000) _lines.RemoveAt(0);
        });
    }

    public class LogLine
    {
        public string Text { get; set; } = "";
        public string Level { get; set; } = "";
    }
}
