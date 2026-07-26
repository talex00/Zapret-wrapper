using System;
using System.Collections.Generic;
using System.Linq;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>Какой трафик починит стратегия и какой трафик проверяет проба.</summary>
public enum StrategyProtocol
{
    /// <summary>TCP 443, TLS ClientHello.</summary>
    Tls,

    /// <summary>TCP 80, обычный HTTP-запрос.</summary>
    Http,

    /// <summary>UDP 443, QUIC Initial.</summary>
    Quic,

    /// <summary>Несколько протоколов сразу — так устроен NFQWS2_OPT из config.</summary>
    Mixed,
}

/// <summary>
/// Планирование теста. Логика та же, что у blockcheck2: сначала смотрим, что вообще
/// заблокировано без обхода, и перебираем методы только для тех протоколов, где есть проблема.
/// Проверять TLS-метод пробой QUIC (и наоборот) бессмысленно: метод на такой
/// трафик просто не влияет, а время прогона растёт кратно.
/// </summary>
public static class StrategyPlan
{
    public static StrategyProtocol Detect(Strategy strategy)
    {
        var args = string.Join(" ", strategy.Args);
        var tls = args.Contains("--filter-l7=tls", StringComparison.OrdinalIgnoreCase);
        var http = args.Contains("--filter-l7=http", StringComparison.OrdinalIgnoreCase);
        var quic = args.Contains("--filter-l7=quic", StringComparison.OrdinalIgnoreCase);

        var count = (tls ? 1 : 0) + (http ? 1 : 0) + (quic ? 1 : 0);
        if (count != 1) return StrategyProtocol.Mixed;

        return tls ? StrategyProtocol.Tls : http ? StrategyProtocol.Http : StrategyProtocol.Quic;
    }

    public static string Label(StrategyProtocol protocol) => protocol switch
    {
        StrategyProtocol.Tls => "TCP/TLS (443)",
        StrategyProtocol.Http => "HTTP (80)",
        StrategyProtocol.Quic => "QUIC (UDP 443)",
        _ => "все протоколы",
    };

    /// <summary>
    /// На какой протокол смотрит проба. STUN — null: ни один метод матрицы не трогает
    /// UDP на произвольных портах, так что подбирать тут нечего — это только диагностика.
    /// </summary>
    public static StrategyProtocol? ProbeProtocol(ProbeSpec spec) => spec.Kind switch
    {
        ProbeKind.Quic => StrategyProtocol.Quic,
        ProbeKind.Http80 => StrategyProtocol.Http,
        ProbeKind.UdpStun => null,
        _ => StrategyProtocol.Tls,
    };

    /// <summary>Протоколы, которые реально сломаны без обхода. Остальные перебирать не нужно.</summary>
    public static HashSet<StrategyProtocol> BlockedProtocols(IReadOnlyList<ProbeResult> baseline)
    {
        var blocked = new HashSet<StrategyProtocol>();
        foreach (var result in baseline)
        {
            if (result.Ok) continue;
            var protocol = ProbeProtocol(result.Spec);
            if (protocol is not null) blocked.Add(protocol.Value);
        }
        return blocked;
    }

    /// <summary>Пробы, относящиеся только к указанному протоколу — вторая фаза теста.</summary>
    public static IReadOnlyList<CheckTarget> ProtocolTargets(
        IReadOnlyList<CheckTarget> targets, StrategyProtocol protocol) =>
        Filter(targets, spec => protocol == StrategyProtocol.Mixed || ProbeProtocol(spec) == protocol);

    /// <summary>
    /// Первая фаза: берём до maxProbes проб, которые упали без обхода. Стратегия, которая
    /// не подняла даже их, до полной проверки не допускается.
    /// </summary>
    public static IReadOnlyList<CheckTarget> QuickTargets(
        IReadOnlyList<CheckTarget> targets,
        IReadOnlyList<ProbeResult> baseline,
        StrategyProtocol protocol,
        int maxProbes = 2)
    {
        var titles = baseline
            .Where(r => !r.Ok)
            .Where(r => protocol == StrategyProtocol.Mixed || ProbeProtocol(r.Spec) == protocol)
            .OrderByDescending(r => r.Spec.Critical)
            .Select(r => r.Spec.Title)
            .Take(maxProbes)
            .ToHashSet(StringComparer.Ordinal);

        return titles.Count == 0
            ? Array.Empty<CheckTarget>()
            : Filter(targets, spec => titles.Contains(spec.Title));
    }

    /// <summary>
    /// Собирает поднабор целей. Важно: отобранные пробы помечаются Critical — именно они
    /// и есть предмет измерения, иначе у QUIC-прогона не было бы ни одного зачётного результата.
    /// </summary>
    private static IReadOnlyList<CheckTarget> Filter(
        IReadOnlyList<CheckTarget> targets, Func<ProbeSpec, bool> keep)
    {
        var list = new List<CheckTarget>();
        foreach (var target in targets)
        {
            var probes = target.Probes
                .Where(keep)
                .Select(p => p with { Critical = true })
                .ToList();
            if (probes.Count == 0) continue;

            list.Add(new CheckTarget { Id = target.Id, Name = target.Name, Probes = probes });
        }
        return list;
    }
}
