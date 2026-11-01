using System.Diagnostics;
using System.Text.Json;

namespace CS2Optimizer.Services;

/// <summary>
/// Frame-time histogram (0.1 ms bins up to 200 ms): an hour at 300 FPS is a million frames,
/// so keeping counts instead of every frame keeps memory flat while percentiles stay exact enough.
/// </summary>
public sealed class FrameTimes
{
    const double BinMs = 0.1;
    const int Bins = 2000;
    // Longer gaps are loading screens or a stalled game, not frames worth averaging.
    const double MaxGapMs = 2000;

    long[] _bins = new long[Bins];
    double _totalMs;

    public long Frames { get; private set; }

    public void Add(double ms)
    {
        if (ms <= 0 || ms > MaxGapMs) return;
        _bins[Math.Min((int)(ms / BinMs), Bins - 1)]++;
        _totalMs += ms;
        Frames++;
    }

    public void Clear()
    {
        Array.Clear(_bins);
        _totalMs = 0;
        Frames = 0;
    }

    public FrameTimes Copy() => new() { _bins = (long[])_bins.Clone(), _totalMs = _totalMs, Frames = Frames };

    public double Seconds => _totalMs / 1000;

    public double AverageFps => _totalMs > 0 ? Frames / Seconds : 0;

    /// <summary>The usual "1% low": average FPS across the slowest 1% of frames.</summary>
    public double Low1PercentFps
    {
        get
        {
            long want = Math.Max(1, Frames / 100), taken = 0;
            double ms = 0;
            for (int i = Bins - 1; i >= 0 && taken < want; i--)
            {
                long use = Math.Min(_bins[i], want - taken);
                ms += use * (i + 0.5) * BinMs;
                taken += use;
            }
            return taken > 0 && ms > 0 ? 1000 * taken / ms : 0;
        }
    }
}

public record GameSession(DateTime Start, double Minutes, double AvgFps, double Low1Fps,
    double? CpuMaxTemp, double? GpuMaxTemp, double CpuAvgLoad, double? GpuAvgLoad, string Preset);

public static class SessionLog
{
    const int Keep = 100;
    static readonly string FilePath = Path.Combine(AppSettings.DataDir, "sessions.json");

    /// <summary>Newest first.</summary>
    public static List<GameSession> All()
    {
        try { return JsonSerializer.Deserialize<List<GameSession>>(File.ReadAllText(FilePath)) ?? []; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public static void Add(GameSession session)
    {
        var all = All();
        all.Insert(0, session);
        Directory.CreateDirectory(AppSettings.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all.Take(Keep)));
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}

/// <summary>Runs inside the watermark process, the one with FPS access: one session per CS2 launch.</summary>
public sealed class SessionRecorder(FpsMonitor? fps)
{
    // Shorter runs (a crash, a quick settings check) would only add noise to the comparison.
    const double MinMinutes = 1;

    int _pid;
    DateTime _start;
    string _preset = "";
    double? _cpuMax, _gpuMax;
    double _cpuSum, _gpuSum;
    int _samples, _gpuSamples;

    public void Tick(Stats s)
    {
        if (fps == null) return;
        var cs2 = Process.GetProcessesByName("cs2");
        int pid = cs2.FirstOrDefault()?.Id ?? 0;
        foreach (var p in cs2) p.Dispose();

        if (pid != _pid)
        {
            Finish();
            if (pid != 0) Begin(pid);
        }
        if (_pid == 0) return;

        _samples++;
        _cpuSum += s.CpuLoad;
        if (s.CpuTemp is { } c) _cpuMax = Math.Max(_cpuMax ?? 0, c);
        if (s.GpuTemp is { } g) _gpuMax = Math.Max(_gpuMax ?? 0, g);
        if (s.GpuLoad is { } l) { _gpuSum += l; _gpuSamples++; }
    }

    void Begin(int pid)
    {
        (_pid, _start, _cpuMax, _gpuMax, _cpuSum, _gpuSum, _samples, _gpuSamples) = (pid, DateTime.Now, null, null, 0, 0, 0, 0);
        _preset = GraphicsPresets.DetectCurrent();
        fps!.Track(pid);
    }

    /// <summary>Saves the session in progress, if it was long enough to mean anything.</summary>
    public void Finish()
    {
        if (_pid == 0 || fps == null) return;
        _pid = 0;
        var frames = fps.StopTracking();
        if (frames.Seconds < MinMinutes * 60) return;
        try
        {
            SessionLog.Add(new GameSession(_start, frames.Seconds / 60, frames.AverageFps, frames.Low1PercentFps,
                _cpuMax, _gpuMax, _samples > 0 ? _cpuSum / _samples : 0, _gpuSamples > 0 ? _gpuSum / _gpuSamples : null, _preset));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
