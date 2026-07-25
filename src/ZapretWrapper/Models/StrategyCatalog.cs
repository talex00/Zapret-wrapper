using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ZapretWrapper.Models;

/// <summary>
/// Каталог встроенных пресетов стратегий. Параметры — точные командные строки winws2.exe,
/// извлечённые из preset2_example.cmd. Все пути к --lua-init и --blob подставляются
/// автоматически из ZapretLocator при запуске.
/// </summary>
public static class StrategyCatalog
{
    public static IReadOnlyList<Strategy> All { get; } = Build();

    private static List<Strategy> Build()
    {
        return new List<Strategy>
        {
            new()
            {
                Id = "auto_qq",
                Name = "auto_qq",
                Description = "Универсальный: TCP 80/443 + UDP 443 (QUIC). Рекомендуется для YouTube/Discord.",
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
                    // Профиль 1: HTTP
                    "--filter-tcp=80", "--filter-l7=http",
                    "--out-range=-d10",
                    "--payload=http_req",
                    "--lua-desync=fake:blob=fake_default_http:ip_autottl=-2,3-20:ip6_autottl=-2,3-20:tcp_md5",
                    "--lua-desync=fakedsplit:ip_autottl=-2,3-20:ip6_autottl=-2,3-20:tcp_md5",
                    "--new",
                    // Профиль 2: TLS YouTube
                    "--filter-tcp=443", "--filter-l7=tls", "--hostlist=<files/list-youtube.txt>",
                    "--out-range=-d10",
                    "--payload=tls_client_hello",
                    "--lua-desync=fake:blob=fake_default_tls:tcp_md5:repeats=11:tls_mod=rnd,dupsid,sni=www.google.com",
                    "--lua-desync=multidisorder:pos=1,midsld",
                    "--new",
                    // Профиль 3: TLS прочее
                    "--filter-tcp=443", "--filter-l7=tls",
                    "--out-range=-d10",
                    "--payload=tls_client_hello",
                    "--lua-desync=fake:blob=fake_default_tls:tcp_md5:tcp_seq=-10000:repeats=6",
                    "--lua-desync=multidisorder:pos=midsld",
                    "--new",
                    // Профиль 4: QUIC YouTube
                    "--filter-udp=443", "--filter-l7=quic", "--hostlist=<files/list-youtube.txt>",
                    "--payload=quic_initial",
                    "--lua-desync=fake:blob=quic_google:repeats=11",
                    "--new",
                    // Профиль 5: QUIC прочее
                    "--filter-udp=443", "--filter-l7=quic",
                    "--payload=quic_initial",
                    "--lua-desync=fake:blob=fake_default_quic:repeats=11",
                    "--new",
                    // Профиль 6: Wireguard / STUN / Discord
                    "--filter-l7=wireguard,stun,discord",
                    "--payload=wireguard_initiation,wireguard_cookie,stun,discord_ip_discovery",
                    "--lua-desync=fake:blob=0x00000000000000000000000000000000:repeats=2",
                },
            },

            new()
            {
                Id = "youtube_qq",
                Name = "youtube_qq",
                Description = "Только YouTube + Discord. Агрессивный fake+multidisorder+md5sig, 11 повторов на TLS.",
                RecommendedFor = new() { "youtube", "discord" },
                Args = new()
                {
                    "--wf-tcp-out=80,443",
                    "--lua-init=@<lua/zapret-lib.lua>",
                    "--lua-init=@<lua/zapret-antidpi.lua>",
                    "--lua-init=\"fake_default_tls = tls_mod(fake_default_tls,'rnd,rndsni')\"",
                    "--blob=quic_google:@<files/fake/quic_initial_www_google_com.bin>",
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
                    "--filter-udp=443", "--filter-l7=quic", "--hostlist=<files/list-youtube.txt>",
                    "--payload=quic_initial",
                    "--lua-desync=fake:blob=quic_google:repeats=11",
                    "--new",
                    "--filter-l7=discord",
                    "--payload=discord_ip_discovery",
                    "--lua-desync=fake:blob=0x00000000000000000000000000000000:repeats=2",
                },
            },

            new()
            {
                Id = "general_tcp",
                Name = "general_tcp",
                Description = "Универсальный TCP. Мягкий fake+multidisorder+badseq, 6 повторов. Без UDP.",
                RecommendedFor = new() { "youtube" },
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
                Name = "udp_fragment",
                Description = "QUIC/HTTP3 с IP-фрагментацией. Подходит для провайдеров, режущих только TLS ClientHello.",
                RecommendedFor = new() { "youtube" },
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
                Name = "tls_fake_sni",
                Description = "TLS c подменой SNI на www.google.com. Лёгкий, для слабых провайдеров.",
                RecommendedFor = new() { "youtube", "discord" },
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

    public static Strategy? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return All.FirstOrDefault(s => s.Id == id);
    }
}
