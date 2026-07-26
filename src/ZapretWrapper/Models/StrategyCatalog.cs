using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZapretWrapper.Services;

namespace ZapretWrapper.Models;

/// <summary>
/// Список стратегий. Сначала определяем сборку (ZapretBackend), потом берём
/// стратегии тем способом, который для этой сборки осмыслен:
///
/// Flowseal/zapret-discord-youtube:
///   general*.bat в корне — два десятка готовых стратегий от автора сборки.
///   Матрицу методов zapret2 здесь подмешивать нельзя: там аргументы --lua-desync,
///   а здесь старый winws.exe с --dpi-desync — он умрёт на неизвестном ключе.
///
/// bol-van/zapret2 — три уровня, как и было:
///   1) .cmd/.bat пресеты (zapret-win-bundle);
///   2) NFQWS2_OPT из config / config.default плюс матрица методов;
///   3) только матрица методов blockcheck2.d.
///
/// Поверх этого живёт подобранный профиль — результат теста, собранный из победителей
/// по отдельным протоколам. Он всегда первый в списке и переживает Reload.
/// Для flowseal склеивание отключено: каждый .bat и так содержит девять профилей
/// через --new и покрывает TCP, QUIC, Discord UDP и игры сразу.
/// </summary>
public static class StrategyCatalog
{
    private static List<Strategy> _all = new();
    private static string? _loadedFrom;
    private static Strategy? _combined;

    /// <summary>Кандидаты из матрицы blockcheck2.d — только для zapret2.</summary>
    public static IReadOnlyList<Strategy> BuiltIn => StrategyMatrix.All;

    /// <summary>Сборка, определённая при последнем Reload.</summary>
    public static ZapretFlavor Flavor { get; private set; } = ZapretFlavor.Unknown;

    /// <summary>Имеет ли смысл склеивать победителей по протоколам в один профиль.</summary>
    public static bool SupportsCombining => Flavor == ZapretFlavor.Zapret2;

    public static IReadOnlyList<Strategy> All
    {
        get
        {
            if (_all.Count == 0) Reload();
            return _all;
        }
    }

    /// <summary>Последний собранный профиль — если тест уже прогоняли.</summary>
    public static Strategy? Combined => _combined;

    /// <summary>
    /// Что гонять на тесте: всё, кроме ранее собранного профиля — он собирается заново
    /// в конце каждого прогона, и тестировать старый бессмысленно.
    /// </summary>
    public static IReadOnlyList<Strategy> TestCandidates =>
        All.Where(s => !StrategyProfileBuilder.IsCombined(s.Id)).ToList();

    /// <summary>Откуда взят текущий список — показываем в UI, чтобы не было сюрпризов.</summary>
    public static string SourceDescription { get; private set; } = "не загружено";

    public static bool IsFromFolder { get; private set; }

    public static event EventHandler? Changed;

    /// <summary>Перечитывает стратегии из папки zapret.</summary>
    public static void Reload(bool force = false)
    {
        var path = SettingsService.Current.ZapretPath;

        if (!force && _all.Count > 0 && string.Equals(_loadedFrom, path, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedFrom = path;
        Flavor = ZapretBackend.Detect(path);

        if (Flavor == ZapretFlavor.Flowseal)
        {
            LoadFlowseal(path);
            return;
        }

        LoadZapret2(path);
    }

    private static void LoadFlowseal(string? path)
    {
        var loaded = FlowsealLoader.Load(path);

        if (loaded.Strategies.Count > 0)
        {
            _all = loaded.Strategies;
            IsFromFolder = true;
            SourceDescription = $"zapret-discord-youtube: стратегий {loaded.Strategies.Count}";
            if (loaded.Skipped.Count > 0)
                SourceDescription += $" (пропущено без вызова winws: {loaded.Skipped.Count})";

            LogService.Info("Стратегии загружены из general*.bat: " + loaded.Strategies.Count);
            Finish();
            return;
        }

        // Папка опознана как flowseal, но файлов стратегий нет. Матрица тут не поможет —
        // лучше пустой список и внятное сообщение, чем два десятка заведомо нерабочих.
        _all = new List<Strategy>();
        IsFromFolder = false;
        SourceDescription = "сборка zapret-discord-youtube опознана, но general*.bat не найдены";
        if (loaded.Error is not null) SourceDescription += ": " + loaded.Error;

        LogService.Warn(SourceDescription);
        Finish();
    }

    private static void LoadZapret2(string? path)
    {
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
            Finish();
            return;
        }

        // 2) Чистый zapret2: стратегия живёт в config (NFQWS2_OPT).
        var fromConfig = ZapretConfigLoader.Load(path);
        if (fromConfig.Strategy is not null)
        {
            _all = new List<Strategy> { fromConfig.Strategy };
            _all.AddRange(StrategyMatrix.All);

            IsFromFolder = true;
            SourceDescription = "конфиг zapret2 " + Path.GetFileName(fromConfig.SourceFile ?? "config") +
                                $" (NFQWS2_OPT) + матрица методов blockcheck2 ({StrategyMatrix.All.Count})";

            LogService.Info("Стратегия загружена из " + fromConfig.SourceFile);
            Finish();
            return;
        }

        // 3) Ни пресетов, ни конфига — работаем только от матрицы методов.
        _all = StrategyMatrix.All.ToList();
        IsFromFolder = false;
        SourceDescription = $"матрица методов blockcheck2 ({StrategyMatrix.All.Count})";
        if (fromConfig.Error is not null)
            SourceDescription += $": {fromConfig.Error}";

        Finish();
    }

    /// <summary>Запоминает собранный профиль и ставит его в начало списка.</summary>
    public static void SetCombined(Strategy strategy)
    {
        _combined = strategy;
        if (_all.Count == 0) Reload();
        InsertCombined();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Finish()
    {
        InsertCombined();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void InsertCombined()
    {
        if (_combined is null) return;

        // Собранный профиль от другой сборки несовместим с текущим бинарником.
        if (!SupportsCombining)
        {
            _combined = null;
            return;
        }

        _all.RemoveAll(s => StrategyProfileBuilder.IsCombined(s.Id));
        _all.Insert(0, _combined);
    }

    public static Strategy? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var found = All.FirstOrDefault(s => s.Id == id);
        if (found is not null) return found;

        // По идентификатору из настроек может прийти стратегия другой сборки —
        // подставлять её вслепую опасно, поэтому только для zapret2.
        return Flavor == ZapretFlavor.Zapret2
            ? StrategyMatrix.All.FirstOrDefault(s => s.Id == id)
            : null;
    }
}
