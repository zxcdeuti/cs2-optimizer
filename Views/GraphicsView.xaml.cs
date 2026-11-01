using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public record DiffRow(string Name, string Current, string New, bool Changed)
{
    public Brush NewBrush => (Brush)Application.Current.Resources[Changed ? "AccentBrush" : "MutedBrush"];
    public FontWeight NewWeight => Changed ? FontWeights.SemiBold : FontWeights.Normal;
}

public partial class GraphicsView : UserControl
{
    VideoConfig? _config;
    Preset? _preset;

    public GraphicsView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e) => Reload();

    void Reload()
    {
        RunningBanner.Visibility = Cs2Paths.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
        var path = Cs2Paths.VideoConfig;
        _config = path != null ? new VideoConfig(path) : null;
        PathText.Text = path ?? "Не найден. Запусти CS2 хотя бы один раз, чтобы игра создала файл настроек.";
        FolderBtn.IsEnabled = path != null;
        RollbackBtn.IsEnabled = path != null && VideoConfig.LatestBackup(path) != null;
        ApplyBtn.IsEnabled = _config != null && _preset != null;
        BuildDiff();
    }

    void BuildDiff()
    {
        DiffList.ItemsSource = GraphicsPresets.Settings.Select(s =>
        {
            var current = s.Describe(_config?.Get(s.Key));
            if (s.Key == "setting.fullscreen" && _config?.Get(s.Key) == "0" && _config.Get("setting.nowindowborder") == "1")
                current = "Окно без рамки";
            var next = _preset != null && _preset.Values.TryGetValue(s.Key, out var v) ? s.Describe(v) : current;
            return new DiffRow(s.Name, current, _preset == null ? "—" : next, _preset != null && next != current);
        }).ToList();
    }

    void Preset_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string id }) return;
        _preset = GraphicsPresets.All.First(p => p.Id == id);
        ResultText.Text = "";
        ApplyBtn.IsEnabled = _config != null;
        BuildDiff();
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null || _preset == null) return;
        if (Cs2Paths.IsGameRunning && AppDialog.Ask(Window.GetWindow(this), "CS2 запущена",
                "При выходе игра перезапишет настройки, и пресет может не сохраниться. Лучше сначала закрыть её.",
                "Всё равно применить", danger: true) != DialogChoice.Primary)
            return;

        try
        {
            foreach (var (key, value) in _preset.Values) _config.Set(key, value);
            var backup = _config.Save();
            Reload();
            ShowResult($"Готово! Пресет «{_preset.Title}» применён. Резервная копия: {System.IO.Path.GetFileName(backup)}", ok: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowResult("Не удалось записать файл: " + ex.Message, ok: false);
        }
    }

    void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null || VideoConfig.LatestBackup(_config.FilePath) is not { } backup) return;
        if (AppDialog.Ask(Window.GetWindow(this), "Вернуть настройки графики?", $"Настройки будут восстановлены из копии {System.IO.Path.GetFileName(backup)}.",
                "Вернуть") != DialogChoice.Primary)
            return;
        File.Copy(backup, _config.FilePath, overwrite: true);
        File.Delete(backup);
        Reload();
        ShowResult("Настройки возвращены из резервной копии.", ok: true);
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_config != null)
            Process.Start("explorer.exe", $"/select,\"{_config.FilePath}\"");
    }

    void ShowResult(string text, bool ok)
    {
        ResultText.Text = text;
        ResultText.Foreground = (Brush)FindResource(ok ? "OkBrush" : "DangerBrush");
    }
}
