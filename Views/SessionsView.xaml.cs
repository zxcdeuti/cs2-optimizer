using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public record SessionRow(DateTime Start, string Duration, string Avg, string Low, string Temps, string Preset);

public partial class SessionsView : UserControl
{
    public SessionsView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e) => Show();

    void Show()
    {
        var all = SessionLog.All();
        EmptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (all.Count == 0) return;

        var last = all[0];
        var previous = all.Count > 1 ? all[1] : null;
        LastTitle.Text = $"ПОСЛЕДНЯЯ СЕССИЯ · {last.Start:dd.MM HH:mm} · {Duration(last.Minutes)} · {last.Preset.ToUpperInvariant()}";
        AvgText.Text = $"{last.AvgFps:0}";
        LowText.Text = $"{last.Low1Fps:0}";
        CpuText.Text = Temp(last.CpuMaxTemp);
        GpuText.Text = Temp(last.GpuMaxTemp);
        Delta(AvgDelta, last.AvgFps, previous?.AvgFps);
        Delta(LowDelta, last.Low1Fps, previous?.Low1Fps);

        History.ItemsSource = all.Select(s => new SessionRow(s.Start, Duration(s.Minutes), $"{s.AvgFps:0} FPS", $"{s.Low1Fps:0} low",
            $"{Temp(s.CpuMaxTemp)} / {Temp(s.GpuMaxTemp)}", s.Preset)).ToList();
    }

    void Delta(TextBlock block, double now, double? before)
    {
        if (before is not { } b)
        {
            block.Text = "ПЕРВЫЙ ЗАМЕР";
            block.Foreground = (Brush)FindResource("DimBrush");
            return;
        }
        double diff = now - b;
        block.Text = $"{(diff >= 0 ? "+" : "−")}{Math.Abs(diff):0} К ПРОШЛОЙ";
        block.Foreground = (Brush)FindResource(diff >= 0 ? "AccentBrush" : "DangerBrush");
    }

    static string Temp(double? c) => c is { } v ? $"{v:0}°" : "—";

    static string Duration(double minutes) => minutes >= 60 ? $"{(int)(minutes / 60)} ч {(int)(minutes % 60)} мин" : $"{minutes:0} мин";

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (AppDialog.Ask(Window.GetWindow(this), "Удалить историю замеров?", "Все сохранённые сессии будут удалены без возможности вернуть.",
                "Удалить", danger: true) != DialogChoice.Primary)
            return;
        SessionLog.Clear();
        Show();
    }
}
