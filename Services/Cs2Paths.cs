using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CS2Optimizer.Services;

/// <summary>Locates Steam, the CS2 install and its config files.</summary>
public static class Cs2Paths
{
    public static string? SteamPath =>
        (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string)?.Replace('/', '\\');

    public static List<string> LibraryFolders()
    {
        var steam = SteamPath;
        if (steam == null) return [];
        var libs = new List<string> { steam };
        try
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch (IOException) { }
        return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? GameDir => LibraryFolders()
        .Select(l => Path.Combine(l, "steamapps", "common", "Counter-Strike Global Offensive"))
        .FirstOrDefault(d => File.Exists(Path.Combine(d, "game", "bin", "win64", "cs2.exe")));

    public static string? Cs2Exe => GameDir is { } d ? Path.Combine(d, "game", "bin", "win64", "cs2.exe") : null;

    public static string? CfgDir => GameDir is { } d ? Path.Combine(d, "game", "csgo", "cfg") : null;

    /// <summary>cs2_video.txt of the Steam account that played most recently.</summary>
    public static string? VideoConfig
    {
        get
        {
            var userdata = SteamPath is { } s ? Path.Combine(s, "userdata") : null;
            if (userdata == null || !Directory.Exists(userdata)) return null;
            return Directory.GetDirectories(userdata)
                .Select(d => Path.Combine(d, "730", "local", "cfg", "cs2_video.txt"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }
    }

    public static bool IsGameRunning => System.Diagnostics.Process.GetProcessesByName("cs2").Length > 0;
}
