using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace CS2Optimizer.Services;

public class ProcGroup : INotifyPropertyChanged
{
    bool _selected;

    public required string Name { get; init; }
    public required string Display { get; init; }
    public int Count { get; init; }
    public long Bytes { get; init; }
    public bool Recommended { get; init; }

    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }

    /// <summary>Length of the memory bar in the list, relative to the heaviest program.</summary>
    public double BarWidth { get; set; }

    public string Memory => ProcessCleaner.FormatBytes(Bytes);
    public string Sub => Count == 1 ? "1 процесс" : $"{Count} процессов";
    public string Initial => Display[..1].ToUpperInvariant();
    public Visibility TagVisibility => Recommended ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class ProcessCleaner
{
    // Apps that commonly eat RAM/CPU in the background while gaming.
    static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firefox"] = "Firefox", ["chrome"] = "Google Chrome", ["msedge"] = "Microsoft Edge",
        ["opera"] = "Opera", ["opera_gx"] = "Opera GX", ["browser"] = "Яндекс Браузер", ["brave"] = "Brave",
        ["Discord"] = "Discord", ["Telegram"] = "Telegram", ["Spotify"] = "Spotify",
        ["HYP"] = "HoYoPlay", ["EpicGamesLauncher"] = "Epic Games", ["Battle.net"] = "Battle.net",
        ["EADesktop"] = "EA App", ["RiotClientServices"] = "Riot Client", ["GalaxyClient"] = "GOG Galaxy",
        ["NVIDIA Overlay"] = "NVIDIA Overlay", ["ms-teams"] = "Microsoft Teams", ["OneDrive"] = "OneDrive",
        ["Zoom"] = "Zoom", ["Skype"] = "Skype", ["WhatsApp"] = "WhatsApp", ["qbittorrent"] = "qBittorrent",
        ["uTorrent"] = "uTorrent",
    };

    // Never offered: Windows itself, Steam/CS2, drivers, antivirus, this app and Claude.
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "LsaIso", "svchost", "dwm", "explorer", "sihost", "fontdrvhost", "ctfmon", "conhost", "dllhost",
        "taskhostw", "RuntimeBroker", "SearchHost", "SearchIndexer", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "ApplicationFrameHost", "SystemSettings", "LockApp",
        "SecurityHealthSystray", "SecurityHealthService", "MsMpEng", "NisSrv", "audiodg", "spoolsv", "WUDFHost",
        "WmiPrvSE", "dasHost", "smartscreen", "Widgets", "WidgetService", "PhoneExperienceHost",
        "steam", "steamwebhelper", "steamservice", "cs2", "NVDisplay.Container", "nvcontainer", "nvsphelper64",
        "CS2Optimizer", "claude", "dotnet",
    };

    const long MinBytes = 60L << 20;

    public static List<ProcGroup> Scan()
    {
        int session = Process.GetCurrentProcess().SessionId;
        var groups = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != session || Protected.Contains(p.ProcessName)) continue;
                    groups.TryGetValue(p.ProcessName, out var g);
                    groups[p.ProcessName] = (g.Count + 1, g.Bytes + p.WorkingSet64);
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
            }
        }

        return groups
            .Where(g => g.Value.Bytes >= MinBytes || Known.ContainsKey(g.Key))
            .Select(g => new ProcGroup
            {
                Name = g.Key,
                Display = Known.TryGetValue(g.Key, out var d) ? d : g.Key,
                Count = g.Value.Count,
                Bytes = g.Value.Bytes,
                Recommended = Known.ContainsKey(g.Key),
                IsSelected = Known.ContainsKey(g.Key),
            })
            .OrderByDescending(g => g.Recommended)
            .ThenByDescending(g => g.Bytes)
            .ToList();
    }

    /// <summary>Asks each window to close politely, then ends whatever is still running.</summary>
    public static (int Closed, int Failed) Close(IEnumerable<ProcGroup> groups)
    {
        var procs = groups.SelectMany(g => Process.GetProcessesByName(g.Name)).ToList();
        foreach (var p in procs)
        {
            try { p.CloseMainWindow(); } catch (InvalidOperationException) { }
        }

        var deadline = DateTime.Now.AddSeconds(2);
        int closed = 0, failed = 0;
        foreach (var p in procs)
        {
            using (p)
            {
                try
                {
                    var left = deadline - DateTime.Now;
                    if (left > TimeSpan.Zero) p.WaitForExit(left);
                    if (!p.HasExited) { p.Kill(); p.WaitForExit(1500); }
                    closed++;
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    failed++;
                }
            }
        }
        return (closed, failed);
    }

    public static (long Used, long Total) MemoryUsage()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? ((long)(m.TotalPhys - m.AvailPhys), (long)m.TotalPhys) : (0, 0);
    }

    public static string FormatBytes(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} ГБ" : $"{b >> 20} МБ";

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
