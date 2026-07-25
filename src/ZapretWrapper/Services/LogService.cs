using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ZapretWrapper.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Success,
}

public class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Message { get; init; } = "";

    public string LevelTag => Level switch
    {
        LogLevel.Warn => "[WARN]   ",
        LogLevel.Error => "[ERROR]  ",
        LogLevel.Success => "[SUCCESS]",
        LogLevel.Debug => "[DEBUG]  ",
        _ => "[INFO]   ",
    };

    public string Text => $"{Timestamp:HH:mm:ss} {LevelTag} {Message}";
}

/// <summary>
/// Единый журнал приложения. Раньше каждая страница вела свой лог, а LogsPage
/// читала файл, который никто не создавал. Теперь всё пишется сюда:
/// в память (для UI) и в %APPDATA%/ZapretWrapper/zapret-wrapper.log.
/// </summary>
public static class LogService
{
    private const int MaxEntries = 2000;
    private static readonly object _fileLock = new();

    /// <summary>Коллекция для биндинга. Изменяется только в UI-потоке.</summary>
    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static string LogPath =>
        Path.Combine(SettingsService.ConfigDir, "zapret-wrapper.log");

    public static event EventHandler<LogEntry>? EntryAdded;

    public static void Debug(string message) => Add(LogLevel.Debug, message);
    public static void Info(string message) => Add(LogLevel.Info, message);
    public static void Warn(string message) => Add(LogLevel.Warn, message);
    public static void Error(string message) => Add(LogLevel.Error, message);
    public static void Success(string message) => Add(LogLevel.Success, message);

    public static void Add(LogLevel level, string message)
    {
        var entry = new LogEntry { Level = level, Message = message };

        // winws2 пишет в stdout из фонового потока — переводим в UI-поток.
        var app = Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
            app.Dispatcher.BeginInvoke(new Action(() => Append(entry)));
        else
            Append(entry);

        WriteToFile(entry);
    }

    private static void Append(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        EntryAdded?.Invoke(null, entry);
    }

    public static void Clear() => Entries.Clear();

    private static void WriteToFile(LogEntry entry)
    {
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(SettingsService.ConfigDir);
                File.AppendAllText(LogPath, entry.Text + Environment.NewLine);
            }
        }
        catch
        {
            // Логгер не имеет права ронять приложение.
        }
    }
}
