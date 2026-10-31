using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace CS2Optimizer.Services;

/// <summary>
/// Counts DXGI Present calls per process through ETW — the same source PresentMon and most FPS
/// counters use — and reports the frame rate of whatever window is in the foreground.
/// ETW sessions need administrator rights, so this only starts in an elevated process.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    const int PresentStart = 42;

    readonly TraceEventSession _session;
    readonly ConcurrentDictionary<int, int> _presents = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();

    FpsMonitor()
    {
        _session = new TraceEventSession("CS2Optimizer-FPS") { StopOnDispose = true };
        _session.EnableProvider(DxgiProvider);
        _session.Source.AllEvents += e =>
        {
            if ((int)e.ID == PresentStart && e.ProviderGuid == DxgiProvider)
                _presents.AddOrUpdate(e.ProcessID, 1, (_, n) => n + 1);
        };
        new Thread(() => _session.Source.Process()) { IsBackground = true, Name = "ETW FPS" }.Start();
    }

    public static bool IsElevated => TraceEventSession.IsElevated() == true;

    public static FpsMonitor? TryStart()
    {
        if (!IsElevated) return null;
        try { return new FpsMonitor(); }
        catch (Exception e) when (e is UnauthorizedAccessException or COMException or InvalidOperationException) { return null; }
    }

    /// <summary>Frames per second of the foreground app since the previous call, or null if it isn't presenting.</summary>
    public int? Sample()
    {
        double seconds = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        var counts = _presents.ToArray();
        _presents.Clear();

        GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
        int frames = counts.FirstOrDefault(c => c.Key == foreground).Value;
        return frames > 0 && seconds > 0 ? (int)Math.Round(frames / seconds) : null;
    }

    public void Dispose() => _session.Dispose();

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
