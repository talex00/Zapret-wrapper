using System.Collections.Generic;

namespace ZapretWrapper.Models;

/// <summary>
/// Стратегия обхода. Соответствует одному вызову winws2.exe с заданными параметрами.
/// Взяты из preset2_example.cmd и вариаций.
/// </summary>
public class Strategy
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Параметры командной строки winws2.exe (без пути к exe).</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Сервисы, для которых стратегия рекомендована.</summary>
    public List<string> RecommendedFor { get; set; } = new();
}
