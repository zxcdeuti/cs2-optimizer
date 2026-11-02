using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class HomeView : UserControl
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    bool _swept, _scanning;
    string? _mapId;

    public HomeView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => ShowWatermark();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShowWatermark();
        ShowGraphics();
        ShowCrosshair();
        ShowLastGame();
        _timer.Start();
        _ = ScanWindows();
        ShowRadar();
        if (!_swept) SweepOnce();
    }

    void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    /// <summary>A short window keeps the status rows in sight; a wide one gets a bigger radar.</summary>
    void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Height < 620;
        Headline.FontSize = compact ? 72 : 104;
        Headline.LineHeight = compact ? 68 : 98;
        double art = Math.Clamp(e.NewSize.Width * 0.66, 660, 860);
        ArtBox.Width = ArtBox.Height = art;
        ArtBox.Margin = new Thickness(0, -art * 0.23, -art * 0.23, 0);
    }

    async Task ScanWindows()
    {
        if (_scanning) return;
        _scanning = true;
        var checks = await Task.Run(SystemInfo.Checks);
        _scanning = false;

        int failed = checks.Count(c => !c.Ok), fixable = checks.Count(c => !c.Ok && c.Fix != null);
        Headline.Text = failed == 0 ? "ГОТОВО\nК ИГРЕ" : "ЕСТЬ ЧТО\nУЛУЧШИТЬ";
        HeadSub.Text = failed == 0
            ? "Все проверки Windows пройдены, можно запускать игру."
            : fixable > 0
                ? $"Не пройдено проверок Windows: {failed}. Исправляется кнопкой: {fixable}."
                : $"Не пройдено проверок Windows: {failed}. Подсказки есть в разделе «Система».";
        FixBtn.Visibility = failed == 0 ? Visibility.Collapsed : Visibility.Visible;
        WinDetail.Text = $"Пройдено проверок: {checks.Count - failed} из {checks.Count}";
        WinState.Text = failed == 0 ? "" : "";
        WinState.Foreground = (Brush)FindResource(failed == 0 ? "OkBrush" : "WarnBrush");
    }

    void ShowGraphics()
    {
        var preset = GraphicsPresets.DetectCurrent();
        GfxDetail.Text = preset == "—" ? "Файл настроек появится после первого запуска CS2." : preset == "Свои настройки" ? preset : "Пресет: " + preset;
    }

    void ShowWatermark()
    {
        bool on = OverlayControl.IsRunning;
        WmDetail.Text = on ? "Работает, игра записывается в замеры." : "Выключена, без неё FPS не записывается.";
        WmBtnText.Text = on ? "Выключить" : "Включить";
    }

    void Overlay_Click(object sender, RoutedEventArgs e)
    {
        if (OverlayControl.IsRunning) OverlayControl.Stop();
        else OverlayControl.Start(AppSettings.Current.OverlayAsAdmin);
        ShowWatermark();
    }

    void ShowCrosshair()
    {
        var s = CrosshairView.LoadState() ?? CrosshairView.DefaultState;
        CrossDetail.Text = $"Длина {s.Length}, толщина {s.Thickness}, промежуток {s.Gap}";
        CrossMini.Children.Clear();
        const int zoom = 3;
        var fill = new SolidColorBrush(Color.FromArgb((byte)s.A, (byte)s.R, (byte)s.G, (byte)s.B));
        foreach (var r in CrosshairView.Parts(s))
        {
            var part = new Rectangle { Width = r.Width * zoom, Height = r.Height * zoom, Fill = fill };
            Canvas.SetLeft(part, 25 + r.X * zoom);
            Canvas.SetTop(part, 17 + r.Y * zoom);
            CrossMini.Children.Add(part);
        }
    }

    void ShowLastGame()
    {
        var all = SessionLog.All();
        LastBody.Visibility = all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (all.Count == 0)
        {
            LastWhen.Text = "Пока замеров нет. Сыграй хотя бы минуту с включённой ватермаркой.";
            return;
        }
        var last = all[0];
        LastWhen.Text = $"{last.Start:dd.MM в HH:mm}, {last.Preset}";
        LastAvg.Text = $"{last.AvgFps:0}";
        LastLow.Text = $"{last.Low1Fps:0}";
        if (all.Count > 1)
        {
            double diff = Math.Round(last.AvgFps) - Math.Round(all[1].AvgFps);
            LastDelta.Text = diff == 0 ? "0" : $"{(diff > 0 ? "+" : "−")}{Math.Abs(diff):0}";
            LastDelta.Foreground = (Brush)FindResource(diff == 0 ? "MutedBrush" : diff > 0 ? "OkBrush" : "DangerBrush");
        }
        else
        {
            LastDelta.Text = "—";
            LastDelta.Foreground = (Brush)FindResource("DimBrush");
        }
    }

    /// <summary>A map the app already has out of the game files if possible; extracting another one would only slow the first screen.</summary>
    async void ShowRadar()
    {
        if (RadarArt.Source != null || Cs2Paths.IsGameRunning) return;
        var cached = MapRadar.Pool.Where(p => MapRadar.IsCached(p.Id)).ToList();
        var (id, name) = cached.Count > 0 ? cached[Random.Shared.Next(cached.Count)] : MapRadar.Pool[0];
        BitmapImage picture;
        try
        {
            picture = await Task.Run(() =>
            {
                var radar = MapRadar.Load(id);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 800;
                bmp.UriSource = new Uri(radar.Layers.GetValueOrDefault("default") ?? radar.Layers.Values.First());
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            });
        }
        catch (Exception ex)
        {
            // No CS2 on this PC or an unreadable map: the page is complete with the rings alone.
            RecorderLog.Write($"Радар на главной не загрузился: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        _mapId = id;
        RadarArt.Source = picture;
        MapLink.Content = $"Открыть {name}";
        MapLinkBox.Visibility = Visibility.Visible;
        RadarArt.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(Animate ? 700 : 0)));
    }

    static bool Animate => SystemParameters.ClientAreaAnimation;

    /// <summary>The one moving thing on this screen: a single pass of the radar when the app opens.</summary>
    void SweepOnce()
    {
        _swept = true;
        if (!Animate) return;
        var turn = TimeSpan.FromSeconds(2.4);
        SweepAngle.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-60, 300, turn) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.7))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(turn)));
        Sweep.BeginAnimation(OpacityProperty, fade);
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Window.GetWindow(this) is MainWindow main) main.Go(tag);
    }

    void MapLink_Click(object sender, RoutedEventArgs e)
    {
        if (_mapId != null && Window.GetWindow(this) is MainWindow main) main.OpenMap($"maps:{_mapId}");
    }
}
