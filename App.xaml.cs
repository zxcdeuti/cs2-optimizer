using System.Windows;
using System.Windows.Threading;
using CS2Optimizer.Services;
using CS2Optimizer.Views;

namespace CS2Optimizer;

public partial class App : Application
{
    const string MainMutexName = @"Local\CS2Optimizer.Main";
    const string ShowEventName = @"Local\CS2Optimizer.Show";

    Mutex? _mainMutex;
    WatermarkHotkey? _hotkey;

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);

        // The same exe runs the in-game watermark as a separate (usually elevated) process.
        if (e.Args.Contains("--overlay"))
        {
            if (!OverlayWindow.TryClaimSingleInstance()) { Shutdown(); return; }
            new OverlayWindow().Show();
            return;
        }

        // One main window: a second launch (e.g. from the shortcut while we sit in the tray) just brings it back.
        // Snapshot runs used for checking the UI are exempt.
        bool snapshot = e.Args.Contains("--snapshot");
        if (!snapshot)
        {
            _mainMutex = new Mutex(true, MainMutexName, out bool first);
            if (!first)
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show)) show.Set();
                Shutdown();
                return;
            }
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown; // the window may live hidden in the tray
        var window = new MainWindow();
        if (!snapshot)
        {
            ListenForShowRequests(window);
            _hotkey = new WatermarkHotkey();
        }
        if (!e.Args.Contains("--tray")) window.Show();

        if (!snapshot && AppSettings.Current.OverlayOnStart)
            OverlayControl.Start(AppSettings.Current.OverlayAsAdmin);
    }

    void ListenForShowRequests(MainWindow window)
    {
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (show.WaitOne()) Dispatcher.Invoke(window.ShowFromTray);
        }) { IsBackground = true, Name = "Show requests" }.Start();
    }

    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "CS2 Optimizer — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
