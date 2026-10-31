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

        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApp;
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

    public void ExitApp()
    {
        _exiting = true;
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
