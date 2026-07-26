using System;
using System.Collections.Generic;
using System.Linq;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Собирает одну рабочую стратегию из победителей по отдельным протоколам.
///
/// Кандидаты матрицы однопротокольные: TLS-метод чинит TCP 443, QUIC-метод — UDP 443.
/// blockcheck2 в итоге тоже выдаёт одну команду из нескольких профилей, разделённых --new.
/// Глобальные аргументы (--wf-tcp-out, --wf-udp-out, --lua-init) выносятся вперёд и
/// объединяются: winws2 ждёт их один раз до первого профиля.
/// </summary>
public static class StrategyProfileBuilder
{
    public const string IdPrefix = "combo:";

    public static bool IsCombined(string? id) =>
        id is not null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    public static Strategy Build(IReadOnlyList<Strategy> parts)
    {
        if (parts.Count == 0) throw new ArgumentException("нет стратегий для сборки", nameof(parts));
        if (parts.Count == 1) return parts[0];

        var tcpPorts = new List<string>();
        var udpPorts = new List<string>();
        var luaInit = new List<string>();
        var profiles = new List<List<string>>();

        foreach (var part in parts)
        {
            var profile = new List<string>();

            foreach (var arg in part.Args)
            {
                if (arg.StartsWith("--wf-tcp-out=", StringComparison.Ordinal))
                {
                    AddPorts(tcpPorts, arg.Substring("--wf-tcp-out=".Length));
                    continue;
                }

                if (arg.StartsWith("--wf-udp-out=", StringComparison.Ordinal))
                {
                    AddPorts(udpPorts, arg.Substring("--wf-udp-out=".Length));
                    continue;
                }

                if (arg.StartsWith("--lua-init", StringComparison.Ordinal))
                {
                    if (!luaInit.Contains(arg, StringComparer.Ordinal)) luaInit.Add(arg);
                    continue;
                }

                // У стратегии из config уже могут быть свои --new: сохраняем её разбиение.
                if (arg == "--new")
                {
                    if (profile.Count > 0) profiles.Add(profile);
                    profile = new List<string>();
                    continue;
                }

                profile.Add(arg);
            }

            if (profile.Count > 0) profiles.Add(profile);
        }

        var args = new List<string>();
        if (tcpPorts.Count > 0) args.Add("--wf-tcp-out=" + string.Join(",", tcpPorts));
        if (udpPorts.Count > 0) args.Add("--wf-udp-out=" + string.Join(",", udpPorts));
        args.AddRange(luaInit);

        for (int i = 0; i < profiles.Count; i++)
        {
            if (i > 0) args.Add("--new");
            args.AddRange(profiles[i]);
        }

        var names = string.Join(" + ", parts.Select(p => p.Name));

        return new Strategy
        {
            Id = IdPrefix + string.Join("+", parts.Select(p => p.Id)),
            Name = "Подобранный профиль: " + names,
            Description =
                "Собран автоматически из методов, победивших каждый в своём протоколе: " + names +
                ". Профили разделены --new, порты и --lua-init объединены.",
            RecommendedFor = parts
                .SelectMany(p => p.RecommendedFor)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Args = args,
        };
    }

    private static void AddPorts(List<string> ports, string value)
    {
        foreach (var port in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ports.Contains(port, StringComparer.Ordinal)) ports.Add(port);
        }
    }
}
