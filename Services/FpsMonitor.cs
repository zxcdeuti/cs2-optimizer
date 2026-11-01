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
        _session = new TraceEventSession(TestScope.Name("CS2Optimizer-FPS")) { StopOnDispose = true };
        _session.EnableProvider(DxgiProvider);
        _session.Source.AllEvents += e =>
        {
            if ((int)e.ID != PresentStart || e.ProviderGuid != DxgiProvider) return;
            _presents.AddOrUpdate(e.ProcessID, 1, (_, n) => n + 1);
            if (e.ProcessID == _trackedPid) CountFrame(e.TimeStampRelativeMSec);
        };
        new Thread(() => _session.Source.Process()) { IsBackground = true, Name = "ETW FPS" }.Start();
    }

    public static bool IsElevated => TraceEventSession.IsElevated() == true;

    /// <summary>Why FPS isn't available, for the log and the sessions page.</summary>
    public static string? LastError { get; private set; }

    public static FpsMonitor? TryStart()
    {
        if (!IsElevated)
        {
            LastError = "Ватермарка запущена без прав администратора.";
            return null;
        }
        // Anything here only costs the FPS counter, so nothing may take the watermark down with it.
        try { return new FpsMonitor(); }
        catch (Exception e)
        {
            LastError = $"Системный трейсинг не запустился: {e.GetType().Name}: {e.Message}";
            return null;
        }
    }

    /// <summary>Frames per second of the foreground app since the previous call, or null if it isn't presenting.</summary>
    public int? Sample()
    {
        double seconds = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        var counts = _presents.ToArray();
        _presents.Clear();

        GetWindowThreadProcessId(GetForegroundWindow(), out uint foreground);
        LastForeground = foreground;
        _trackedInFront = foreground == (uint)_trackedPid;
#if DEBUG
        if (TestScope.Arg("--test-pid") != null) _trackedInFront = true; // a test presenter isn't the foreground app
#endif
        int frames = counts.FirstOrDefault(c => c.Key == foreground).Value;
        return frames > 0 && seconds > 0 ? (int)Math.Round(frames / seconds) : null;
    }

    // ── frame-time histogram for session stats ─────────────────────────────────

    volatile int _trackedPid;
    volatile bool _trackedInFront;
    double _lastFrameMs;
    readonly FrameTimes _frames = new();

    /// <summary>Starts counting this process's frames while it is in the foreground (alt-tabbed time is skipped).</summary>
    public void Track(int pid)
    {
        lock (_frames) { _frames.Clear(); _lastFrameMs = 0; }
        Interlocked.Exchange(ref _trackedPresents, 0);
        _trackedPid = pid;
    }

    long _trackedPresents;

    /// <summary>For the log: all frames seen from the tracked process, counted or not, and who held the foreground.</summary>
    public long TrackedPresents => Interlocked.Read(ref _trackedPresents);
    public uint LastForeground { get; private set; }

    public FrameTimes FramesSoFar()
    {
        lock (_frames) return _frames.Copy();
    }

    public FrameTimes StopTracking()
    {
        _trackedPid = 0;
        lock (_frames) return _frames.Copy();
    }

    void CountFrame(double nowMs)
    {
        Interlocked.Increment(ref _trackedPresents);
        lock (_frames)
        {
            if (!_trackedInFront) { _lastFrameMs = 0; return; }
            if (_lastFrameMs > 0) _frames.Add(nowMs - _lastFrameMs);
            _lastFrameMs = nowMs;
        }
    }

    public void Dispose() => _session.Dispose();

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
