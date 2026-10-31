namespace CS2Optimizer.Services;

public record SettingInfo(string Key, string Name, Func<string?, string> Describe);

public record Preset(string Id, string Title, Dictionary<string, string> Values);

public static class GraphicsPresets
{
    static Func<string?, string> Names(params string[] names) => v =>
        v == null ? "—" : int.TryParse(v, out var i) && i >= 0 && i < names.Length ? names[i] : v;

    public static readonly SettingInfo[] Settings =
    [
        new("setting.fullscreen", "Режим экрана", v => v == "1" ? "Полный экран" : v == null ? "—" : "Оконный"),
        new("setting.mat_vsync", "Вертикальная синхронизация", Names("Выкл", "Вкл")),
        new("setting.r_low_latency", "NVIDIA Reflex", Names("Выкл", "Вкл", "Вкл + Boost")),
        new("setting.msaa_samples", "Сглаживание MSAA", v => v switch { null => "—", "0" => "Нет", _ => v + "x" }),
        new("setting.r_csgo_cmaa_enable", "CMAA2", Names("Выкл", "Вкл")),
        new("setting.videocfg_shadow_quality", "Качество теней", Names("Низкое", "Среднее", "Высокое", "Очень высокое")),
        new("setting.videocfg_dynamic_shadows", "Динамические тени", Names("Только солнце", "Все")),
        new("setting.videocfg_texture_detail", "Детализация текстур", Names("Низкая", "Средняя", "Высокая")),
        new("setting.r_texturefilteringquality", "Фильтрация текстур", Names("Билинейная", "Трилинейная", "Аниз. 2x", "Аниз. 4x", "Аниз. 8x", "Аниз. 16x")),
        new("setting.shaderquality", "Качество шейдеров", Names("Низкое", "Высокое")),
        new("setting.videocfg_particle_detail", "Детализация частиц", Names("Низкая", "Средняя", "Высокая", "Очень высокая")),
        new("setting.videocfg_ao_detail", "Ambient Occlusion", Names("Выкл", "Среднее", "Высокое")),
        new("setting.videocfg_fsr_detail", "FidelityFX Super Resolution", v => v is null or "0" ? "Выкл" : "Вкл"),
    ];

    // Dynamic shadows stay on "All" everywhere: they show enemy shadows round corners.
    public static readonly Preset[] All =
    [
        new("fps", "Максимум FPS", new()
        {
            ["setting.fullscreen"] = "1", ["setting.nowindowborder"] = "0", ["setting.mat_vsync"] = "0",
            ["setting.r_low_latency"] = "2", ["setting.msaa_samples"] = "0", ["setting.r_csgo_cmaa_enable"] = "1",
            ["setting.videocfg_shadow_quality"] = "0", ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.videocfg_texture_detail"] = "0", ["setting.r_texturefilteringquality"] = "0",
            ["setting.shaderquality"] = "0", ["setting.videocfg_particle_detail"] = "0",
            ["setting.videocfg_ao_detail"] = "0", ["setting.videocfg_fsr_detail"] = "0",
        }),
        new("balance", "Баланс", new()
        {
            ["setting.fullscreen"] = "1", ["setting.nowindowborder"] = "0", ["setting.mat_vsync"] = "0",
            ["setting.r_low_latency"] = "2", ["setting.msaa_samples"] = "2", ["setting.r_csgo_cmaa_enable"] = "0",
            ["setting.videocfg_shadow_quality"] = "1", ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.videocfg_texture_detail"] = "1", ["setting.r_texturefilteringquality"] = "1",
            ["setting.shaderquality"] = "0", ["setting.videocfg_particle_detail"] = "0",
            ["setting.videocfg_ao_detail"] = "0", ["setting.videocfg_fsr_detail"] = "0",
        }),
        new("quality", "Качество", new()
        {
            ["setting.fullscreen"] = "1", ["setting.nowindowborder"] = "0", ["setting.mat_vsync"] = "0",
            ["setting.r_low_latency"] = "2", ["setting.msaa_samples"] = "4", ["setting.r_csgo_cmaa_enable"] = "0",
            ["setting.videocfg_shadow_quality"] = "2", ["setting.videocfg_dynamic_shadows"] = "1",
            ["setting.videocfg_texture_detail"] = "2", ["setting.r_texturefilteringquality"] = "3",
            ["setting.shaderquality"] = "1", ["setting.videocfg_particle_detail"] = "1",
            ["setting.videocfg_ao_detail"] = "1", ["setting.videocfg_fsr_detail"] = "0",
        }),
    ];
}
