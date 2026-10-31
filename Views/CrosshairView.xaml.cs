using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class CrosshairView : UserControl
{
    static readonly string StatePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CS2Optimizer", "crosshair.json");

    static readonly Dictionary<string, CrosshairState> Presets = new()
    {
        ["best"] = new(2, 1, 1, false, false, false, 0, 255, 255, 255),
        ["green"] = new(2, 1, 1, false, false, true, 0, 255, 0, 255),
        ["power"] = new(3, 2, 1, false, false, true, 0, 255, 255, 255),
        ["tyellow"] = new(2, 1, 1, false, true, false, 255, 255, 0, 255),
        ["pink"] = new(2, 1, 1, false, false, false, 255, 0, 255, 255),
    };

    bool _ready;

    public CrosshairView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            Apply(LoadState() ?? Presets["best"]);
            _ready = true;
        }
        Update();
    }

    CrosshairState Current => new((int)LenSlider.Value, (int)ThickSlider.Value, (int)GapSlider.Value,
        DotToggle.IsChecked == true, TToggle.IsChecked == true, OutlineToggle.IsChecked == true,
        (int)RSlider.Value, (int)GSlider.Value, (int)BSlider.Value, (int)ASlider.Value);

    void Apply(CrosshairState s)
    {
        bool ready = _ready;
        _ready = false;
        (LenSlider.Value, ThickSlider.Value, GapSlider.Value) = (s.Length, s.Thickness, s.Gap);
        (DotToggle.IsChecked, TToggle.IsChecked, OutlineToggle.IsChecked) = (s.Dot, s.T, s.Outline);
        (RSlider.Value, GSlider.Value, BSlider.Value, ASlider.Value) = (s.R, s.G, s.B, s.A);
        _ready = ready;
    }

    void Changed(object sender, RoutedEventArgs e)
    {
        if (_ready) Update();
    }

    void Update()
    {
        var s = Current;
        Redraw(s);
        CommandBox.Text = s.ToCommand();
        HintText.Text = s.Outline
            ? "Обводку в консоли не задать — включи её в меню: Настройки → Прицел/Прицелы → Обводка: Полная. Стиль: «Статичный крест»."
            : "В меню выбери стиль «Статичный крест». Если консоль пишет Unknown command — введи find crosshair, Valve могла снова переименовать команды.";
        SaveState(s);
    }

    void Redraw(CrosshairState s)
    {
        Preview.Children.Clear();
        double w = PreviewBg.ActualWidth, h = PreviewBg.ActualHeight;
        if (w <= 0 || h <= 0) return;

        int zoom = (int)ZoomSlider.Value;
        double cx = Math.Round(w / 2), cy = Math.Round(h / 2);
        double half = s.Thickness / 2.0;

        // Rectangles in game pixels, relative to the screen centre.
        var parts = new List<Rect>();
        if (s.Length > 0)
        {
            parts.Add(new Rect(s.Gap, -half, s.Length, s.Thickness));
            parts.Add(new Rect(-s.Gap - s.Length, -half, s.Length, s.Thickness));
            parts.Add(new Rect(-half, s.Gap, s.Thickness, s.Length));
            if (!s.T) parts.Add(new Rect(-half, -s.Gap - s.Length, s.Thickness, s.Length));
        }
        if (s.Dot) parts.Add(new Rect(-half, -half, s.Thickness, s.Thickness));

        if (s.Outline)
            foreach (var r in parts)
                AddRect(new Rect(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Brushes.Black);

        var fill = new SolidColorBrush(Color.FromArgb((byte)s.A, (byte)s.R, (byte)s.G, (byte)s.B));
        foreach (var r in parts) AddRect(r, fill);

        void AddRect(Rect r, Brush brush)
        {
            var rect = new Rectangle { Width = r.Width * zoom, Height = r.Height * zoom, Fill = brush };
            Canvas.SetLeft(rect, cx + r.X * zoom);
            Canvas.SetTop(rect, cy + r.Y * zoom);
            Preview.Children.Add(rect);
        }
    }

    void Preview_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_ready) Redraw(Current);
    }

    void Bg_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || PreviewBg == null) return;
        bool grid = tag == "grid";
        GridOverlay.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        PreviewBg.Background = new BrushConverter().ConvertFromString(grid ? "#0E0E0F" : tag) as Brush;
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        Apply(Presets[id]);
        Update();
    }

    void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string rgb }) return;
        var c = rgb.Split(',').Select(double.Parse).ToArray();
        bool ready = _ready;
        _ready = false;
        (RSlider.Value, GSlider.Value, BSlider.Value) = (c[0], c[1], c[2]);
        _ready = ready;
        Update();
    }

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(CommandBox.Text);
        CopyText.Text = "Скопировано!";
        await Task.Delay(1500);
        CopyText.Text = "Копировать";
    }

    void SaveAutoexec_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = Autoexec.WriteBlock("прицел", CommandBox.Text.Split("; "));
            HintText.Text = $"Сохранено в {file}. Чтобы прицел ставился сам, добавь +exec autoexec в параметры запуска (раздел 03).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HintText.Text = "Не удалось записать autoexec.cfg: " + ex.Message;
        }
    }

    static CrosshairState? LoadState()
    {
        try { return JsonSerializer.Deserialize<CrosshairState>(File.ReadAllText(StatePath)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    static void SaveState(CrosshairState s)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
