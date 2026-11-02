using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class SensView : UserControl
{
    const double Cs2Yaw = 0.022; // degrees per mouse count at sensitivity 1
    const double ScaleMin = 200, ScaleMax = 2000, ProLow = 600, ProHigh = 1000;

    double? _edpi;

    public SensView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        Calc();
        Convert();
        RefreshMouse();
    }

    static double? Parse(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;

    void Calc_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) Calc();
    }

    void Calc()
    {
        var dpi = Parse(DpiBox.Text);
        var sens = Parse(SensBox.Text);
        _edpi = dpi * sens;
        EdpiText.Text = _edpi is { } edpi ? edpi.ToString("0", CultureInfo.InvariantCulture) : "—";
        Cm360Text.Text = dpi is { } d && sens is { } s
            ? (360 / (Cs2Yaw * s) / d * 2.54).ToString("0.0", CultureInfo.InvariantCulture)
            : "—";
        ScaleNote.Text = _edpi switch
        {
            null => "Введи DPI и сенсу.",
            < ProLow => "Низкий eDPI: точная стрельба, но для резких поворотов нужен большой коврик.",
            <= ProHigh => "Как у большинства про-игроков: их eDPI обычно в диапазоне 600–1000.",
            _ => "Высокий eDPI: быстрые повороты, но сложнее точно доводить прицел. У большинства про — 600–1000.",
        };
        DrawScale();
    }

    void Scale_SizeChanged(object sender, SizeChangedEventArgs e) => DrawScale();

    void DrawScale()
    {
        ScaleCanvas.Children.Clear();
        double w = ScaleHost.ActualWidth;
        if (w <= 0) return;
        double X(double v) => (Math.Clamp(v, ScaleMin, ScaleMax) - ScaleMin) / (ScaleMax - ScaleMin) * w;
        var accent = (Brush)FindResource("AccentBrush");
        var dim = (Brush)FindResource("DimBrush");

        var band = new Rectangle { Width = X(ProHigh) - X(ProLow), Height = 6, Fill = (Brush)FindResource("AccentSoftBrush") };
        Canvas.SetLeft(band, X(ProLow));
        Canvas.SetTop(band, 7);
        ScaleCanvas.Children.Add(band);

        foreach (var tick in new[] { 400, 800, 1200, 1600 })
        {
            var line = new Rectangle { Width = 1, Height = 8, Fill = dim };
            Canvas.SetLeft(line, X(tick));
            Canvas.SetTop(line, 6);
            var label = new TextBlock { Text = tick.ToString(), Style = (Style)FindResource("Label") };
            Canvas.SetLeft(label, X(tick) - 12);
            Canvas.SetTop(label, 22);
            ScaleCanvas.Children.Add(line);
            ScaleCanvas.Children.Add(label);
        }

        var pro = new TextBlock { Text = "Про", Style = (Style)FindResource("Label"), Foreground = accent };
        Canvas.SetLeft(pro, (X(ProLow) + X(ProHigh)) / 2 - 10);
        Canvas.SetTop(pro, -8);
        ScaleCanvas.Children.Add(pro);

        if (_edpi is { } edpi)
        {
            var marker = new Rectangle { Width = 10, Height = 10, Fill = accent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45) };
            Canvas.SetLeft(marker, X(edpi) - 5);
            Canvas.SetTop(marker, 5);
            ScaleCanvas.Children.Add(marker);
        }
    }

    void Convert_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Convert();
    }

    void Convert()
    {
        var yaw = GameHost.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        var other = Parse(OtherBox.Text);
        ConvertedText.Text = yaw != null && other is { } o
            ? (o * double.Parse(yaw, CultureInfo.InvariantCulture) / Cs2Yaw).ToString("0.###", CultureInfo.InvariantCulture)
            : "—";
    }

    async void CopySens_Click(object sender, RoutedEventArgs e)
    {
        if (ConvertedText.Text == "—") return;
        Clipboard.SetText($"sensitivity {ConvertedText.Text}");
        CopySensText.Text = "Скопировано";
        await Task.Delay(1500);
        CopySensText.Text = "Копировать команду";
    }

    void RefreshMouse()
    {
        var accent = (Brush)FindResource("OkBrush");
        var warn = (Brush)FindResource("WarnBrush");

        bool accel = MouseSettings.AccelerationEnabled;
        AccelStatus.Text = accel ? "\uE7BA" : "\uE73E";
        AccelStatus.Foreground = accel ? warn : accent;
        AccelDetail.Text = accel
            ? "Включена: курсор ускоряется от скорости движения. В самой CS2 мышь идёт напрямую (raw input), но в меню и на рабочем столе мышечная память сбивается."
            : "Выключена — движение мыши всегда одинаково переводится в движение курсора.";
        AccelBtn.Visibility = accel ? Visibility.Visible : Visibility.Collapsed;

        int speed = MouseSettings.PointerSpeed;
        bool neutral = speed == 10;
        SpeedStatus.Text = neutral ? "\uE73E" : "\uE946";
        SpeedStatus.Foreground = neutral ? accent : (Brush)FindResource("MutedBrush");
        SpeedDetail.Text = neutral
            ? "10 из 20 — без пропуска пикселей, 1:1 с DPI мыши."
            : $"{speed} из 20. Значение 10 — точное 1:1 с DPI мыши; на игру не влияет, только на рабочий стол.";
    }

    void Accel_Click(object sender, RoutedEventArgs e)
    {
        MouseSettings.DisableAcceleration();
        RefreshMouse();
    }
}
