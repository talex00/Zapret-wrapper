using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZapretWrapper.Models;

namespace ZapretWrapper.Services;

/// <summary>
/// Собирает стратегии из самой папки zapret2 / zapret-win-bundle.
/// Фиксированный список в коде быстро устаревал и не совпадал с тем, что реально
/// лежит у пользователя: пресеты в zapret раздаются .cmd-файлами, их и читаем.
///
/// Разбор cmd сознательно минимальный, но покрывает то, что встречается в пресетах zapret:
///   set WINWS="%~dp0binaries\windows-x86_64\winws2.exe"
///   %WINWS% --wf-tcp-out=80,443 ^
///           --lua-init=@"%~dp0lua\zapret-lib.lua" ^
///           ...
/// </summary>
public static class StrategyLoader
{
    /// <summary>Папки, в которых пресетов точно нет — не тратим на них время.</summary>
    private static readonly string[] SkipDirs =
        { "binaries", "lua", "files", "init.d", ".git", "tmp", "logs" };

    public sealed class LoadResult
    {
        public List<Strategy> Strategies { get; } = new();
        public List<string> Skipped { get; } = new();
        public string? Error { get; set; }
    }

    public static LoadResult Load(string? zapretPath)
    {
        var result = new LoadResult();

        if (string.IsNullOrWhiteSpace(zapretPath))
        {
            result.Error = "Путь к zapret не указан.";
            return result;
        }

        if (!Directory.Exists(zapretPath))
        {
            result.Error = "Папка не найдена: " + zapretPath;
            return result;
        }

        try
        {
            foreach (var file in EnumeratePresetFiles(zapretPath))
            {
                try
                {
                    var strategy = Parse(file, zapretPath);
                    if (strategy is not null) result.Strategies.Add(strategy);
                    else result.Skipped.Add(Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    LogService.Debug($"Пресет {Path.GetFileName(file)} не разобран: {ex.Message}");
                    result.Skipped.Add(Path.GetFileName(file));
                }
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        // Стабильный порядок: сначала пресеты из корня, потом алфавитно.
        result.Strategies.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static IEnumerable<string> EnumeratePresetFiles(string root)
    {
        var files = new List<string>();

        files.AddRange(Directory.EnumerateFiles(root, "*.cmd", SearchOption.TopDirectoryOnly));
        files.AddRange(Directory.EnumerateFiles(root, "*.bat", SearchOption.TopDirectoryOnly));

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

            files.AddRange(Directory.EnumerateFiles(dir, "*.cmd", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.EnumerateFiles(dir, "*.bat", SearchOption.TopDirectoryOnly));
        }

        return files;
    }

    private static Strategy? Parse(string file, string zapretPath)
    {
        var raw = File.ReadAllText(file);
        if (raw.IndexOf("winws", StringComparison.OrdinalIgnoreCase) < 0) return null;

        var lines = JoinContinuations(raw);
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? description = null;
        List<string>? args = null;

        foreach (var original in lines)
        {
            var line = original.Trim();
            if (line.Length == 0) continue;

            if (IsComment(line))
            {
                description ??= CommentText(line);
                continue;
            }

            if (TryReadSet(line, out var key, out var value))
            {
                vars[key] = Expand(value, vars, zapretPath);
                continue;
            }

            var expanded = Expand(line, vars, zapretPath);
            if (expanded.IndexOf("winws", StringComparison.OrdinalIgnoreCase) < 0) continue;

            var parsed = ExtractArgs(expanded);
            // В пресете может быть несколько вызовов (например, проверка версии) —
            // берём самый содержательный.
            if (parsed.Count > (args?.Count ?? 0)) args = parsed;
        }

        if (args is null || args.Count == 0) return null;

        var name = Path.GetFileNameWithoutExtension(file);
        var relative = Path.GetRelativePath(zapretPath, file);

        return new Strategy
        {
            Id = "file:" + relative.Replace('\\', '/'),
            Name = name,
            Description = string.IsNullOrWhiteSpace(description)
                ? $"Пресет из {relative} — {args.Count} аргументов winws2."
                : description!.Trim(),
            RecommendedFor = GuessTags(args),
            Args = args,
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

    private static bool IsComment(string line) =>
        line.StartsWith("::", StringComparison.Ordinal)
        || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)
        || line.Equals("rem", StringComparison.OrdinalIgnoreCase);

    private static string CommentText(string line)
    {
        var text = line.StartsWith("::", StringComparison.Ordinal)
            ? line[2..]
            : line[3..];
        return text.Trim();
    }

    /// <summary>Понимает и set VAR=value, и set "VAR=value".</summary>
    private static bool TryReadSet(string line, out string key, out string value)
    {
        key = "";
        value = "";

        if (!line.StartsWith("set", StringComparison.OrdinalIgnoreCase)) return false;
        if (line.Length < 5 || !char.IsWhiteSpace(line[3])) return false;

        var body = line[4..].Trim();
        if (body.StartsWith("/a", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("/p", StringComparison.OrdinalIgnoreCase))
            body = body[2..].Trim();

        if (body.Length > 1 && body[0] == '"' && body[^1] == '"') body = body[1..^1];

        var eq = body.IndexOf('=');
        if (eq <= 0) return false;

        key = body[..eq].Trim();
        value = body[(eq + 1)..].Trim();
        return key.Length > 0;
    }

    /// <summary>Раскрывает %VAR% и %~dp0 (папка самого пресета = папка zapret).</summary>
    private static string Expand(string text, Dictionary<string, string> vars, string zapretPath)
    {
        var root = zapretPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;

        text = text.Replace("%~dp0", root, StringComparison.OrdinalIgnoreCase);

        // Глубина ограничена: защита от самоссылочных переменных.
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

    /// <summary>Берёт всё, что идёт после winws в строке запуска, и режет на аргументы.</summary>
    private static List<string> ExtractArgs(string line)
    {
        var tokens = Tokenize(line);
        var args = new List<string>();
        var started = false;

        foreach (var token in tokens)
        {
            if (!started)
            {
                if (token.IndexOf("winws", StringComparison.OrdinalIgnoreCase) >= 0) started = true;
                continue;
            }

            // cmd-шум: перенаправления и связки команд в аргументы winws2 попасть не должны.
            if (token.StartsWith(">", StringComparison.Ordinal)
                || token.StartsWith("<", StringComparison.Ordinal)
                || token.Contains(">&", StringComparison.Ordinal)
                || token is "&" or "&&" or "|" or "||")
                break;

            if (token.Length > 0) args.Add(token);
        }

        return args;
    }

    /// <summary>
    /// Режет строку на токены по cmd-правилам: кавычки только группируют и в значение
    /// не попадают. Это важно: winws2 запускается через ArgumentList, где лишняя
    /// кавычка уехала бы в процесс частью значения и процесс упал бы сразу.
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

    /// <summary>По аргументам понятно, что пресет вообще лечит — показываем это метками.</summary>
    private static List<string> GuessTags(List<string> args)
    {
        var joined = string.Join(" ", args).ToLowerInvariant();
        var tags = new List<string>();

        if (joined.Contains("youtube") || joined.Contains("googlevideo")) tags.Add("youtube");
        if (joined.Contains("discord")) tags.Add("discord");
        if (joined.Contains("instagram")) tags.Add("instagram");
        if (joined.Contains("twitch")) tags.Add("twitch");

        if (joined.Contains("quic") || joined.Contains("udp")) tags.Add("UDP/QUIC");
        if (joined.Contains("tcp") || joined.Contains("tls")) tags.Add("TCP/TLS");

        return tags;
    }
}
