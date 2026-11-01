using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace CS2Optimizer.Services;

public record HwCard(string Glyph, string Label, string Value, string Detail);

public class Check
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public bool Ok { get; init; }
    public string? FixLabel { get; init; }
    public Func<string>? Fix { get; init; }

    public string Status => Ok ? "OK" : "!";
    public Brush StatusBrush => (Brush)Application.Current.Resources[Ok ? "OkBrush" : "WarnBrush"];
    public Visibility FixVisibility => !Ok && Fix != null ? Visibility.Visible : Visibility.Collapsed;
}

public static class SystemInfo
{
    const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string DxGlobalValue = "DirectXUserGlobalSettings";
    const string GameBarKey = @"Software\Microsoft\GameBar";
    const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            return searcher.Get().Cast<ManagementBaseObject>().ToList();
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static string S(ManagementBaseObject o, string p) => o[p]?.ToString()?.Trim() ?? "";
    static long L(ManagementBaseObject o, string p) => o[p] is { } v ? Convert.ToInt64(v) : 0;

    static string Clean(string name) =>
        Regex.Replace(name.Replace("(R)", "").Replace("(TM)", "").Replace(" CPU", ""), @"\s+", " ").Trim();

    static bool IsIntegrated(string gpu) => gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) && !gpu.Contains("Arc")
                                            || gpu.Contains("Radeon(TM) Graphics") || gpu.Contains("Radeon Graphics");

    public static List<HwCard> Hardware()
    {
        var cards = new List<HwCard>();

        var cpu = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").FirstOrDefault();
        cards.Add(cpu == null
            ? new("", "ПРОЦЕССОР", "Не удалось определить", "")
            : new("", "ПРОЦЕССОР", Clean(S(cpu, "Name")),
                $"{S(cpu, "NumberOfCores")} ядер · {S(cpu, "NumberOfLogicalProcessors")} потоков"));

        var gpus = Query("SELECT Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
        var main = gpus.FirstOrDefault(g => !IsIntegrated(S(g, "Name"))) ?? gpus.FirstOrDefault();
        var others = gpus.Where(g => g != main).Select(g => S(g, "Name").Replace("(R)", "")).ToList();
        cards.Add(main == null
            ? new("", "ВИДЕОКАРТА", "Не удалось определить", "")
            : new("", "ВИДЕОКАРТА", S(main, "Name"),
                $"Драйвер {S(main, "DriverVersion")}" + (others.Count > 0 ? $"\n+ {string.Join(", ", others)}" : "")));

        var sticks = Query("SELECT Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
        long total = sticks.Sum(s => L(s, "Capacity"));
        var mhz = sticks.Select(s => L(s, "ConfiguredClockSpeed") is > 0 and var c ? c : L(s, "Speed")).DefaultIfEmpty(0).Min();
        var sizes = string.Join(" + ", sticks.Select(s => $"{L(s, "Capacity") >> 30}"));
        cards.Add(new("", "ОПЕРАТИВНАЯ ПАМЯТЬ", $"{total >> 30} ГБ",
            sticks.Count == 0 ? "" : $"{sizes} ГБ · {mhz} МГц · {(sticks.Count >= 2 ? "двухканал" : "одноканал")}"));

        var display = gpus.FirstOrDefault(g => L(g, "CurrentHorizontalResolution") > 0);
        cards.Add(display == null
            ? new("", "ЭКРАН", "—", "")
            : new("", "ЭКРАН", $"{L(display, "CurrentHorizontalResolution")} × {L(display, "CurrentVerticalResolution")}",
                $"{L(display, "CurrentRefreshRate")} Гц"));

        var disks = Query("SELECT FriendlyName, MediaType, Size FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
        cards.Add(new("", "НАКОПИТЕЛИ", disks.Count switch { 0 => "—", 1 => "1 диск", < 5 => $"{disks.Count} диска", _ => $"{disks.Count} дисков" },
            string.Join("\n", disks.Select(d => $"{(L(d, "MediaType") == 3 ? "HDD" : L(d, "MediaType") == 4 ? "SSD" : "Диск")} · {L(d, "Size") / 1_000_000_000} ГБ"))));

        var os = Query("SELECT Caption, Version FROM Win32_OperatingSystem").FirstOrDefault();
        cards.Add(new("", "СИСТЕМА",
            os == null ? "Windows" : Regex.Replace(S(os, "Caption"), "^(Майкрософт|Microsoft) ", ""),
            os == null ? "" : $"Сборка {S(os, "Version")}"));

        return cards;
    }

    public static List<Check> Checks()
    {
        var checks = new List<Check>();

        if (GetSystemPowerStatus(out var ps) && ps.BatteryFlag != 128)
        {
            bool ac = ps.ACLineStatus == 1;
            checks.Add(new Check
            {
                Title = "Питание от сети",
                Ok = ac,
                Detail = ac ? "Ноутбук на зарядке — процессор и видеокарта работают на полную."
                            : "Ноутбук работает от батареи: частоты режутся, FPS падает. Подключи зарядку.",
            });
        }

        var (planGuid, planName) = ActivePowerPlan();
        bool perf = planGuid is HighPerfGuid or UltimateGuid
                    || Regex.IsMatch(planName, "high|ultimate|performance|производительн", RegexOptions.IgnoreCase);
        checks.Add(new Check
        {
            Title = "План электропитания",
            Ok = perf,
            Detail = perf ? $"Активен «{planName}»." : $"Активен «{planName}». Для игр нужна «Высокая производительность».",
            FixLabel = "Включить",
            Fix = SetHighPerformance,
        });

        // Choosing a GPU only matters with two of them (laptops, iGPU + card); virtual adapters don't count.
        int realGpus = Query("SELECT Name FROM Win32_VideoController")
            .Count(g => !Regex.IsMatch(S(g, "Name"), "Basic|Remote|Virtual|Parsec|Meta|DisplayLink", RegexOptions.IgnoreCase));
        var exe = Cs2Paths.Cs2Exe;
        if (exe == null)
        {
            checks.Add(new Check { Title = "CS2 найдена", Ok = false, Detail = "Не нашёл cs2.exe в библиотеках Steam. Графика, конфиг и карты заработают, когда игра будет установлена." });
        }
        else if (realGpus >= 2)
        {
            bool dgpu = (Registry.CurrentUser.OpenSubKey(GpuPrefKey)?.GetValue(exe) as string)?.Contains("GpuPreference=2") == true;
            checks.Add(new Check
            {
                Title = "Видеокарта для CS2",
                Ok = dgpu,
                Detail = dgpu ? "cs2.exe закреплена за мощной (дискретной) видеокартой."
                              : "Windows сама решает, на какой видеокарте запускать CS2 — может выбрать встроенную.",
                FixLabel = "Закрепить",
                Fix = () =>
                {
                    WindowsChanges.SetRegistry("gpu-pref", "CS2 закреплена за дискретной видеокартой", GpuPrefKey, exe, "GpuPreference=2;");
                    return "CS2 теперь всегда запускается на дискретной видеокарте.";
                },
            });
        }

        int sticks = Query("SELECT Capacity FROM Win32_PhysicalMemory").Count;
        checks.Add(new Check
        {
            Title = "Двухканальная память",
            Ok = sticks >= 2,
            Detail = sticks >= 2 ? $"Установлено планок: {sticks}. Память работает в двухканале."
                                 : "Стоит одна планка памяти. В CS2 это даёт статтеры — вторая такая же планка заметно поможет.",
        });

        bool gameMode = Registry.CurrentUser.OpenSubKey(GameBarKey)?.GetValue("AutoGameModeEnabled") is not 0;
        checks.Add(new Check
        {
            Title = "Игровой режим Windows",
            Ok = gameMode,
            Detail = gameMode ? "Включён: Windows отдаёт приоритет игре." : "Выключен. Он снижает влияние фоновых задач на игру.",
            FixLabel = "Включить",
            Fix = () =>
            {
                WindowsChanges.SetRegistry("game-mode", "Игровой режим включён", GameBarKey, "AutoGameModeEnabled", 1);
                return "Игровой режим включён.";
            },
        });

        bool bgRecording = Registry.CurrentUser.OpenSubKey(GameDvrKey)?.GetValue("HistoricalCaptureEnabled") is 1;
        checks.Add(new Check
        {
            Title = "Фоновая запись Xbox",
            Ok = !bgRecording,
            Detail = bgRecording ? "Xbox Game Bar постоянно пишет последние минуты игры — это отнимает FPS."
                                 : "Выключена — игра не записывается в фоне.",
            FixLabel = "Выключить",
            Fix = () =>
            {
                WindowsChanges.SetRegistry("xbox-dvr", "Фоновая запись Xbox выключена", GameDvrKey, "HistoricalCaptureEnabled", 0);
                return "Фоновая запись выключена.";
            },
        });

        bool hags = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers")?.GetValue("HwSchMode") is 2;
        checks.Add(new Check
        {
            Title = "Аппаратное планирование GPU",
            Ok = hags,
            Detail = hags ? "Включено: видеокарта сама управляет очередью кадров, задержка ниже."
                          : "Выключено. Переключатель находится в параметрах графики Windows, после включения нужна перезагрузка.",
            FixLabel = "Открыть настройку",
            Fix = () =>
            {
                Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics") { UseShellExecute = true });
                return "Открыл параметры графики: включи «Планирование GPU с аппаратным ускорением» и перезагрузи ПК.";
            },
        });

        var dxSettings = Registry.CurrentUser.OpenSubKey(GpuPrefKey)?.GetValue(DxGlobalValue) as string ?? "";
        bool windowedOpt = dxSettings.Contains("SwapEffectUpgradeEnable=1");
        checks.Add(new Check
        {
            Title = "Оптимизация для оконных игр",
            Ok = windowedOpt,
            Detail = windowedOpt ? "Включена: в режиме «Окно без рамки» задержка как в полноэкранном."
                                 : "Выключена. Снижает задержку в режиме «Окно без рамки» — а в нём поверх игры видна ватермарка.",
            FixLabel = "Включить",
            Fix = () =>
            {
                var parts = dxSettings.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !p.StartsWith("SwapEffectUpgradeEnable=")).Append("SwapEffectUpgradeEnable=1");
                WindowsChanges.SetRegistry("windowed-opt", "Оптимизация для оконных игр включена", GpuPrefKey, DxGlobalValue, string.Join(";", parts) + ";");
                return "Оптимизация для оконных игр включена. Перезапусти CS2, если она открыта.";
            },
        });

        return checks;
    }

    static (string Guid, string Name) ActivePowerPlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return ("", "неизвестно");
        var guid = Marshal.PtrToStructure<Guid>(ptr);
        LocalFree(ptr);

        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref size);
        var buffer = new byte[size];
        var name = PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : "неизвестно";
        return (guid.ToString(), name);
    }

    [DllImport("powrprof.dll")]
    static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr activeScheme);

    [DllImport("powrprof.dll")]
    static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subGroup, IntPtr setting, byte[]? buffer, ref uint size);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr mem);

    public static bool SetPowerPlan(string guid) => Powercfg($"/setactive {guid}").Code == 0;

    static string SetHighPerformance()
    {
        var (oldGuid, oldName) = ActivePowerPlan();
        if (oldGuid.Length > 0) WindowsChanges.RecordPowerPlan(oldGuid, oldName);
        if (Powercfg($"/setactive {HighPerfGuid}").Code == 0)
            return "Включена «Высокая производительность».";

        // Many laptops hide the stock scheme; restoring a copy of it brings it back.
        var (code, output) = Powercfg($"-duplicatescheme {HighPerfGuid}");
        var guid = Regex.Match(output, "[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}").Value;
        if (code == 0 && guid.Length > 0 && Powercfg($"/setactive {guid}").Code == 0)
            return "Включена «Высокая производительность».";
        return "Не получилось переключить план. Сделай это вручную: Панель управления → Электропитание.";
    }

    static (int Code, string Output) Powercfg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("powercfg", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
