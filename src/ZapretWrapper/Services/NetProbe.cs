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
/// TLS-хендшейк с валидацией сертификата, реальный код ответа HTTPS и проход UDP.
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
