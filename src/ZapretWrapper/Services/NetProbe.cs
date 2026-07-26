using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretWrapper.Services;

public enum ProbeKind
{
    /// <summary>Полноценный TLS-хендшейк с проверкой сертификата.</summary>
    Tls,

    /// <summary>HTTPS-запрос с проверкой кода ответа.</summary>
    Http,

    /// <summary>STUN Binding Request по UDP — проверка, что UDP/RTP реально ходит.</summary>
    UdpStun,

    /// <summary>HTTP-запрос без TLS на порт 80: только так проверяются методы для plain HTTP.</summary>
    Http80,

    /// <summary>QUIC/HTTP3 на UDP 443 — без этого нечем измерять QUIC-методы.</summary>
    Quic,
}

/// <param name="Critical">Учитывается ли проба в итоговой оценке стратегии.</param>
public sealed record ProbeSpec(
    string Title,
    ProbeKind Kind,
    string Host,
    int Port = 443,
    string? Url = null,
    int MaxStatus = 400,
    bool Critical = true);

public sealed record ProbeResult(ProbeSpec Spec, bool Ok, double Ms, string? Error);

/// <summary>
/// Проверка доступности ресурса. Раньше это был просто HTTP GET, который слишком легко
/// «проходил»: редирект на заглушку провайдера, ответ из кэша или 4xx считались успехом.
/// Теперь каждая проба открывает новое соединение и проверяет именно то, что ломает DPI:
/// TLS-хендшейк с валидацией сертификата, реальный код ответа HTTPS, проход UDP,
/// ответ QUIC на UDP 443 и запрос plain HTTP на порт 80.
/// </summary>
public static class NetProbe
{
    public static async Task<ProbeResult> RunAsync(ProbeSpec spec, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            return spec.Kind switch
            {
                ProbeKind.Http => await HttpAsync(spec, timeoutSeconds, ct),
                ProbeKind.UdpStun => await StunAsync(spec, timeoutSeconds, ct),
                ProbeKind.Http80 => await Http80Async(spec, timeoutSeconds, ct),
                ProbeKind.Quic => await QuicAsync(spec, timeoutSeconds, ct),
                _ => await TlsAsync(spec, timeoutSeconds, ct),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProbeResult(spec, false, 0, Describe(ex));
        }
    }

    private static async Task<ProbeResult> TlsAsync(ProbeSpec spec, int timeoutSeconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(spec.Host, spec.Port, timeout.Token);

            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                // SNI — именно то поле, по которому DPI и режет соединение.
                TargetHost = spec.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, timeout.Token);

            sw.Stop();
            return new ProbeResult(spec, true, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeResult(spec, false, sw.Elapsed.TotalMilliseconds, "таймаут TLS-хендшейка");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult(spec, false, sw.Elapsed.TotalMilliseconds, Describe(ex));
        }
    }

    private static async Task<ProbeResult> HttpAsync(ProbeSpec spec, int timeoutSeconds, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            AllowAutoRedirect = true,
            // Обязательно: без этого следующая стратегия переиспользовала бы TCP-соединение,
            // открытое при предыдущей, и результат теста был бы ложноположительным.
            PooledConnectionLifetime = TimeSpan.Zero,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        var url = spec.Url ?? ("https://" + spec.Host + "/");
        var sw = Stopwatch.StartNew();

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        sw.Stop();

        var code = (int)response.StatusCode;
        var ok = code < spec.MaxStatus;
        return new ProbeResult(spec, ok, sw.Elapsed.TotalMilliseconds, ok ? null : "HTTP " + code);
    }

    /// <summary>
    /// Запрос по обычному HTTP на порт 80. Редирект на https не разворачиваем: сам факт
    /// полученного ответа (в том числе 301/302) означает, что запрос прошёл DPI.
    /// Именно этой пробой проверяются методы вида --filter-l7=http.
    /// </summary>
    private static async Task<ProbeResult> Http80Async(ProbeSpec spec, int timeoutSeconds, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.Zero,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        var port = spec.Port <= 0 ? 80 : spec.Port;
        var url = spec.Url ?? (port == 80
            ? "http://" + spec.Host + "/"
            : "http://" + spec.Host + ":" + port + "/");

        var sw = Stopwatch.StartNew();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        sw.Stop();

        var code = (int)response.StatusCode;
        var ok = code < spec.MaxStatus;
        return new ProbeResult(spec, ok, sw.Elapsed.TotalMilliseconds, ok ? null : "HTTP " + code);
    }

    /// <summary>
    /// STUN Binding Request (RFC 5389). Если ответ не приходит — UDP до внешних узлов не ходит,
    /// а значит голосовой чат Discord работать не будет, даже если текстовый доступен.
    /// </summary>
    private static async Task<ProbeResult> StunAsync(ProbeSpec spec, int timeoutSeconds, CancellationToken ct)
    {
        var request = new byte[20];
        request[0] = 0x00; request[1] = 0x01;  // Binding Request
        request[2] = 0x00; request[3] = 0x00;  // Message Length = 0
        request[4] = 0x21; request[5] = 0x12; request[6] = 0xA4; request[7] = 0x42;  // Magic Cookie
        var txId = RandomNumberGenerator.GetBytes(12);
        Buffer.BlockCopy(txId, 0, request, 8, 12);

        using var udp = new UdpClient();
        var sw = Stopwatch.StartNew();
        await udp.SendAsync(request, spec.Host, spec.Port, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(timeout.Token);
                var data = received.Buffer;
                if (data.Length < 20) continue;
                if (data[0] != 0x01 || data[1] != 0x01) continue;  // ждём Binding Success Response

                var sameTransaction = true;
                for (int i = 0; i < 12; i++)
                {
                    if (data[8 + i] != txId[i]) { sameTransaction = false; break; }
                }
                if (!sameTransaction) continue;

                sw.Stop();
                return new ProbeResult(spec, true, sw.Elapsed.TotalMilliseconds, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeResult(spec, false, sw.Elapsed.TotalMilliseconds, "UDP не проходит (нет ответа STUN)");
        }
    }

    /// <summary>
    /// QUIC на UDP 443 без криптографии: отправляем пакет с длинным заголовком и
    /// зарезервированной версией 0x0A0A0A0A. По RFC 9000 сервер обязан ответить
    /// Version Negotiation, поэтому ответ доказывает, что QUIC Initial дошёл до сервера,
    /// а его отсутствие — что DPI режет UDP 443 (браузер в этом случае молча падает на TCP).
    /// </summary>
    private static async Task<ProbeResult> QuicAsync(ProbeSpec spec, int timeoutSeconds, CancellationToken ct)
    {
        var dcid = RandomNumberGenerator.GetBytes(8);
        var scid = RandomNumberGenerator.GetBytes(8);

        // Размер 1200 байт обязателен: пакеты меньше минимального размера Initial отбрасываются.
        var packet = new byte[1200];
        packet[0] = 0xC3;                                                        // long header, Initial
        packet[1] = 0x0A; packet[2] = 0x0A; packet[3] = 0x0A; packet[4] = 0x0A;  // зарезервированная версия
        packet[5] = 8; Buffer.BlockCopy(dcid, 0, packet, 6, 8);
        packet[14] = 8; Buffer.BlockCopy(scid, 0, packet, 15, 8);
        // Дальше нули: пустой token и паддинг до 1200 байт.

        var port = spec.Port <= 0 ? 443 : spec.Port;

        using var udp = new UdpClient();
        var sw = Stopwatch.StartNew();
        await udp.SendAsync(packet, spec.Host, port, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            while (true)
            {
                var received = await udp.ReceiveAsync(timeout.Token);
                var data = received.Buffer;
                if (data.Length < 5) continue;
                if ((data[0] & 0x80) == 0) continue;  // ждём пакет с длинным заголовком

                sw.Stop();
                return new ProbeResult(spec, true, sw.Elapsed.TotalMilliseconds, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeResult(spec, false, sw.Elapsed.TotalMilliseconds, "нет ответа QUIC (UDP 443 не проходит)");
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        SocketException { SocketErrorCode: SocketError.ConnectionReset } =>
            "соединение сброшено (RST — типичный признак DPI)",
        SocketException { SocketErrorCode: SocketError.HostNotFound } =>
            "домен не резолвится (возможна подмена DNS)",
        SocketException { SocketErrorCode: SocketError.TimedOut } => "таймаут соединения",
        SocketException se => "сеть: " + se.SocketErrorCode,
        AuthenticationException => "TLS-хендшейк отклонён (обрыв или подмена сертификата)",
        TaskCanceledException => "таймаут",
        HttpRequestException hre => "HTTP: " + (hre.InnerException?.Message ?? hre.Message),
        _ => ex.Message,
    };
}
