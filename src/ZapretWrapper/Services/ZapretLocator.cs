using System;
using System.IO;

namespace ZapretWrapper.Services;

/// <summary>
/// Проверяет наличие winws2.exe и связанных файлов в указанной пользователем папке zapret2.
/// Не модифицирует файлы — только чтение.
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

    /// <summary>Возвращает пути к основным ресурсам (даже если Validate ещё не звался).</summary>
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
            FakeDir: Path.Combine(zapretPath, "files", "fake"),
            WindivertFilterDir: Path.Combine(zapretPath, "init.d", "windivert.filter"));
    }

    /// <summary>Заменяет плейсхолдеры &lt;lua/...&gt;, &lt;files/...&gt; в аргументах стратегии на абсолютные пути.</summary>
    public static string[] ResolveArgs(string[] templateArgs, string zapretPath)
    {
        var paths = ResolvePaths(zapretPath);
        var resolved = new string[templateArgs.Length];
        for (int i = 0; i < templateArgs.Length; i++)
        {
            var a = templateArgs[i];
            if (a.StartsWith("@<lua/"))
                a = "@" + Path.Combine(paths.LuaDir, a.Substring("@<lua/".Length).TrimEnd('>'));
            else if (a.StartsWith("@<files/"))
                a = "@" + Path.Combine(zapretPath, a.Substring("@<files/".Length).TrimEnd('>'));
            else if (a.StartsWith("<files/"))
                a = Path.Combine(zapretPath, a.Substring("<files/".Length).TrimEnd('>'));
            else if (a.StartsWith("<lua/"))
                a = Path.Combine(paths.LuaDir, a.Substring("<lua/".Length).TrimEnd('>'));
            // Опции вида "--key=<files/list-youtube.txt>"
            else if (a.Contains("<files/"))
                a = ResolveInPlace(a, zapretPath, paths);
            resolved[i] = a;
        }
        return resolved;
    }

    private static string ResolveInPlace(string arg, string zapretPath, Paths paths)
    {
        if (arg.Contains("<files/"))
            arg = arg.Replace("<files/", zapretPath + Path.DirectorySeparatorChar);
        if (arg.Contains("<lua/"))
            arg = arg.Replace("<lua/", paths.LuaDir + Path.DirectorySeparatorChar);
        return arg.TrimEnd('>');
    }
}
