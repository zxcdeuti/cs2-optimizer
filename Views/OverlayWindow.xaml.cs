using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

/// <summary>Click-through, always-on-top watermark that lives in its own process.</summary>
public partial class OverlayWindow : Window
{
    static readonly string MutexName = TestScope.Name(@"Local\CS2Optimizer.Overlay");

    static Mutex? _instance;

    readonly Telemetry _telemetry = new(withFps: true);
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    OverlaySettings _settings = OverlaySettings.Load();
    DateTime _settingsStamp;
    RegisteredWaitHandle? _hotkeyWait;
    readonly SessionRecorder _recorder;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
        SizeChanged += (_, _) => Place();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _recorder = new SessionRecorder(_telemetry.Fps);
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _hotkeyWait?.Unregister(null);
            _recorder.Finish();
            RecorderLog.Status("off", "Ватермарка выключена.");
            _telemetry.Dispose();
        };
        Loaded += (_, _) => Tick();
        new Thread(Watchdog) { IsBackground = true, Name = "Watermark watchdog" }.Start();
#if DEBUG
        if (TestScope.Arg("--test-seconds") is { } seconds)
        {
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(int.Parse(seconds)) };
            stop.Tick += (_, _) => { stop.Stop(); Close(); };
            stop.Start();
        }
        if (TestScope.Arg("--test-hang") is { } hangAfter)
        {
            var hang = new DispatcherTimer { Interval = TimeSpan.FromSeconds(int.Parse(hangAfter)) };
            hang.Tick += (_, _) => Thread.Sleep(Timeout.Infinite);
            hang.Start();
        }
#endif
    }

    bool _closing;
    long _lastTick = DateTime.UtcNow.Ticks;
    int _ticks;

    static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(15);
    const int StopGraceSeconds = 6;

    /// <summary>
    /// The timer on the window closes it when Enabled turns false, but if the UI thread stalls that never happens, and
    /// nothing else can stop an elevated process. This thread makes the same check on its own and records the stall.
    /// </summary>
    void Watchdog()
    {
        bool stalled = false;
        int offFor = 0;
        while (true)
        {
            Thread.Sleep(1000);

            var last = new DateTime(Interlocked.Read(ref _lastTick), DateTimeKind.Utc);
            bool stallNow = DateTime.UtcNow - last > StallAfter;
            if (stallNow && !stalled)
            {
                RecorderLog.Write($"Ватермарка не отвечает: таймер в последний раз сработал в {last.ToLocalTime():HH:mm:ss}, диспетчер завершается: {Dispatcher.HasShutdownStarted}.");
                // Tells a blocked UI thread from a timer that just stopped, and revives the latter.
                Dispatcher.BeginInvoke(() =>
                {
                    RecorderLog.Write("Диспетчер ответил, перезапускаю таймер.");
                    _timer.Stop();
                    _timer.Start();
                });
            }
            else if (stalled && !stallNow)
                RecorderLog.Write("Ватермарка снова отвечает.");
            stalled = stallNow;

            offFor = OverlaySettings.TryLoad() is { Enabled: false } ? offFor + 1 : 0;
            if (offFor >= StopGraceSeconds) ForceExit();
        }
    }

    void ForceExit()
    {
        RecorderLog.Write("Окно не закрылось после команды выключения, завершаю процесс.");
        // Keep what can be kept, but none of it may keep the process alive.
        try
        {
            Task.Run(() =>
            {
                _recorder.Finish();
                _telemetry.Dispose();
            }).Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException e) { RecorderLog.Write($"Ошибка при завершении: {e.InnerException?.Message}"); }
        RecorderLog.Status("off", "Ватермарка выключена.");
        Process.GetCurrentProcess().Kill();
    }

    /// <summary>
    /// The main app owns the hotkey and signals this event; it may start after us, so keep trying to open it.
    /// We only open, never create it: an event made by an elevated process couldn't be opened by the main app.
    /// </summary>
    void ListenForHotkey()
    {
        if (_hotkeyWait != null || !EventWaitHandle.TryOpenExisting(WatermarkHotkey.ToggleEventName, out var toggle)) return;
        // BeginInvoke, not Invoke: this runs on a pool thread, where an exception would take the whole process down.
        // The press can land while the window is closing, and a closing window can't change visibility.
        _hotkeyWait = ThreadPool.RegisterWaitForSingleObject(toggle,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (_closing) return;
                Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                RecorderLog.Write(Visibility == Visibility.Visible ? "Горячая клавиша: ватермарка показана." : "Горячая клавиша: ватермарка скрыта.");
            }),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static bool TryClaimSingleInstance()
    {
        _instance = new Mutex(true, MutexName, out bool created);
        return created;
    }

    /// <summary>True when an overlay process holds the mutex — including an elevated one we can't open.</summary>
    public static bool IsRunning()
    {
        try { Mutex.OpenExisting(MutexName).Dispose(); return true; }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }

    void Tick()
    {
        Interlocked.Exchange(ref _lastTick, DateTime.UtcNow.Ticks);
        if (++_ticks % 5 == 1) RecorderLog.Beat();

        var stamp = File.Exists(OverlaySettings.FilePath) ? File.GetLastWriteTimeUtc(OverlaySettings.FilePath) : default;
        if (stamp != _settingsStamp)
        {
            // Being written right now: a failed read must not look like "turned off", so read again on the next tick.
            if (OverlaySettings.TryLoad() is { } loaded)
            {
                _settingsStamp = stamp;
                _settings = loaded;
                if (!_settings.Enabled)
                {
                    RecorderLog.Write("Команда выключения получена.");
                    Close();
                    return;
                }
                Place();
            }
        }
        ListenForHotkey();

        var stats = _telemetry.Sample();
        Bar.Show(stats, _settings);
        _recorder.Tick(stats);

        // Games sometimes push themselves above other topmost windows; take the spot back.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    void Place()
    {
        double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight, edge = _settings.Offset;
        Left = _settings.Corner.EndsWith("Right") ? w - ActualWidth - edge : edge;
        Top = _settings.Corner.StartsWith("Bottom") ? h - ActualHeight - edge : edge;
    }

    void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, style | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate);
    }

    const int GwlExStyle = -20;
    const nint WsExTransparent = 0x20, WsExLayered = 0x80000, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    [DllImport("user32.dll")] static extern nint GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
