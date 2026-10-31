using System.Text.Json;

namespace CS2Optimizer.Services;

/// <summary>
/// Shared between the main window and the overlay process through a JSON file: the overlay polls it,
/// so every change applies live and Enabled=false closes it — even when it runs elevated.
/// </summary>
/// <param name="Look">Panel, Clear (no background) or Capsule.</param>
/// <param name="Layout">Row or Column.</param>
/// <param name="Scale">Percent.</param>
/// <param name="Opacity">Background opacity, percent.</param>
/// <param name="Tag">Free text next to the logo, e.g. a nickname.</param>
/// <param name="Offset">Distance from the screen edge, px.</param>
public record OverlaySettings(
    bool Enabled = false,
    string Corner = "TopLeft",
    bool ShowFps = true,
    bool ShowCpu = true,
    bool ShowGpu = true,
    string Look = "Panel",
    string Layout = "Row",
    string Accent = "#D4FF3F",
    int Scale = 100,
    int Opacity = 88,
    bool ShowLogo = true,
    bool ShowLabels = true,
    string Tag = "",
    int Offset = 14)
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CS2Optimizer", "overlay.json");

    public static OverlaySettings Load()
    {
        try { return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
