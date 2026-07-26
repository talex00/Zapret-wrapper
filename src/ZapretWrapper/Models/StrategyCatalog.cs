using System;
using System.Collections.Generic;
using System.Linq;
using ZapretWrapper.Services;

namespace ZapretWrapper.Models;

/// <summary>
/// Список стратегий. Основной источник — пресеты из папки zapret пользователя
/// (их читает StrategyLoader). Встроенные пресеты остались только как запасной вариант:
/// жёсткий список в коде неизбежно расходится с тем, что лежит в релизе zapret.
/// </summary>
public static class StrategyCatalog
{
    private static List<Strategy> _all = new();
    private static string? _loadedFrom;

    public static IReadOnlyList<Strategy> BuiltIn { get; } = BuildBuiltIn();

    public static IReadOnlyList<Strategy> All
    {
        get
        {
            if (_all.Count == 0) Reload();
            return _all;
        }
    }

    /// <summary>Откуда взят текущий список — показываем в UI, чтобы не было сюрпризов.</summary>
    public static string SourceDescription { get; private set; } = "не загружено";

    public static bool IsFromFolder { get; private set; }

    public static event EventHandler? Changed;

    /// <summary>Перечитывает пресеты из папки zapret. Дешёво: вызывается при переходе между страницами.</summary>
    public static void Reload(bool force = false)
    {
        var path = SettingsService.Current.ZapretPath;

        if (!force && _all.Count > 0 && string.Equals(_loadedFrom, path, StringComparison.OrdinalIgnoreCase))
            return;

        var result = StrategyLoader.Load(path);
        _loadedFrom = path;

        if (result.Strategies.Count > 0)
        {
            _all = result.Strategies;
            IsFromFolder = true;
            SourceDescription =
                $"найдено в папке zapret: {result.Strategies.Count} пресетов";
            if (result.Skipped.Count > 0)
                SourceDescription += $" (пропущено без вызова winws2: {result.Skipped.Count})";

            LogService.Info("Стратегии загружены из папки zapret: " + result.Strategies.Count);
        }
        else
        {
            _all = BuiltIn.ToList();
            IsFromFolder = false;
            SourceDescription = result.Error is not null
                ? $"встроенные пресеты ({result.Error})"
                : "встроенные пресеты: .cmd-файлов с вызовом winws2 в папке не найдено";
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Strategy? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return All.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>
    /// Запасной список на случай, если в папке zapret нет .cmd-пресетов.
    /// Аргументы — из preset2_example.cmd, пути подставляет ZapretLocator.
    /// </summary>
    private static List<Strategy> BuildBuiltIn()
    {
        return new List<Strategy>
        {
            new()
            {
                Id = "auto_qq",
                Name = "auto_qq (встроенный)",
                Description = "Универсальный: TCP 80/443 + UDP 443 (QUIC).",
                RecommendedFor = new() { "youtube", "discord", "instagram" },
                Args = new()
                {
                    "--wf-tcp-out=80,443",
                    "--lua-init=@<lua/zapret-lib.lua>",
                    "--lua-init=@<lua/zapret-antidpi.lua>",
                    "--blob=quic_google:@<files/fake/quic_initial_www_google_com.bin>",
                    "--wf-raw-part=@<windivert.filter/windivert_part.discord_media.txt>",
                    "--wf-raw-part=@<windivert.filter/windivert_part.stun.txt>",
                    "--wf-raw-part=@<windivert.filter/windivert_part.wireguard.txt>",
                    "--wf-raw-part=@<windivert.filter/windivert_part.quic_initial_ietf.txt>",
                    "--filter-tcp=80", "--filter-l7=http",
                    "--out-range=-d10",
                    "--payload=http_req",
                    "--lua-desync=fake:blob=fake_default_http:ip_autottl=-2,3-20:ip6_autottl=-2,3-20:tcp_md5",
                    "--lua-desync=fakedsplit:ip_autottl=-2,3-20:ip6_autottl=-2,3-20:tcp_md5",
                    "--new",
                    "--filter-tcp=443", "--filter-l7=tls", "--hostlist=<files/list-youtube.txt>",
                    "--out-range=-d10",
                    "--payload=tls_client_hello",
                    "--lua-desync=fake:blob=fake_default_tls:tcp_md5:repeats=11:tls_mod=rnd,dupsid,sni=www.google.com",
                    "--lua-desync=multidisorder:pos=1,midsld",
                    "--new",
                    "--filter-tcp=443", "--filter-l7=tls",
                    "--out-range=-d10",
                    "--payload=tls_client_hello",
                    "--lua-desync=fake:blob=fake_default_tls:tcp_md5:tcp_seq=-10000:repeats=6",
                    "--lua-desync=multidisorder:pos=midsld",
                    "--new",
                    "--filter-udp=443", "--filter-l7=quic", "--hostlist=<files/list-youtube.txt>",
                    "--payload=quic_initial",
                    "--lua-desync=fake:blob=quic_google:repeats=11",
                    "--new",
                    "--filter-udp=443", "--filter-l7=quic",
                    "--payload=quic_initial",
                    "--lua-desync=fake:blob=fake_default_quic:repeats=11",
                    "--new",
                    "--filter-l7=wireguard,stun,discord",
                    "--payload=wireguard_initiation,wireguard_cookie,stun,discord_ip_discovery",
                    "--lua-desync=fake:blob=0x00000000000000000000000000000000:repeats=2",
                },
            },

            new()
            {
                Id = "general_tcp",
                Name = "general_tcp (встроенный)",
                Description = "Универсальный TCP: fake + multidisorder, без UDP.",
                RecommendedFor = new() { "youtube", "TCP/TLS" },
                Args = new()
                {
                    "--wf-tcp-out=80,443",
                    "--lua-init=@<lua/zapret-lib.lua>",
                    "--lua-init=@<lua/zapret-antidpi.lua>",
                    "--filter-tcp=443", "--filter-l7=tls",
                    "--out-range=-d10",
                    "--payload=tls_client_hello",
                    "--lua-desync=fake:blob=fake_default_tls:tcp_md5:tcp_seq=-10000:repeats=6",
                    "--lua-desync=multidisorder:pos=midsld",
                    "--new",
                    "--filter-tcp=80", "--filter-l7=http",
                    "--out-range=-d10",
                    "--payload=http_req",
                    "--lua-desync=fake:blob=fake_default_http:tcp_md5",
                },
            },

            new()
            {
                Id = "udp_fragment",
                Name = "udp_fragment (встроенный)",
                Description = "QUIC/HTTP3 с IP-фрагментацией.",
                RecommendedFor = new() { "youtube", "UDP/QUIC" },
                Args = new()
                {
                    "--wf-udp-out=443",
                    "--lua-init=@<lua/zapret-lib.lua>",
                    "--lua-init=@<lua/zapret-antidpi.lua>",
                    "--filter-udp=443", "--filter-l7=quic",
                    "--payload=quic_initial",
                    "--lua-desync=send:ipfrag:ipfrag_pos_udp=8",
                    "--lua-desync=drop",
                },
            },

            new()
            {
                Id = "tls_fake_sni",
                Name = "tls_fake_sni (встроенный)",
                Description = "TLS с подменой SNI на www.google.com.",
                RecommendedFor = new() { "youtube", "discord", "TCP/TLS" },
                Args = new()
                {
                    "--wf-tcp-out=443",
                    "--lua-init=@<lua/zapret-lib.lua>",
                    "--lua-init=@<lua/zapret-antidpi.lua>",
                    "--filter-tcp=443", "--filter-l7=tls",
                    "--out-range=-d10",
                    "--payload=tls_client_hello",
                    "--lua-desync=fake:blob=fake_default_tls:tcp_md5:tls_mod=rnd,dupsid,sni=www.google.com",
                },
            },
        };
    }
}
