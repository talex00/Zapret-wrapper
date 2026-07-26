namespace ZapretWrapper.Models;

/// <summary>
/// Настройки приложения. Хранятся в %APPDATA%/ZapretWrapper/settings.json.
/// </summary>
public class AppSettings
{
    /// <summary>Путь к папке zapret (flowseal-сборка или zapret2 — флейвор определяется автоматически).</summary>
    public string? ZapretPath { get; set; }

    /// <summary>Выбранная стратегия (id из списка).</summary>
    public string? SelectedStrategyId { get; set; }

    /// <summary>Тема: "System" | "Light" | "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Домены для тестирования (через запятую). Если пусто — берётся дефолтный набор
    /// (YouTube + Discord). Именно они и есть практический смысл flowseal-сборки.
    /// </summary>
    public string TestDomains { get; set; } = "";

    /// <summary>Сколько мс ждать после запуска winws, прежде чем начинать пробы (WinDivert должен успеть поставить фильтр).</summary>
    public int StartupDelayMs { get; set; } = 1500;
}
