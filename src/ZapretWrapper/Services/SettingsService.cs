using System;
using System.IO;
using System.Text.Json;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>Хранит и загружает настройки приложения в %APPDATA%/ZapretWrapper.</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZapretWrapper");

    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // Повреждённый или нечитаемый файл — откатываемся к дефолтам.
        }

        return new AppSettings();
    }

    /// <summary>Перечитывает настройки с диска, заменяя текущий экземпляр.</summary>
    public static void Reload()
    {
        Current = Load();
    }

    /// <summary>Сохраняет текущие настройки. Возвращает false при ошибке вместо того, чтобы её проглотить.</summary>
    public static bool Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(ConfigPath, json);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Не удалось сохранить настройки: {ex.Message}");
            return false;
        }
    }
}
