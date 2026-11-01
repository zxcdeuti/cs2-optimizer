using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class SettingsView : UserControl
{
    static readonly string MapsCache = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CS2Optimizer", "maps");

    bool _ready;

    public SettingsView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ready = false;
        var s = AppSettings.Current;
        (CloseToTray.IsChecked, MinimizeToTray.IsChecked, StartMinimized.IsChecked) = (s.CloseToTray, s.MinimizeToTray, s.StartMinimized);
        (OverlayOnStart.IsChecked, OverlayAsAdmin.IsChecked) = (s.OverlayOnStart, s.OverlayAsAdmin);
        (LabelsRu.IsChecked, LabelsEn.IsChecked) = (s.MapLabelsRu, !s.MapLabelsRu);
        Autostart.IsChecked = Services.Autostart.Enabled;
        DataPathText.Text = AppSettings.DataDir;
        VersionText.Text = $"CS2/OPT {typeof(App).Assembly.GetName().Version?.ToString(2)}";
        (PrepCloseApps.IsChecked, PrepConfirm.IsChecked, PrepOverlay.IsChecked) = (s.PrepCloseApps, s.PrepConfirm, s.PrepOverlay);
        AutoUpdate.IsChecked = s.AutoUpdate;
        Updater.Instance.Changed -= ShowUpdateStatus;
        Updater.Instance.Changed += ShowUpdateStatus;
        ShowUpdateStatus();
        ShowCacheSize();
        _ready = true;
    }

    void ShowUpdateStatus()
    {
        UpdateVersion.Text = $"Установлена версия {Updater.CurrentVersion}";
        UpdateStatus.Text = Updater.Instance.Status;
    }

    async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!AppSettings.Current.AutoUpdate)
        {
            ResultText.Text = "Сначала включи автоматическое обновление.";
            return;
        }
        await Updater.Instance.Check();
    }

    void ShowCacheSize()
    {
        var files = Directory.Exists(MapsCache) ? Directory.GetFiles(MapsCache) : [];
        long bytes = files.Sum(f => new FileInfo(f).Length);
        int maps = files.Count(f => f.EndsWith(".json"));
        MapsCacheText.Text = maps == 0
            ? "Пусто. Радары достаются из игры при первом открытии карты."
            : $"Карт: {maps}, {ProcessCleaner.FormatBytes(bytes)}. Обновляется сам после патчей CS2; очистка нужна, только если карта отображается неправильно.";
    }

    void Option_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        AppSettings.Save(AppSettings.Current with
        {
            CloseToTray = CloseToTray.IsChecked == true,
            MinimizeToTray = MinimizeToTray.IsChecked == true,
            StartMinimized = StartMinimized.IsChecked == true,
            OverlayOnStart = OverlayOnStart.IsChecked == true,
            OverlayAsAdmin = OverlayAsAdmin.IsChecked == true,
            MapLabelsRu = LabelsRu.IsChecked == true,
            PrepCloseApps = PrepCloseApps.IsChecked == true,
            PrepConfirm = PrepConfirm.IsChecked == true,
            PrepOverlay = PrepOverlay.IsChecked == true,
            AutoUpdate = AutoUpdate.IsChecked == true,
        });
        ResultText.Text = "Сохранено.";
    }

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        Services.Autostart.Set(Autostart.IsChecked == true, AppSettings.Current.StartMinimized);
        ResultText.Text = Autostart.IsChecked == true ? "Приложение будет запускаться вместе с Windows." : "Автозапуск выключен.";
    }

    void ClearMaps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(MapsCache)) Directory.Delete(MapsCache, recursive: true);
            ResultText.Text = "Кеш радаров очищен — карты заново достанутся из игры при открытии.";
        }
        catch (IOException)
        {
            ResultText.Text = "Часть файлов занята открытой картой — перезапусти приложение и попробуй снова.";
        }
        ShowCacheSize();
    }

    void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataDir);
        Process.Start("explorer.exe", $"\"{AppSettings.DataDir}\"");
    }

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (AppDialog.Ask(Window.GetWindow(this), "Сбросить всё?",
                "Прицел, конфиг, ватермарка и настройки приложения вернутся к значениям по умолчанию. Файлы CS2 (autoexec.cfg, cs2_video.txt) не изменятся.",
                "Сбросить", danger: true) != DialogChoice.Primary)
            return;

        if (OverlayControl.IsRunning) OverlayControl.Stop();
        foreach (var file in Directory.Exists(AppSettings.DataDir) ? Directory.GetFiles(AppSettings.DataDir, "*.json") : [])
            File.Delete(file);
        Services.Autostart.Set(false, false);
        AppSettings.Reset();
        OnLoaded(this, e);
        ResultText.Text = "Всё сброшено. Открытые разделы подхватят значения по умолчанию после перезапуска.";
    }
}
