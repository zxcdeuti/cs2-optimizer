using System.Text.Json;
using Microsoft.Win32;

namespace CS2Optimizer.Services;

/// <summary>App-level preferences from the Settings page.</summary>
public record AppSettings(
    bool CloseToTray = true,
    bool MinimizeToTray = false,
    bool StartMinimized = true,
    bool OverlayOnStart = false,
    bool OverlayAsAdmin = true,
    bool MapLabelsRu = true,
    bool TrayHintShown = false)
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CS2Optimizer");

    static readonly string FilePath = Path.Combine(DataDir, "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public static void Save(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        if (Autostart.Enabled) Autostart.Set(true, settings.StartMinimized); // keep the --tray flag in sync
    }

    public static void Reset() => Current = new();
}

/// <summary>"Start with Windows" through the current user's Run key.</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "CS2Optimizer";

    public static bool Enabled =>
        Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(ValueName) is string cmd
        && cmd.Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase);

    public static void Set(bool on, bool minimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"" + (minimized ? " --tray" : ""));
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
