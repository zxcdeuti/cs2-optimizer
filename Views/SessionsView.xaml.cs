using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public record SessionRow(DateTime Start, string Duration, string Avg, string Low, string Temps, string Preset);

public partial class SessionsView : UserControl
{
    readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public SessionsView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Show();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        Show();
        _timer.Start();
    }

    void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    void ShowRecorderState()
    {
        var state = RecorderLog.Read();
        // A crashed or killed watermark leaves its last state behind, so the running check comes first.
        var (title, detail, dot) = !OverlayControl.IsRunning
            ? ("Запись не идёт", "Ватермарка выключена. Включи её или нажми «Играть», замер запишется сам.", "DimBrush")
            : RecorderLog.IsStuck()
            ? ("Ватермарка не отвечает", "Запись не идёт. Выключи ватермарку и включи снова в её разделе.", "WarnBrush")
            : state?.State switch
            {
                "recording" => ("Идёт запись", state.Detail, "OkBrush"),
                "no-fps" => ("Запись не идёт", state.Detail + " Выключи ватермарку и включи снова, разрешив запрос Windows.", "WarnBrush"),
                "waiting" => ("Готово к записи", "Запись начнётся сама, когда запустится CS2.", "OkBrush"),
                _ => ("Ждём ватермарку", "Статус появится через пару секунд.", "DimBrush"),
            };
        StateTitle.Text = title;
        StateDetail.Text = detail;
        StateDot.Fill = (Brush)FindResource(dot);
    }

    void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(RecorderLog.LogPath))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RecorderLog.LogPath) { UseShellExecute = true });
        else
            AppDialog.Inform(Window.GetWindow(this), "Журнала пока нет", "Он появится, когда ватермарка новой версии запустится хотя бы раз.");
    }

    void Show()
    {
        ShowRecorderState();
        var all = SessionLog.All();
        EmptyText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = all.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (all.Count == 0) return;

        var last = all[0];
        var previous = all.Count > 1 ? all[1] : null;
        LastTitle.Text = $"{last.Start:dd.MM в HH:mm}, {Duration(last.Minutes)}, {last.Preset}";
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
            block.Text = "первый замер";
            block.Foreground = (Brush)FindResource("DimBrush");
            return;
        }
        double diff = Math.Round(now) - Math.Round(b);
        block.Text = diff == 0 ? "как в прошлой" : $"{(diff > 0 ? "+" : "−")}{Math.Abs(diff):0} к прошлой";
        block.Foreground = (Brush)FindResource(diff == 0 ? "MutedBrush" : diff > 0 ? "OkBrush" : "DangerBrush");
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
