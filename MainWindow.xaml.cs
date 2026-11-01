using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CS2Optimizer.Services;
using CS2Optimizer.Views;

namespace CS2Optimizer;

public partial class MainWindow : Window
{
    readonly Dictionary<string, UserControl> _views = [];
    readonly TrayIcon _tray = new();
    bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        // Fit small or heavily scaled screens (e.g. 1366×768 at 125%).
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 32);
        Height = Math.Min(Height, area.Height - 32);
        // Without this a WindowStyle=None window spills past the screen edges when maximized.
        StateChanged += (_, _) =>
        {
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            if (WindowState == WindowState.Minimized && AppSettings.Current.MinimizeToTray) HideToTray();
        };

        VersionLabel.Text = "v" + Updater.CurrentVersion[..Updater.CurrentVersion.LastIndexOf('.')];
        Updater.Instance.Changed += ShowUpdateNotice;
        Updater.Instance.QuietInstallPossible += InstallIfQuiet;

        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
        _tray.PrepareRequested += PrepareForGame;
        _tray.OverlayToggleRequested += () =>
        {
            if (OverlayControl.IsRunning) OverlayControl.Stop();
            else OverlayControl.Start(AppSettings.Current.OverlayAsAdmin);
        };

        var args = Environment.GetCommandLineArgs();
        int page = Array.IndexOf(args, "--page");
        var start = page >= 0 && page + 1 < args.Length ? args[page + 1] : "system";
        var nav = LogicalTreeHelper.FindLogicalNode(this, "Nav_" + start) as RadioButton;
        if (nav != null) nav.IsChecked = true;
        Navigate(nav != null ? start : "system");

        // Test hook: open a map (and place) directly, e.g. --nav maps:nuke:Hut.
        int go = Array.IndexOf(args, "--nav");
        if (go >= 0 && go + 1 < args.Length)
            Loaded += (_, _) => OpenMap(args[go + 1]);

        int snap = Array.IndexOf(args, "--snapshot");
        if (snap >= 0 && snap + 1 < args.Length)
            Loaded += async (_, _) => { await Task.Delay(3500); SaveSnapshot(args[snap + 1]); ExitApp(); };

        Loaded += async (_, _) =>
        {
            var cards = await Task.Run(SystemInfo.Hardware);
            FooterGpu.Text = cards.FirstOrDefault(c => c.Label == "ВИДЕОКАРТА")?.Value.ToUpperInvariant() ?? "";
            FooterDisplay.Text = cards.FirstOrDefault(c => c.Label == "ЭКРАН") is { } d ? $"{d.Value.Replace(" ", "")} · {d.Detail}".ToUpperInvariant() : "";
        };
    }

    // ── tray ───────────────────────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && AppSettings.Current.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    void HideToTray()
    {
        Hide();
        if (AppSettings.Current.TrayHintShown) return;
        _tray.Notify("CS2 Optimizer в трее", "Приложение продолжает работать. Открыть — клик по значку, выйти — правой кнопкой → Выход.");
        AppSettings.Save(AppSettings.Current with { TrayHintShown = true });
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ── one click before a match ───────────────────────────────────────────────

    void Play_Click(object sender, RoutedEventArgs e) => PrepareForGame();

    async void PrepareForGame()
    {
        var s = AppSettings.Current;
        PlayBtn.IsEnabled = false;
        try
        {
            var toClose = s.PrepCloseApps ? (await Task.Run(ProcessCleaner.Scan)).Where(g => g.IsSelected).ToList() : [];
            if (toClose.Count > 0 && s.PrepConfirm)
            {
                var answer = AppDialog.Ask(this, "Подготовить к игре?",
                    "Эти программы будут закрыты. Несохранённое в них может пропасть. Вопрос можно отключить в настройках.",
                    "Закрыть и играть", secondary: "Играть, не закрывая", items: toClose.Select(g => g.Display).ToList());
                // Quitting while the question is open also dismisses it; that must not count as an answer.
                if (_exiting || answer == DialogChoice.Cancel) return;
                if (answer == DialogChoice.Secondary) toClose = [];
            }

            ShowPlayStatus("Готовлю…");
            int closed = toClose.Count;
            if (closed > 0) await Task.Run(() => ProcessCleaner.Close(toClose));
            if (s.PrepOverlay) OverlayControl.Start(s.OverlayAsAdmin);

            if (Cs2Paths.IsGameRunning)
            {
                ShowPlayStatus("CS2 уже запущена.");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://rungameid/730") { UseShellExecute = true });
                ShowPlayStatus(closed > 0 ? $"Закрыто программ: {closed}. CS2 запускается…" : "CS2 запускается…");
            }
            catch (Win32Exception)
            {
                ShowPlayStatus("Не удалось запустить CS2: похоже, Steam не установлен.");
            }
        }
        finally
        {
            PlayBtn.IsEnabled = true;
        }
    }

    async void ShowPlayStatus(string text)
    {
        PlayStatus.Text = text;
        PlayStatus.Visibility = Visibility.Visible;
        if (!IsVisible) _tray.Notify("CS2 Optimizer", text);
        await Task.Delay(TimeSpan.FromSeconds(8));
        if (PlayStatus.Text == text) PlayStatus.Visibility = Visibility.Collapsed;
    }

    // ── updates ────────────────────────────────────────────────────────────────

    void ShowUpdateNotice()
    {
        var version = Updater.Instance.ReadyVersion;
        UpdateNotice.Visibility = version != null ? Visibility.Visible : Visibility.Collapsed;
        if (version != null) UpdateText.Text = $"ОБНОВЛЕНИЕ {version} ГОТОВО. УСТАНОВИТСЯ САМО, КОГДА НЕ БУДЕТ МЕШАТЬ";
    }

    /// <summary>Never interrupts: only while the app is out of sight and neither the game nor the watermark runs.</summary>
    void InstallIfQuiet()
    {
        if (IsVisible || Updater.GameOrOverlayRunning || !Updater.Instance.SwapExe()) return;
        Restart(hidden: true, restoreOverlay: false);
    }

    void Restart(bool hidden, bool restoreOverlay)
    {
        _exiting = true;
        _tray.Dispose();
        ((App)Application.Current).RestartIntoUpdate(hidden, restoreOverlay);
    }

    async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (Cs2Paths.IsGameRunning)
        {
            AppDialog.Inform(this, "Сначала закрой CS2", "Обновление перезапускает приложение, поэтому во время игры оно не ставится.");
            return;
        }
        bool overlay = OverlayControl.IsRunning;
        if (overlay)
        {
            OverlayControl.Stop();
            await Task.Delay(TimeSpan.FromSeconds(2)); // it notices within a second and closes
        }
        if (Updater.Instance.SwapExe()) Restart(hidden: false, restoreOverlay: overlay);
    }

    public void NotifyUpdated() =>
        _tray.Notify("CS2 Optimizer обновлён", $"Установлена версия {Updater.CurrentVersion}.");

    public void ExitApp()
    {
        _exiting = true;
        // Leaving is the perfect moment: the next start is already the new version.
        if (Updater.Instance.ReadyVersion != null && !Cs2Paths.IsGameRunning) Updater.Instance.SwapExe();
        _tray.Dispose();
        // Shutdown also closes a window that was never shown (started with --tray).
        Application.Current.Shutdown();
    }

    // ── navigation ─────────────────────────────────────────────────────────────

    /// <summary>Renders the window itself to a PNG — used to check the UI without capturing the screen.</summary>
    void SaveSnapshot(string path)
    {
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(this);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        png.Save(file);
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Fires once during InitializeComponent, before Host exists.
        if (Host != null && sender is RadioButton { Tag: string tag })
            Navigate(tag);
    }

    void Navigate(string tag)
    {
        if (!_views.TryGetValue(tag, out var view))
        {
            view = tag switch
            {
                "graphics" => new GraphicsView(),
                "crosshair" => new CrosshairView(),
                "cleanup" => new CleanupView(),
                "watermark" => new WatermarkView(),
                "sens" => new SensView(),
                "launch" => new LaunchView(),
                "config" => new ConfigView(),
                "maps" => new MapsView(),
                "settings" => new SettingsView(),
                "sessions" => new SessionsView(),
                _ => new SystemView(),
            };
            _views[tag] = view;
        }
        Host.Content = view;
        Dispatcher.InvokeAsync(() => (view.Content as ScrollViewer)?.ScrollToTop(),
            System.Windows.Threading.DispatcherPriority.ContextIdle);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        HostShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    /// <summary>"maps:&lt;id&gt;[:place]" — switches to the maps page and opens that map.</summary>
    void OpenMap(string target)
    {
        var parts = target.Split(':', 3);
        if (parts[0] != "maps") return;
        Nav_maps.IsChecked = true;
        if (parts.Length > 1 && _views.TryGetValue("maps", out var view) && view is MapsView maps)
            maps.Open(parts[1], parts.Length > 2 ? parts[2] : null);
    }

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
