using System;
using System.IO;
using System.Text.Json;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Хранит настройки приложения в %APPDATA%/ZapretWrapper/settings.json.
/// Потокобезопасно: один процесс, но на всякий случай используем lock.
/// </summary>
public static class SettingsService
{
    private static readonly object _lock = new();
    private static readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZapretWrapper");
    private static readonly string _path = Path.Combine(_dir, "settings.json");

    private static AppSettings? _current;

    public static AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                if (_current is null) Load();
                return _current!;
            }
        }
    }

    public static void Save()
    {
        lock (_lock)
        {
            if (_current is null) return;
            Directory.CreateDirectory(_dir);
            var json = JsonSerializer.Serialize(_current,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }

    /// <summary>Полная перезагрузка с диска (для отладки).</summary>
    public static void Reload() { lock (_lock) { _current = null; Load(); } }

    private static void Load()
    {
        if (!File.Exists(_path))
        {
            _current = new AppSettings();
            return;
        }
        try
        {
            var json = File.ReadAllText(_path);
            _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // Повреждённый файл → дефолтные настройки.
            _current = new AppSettings();
        }
    }

    public static string ConfigPath => _path;
    public static string ConfigDir => _dir;
}
