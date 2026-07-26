using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZapretWrapper.Services;

namespace ZapretWrapper.Models;

/// <summary>
/// Список стратегий. Источники по приоритету:
/// 1) .cmd/.bat пресеты из папки (так устроен zapret-win-bundle);
/// 2) переменная NFQWS2_OPT из config / config.default плюс матрица методов
///    (так устроен чистый zapret2: готовый конфиг один, а подбирать нужно из многих);
/// 3) только матрица методов из blockcheck2.d — если нет ни пресетов, ни конфига.
///
/// Своих выдуманных пресетов больше нет: жёсткий список с путями вроде
/// files/list-youtube.txt и init.d/windivert.filter/* расходится с реальной сборкой,
/// и winws2 завершается сразу после запуска.
/// </summary>
public static class StrategyCatalog
{
    private static List<Strategy> _all = new();
    private static string? _loadedFrom;

    /// <summary>Кандидаты из матрицы blockcheck2.d — работают на любой сборке zapret2.</summary>
    public static IReadOnlyList<Strategy> BuiltIn => StrategyMatrix.All;

    public static IReadOnlyList<Strategy> All
    {
        get
        {
            if (_all.Count == 0) Reload();
            return _all;
        }
    }

    /// <summary>Что гонять на тесте. Совпадает с All: список уже включает кандидатов.</summary>
    public static IReadOnlyList<Strategy> TestCandidates => All;

    /// <summary>Откуда взят текущий список — показываем в UI, чтобы не было сюрпризов.</summary>
    public static string SourceDescription { get; private set; } = "не загружено";

    public static bool IsFromFolder { get; private set; }

    public static event EventHandler? Changed;

    /// <summary>Перечитывает стратегии из папки zapret. Дешёво: вызывается при переходе между страницами.</summary>
    public static void Reload(bool force = false)
    {
        var path = SettingsService.Current.ZapretPath;

        if (!force && _all.Count > 0 && string.Equals(_loadedFrom, path, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedFrom = path;

        // 1) .cmd/.bat пресеты (zapret-win-bundle и сборки на его основе).
        var fromFiles = StrategyLoader.Load(path);
        if (fromFiles.Strategies.Count > 0)
        {
            _all = fromFiles.Strategies;
            IsFromFolder = true;
            SourceDescription = $"пресеты из папки zapret: {fromFiles.Strategies.Count}";
            if (fromFiles.Skipped.Count > 0)
                SourceDescription += $" (пропущено без вызова winws2: {fromFiles.Skipped.Count})";

            LogService.Info("Стратегии загружены из .cmd-пресетов: " + fromFiles.Strategies.Count);
            Changed?.Invoke(null, EventArgs.Empty);
            return;
        }

        // 2) Чистый zapret2: .cmd-файлов нет, стратегия живёт в config (NFQWS2_OPT).
        // Одного конфига для подбора мало, поэтому добавляем матрицу методов.
        var fromConfig = ZapretConfigLoader.Load(path);
        if (fromConfig.Strategy is not null)
        {
            _all = new List<Strategy> { fromConfig.Strategy };
            _all.AddRange(StrategyMatrix.All);

            IsFromFolder = true;
            SourceDescription = "конфиг zapret2 " + Path.GetFileName(fromConfig.SourceFile ?? "config") +
                                $" (NFQWS2_OPT) + матрица методов blockcheck2 ({StrategyMatrix.All.Count})";

            LogService.Info("Стратегия загружена из " + fromConfig.SourceFile);
            Changed?.Invoke(null, EventArgs.Empty);
            return;
        }

        // 3) Ни пресетов, ни конфига — работаем только от матрицы методов.
        _all = StrategyMatrix.All.ToList();
        IsFromFolder = false;
        SourceDescription = $"матрица методов blockcheck2 ({StrategyMatrix.All.Count})";
        if (fromConfig.Error is not null)
            SourceDescription += $": {fromConfig.Error}";

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Strategy? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return All.FirstOrDefault(s => s.Id == id)
               ?? StrategyMatrix.All.FirstOrDefault(s => s.Id == id);
    }
}
