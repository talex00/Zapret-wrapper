using System;
using System.Collections.Generic;
using System.Linq;

namespace ZapretWrapper.Services;

/// <summary>Проверяемый ресурс и набор проб, которые доказывают, что он реально работает.</summary>
public sealed class CheckTarget
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<ProbeSpec> Probes { get; init; } = Array.Empty<ProbeSpec>();
}

public static class TargetCatalog
{
    public static IReadOnlyList<CheckTarget> All { get; } = new List<CheckTarget>
    {
        new()
        {
            Id = "youtube",
            Name = "YouTube",
            Probes = new List<ProbeSpec>
            {
                new("YouTube — сайт (TLS)", ProbeKind.Tls, "www.youtube.com"),
                new("YouTube — API (HTTPS 204)", ProbeKind.Http, "www.youtube.com", 443, "https://www.youtube.com/generate_204"),
                // Без этого хоста страница откроется, а видео крутиться не будет.
                new("YouTube — видео-CDN (TLS)", ProbeKind.Tls, "redirector.googlevideo.com"),
            },
        },
        new()
        {
            Id = "discord",
            Name = "Discord",
            Probes = new List<ProbeSpec>
            {
                new("Discord — текст/API (HTTPS)", ProbeKind.Http, "discord.com", 443, "https://discord.com/api/v9/gateway"),
                // Голосовой чат = сигналинг по WSS +媒 медиапоток по UDP. Проверяем оба.
                new("Discord — голос: сигналинг (TLS)", ProbeKind.Tls, "gateway.discord.gg"),
                new("Discord — голос: UDP/RTP (STUN)", ProbeKind.UdpStun, "stun.l.google.com", 19302),
                new("Discord — картинки/CDN (TLS)", ProbeKind.Tls, "cdn.discordapp.com", 443, null, 400, false),
            },
        },
        new()
        {
            Id = "instagram",
            Name = "Instagram",
            Probes = new List<ProbeSpec>
            {
                new("Instagram — сайт (TLS)", ProbeKind.Tls, "www.instagram.com"),
                new("Instagram — CDN (TLS)", ProbeKind.Tls, "scontent.cdninstagram.com", 443, null, 400, false),
            },
        },
        new()
        {
            Id = "twitch",
            Name = "Twitch",
            Probes = new List<ProbeSpec>
            {
                new("Twitch — сайт (TLS)", ProbeKind.Tls, "www.twitch.tv"),
                new("Twitch — видеопоток (TLS)", ProbeKind.Tls, "usher.ttvnw.net"),
            },
        },
    };

    public static CheckTarget? FindById(string? id) =>
        string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(t => t.Id == id);

    /// <summary>Превращает список доменов из настроек в набор целей с осмысленными пробами.</summary>
    public static IReadOnlyList<CheckTarget> Resolve(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return All;

        var list = new List<CheckTarget>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var host = raw
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/');
            if (host.Length == 0) continue;

            var known = All.FirstOrDefault(t => host.Contains(t.Id, StringComparison.OrdinalIgnoreCase));
            var target = known ?? Generic(host);
            if (list.All(t => t.Id != target.Id)) list.Add(target);
        }

        return list.Count > 0 ? list : All;
    }

    private static CheckTarget Generic(string host) => new()
    {
        Id = host,
        Name = host,
        Probes = new List<ProbeSpec>
        {
            new(host + " — TLS", ProbeKind.Tls, host),
            new(host + " — HTTPS", ProbeKind.Http, host),
        },
    };
}
