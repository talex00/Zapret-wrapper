using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ZapretWrapper.Services;

/// <summary>
/// Проверяет структуру папки zapret2 и разворачивает плейсхолдеры в аргументах
/// стратегий в абсолютные пути. Только чтение, ничего не модифицирует.
/// </summary>
public static class ZapretLocator
{
    public class ZapretLayout
    {
        public bool IsValid { get; set; }
        public List<string> Missing { get; set; } = new();
        public string? Error { get; set; }
    }

    public record Paths(
        string WinwsExe,
        string LuaDir,
        string FilesDir,
        string FakeDir,
        string WindivertFilterDir);

    /// <summary>Проверяет структуру папки zapret2.</summary>
    public static ZapretLayout Validate(string? zapretPath)
    {
        var layout = new ZapretLayout();
        if (string.IsNullOrWhiteSpace(zapretPath))
        {
            layout.Error = "Путь к zapret2 не указан.";
            return layout;
        }

        if (!Directory.Exists(zapretPath))
        {
            layout.Error = $"Папка не найдена: {zapretPath}";
            return layout;
        }

        var paths = ResolvePaths(zapretPath);

        if (!File.Exists(paths.WinwsExe))
            layout.Missing.Add("binaries/windows-x86_64/winws2.exe");
        if (!Directory.Exists(paths.LuaDir))
            layout.Missing.Add("lua/");
        if (!Directory.Exists(paths.FakeDir))
            layout.Missing.Add("files/fake/");

        layout.IsValid = layout.Missing.Count == 0 && layout.Error is null;
        return layout;
    }

    /// <summary>Пути к основным ресурсам внутри папки zapret2.</summary>
    public static Paths ResolvePaths(string zapretPath)
    {
        // Структура zapret-win-bundle / zapret2:
        //   zapret-dir/
        //     binaries/windows-x86_64/winws2.exe
        //     lua/zapret-*.lua
        //     files/fake/*.bin
        //     init.d/windivert.filter/*.txt
        return new Paths(
            WinwsExe: Path.Combine(zapretPath, "binaries", "windows-x86_64", "winws2.exe"),
            LuaDir: Path.Combine(zapretPath, "lua"),
            FilesDir: Path.Combine(zapretPath, "files"),
            FakeDir: Path.Combine(zapretPath, "files", "fake"),
            WindivertFilterDir: Path.Combine(zapretPath, "init.d", "windivert.filter"));
    }

    private static readonly Regex PlaceholderRegex =
        new(@"<([^<>]+)>", RegexOptions.Compiled);

    /// <summary>
    /// Заменяет плейсхолдеры вида &lt;lua/...&gt;, &lt;files/...&gt;, &lt;windivert.filter/...&gt;
    /// на абсолютные пути. Работает в любой части аргумента, а не только в начале,
    /// поэтому корректно обрабатывает и "--hostlist=&lt;files/list-youtube.txt&gt;",
    /// и "--wf-raw-part=@&lt;windivert.filter/windivert_part.tcp80.txt&gt;".
    /// </summary>
    public static string[] ResolveArgs(string[] templateArgs, string zapretPath)
    {
        var resolved = new string[templateArgs.Length];
        for (int i = 0; i < templateArgs.Length; i++)
            resolved[i] = ResolveArg(templateArgs[i], zapretPath);
        return resolved;
    }

    public static string ResolveArg(string arg, string zapretPath)
    {
        if (string.IsNullOrEmpty(arg)) return arg;

        arg = StripCmdQuotes(arg);
        if (!arg.Contains('<')) return arg;

        return PlaceholderRegex.Replace(
            arg, m => ResolvePlaceholder(m.Groups[1].Value, zapretPath));
    }

    /// <summary>
    /// Пресеты скопированы из .cmd-файлов, где значения обёрнуты в кавычки для cmd.exe.
    /// При запуске через ProcessStartInfo.ArgumentList кавычки экранируются и уезжают
    /// в winws2 как часть значения — снимаем их.
    /// </summary>
    private static string StripCmdQuotes(string arg)
    {
        var eq = arg.IndexOf('=');
        if (eq > 0
            && arg.Length > eq + 2
            && arg[eq + 1] == '"'
            && arg[^1] == '"')
        {
            return string.Concat(
                arg.AsSpan(0, eq + 1),
                arg.AsSpan(eq + 2, arg.Length - eq - 3));
        }

        if (arg.Length > 1 && arg[0] == '"' && arg[^1] == '"')
            return arg[1..^1];

        return arg;
    }

    private static string ResolvePlaceholder(string body, string zapretPath)
    {
        var paths = ResolvePaths(zapretPath);

        var normalized = body
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();

        var slash = normalized.IndexOf(Path.DirectorySeparatorChar);
        if (slash <= 0)
            return Path.Combine(zapretPath, normalized);

        var root = normalized[..slash];
        var rest = normalized[(slash + 1)..];

        return root.ToLowerInvariant() switch
        {
            "lua" => Path.Combine(paths.LuaDir, rest),
            // Важно: подпапка files/ должна сохраняться — раньше она терялась.
            "files" => Path.Combine(paths.FilesDir, rest),
            "windivert.filter" => Path.Combine(paths.WindivertFilterDir, rest),
            "binaries" => Path.Combine(zapretPath, "binaries", rest),
            _ => Path.Combine(zapretPath, normalized),
        };
    }
}
