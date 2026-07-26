using System.Collections.Generic;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Кандидаты для подбора стратегии. Набор методов и их параметры повторяют
/// blockcheck2.d/standard из zapret2 (20-multi.sh, 25-fake.sh, 90-quic.sh, def.inc),
/// но перебор урезан до вменяемого по времени объёма.
///
/// Важно: здесь сознательно нет --hostlist и --wf-raw-part. В чистом клоне zapret2
/// файлов вроде files/list-youtube.txt и init.d/windivert.filter/* нет, и winws2
/// падает сразу после запуска. Матрица должна работать на любой сборке.
/// </summary>
public static class StrategyMatrix
{
    private const string LuaLib = "--lua-init=@<lua/zapret-lib.lua>";
    private const string LuaAntiDpi = "--lua-init=@<lua/zapret-antidpi.lua>";

    /// <summary>Позиции разреза для TLS — из splits_tls в 20-multi.sh.</summary>
    private static readonly string[] TlsPositions = { "2", "1", "sniext+1", "midsld", "1,midsld" };

    /// <summary>Позиции разреза для HTTP — из splits_http в 20-multi.sh.</summary>
    private static readonly string[] HttpPositions = { "method+2", "midsld" };

    public static IReadOnlyList<Strategy> All { get; } = Build();

    private static List<Strategy> Build()
    {
        var list = new List<Strategy>();

        // ---- TLS: простые разрезы (20-multi.sh) ----
        foreach (var pos in TlsPositions)
        {
            list.Add(Tls($"m:tls:multisplit:{pos}", $"TLS multisplit pos={pos}",
                "Разрез TLS ClientHello на несколько сегментов по порядку.",
                $"--lua-desync=multisplit:pos={pos}"));

            list.Add(Tls($"m:tls:multidisorder:{pos}", $"TLS multidisorder pos={pos}",
                "Разрез TLS ClientHello с отправкой сегментов в обратном порядке.",
                $"--lua-desync=multidisorder:pos={pos}"));
        }

        // ---- TLS: поддельный пакет (25-fake.sh + FOOLINGS46_TCP из def.inc) ----
        list.Add(Tls("m:tls:fake:md5", "TLS fake + tcp_md5",
            "Фальшивый ClientHello с опцией TCP MD5 — DPI видит подделку, сервер её отбрасывает.",
            "--lua-desync=fake:blob=fake_default_tls:tcp_md5"));

        list.Add(Tls("m:tls:fake:seq", "TLS fake + tcp_seq",
            "Фальшивый ClientHello со сдвинутым номером последовательности.",
            "--lua-desync=fake:blob=fake_default_tls:tcp_seq=-3000"));

        list.Add(Tls("m:tls:fake:badsum", "TLS fake + badsum",
            "Фальшивый ClientHello с неверной контрольной суммой.",
            "--lua-desync=fake:blob=fake_default_tls:badsum"));

        list.Add(Tls("m:tls:fake:autottl", "TLS fake + autottl",
            "Фальшивый ClientHello с автоподбором TTL: доходит до DPI, но не до сервера.",
            "--lua-desync=fake:blob=fake_default_tls:ip_autottl=-2,3-20:ip6_autottl=-2,3-20"));

        list.Add(Tls("m:tls:fake:tlsmod", "TLS fake + tls_mod",
            "Фальшивый ClientHello со случайным содержимым и дублем session id.",
            "--lua-desync=fake:blob=fake_default_tls:tcp_md5:tls_mod=rnd,dupsid"));

        // ---- TLS: комбинации fake + разрез (50-fake-multi.sh) ----
        list.Add(Tls("m:tls:fake+multidisorder", "TLS fake + multidisorder",
            "Фальшивый пакет и следом разрез настоящего в обратном порядке.",
            "--lua-desync=fake:blob=fake_default_tls:tcp_md5",
            "--lua-desync=multidisorder:pos=1,midsld"));

        list.Add(Tls("m:tls:fake+multisplit", "TLS fake (autottl) + multisplit",
            "Фальшивый пакет с autottl и разрез настоящего по midsld.",
            "--lua-desync=fake:blob=fake_default_tls:ip_autottl=-2,3-20:ip6_autottl=-2,3-20",
            "--lua-desync=multisplit:pos=midsld"));

        // ---- TLS: маленькое окно (pktws_check_https_tls12 в 20-multi.sh) ----
        list.Add(Tls("m:tls:wssize", "TLS wssize + multidisorder",
            "Урезанное окно TCP заставляет клиента дробить ClientHello, плюс разрез.",
            "--lua-desync=wssize:wsize=1:scale=6",
            "--lua-desync=multidisorder:pos=1,midsld"));

        // ---- HTTP (порт 80) ----
        foreach (var pos in HttpPositions)
        {
            list.Add(Http($"m:http:multisplit:{pos}", $"HTTP multisplit pos={pos}",
                "Разрез HTTP-запроса.",
                $"--lua-desync=multisplit:pos={pos}"));
        }

        list.Add(Http("m:http:fake:md5", "HTTP fake + tcp_md5",
            "Фальшивый HTTP-запрос с опцией TCP MD5.",
            "--lua-desync=fake:blob=fake_default_http:tcp_md5"));

        list.Add(Http("m:http:fake:autottl", "HTTP fake + autottl",
            "Фальшивый HTTP-запрос с автоподбором TTL.",
            "--lua-desync=fake:blob=fake_default_http:ip_autottl=-2,3-20:ip6_autottl=-2,3-20"));

        // ---- QUIC / HTTP3 (90-quic.sh) ----
        foreach (var repeats in new[] { 1, 2, 5, 11 })
        {
            list.Add(Quic($"m:quic:fake:{repeats}", $"QUIC fake x{repeats}",
                "Фальшивые QUIC Initial-пакеты перед настоящим.",
                $"--lua-desync=fake:blob=fake_default_quic:repeats={repeats}"));
        }

        foreach (var pos in new[] { 8, 16, 32, 64 })
        {
            list.Add(Quic($"m:quic:ipfrag:{pos}", $"QUIC ipfrag pos={pos}",
                "IP-фрагментация QUIC Initial: DPI не собирает фрагменты.",
                $"--lua-desync=send:ipfrag:ipfrag_pos_udp={pos}",
                "--lua-desync=drop"));
        }

        list.Add(Quic("m:quic:fake+ipfrag", "QUIC fake + ipfrag",
            "Фальшивый QUIC-пакет плюс фрагментация настоящего.",
            "--lua-desync=fake:blob=fake_default_quic:repeats=2",
            "--lua-desync=send:ipfrag:ipfrag_pos_udp=8",
            "--lua-desync=drop"));

        return list;
    }

    private static Strategy Tls(string id, string name, string description, params string[] desync)
    {
        var args = new List<string>
        {
            "--wf-tcp-out=443", LuaLib, LuaAntiDpi,
            "--filter-tcp=443", "--filter-l7=tls", "--payload=tls_client_hello",
        };
        args.AddRange(desync);

        return new Strategy
        {
            Id = id,
            Name = name,
            Description = description,
            RecommendedFor = new List<string> { "TCP/TLS" },
            Args = args,
        };
    }

    private static Strategy Http(string id, string name, string description, params string[] desync)
    {
        var args = new List<string>
        {
            "--wf-tcp-out=80", LuaLib, LuaAntiDpi,
            "--filter-tcp=80", "--filter-l7=http", "--payload=http_req",
        };
        args.AddRange(desync);

        return new Strategy
        {
            Id = id,
            Name = name,
            Description = description,
            RecommendedFor = new List<string> { "HTTP" },
            Args = args,
        };
    }

    private static Strategy Quic(string id, string name, string description, params string[] desync)
    {
        var args = new List<string>
        {
            "--wf-udp-out=443", LuaLib, LuaAntiDpi,
            "--filter-udp=443", "--filter-l7=quic", "--payload=quic_initial",
        };
        args.AddRange(desync);

        return new Strategy
        {
            Id = id,
            Name = name,
            Description = description,
            RecommendedFor = new List<string> { "UDP/QUIC" },
            Args = args,
        };
    }
}
