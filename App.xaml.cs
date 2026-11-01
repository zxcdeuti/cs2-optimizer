using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CS2Optimizer.Services;
using CS2Optimizer.Views;

namespace CS2Optimizer;

public partial class App : Application
{
    static readonly string MainMutexName = TestScope.Name(@"Local\CS2Optimizer.Main");
    static readonly string ShowEventName = TestScope.Name(@"Local\CS2Optimizer.Show");

    const string AfterUpdateArg = "--after-update";
    const string RestoreOverlayArg = "--restore-overlay";

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

#if DEBUG
        // Test hook: render the dialogs to PNG files, without touching anything else.
        int shots = Array.IndexOf(e.Args, "--dialog-snapshot");
        if (shots >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var variants = new (string File, AppDialog Dialog)[]
            {
                ("confirm", new AppDialog("Подготовить к игре?", "Эти программы будут закрыты. Несохранённое в них может пропасть. Вопрос можно отключить в настройках.",
                    "Закрыть и играть", "Играть, не закрывая", "Отмена", false, ["Firefox", "Discord", "Telegram"])),
                ("danger", new AppDialog("Удалить историю замеров?", "Все сохранённые сессии будут удалены без возможности вернуть.", "Удалить", null, "Отмена", true, null)),
                ("info", new AppDialog("Сначала закрой CS2", "Обновление перезапускает приложение, поэтому во время игры оно не ставится.", "Понятно", null, null, false, null)),
            };
            int left = variants.Length;
            foreach (var (file, dialog) in variants)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -3000;
                dialog.ContentRendered += (_, _) =>
                {
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(dialog);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var f = File.Create(Path.Combine(e.Args[shots + 1], file + ".png"))) png.Save(f);
                    dialog.Close();
                    if (--left == 0) Shutdown();
                };
                dialog.Show();
            }
            return;
        }
#endif

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
        bool afterUpdate = e.Args.Contains(AfterUpdateArg);
        if (!snapshot)
        {
            _mainMutex = new Mutex(true, MainMutexName, out bool first);
            // After an update the previous version is still closing; wait for it instead of giving up.
            if (!first && afterUpdate)
            {
                try { first = _mainMutex.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { first = true; }
            }
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

        if (snapshot) return;
        // An update restart must not pop up the admin prompt on its own, unless the user pressed "install now".
        if (e.Args.Contains(RestoreOverlayArg) || (!afterUpdate && AppSettings.Current.OverlayOnStart))
            OverlayControl.Start(AppSettings.Current.OverlayAsAdmin);
        if (afterUpdate) window.NotifyUpdated();
        Updater.Instance.Start();
    }

    /// <summary>Hands over to the freshly swapped-in exe and exits.</summary>
    public void RestartIntoUpdate(bool hidden, bool restoreOverlay)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        var args = AfterUpdateArg + (hidden ? " --tray" : "") + (restoreOverlay ? " " + RestoreOverlayArg : "");
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args) { UseShellExecute = true });
        Shutdown();
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
        RecorderLog.Write($"Ошибка: {e.Exception.GetType().Name}: {e.Exception.Message}");
        MessageBox.Show(e.Exception.Message, "CS2 Optimizer — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
