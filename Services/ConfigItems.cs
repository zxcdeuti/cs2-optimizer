using System.Globalization;
using System.Text.Json;

namespace CS2Optimizer.Services;

/// <summary>A console variable the config page can set: either a switch (On/Off values) or a slider.</summary>
public record ConfigItem(
    string Cvar, string Title, string Hint, bool IncludedByDefault,
    string? On = null, string? Off = null, bool SwitchDefault = true,
    double Min = 0, double Max = 0, double Step = 1, double Default = 0, string Format = "0")
{
    public bool IsSwitch => On != null;
}

public record ConfigEntry(bool Included, string Value);

public static class ConfigItems
{
    public static readonly ConfigItem[] All =
    [
        new("fps_max", "Лимит FPS в игре", "0 — без ограничения. Лимит чуть выше частоты экрана снижает нагрев ноутбука.", true,
            Min: 0, Max: 500, Step: 5, Default: 0),
        new("fps_max_ui", "Лимит FPS в меню", "В меню высокий FPS не нужен — ноутбук меньше греется между матчами.", true,
            Min: 30, Max: 240, Step: 10, Default: 120),
        new("cl_hud_telemetry_frametime_show", "FPS и время кадра в HUD", "Встроенный счётчик игры в углу экрана.", true,
            On: "2", Off: "0"),
        new("cl_hud_telemetry_ping_show", "Пинг в HUD", "Показывать задержку до сервера всегда.", true,
            On: "2", Off: "0"),
        new("cl_hud_telemetry_net_misdelivery_show", "Потери пакетов в HUD", "Предупреждение о проблемах с сетью.", false,
            On: "2", Off: "0"),
        new("snd_menumusic_volume", "Музыка в меню", "Обычно её выключают, чтобы не мешала между матчами.", true,
            On: "0.5", Off: "0", SwitchDefault: false),
        new("snd_mute_losefocus", "Тишина в свёрнутой игре", "Глушить звук, когда переключаешься в другое окно.", false,
            On: "1", Off: "0"),
        new("volume", "Общая громкость", "Громкость игры целиком.", false,
            Min: 0, Max: 1, Step: 0.05, Default: 0.5, Format: "0.00"),
        new("cl_radar_scale", "Масштаб радара", "Меньше значение — видно больше карты.", false,
            Min: 0.25, Max: 1, Step: 0.05, Default: 0.4, Format: "0.00"),
        new("cl_hud_radar_scale", "Размер радара на экране", "Физический размер миникарты в HUD.", false,
            Min: 0.8, Max: 1.3, Step: 0.05, Default: 1.15, Format: "0.00"),
        new("cl_radar_always_centered", "Радар по центру игрока", "Выключи, чтобы радар не обрезал карту у краёв.", false,
            On: "1", Off: "0", SwitchDefault: false),
        new("r_fullscreen_gamma", "Яркость", "Работает только в полноэкранном режиме.", false,
            Min: 1.6, Max: 2.6, Step: 0.1, Default: 2.2, Format: "0.0"),
    ];

    static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CS2Optimizer", "config.json");

    public static Dictionary<string, ConfigEntry> Load()
    {
        Dictionary<string, ConfigEntry>? saved = null;
        try { saved = JsonSerializer.Deserialize<Dictionary<string, ConfigEntry>>(File.ReadAllText(StatePath)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }

        return All.ToDictionary(i => i.Cvar, i => saved != null && saved.TryGetValue(i.Cvar, out var s)
            ? s
            : new ConfigEntry(i.IncludedByDefault, i.IsSwitch ? (i.SwitchDefault ? i.On! : i.Off!) : Format(i, i.Default)));
    }

    public static void Save(Dictionary<string, ConfigEntry> state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
    }

    public static string Format(ConfigItem item, double value) => value.ToString(item.Format, CultureInfo.InvariantCulture);

    public static IEnumerable<string> Lines(Dictionary<string, ConfigEntry> state) =>
        All.Where(i => state[i.Cvar].Included).Select(i => $"{i.Cvar} {state[i.Cvar].Value}");
}
