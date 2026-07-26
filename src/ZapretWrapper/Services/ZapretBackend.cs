using System;
using System.Collections.Generic;
using System.IO;

namespace ZapretWrapper.Services;

/// <summary>Какая сборка zapret лежит в выбранной пользователем папке.</summary>
public enum ZapretFlavor
{
    Unknown,

    /// <summary>
    /// bol-van/zapret2: binaries/windows-x86_64/winws2.exe, lua/, files/fake/.
    /// Аргументы в стиле --lua-desync=... и плейсхолдеры &lt;lua/...&gt;.
    /// </summary>
    Zapret2,

    /// <summary>
    /// Flowseal/zapret-discord-youtube: bin/winws.exe, lists/, general*.bat.
    /// Это старый zapret, аргументы в стиле --dpi-desync=fake,fakedsplit.
    /// </summary>
    Flowseal,
}

/// <summary>
/// Единая точка входа для «где лежит бинарник и как готовить аргументы».
/// Две поддерживаемые сборки несовместимы между собой: у них разные имена
/// исполняемого файла, разная раскладка папок и, главное, полностью разный
/// синтаксис аргументов. Поэтому определяем сборку один раз и дальше всё
/// спрашиваем здесь, а не разбросанными по коду Path.Combine.
/// </summary>
public static class ZapretBackend
{
    public sealed record BackendPaths(
        ZapretFlavor Flavor,
        string Root,
        string WinwsExe,
        string WorkingDirectory,
        string BinDir,
        string ListsDir,
        string Label);

    public sealed class BackendLayout
    {
        public ZapretFlavor Flavor { get; set; } = ZapretFlavor.Unknown;
        public bool IsValid { get; set; }
        public List<string> Missing { get; set; } = new();
        public string? Error { get; set; }
        public string Label { get; set; } = "сборка не определена";
    }

    public static string Label(ZapretFlavor flavor) => flavor switch
    {
        ZapretFlavor.Zapret2 => "zapret2 (winws2.exe)",
        ZapretFlavor.Flowseal => "zapret-discord-youtube (winws.exe)",
        _ => "сборка не определена",
    };

    /// <summary>
    /// Определяет сборку по содержимому папки. Сначала ищем бинарники, потом —
    /// характерные файлы: папка может быть клоном репозитория без релизных бинарей,
    /// и в этом случае лучше честно сказать «нет winws», чем «непонятная папка».
    /// </summary>
    public static ZapretFlavor Detect(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return ZapretFlavor.Unknown;

        if (File.Exists(Path.Combine(root, "bin", "winws.exe")))
            return ZapretFlavor.Flowseal;

        if (File.Exists(Path.Combine(root, "binaries", "windows-x86_64", "winws2.exe")))
            return ZapretFlavor.Zapret2;

        // Клон без бинарников — опознаём по структуре.
        if (File.Exists(Path.Combine(root, "service.bat")) || Directory.Exists(Path.Combine(root, "lists")))
            return ZapretFlavor.Flowseal;

        if (Directory.Exists(Path.Combine(root, "lua")) || File.Exists(Path.Combine(root, "config.default")))
            return ZapretFlavor.Zapret2;

        return ZapretFlavor.Unknown;
    }

    /// <summary>Пути внутри папки для определённой сборки. null — сборку опознать не удалось.</summary>
    public static BackendPaths? Resolve(string? root)
    {
        var flavor = Detect(root);
        if (flavor == ZapretFlavor.Unknown || string.IsNullOrWhiteSpace(root)) return null;
        return Resolve(root!, flavor);
    }

    public static BackendPaths Resolve(string root, ZapretFlavor flavor)
    {
        if (flavor == ZapretFlavor.Flowseal)
        {
            var bin = Path.Combine(root, "bin");
            return new BackendPaths(
                Flavor: flavor,
                Root: root,
                WinwsExe: Path.Combine(bin, "winws.exe"),
                // Критично: winws.exe собран под cygwin и грузит cygwin1.dll из своей
                // папки. В general*.bat перед запуском стоит cd /d %BIN% ровно поэтому.
                WorkingDirectory: bin,
                BinDir: bin,
                ListsDir: Path.Combine(root, "lists"),
                Label: Label(flavor));
        }

        var paths = ZapretLocator.ResolvePaths(root);
        return new BackendPaths(
            Flavor: ZapretFlavor.Zapret2,
            Root: root,
            WinwsExe: paths.WinwsExe,
            WorkingDirectory: root,
            BinDir: Path.Combine(root, "binaries", "windows-x86_64"),
            ListsDir: paths.FilesDir,
            Label: Label(ZapretFlavor.Zapret2));
    }

    /// <summary>Проверяет, что папки достаточно для запуска.</summary>
    public static BackendLayout Validate(string? root)
    {
        var layout = new BackendLayout();

        if (string.IsNullOrWhiteSpace(root))
        {
            layout.Error = "Путь к папке zapret не указан.";
            return layout;
        }

        if (!Directory.Exists(root))
        {
            layout.Error = "Папка не найдена: " + root;
            return layout;
        }

        layout.Flavor = Detect(root);
        layout.Label = Label(layout.Flavor);

        if (layout.Flavor == ZapretFlavor.Unknown)
        {
            layout.Error = "Не удалось определить сборку. Ожидается либо zapret2 " +
                           "(binaries/windows-x86_64/winws2.exe), либо zapret-discord-youtube " +
                           "(bin/winws.exe).";
            return layout;
        }

        if (layout.Flavor == ZapretFlavor.Zapret2)
        {
            var legacy = ZapretLocator.Validate(root);
            layout.Missing = legacy.Missing;
            layout.Error = legacy.Error;
            layout.IsValid = legacy.IsValid;
            return layout;
        }

        var paths = Resolve(root, ZapretFlavor.Flowseal);

        if (!File.Exists(paths.WinwsExe)) layout.Missing.Add("bin/winws.exe");
        if (!File.Exists(Path.Combine(paths.BinDir, "WinDivert.dll"))) layout.Missing.Add("bin/WinDivert.dll");
        if (!File.Exists(Path.Combine(paths.BinDir, "cygwin1.dll"))) layout.Missing.Add("bin/cygwin1.dll");
        if (!Directory.Exists(paths.ListsDir)) layout.Missing.Add("lists/");

        layout.IsValid = layout.Missing.Count == 0;

        if (!layout.IsValid)
            layout.Error = "В папке не хватает файлов: " + string.Join(", ", layout.Missing) +
                           ". Скачайте архив со страницы релизов — в git-клоне бинарников нет.";

        return layout;
    }

    /// <summary>
    /// Готовит аргументы к передаче в процесс. У zapret2 в аргументах остаются
    /// плейсхолдеры вида &lt;lua/...&gt;, их надо развернуть. У flowseal все пути уже
    /// абсолютные — их разворачивает загрузчик при разборе .bat.
    /// </summary>
    public static string[] ResolveArgs(IReadOnlyList<string> args, string root)
    {
        var flavor = Detect(root);
        var array = new string[args.Count];
        for (int i = 0; i < args.Count; i++) array[i] = args[i];

        return flavor == ZapretFlavor.Zapret2
            ? ZapretLocator.ResolveArgs(array, root)
            : array;
    }
}
