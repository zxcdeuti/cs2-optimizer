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

    /// <summary>Adds the session, or updates it in place: a running game is saved every minute under the same start time.</summary>
    public static void Save(GameSession session)
    {
        var all = All();
        all.RemoveAll(s => s.Start == session.Start);
        all.Insert(0, session);
        Directory.CreateDirectory(AppSettings.DataDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all.Take(Keep).ToList()));
    }

    public static void Clear()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}

/// <param name="State">"off", "no-fps", "waiting" or "recording".</param>
public record RecorderState(string State, string Detail, DateTime When);

/// <summary>What the recorder is doing, written by the watermark process and shown on the sessions page, plus a plain log.</summary>
public static class RecorderLog
{
    public static readonly string LogPath = Path.Combine(AppSettings.DataDir, "overlay.log");
    static readonly string StatePath = Path.Combine(AppSettings.DataDir, "recorder-state.json");
    const long MaxLogBytes = 256 * 1024;
    static string? _lastState;

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes) File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static void Status(string state, string detail)
    {
        if (state + detail == _lastState) return;
        _lastState = state + detail;
        Write($"[{state}] {detail}");
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new RecorderState(state, detail, DateTime.Now)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static RecorderState? Read()
    {
        try { return JsonSerializer.Deserialize<RecorderState>(File.ReadAllText(StatePath)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The running watermark marks the state file as fresh, so a frozen one can be told from a working one.</summary>
    public static void Beat()
    {
        try { if (File.Exists(StatePath)) File.SetLastWriteTimeUtc(StatePath, DateTime.UtcNow); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>For a watermark process that is running: whether it stopped reporting. Just started ones haven't written "off" over yet.</summary>
    public static bool IsStuck() =>
        Read()?.State is "recording" or "waiting" or "no-fps"
        && DateTime.UtcNow - File.GetLastWriteTimeUtc(StatePath) > TimeSpan.FromSeconds(20);
}

/// <summary>Runs inside the watermark process, the one with FPS access: one session per CS2 launch.</summary>
public sealed class SessionRecorder(FpsMonitor? fps)
{
    // Shorter runs (a crash, a quick settings check) would only add noise to the comparison.
#if DEBUG
    static readonly double MinMinutes = TestScope.Arg("--test-pid") != null ? 0.1 : 1;
#else
    const double MinMinutes = 1;
#endif
    const int CheckpointEverySeconds = 60;

    int _pid;
    DateTime _start;
    string _preset = "";
    double? _cpuMax, _gpuMax;
    double _cpuSum, _gpuSum;
    int _samples, _gpuSamples;

    public void Tick(Stats s)
    {
        if (fps == null)
        {
            RecorderLog.Status("no-fps", FpsMonitor.LastError ?? "FPS недоступен.");
            return;
        }

        int pid = FindGame();
        if (pid != _pid)
        {
            Finish();
            if (pid != 0) Begin(pid);
        }
        if (_pid == 0)
        {
            RecorderLog.Status("waiting", "Ждём запуска CS2.");
            return;
        }

        _samples++;
        _cpuSum += s.CpuLoad;
        if (s.CpuTemp is { } c) _cpuMax = Math.Max(_cpuMax ?? 0, c);
        if (s.GpuTemp is { } g) _gpuMax = Math.Max(_gpuMax ?? 0, g);
        if (s.GpuLoad is { } l) { _gpuSum += l; _gpuSamples++; }

        // A crash or a power cut then costs at most a minute of the game, not all of it.
        if (_samples % CheckpointEverySeconds == 0)
        {
            RecorderLog.Write($"Проверка: процесс {_pid}, кадров пришло {fps.TrackedPresents}, на переднем плане был {fps.LastForeground}.");
            Save(fps.FramesSoFar());
        }
    }

    int FindGame()
    {
#if DEBUG
        if (TestScope.Arg("--test-pid") is { } testPid) return int.Parse(testPid);
#endif
        var cs2 = Process.GetProcessesByName("cs2");
        try
        {
            if (cs2.Length > 1 && _lastGameCount != cs2.Length)
                RecorderLog.Write($"Найдено процессов cs2: {cs2.Length} ({string.Join(", ", cs2.Select(p => p.Id))}).");
            _lastGameCount = cs2.Length;

            // Stay on the process being recorded: the order of several processes isn't stable, and
            // hopping between them would restart the session every second.
            if (cs2.Any(p => p.Id == _pid)) return _pid;
            return (cs2.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero) ?? cs2.FirstOrDefault())?.Id ?? 0;
        }
        finally
        {
            foreach (var p in cs2) p.Dispose();
        }
    }

    int _lastGameCount;

    void Begin(int pid)
    {
        (_pid, _start, _cpuMax, _gpuMax, _cpuSum, _gpuSum, _samples, _gpuSamples) = (pid, DateTime.Now, null, null, 0, 0, 0, 0);
        _preset = GraphicsPresets.DetectCurrent();
        fps!.Track(pid);
        RecorderLog.Status("recording", $"Идёт запись с {_start:HH:mm}.");
    }

    /// <summary>Saves the session in progress, if it was long enough to mean anything.</summary>
    public void Finish()
    {
        if (_pid == 0 || fps == null) return;
        int pid = _pid;
        _pid = 0;
        var frames = fps.StopTracking();
        RecorderLog.Write($"Сессия pid={pid} завершена: кадров {frames.Frames}, {frames.Seconds:0.0} с на переднем плане, тиков {_samples}.");
        Save(frames);
    }

    void Save(FrameTimes frames)
    {
        if (frames.Seconds < MinMinutes * 60)
        {
            RecorderLog.Write($"Не сохраняю: {frames.Seconds:0.0} с меньше минимума {MinMinutes * 60:0} с.");
            return;
        }
        try
        {
            SessionLog.Save(new GameSession(_start, frames.Seconds / 60, frames.AverageFps, frames.Low1PercentFps,
                _cpuMax, _gpuMax, _samples > 0 ? _cpuSum / _samples : 0, _gpuSamples > 0 ? _gpuSum / _gpuSamples : null, _preset));
            RecorderLog.Write($"Сохранено: {frames.AverageFps:0} FPS, 1% low {frames.Low1PercentFps:0}, {frames.Seconds / 60:0.0} мин.");
        }
        catch (Exception e)
        {
            // Losing one save must not take the watermark down in the middle of a game.
            RecorderLog.Write($"Ошибка сохранения: {e.GetType().Name}: {e.Message}");
        }
    }
}
