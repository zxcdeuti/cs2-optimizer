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
        Closed += (_, _) =>
        {
            _recorder.Finish();
            _telemetry.Dispose();
            _hotkeyWait?.Unregister(null);
        };
        Loaded += (_, _) => Tick();
    }

    /// <summary>
    /// The main app owns the hotkey and signals this event; it may start after us, so keep trying to open it.
    /// We only open, never create it: an event made by an elevated process couldn't be opened by the main app.
    /// </summary>
    void ListenForHotkey()
    {
        if (_hotkeyWait != null || !EventWaitHandle.TryOpenExisting(WatermarkHotkey.ToggleEventName, out var toggle)) return;
        _hotkeyWait = ThreadPool.RegisterWaitForSingleObject(toggle,
            (_, _) => Dispatcher.Invoke(() => Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible),
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
        var stamp = File.Exists(OverlaySettings.FilePath) ? File.GetLastWriteTimeUtc(OverlaySettings.FilePath) : default;
        if (stamp != _settingsStamp)
        {
            _settingsStamp = stamp;
            _settings = OverlaySettings.Load();
            if (!_settings.Enabled) { Close(); return; }
            Place();
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
