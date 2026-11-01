using System.Text.Json;
using Microsoft.Win32;

namespace CS2Optimizer.Services;

/// <param name="Id">One entry per setting: re-applying keeps the original value, so undo always goes all the way back.</param>
/// <param name="Kind">"registry", "power" or "mouse".</param>
/// <param name="OldValue">Null when a registry value didn't exist before.</param>
public record WindowsChange(string Id, string Title, DateTime When, string Kind, string? Key, string? Name, string? OldValue, bool OldIsDword);

/// <summary>Journal of what the app changed in Windows, with one-click undo. Stored in changes.json.</summary>
public static class WindowsChanges
{
    static readonly string FilePath = Path.Combine(AppSettings.DataDir, "changes.json");

    public static List<WindowsChange> All()
    {
        try { return JsonSerializer.Deserialize<List<WindowsChange>>(File.ReadAllText(FilePath)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    static void Save(List<WindowsChange> changes)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(changes));
    }

    static void Record(WindowsChange change)
    {
        var all = All();
        if (all.Any(c => c.Id == change.Id)) return;
        all.Add(change);
        Save(all);
    }

    /// <summary>Sets a value under HKCU, journaling what was there before.</summary>
    public static void SetRegistry(string id, string title, string keyPath, string name, object value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        var old = key.GetValue(name);
        Record(new WindowsChange(id, title, DateTime.Now, "registry", keyPath, name, old?.ToString(), old is int));
        if (value is int) key.SetValue(name, value, RegistryValueKind.DWord);
        else key.SetValue(name, value);
    }

    public static void RecordPowerPlan(string oldGuid, string oldName) =>
        Record(new WindowsChange("power", $"План электропитания (был «{oldName}»)", DateTime.Now, "power", null, null, oldGuid, false));

    public static void RecordMouse(int[] old) =>
        Record(new WindowsChange("mouse", "Акселерация мыши выключена", DateTime.Now, "mouse", null, null, string.Join(",", old), false));

    /// <summary>Restores the original value and removes the entry. Returns a message for the user.</summary>
    public static string Undo(WindowsChange change)
    {
        switch (change.Kind)
        {
            case "registry":
                using (var key = Registry.CurrentUser.CreateSubKey(change.Key!))
                {
                    if (change.OldValue == null) key.DeleteValue(change.Name!, throwOnMissingValue: false);
                    else if (change.OldIsDword) key.SetValue(change.Name!, int.Parse(change.OldValue), RegistryValueKind.DWord);
                    else key.SetValue(change.Name!, change.OldValue);
                }
                break;
            case "power":
                if (!SystemInfo.SetPowerPlan(change.OldValue!))
                    return "Не получилось вернуть план: возможно, его больше нет в Windows.";
                break;
            case "mouse":
                MouseSettings.Thresholds = change.OldValue!.Split(',').Select(int.Parse).ToArray();
                break;
        }
        Save(All().Where(c => c.Id != change.Id).ToList());
        return $"Возвращено: {change.Title}.";
    }
}
