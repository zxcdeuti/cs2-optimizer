using System.Text.RegularExpressions;

namespace CS2Optimizer.Services;

public record LaunchOption(string Flag, string Title, string Hint, bool Default);

public static class LaunchOptions
{
    public static readonly LaunchOption[] All =
    [
        new("+exec autoexec", "Загружать autoexec.cfg",
            "Нужно, чтобы прицел и конфиг из этого приложения применялись сами при каждом запуске.", true),
        new("-novid", "Пропустить вступительный ролик",
            "Игра стартует на пару секунд быстрее. На FPS не влияет.", true),
        new("-console", "Открывать консоль при запуске",
            "Удобно, если часто вводишь команды.", false),
        new("+fps_max 0", "Без лимита FPS",
            "Снимает ограничение кадров с первого запуска. То же можно задать в конфиге (раздел 07).", false),
        new("-language english", "Игра на английском",
            "Названия настроек как в гайдах и у про-игроков.", false),
        new("-vulkan", "Рендер через Vulkan",
            "На части систем даёт больше FPS, на других — статтеры. Проверь на своём ПК, сравни с DirectX.", false),
        new("-high", "Высокий приоритет процесса",
            "Спорный параметр: иногда помогает на слабых процессорах, иногда даёт статтеры звука.", false),
    ];

    /// <summary>
    /// Launch options Steam currently has for CS2, read from the most recent account's localconfig.vdf.
    /// Read only: Steam rewrites that file while it runs.
    /// </summary>
    public static string? ReadCurrent()
    {
        var userdata = Cs2Paths.SteamPath is { } s ? Path.Combine(s, "userdata") : null;
        if (userdata == null || !Directory.Exists(userdata)) return null;
        var file = Directory.GetDirectories(userdata)
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
        if (file == null) return null;

        string text;
        try { text = File.ReadAllText(file); }
        catch (IOException) { return null; }

        // The "730" block runs until the next sibling app id line; LaunchOptions sits directly in it.
        var app = Regex.Match(text, "\"apps\"\\s*\\{.*?\\n(\\s*)\"730\"\\s*\\{", RegexOptions.Singleline);
        if (!app.Success) return "";
        int start = app.Index + app.Length;
        var next = Regex.Match(text[start..], "\\n" + app.Groups[1].Value + "\"\\d+\"\\s*\\n");
        var block = next.Success ? text.Substring(start, next.Index) : text[start..];
        var opts = Regex.Match(block, "\"LaunchOptions\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return opts.Success ? opts.Groups[1].Value.Replace("\\\"", "\"") : "";
    }
}
