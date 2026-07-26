using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZapretWrapper.Services;

/// <summary>
/// Работа с хостлистами сборки Flowseal (папка lists/).
///
/// Важный момент, без которого тестирование врёт: каждый профиль в general*.bat
/// ограничен --hostlist="lists\list-general.txt" и т.п. Домен, которого в списках нет,
/// winws просто не трогает — и любая стратегия выглядит как нерабочая.
///
/// Пишем только в *-user.txt: именно они предназначены для своих доменов и не
/// перезаписываются при обновлении сборки.
/// </summary>
public static class HostlistService
{
    private const string UserListName = "list-general-user.txt";

    /// <summary>
    /// Файлы, которые в оригинальной сборке создаёт service.bat (load_user_lists)
    /// перед запуском winws. Аргументы в general*.bat ссылаются на них всегда,
    /// а winws при отсутствии файла ipset не предупреждает, а сразу выходит.
    /// </summary>
    private static readonly string[] UserFiles =
    {
        "list-general-user.txt",
        "list-exclude-user.txt",
        "ipset-exclude-user.txt",
    };

    public sealed class EnsureResult
    {
        /// <summary>Домены, которые мы только что дописали в пользовательский список.</summary>
        public List<string> Added { get; } = new();

        /// <summary>Домены, попавшие в списки исключений — обход к ним не применяется.</summary>
        public List<string> Excluded { get; } = new();

        public string? File { get; set; }
        public string? Error { get; set; }

        public bool Changed => Added.Count > 0;
    }

    public static string ListsDir(string root) => Path.Combine(root, "lists");

    public static string UserListPath(string root) => Path.Combine(ListsDir(root), UserListName);

    /// <summary>
    /// Создаёт отсутствующие пользовательские списки (пустыми). Вызывается перед
    /// каждым запуском winws: файлы мог удалить пользователь или их не было
    /// вовсе, если сборку ещё ни разу не запускали через service.bat.
    /// Для zapret2 ничего не делает.
    /// </summary>
    public static void EnsureUserFiles(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        if (ZapretBackend.Detect(root) != ZapretFlavor.Flowseal) return;

        var listsDir = ListsDir(root!);
        if (!Directory.Exists(listsDir)) return;

        foreach (var name in UserFiles)
        {
            var path = Path.Combine(listsDir, name);
            if (File.Exists(path)) continue;

            try
            {
                // Именно пустой файл: в ipset-списках комментарии не гарантированно
                // разбираются, а пустой список winws принимает нормально.
                File.WriteAllText(path, string.Empty);
                LogService.Info($"Создан пустой пользовательский список {name}.");
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось создать {name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Дописывает в list-general-user.txt те из хостов, которые не покрыты
    /// существующими списками. Для zapret2 ничего не делает.
    /// </summary>
    public static EnsureResult EnsureCovered(string? root, IEnumerable<string> hosts)
    {
        var result = new EnsureResult();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            result.Error = "Папка zapret недоступна.";
            return result;
        }

        if (ZapretBackend.Detect(root) != ZapretFlavor.Flowseal) return result;

        var listsDir = ListsDir(root!);
        if (!Directory.Exists(listsDir))
        {
            result.Error = "Не найдена папка lists/.";
            return result;
        }

        EnsureUserFiles(root);

        try
        {
            var included = ReadEntries(listsDir, includeLists: true);
            var excluded = ReadEntries(listsDir, includeLists: false);

            var wanted = hosts
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => Normalize(h))
                .Where(h => h.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var missing = new List<string>();
            foreach (var host in wanted)
            {
                if (IsCovered(host, excluded)) result.Excluded.Add(host);
                if (!IsCovered(host, included)) missing.Add(host);
            }

            var file = UserListPath(root!);
            result.File = file;

            if (missing.Count == 0) return result;

            var text = new List<string>();
            var existing = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            if (existing.Trim().Length == 0)
            {
                text.Add("# Домены, добавленные Zapret Wrapper для тестирования стратегий.");
            }
            else if (!existing.EndsWith("\n", StringComparison.Ordinal))
            {
                text.Add(string.Empty);
            }

            text.AddRange(missing);

            File.AppendAllLines(file, text);
            result.Added.AddRange(missing);

            LogService.Info($"В {UserListName} добавлены домены: {string.Join(", ", missing)}");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            LogService.Warn($"Не удалось обновить {UserListName}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Собирает записи из списков включения (list-general*, list-google*) либо
    /// из списков исключения (list-exclude*).
    /// </summary>
    private static List<string> ReadEntries(string listsDir, bool includeLists)
    {
        var entries = new List<string>();

        foreach (var file in Directory.EnumerateFiles(listsDir, "list-*.txt", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var isExclude = name.StartsWith("list-exclude", StringComparison.OrdinalIgnoreCase);
            if (isExclude == includeLists) continue;

            foreach (var line in File.ReadLines(file))
            {
                var entry = Normalize(line);
                if (entry.Length == 0 || entry.StartsWith("#", StringComparison.Ordinal)) continue;
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>В хостлистах zapret запись покрывает сам домен и все его поддомены.</summary>
    private static bool IsCovered(string host, List<string> entries) =>
        entries.Any(e =>
            host.Equals(e, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + e, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value)
    {
        var text = value.Trim().Trim('.').ToLowerInvariant();

        // В настройках домен может быть введён со схемой или путём.
        var scheme = text.IndexOf("//", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 2)..];

        var slash = text.IndexOf('/');
        if (slash > 0) text = text[..slash];

        var colon = text.IndexOf(':');
        if (colon > 0) text = text[..colon];

        return text.Trim();
    }
}
