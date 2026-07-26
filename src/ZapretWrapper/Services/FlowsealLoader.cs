using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Загружает стратегии из сборки Flowseal/zapret-discord-youtube.
///
/// Там каждая стратегия — отдельный general*.bat в корне с одной длинной
/// строкой запуска вида:
///
///   start "zapret: %~n0" /min "%BIN%winws.exe" --wf-tcp=80,443,... ^
///   --filter-udp=443 --hostlist="%LISTS%list-general.txt" --dpi-desync=fake ... --new ^
///   ...
///
/// Сам файл запускать нельзя: в начале он вызывает service.bat с проверкой
/// обновлений и может ждать ответа в консоли. Задача — вытащить ровно
/// аргументы winws.exe и поднять процесс самим, тихо и без консолей.
/// </summary>
public static class FlowsealLoader
{
    /// <summary>Диапазоны игрового фильтра (всё выше 1023), как в service.bat.</summary>
    private const string GameFilterTcp = "1024-65535";
    private const string GameFilterUdp = "1024-65535";

    /// <summary>
    /// Корень сборки относительно рабочей папки процесса.
    ///
    /// winws.exe собран под cygwin и декодирует командную строку в своей локали, а не
    /// в UTF-8. Абсолютный путь вроде C:\Users\...\Документы\zapret\lists\... приезжал
    /// к нему искажённым, файлы не открывались (cannot access ipset file) и процесс умирал.
    ///
    /// Процесс всегда стартует из bin (там cygwin1.dll), поэтому корень сборки — это «..\»,
    /// и все пути в аргументах остаются чисто латинскими: ..\lists\list-general.txt.
    /// </summary>
    private const string RootPrefix = "..\\";

    /// <summary>Служебные .bat, в которых стратегий нет.</summary>
    private static readonly string[] SkipFiles = { "service.bat" };

    public sealed class LoadResult
    {
        public List<Strategy> Strategies { get; } = new();
        public List<string> Skipped { get; } = new();
        public string? Error { get; set; }
    }

    /// <param name="gameFilter">Обход для портов выше 1023 (игры). По умолчанию выключен.</param>
    public static LoadResult Load(string? root, bool gameFilter = false)
    {
        var result = new LoadResult();

        if (string.IsNullOrWhiteSpace(root))
        {
            result.Error = "Путь к папке zapret не указан.";
            return result;
        }

        if (!Directory.Exists(root))
        {
            result.Error = "Папка не найдена: " + root;
            return result;
        }

        try
        {
            foreach (var file in Directory
                         .EnumerateFiles(root, "*.bat", SearchOption.TopDirectoryOnly)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                if (SkipFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                try
                {
                    var strategy = Parse(file, gameFilter);
                    if (strategy is not null) result.Strategies.Add(strategy);
                    else result.Skipped.Add(name);
                }
                catch (Exception ex)
                {
                    LogService.Debug($"Стратегия {name} не разобрана: {ex.Message}");
                    result.Skipped.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    private static Strategy? Parse(string file, bool gameFilter)
    {
        var raw = File.ReadAllText(file);
        if (raw.IndexOf("winws", StringComparison.OrdinalIgnoreCase) < 0) return null;

        var fileName = Path.GetFileNameWithoutExtension(file);
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<string>? args = null;

        foreach (var original in JoinContinuations(raw))
        {
            var line = original.Trim();
            if (line.Length == 0) continue;

            // Вызовы service.bat и прочий cmd-шум нас не интересуют.
            if (line.StartsWith("call", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("::", StringComparison.Ordinal)) continue;
            if (line.StartsWith("rem", StringComparison.OrdinalIgnoreCase)) continue;

            if (TryReadSet(line, out var key, out var value))
            {
                vars[key] = Expand(value, vars, fileName, gameFilter);
                continue;
            }

            var expanded = Expand(line, vars, fileName, gameFilter);
            if (expanded.IndexOf("winws", StringComparison.OrdinalIgnoreCase) < 0) continue;

            var parsed = ExtractArgs(expanded);
            if (parsed.Count > (args?.Count ?? 0)) args = parsed;
        }

        if (args is null || args.Count == 0) return null;

        var normalized = Normalize(args);
        if (normalized.Count == 0) return null;

        return new Strategy
        {
            Id = "flowseal:" + Path.GetFileName(file),
            Name = fileName,
            Description = Describe(normalized),
            RecommendedFor = GuessTags(normalized),
            Args = normalized,
        };
    }

    /// <summary>Склеивает строки, разорванные символом продолжения ^ в конце.</summary>
    private static List<string> JoinContinuations(string raw)
    {
        var result = new List<string>();
        var current = new StringBuilder();

        foreach (var rawLine in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.EndsWith("^", StringComparison.Ordinal))
            {
                current.Append(line, 0, line.Length - 1).Append(' ');
                continue;
            }

            current.Append(line);
            result.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>Понимает и set VAR=value, и set "VAR=value".</summary>
    private static bool TryReadSet(string line, out string key, out string value)
    {
        key = "";
        value = "";

        if (!line.StartsWith("set", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.Length < 5 || !char.IsWhiteSpace(line[3])) return false;

        var body = line[4..].Trim();
        if (body.Length > 1 && body[0] == '"' && body[^1] == '"') body = body[1..^1];

        var eq = body.IndexOf('=');
        if (eq <= 0) return false;

        key = body[..eq].Trim();
        value = body[(eq + 1)..].Trim();
        return key.Length > 0;
    }

    /// <summary>
    /// Раскрывает %~dp0 (корень сборки — как относительный путь), %~n0 (имя файла),
    /// переменные set и игровой фильтр. GameFilter* в самом репозитории заполняет
    /// service.bat, у нас его нет — подставляем сами.
    /// </summary>
    private static string Expand(
        string text, Dictionary<string, string> vars, string fileName, bool gameFilter)
    {
        text = text.Replace("%~dp0", RootPrefix, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("%~nx0", fileName + ".bat", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("%~n0", fileName, StringComparison.OrdinalIgnoreCase);

        text = text.Replace("%GameFilterTCP%", gameFilter ? GameFilterTcp : "", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("%GameFilterUDP%", gameFilter ? GameFilterUdp : "", StringComparison.OrdinalIgnoreCase);

        for (int pass = 0; pass < 5 && text.Contains('%'); pass++)
        {
            var replaced = false;
            foreach (var pair in vars)
            {
                var token = "%" + pair.Key + "%";
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Replace(token, pair.Value, StringComparison.OrdinalIgnoreCase);
                    replaced = true;
                }
            }

            if (!replaced) break;
        }

        return text;
    }

    /// <summary>Берёт всё, что идёт после winws.exe в строке запуска.</summary>
    private static List<string> ExtractArgs(string line)
    {
        var args = new List<string>();
        var started = false;

        foreach (var token in Tokenize(line))
        {
            if (!started)
            {
                if (token.IndexOf("winws", StringComparison.OrdinalIgnoreCase) >= 0) started = true;
                continue;
            }

            if (token.StartsWith(">", StringComparison.Ordinal)
                || token.Contains(">&", StringComparison.Ordinal)
                || token is "&" or "&&" or "|" or "||")
                break;

            if (token.Length > 0) args.Add(token);
        }

        return args;
    }

    /// <summary>
    /// Режет строку на токены по cmd-правилам: кавычки только группируют и в
    /// значение не попадают. Важно: аргументы уезжают в ArgumentList, где лишняя
    /// кавычка стала бы частью пути к .bin-файлу и winws не нашёл бы фейк.
    /// </summary>
    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>
    /// Приводит аргументы в вид, который winws точно прожуёт:
    /// 1) выбрасывает пустые элементы списков портов (висячая запятая остаётся
    ///    после подстановки пустого игрового фильтра);
    /// 2) целиком убирает профили (участки между --new), у которых фильтр стал
    ///    пустым: в general*.bat последние два профиля существуют только для игрового
    ///    фильтра, и без него превратились бы в --filter-tcp= — ошибка разбора.
    /// </summary>
    private static List<string> Normalize(List<string> args)
    {
        var cleaned = args.Select(CleanList).ToList();

        var profiles = new List<List<string>>();
        var current = new List<string>();

        foreach (var arg in cleaned)
        {
            if (string.Equals(arg, "--new", StringComparison.OrdinalIgnoreCase))
            {
                profiles.Add(current);
                current = new List<string>();
                continue;
            }

            current.Add(arg);
        }

        profiles.Add(current);

        var kept = new List<List<string>>();
        foreach (var profile in profiles)
        {
            if (profile.Count == 0) continue;

            var dropProfile = profile.Any(a =>
                a.StartsWith("--filter-", StringComparison.OrdinalIgnoreCase) && IsEmptyValue(a));

            if (dropProfile) continue;

            // Остальные пустые значения — просто лишние аргументы.
            var body = profile.Where(a => !IsEmptyValue(a)).ToList();
            if (body.Count > 0) kept.Add(body);
        }

        var result = new List<string>();
        for (int i = 0; i < kept.Count; i++)
        {
            if (i > 0) result.Add("--new");
            result.AddRange(kept[i]);
        }

        return result;
    }

    private static bool IsEmptyValue(string arg)
    {
        var eq = arg.IndexOf('=');
        return eq > 0 && eq == arg.Length - 1;
    }

    /// <summary>Убирает пустые элементы в списках вида --wf-tcp=80,443,,2053.</summary>
    private static string CleanList(string arg)
    {
        var eq = arg.IndexOf('=');
        if (eq <= 0 || eq == arg.Length - 1) return arg;

        var key = arg[..eq];
        var value = arg[(eq + 1)..];
        if (!value.Contains(',')) return arg;

        var parts = value
            .Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        return key + "=" + string.Join(",", parts);
    }

    private static string Describe(List<string> args)
    {
        var methods = args
            .Where(a => a.StartsWith("--dpi-desync=", StringComparison.OrdinalIgnoreCase))
            .Select(a => a["--dpi-desync=".Length..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profiles = args.Count(a => string.Equals(a, "--new", StringComparison.OrdinalIgnoreCase)) + 1;

        var text = $"Стратегия из zapret-discord-youtube: профилей {profiles}";
        if (methods.Count > 0) text += ", методы: " + string.Join(", ", methods);
        return text + ".";
    }

    private static List<string> GuessTags(List<string> args)
    {
        var joined = string.Join(" ", args).ToLowerInvariant();
        var tags = new List<string>();

        if (joined.Contains("list-google") || joined.Contains("youtube")) tags.Add("youtube");
        if (joined.Contains("discord")) tags.Add("discord");
        if (joined.Contains("stun")) tags.Add("discord голос");
        if (joined.Contains("--filter-udp") || joined.Contains("quic")) tags.Add("UDP/QUIC");
        if (joined.Contains("--filter-tcp") || joined.Contains("tls")) tags.Add("TCP/TLS");

        return tags;
    }
}
