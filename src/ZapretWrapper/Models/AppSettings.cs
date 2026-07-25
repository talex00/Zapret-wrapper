namespace ZapretWrapper.Models;

/// <summary>
/// Настройки приложения. Хранятся в %APPDATA%/ZapretWrapper/settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>Путь к папке zapret2 (где лежат winws2.exe, lua/, files/fake/).</summary>
    public string? ZapretPath { get; set; }

    /// <summary>Выбранная стратегия (id из списка).</summary>
    public string? SelectedStrategyId { get; set; }

    /// <summary>Тема: "System" | "Light" | "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Домены для тестирования (через запятую). Если пусто — дефолтный список.</summary>
    public string TestDomains { get; set; } = "";

    /// <summary>Сколько мс ждать после запуска winws2, прежде чем начинать пробы (WinDivert должен успеть поставить фильтр).</summary>
    public int StartupDelayMs { get; set; } = 1500;
}
