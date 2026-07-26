using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Читает стратегию из файла config (или config.default) чистого zapret2.
///
/// В самом zapret2 нет .cmd-пресетов — это репозиторий под Linux, и стратегия там
/// задаётся переменной NFQWS2_OPT. Мы её вытаскиваем и адаптируем под winws2:
/// добавляем --wf-tcp-out / --wf-udp-out (в Linux фильтрацию делает firewall,
/// под Windows это задаётся аргументами) и подключаем lua-библиотеки, как это
/// делают init.d-скрипты zapret2.
/// </summary>
public static class ZapretConfigLoader
{
    public sealed class ConfigLoadResult
    {
        public Strategy? Strategy { get; init; }
        public string? SourceFile { get; init; }
        public string? Error { get; init; }
    }

    public static ConfigLoadResult Load(string? zapretPath)
    {
        if (string.IsNullOrWhiteSpace(zapretPath) || !Directory.Exists(zapretPath))
            return new ConfigLoadResult { Error = "папка zapret не указана" };

        // config — правленый пользователем, config.default — эталон из релиза.
        var file = FirstExisting(zapretPath, "config", "config.default");
        if (file is null)
            return new ConfigLoadResult { Error = "config и config.default не найдены" };

        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            return new ConfigLoadResult { Error = "не удалось прочитать config: " + ex.Message };
        }

        var opt = ReadVariable(text, "NFQWS2_OPT");
        if (string.IsNullOrWhiteSpace(opt))
            return new ConfigLoadResult
            {
                SourceFile = file,
                Error = "в config не найдена переменная NFQWS2_OPT",
            };

        var tcpPorts = ReadVariable(text, "NFQWS2_PORTS_TCP") ?? "80,443";
        var udpPorts = ReadVariable(text, "NFQWS2_PORTS_UDP") ?? "443";

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(tcpPorts)) args.Add("--wf-tcp-out=" + tcpPorts.Trim());
        if (!string.IsNullOrWhiteSpace(udpPorts)) args.Add("--wf-udp-out=" + udpPorts.Trim());
        args.Add("--lua-init=@<lua/zapret-lib.lua>");
        args.Add("--lua-init=@<lua/zapret-antidpi.lua>");

        var skippedHostlist = false;
        foreach (var token in Tokenize(opt))
        {
            // <HOSTLIST> и <HOSTLIST_NOAUTO> — плейсхолдеры init.d-скриптов zapret2.
            // Они раскрываются в --hostlist=... только при MODE_FILTER=hostlist.
            // winws2 таких скобок не понимает и падает, поэтому выбрасываем.
            if (token is "<HOSTLIST>" or "<HOSTLIST_NOAUTO>")
            {
                skippedHostlist = true;
                continue;
            }

            args.Add(token);
        }

        var name = Path.GetFileName(file);
        var description = "Стратегия из " + name + " (переменная NFQWS2_OPT).";
        if (skippedHostlist)
            description += " Фильтр по хостлистам отключён: обход применяется ко всем сайтам.";

        return new ConfigLoadResult
        {
            SourceFile = file,
            Strategy = new Strategy
            {
                Id = "config:" + name,
                Name = "NFQWS2_OPT из " + name,
                Description = description,
                RecommendedFor = new List<string> { "TCP/TLS", "UDP/QUIC" },
                Args = args,
            },
        };
    }

    private static string? FirstExisting(string dir, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>
    /// Достаёт значение переменной вида NAME="..." (в том числе многострочное) или NAME=value.
    /// Строки-комментарии (#) игнорируются, чтобы не поймать закомментированный пример.
    /// </summary>
    private static string? ReadVariable(string text, string name)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
            if (!trimmed.StartsWith(name + "=", StringComparison.Ordinal)) continue;

            var value = trimmed.Substring(name.Length + 1);

            if (!value.StartsWith("\"", StringComparison.Ordinal))
                return value.Trim();

            // Значение в кавычках: возможно многострочное.
            value = value.Substring(1);
            var closing = value.IndexOf('"');
            if (closing >= 0) return value.Substring(0, closing);

            var sb = new StringBuilder(value);
            for (var j = i + 1; j < lines.Length; j++)
            {
                var next = lines[j];
                var end = next.IndexOf('"');
                if (end >= 0)
                {
                    sb.Append(' ').Append(next.Substring(0, end));
                    return sb.ToString();
                }

                sb.Append(' ').Append(next);
            }

            return sb.ToString();
        }

        return null;
    }

    /// <summary>
    /// Режет строку аргументов по пробелам с учётом кавычек. Сами кавычки убираются:
    /// аргументы уходят в ArgumentList, где экранирование делает .NET.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var c in text)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) result.Add(current.ToString());

        return result;
    }
}
