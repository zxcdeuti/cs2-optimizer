using System.Windows;
using System.Windows.Controls;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class SystemView : UserControl
{
    bool _scanned;

    public SystemView() => InitializeComponent();

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_scanned) return;
        _scanned = true;
        await RunChecks();
        HwList.ItemsSource = await Task.Run(SystemInfo.Hardware);
        Scroller.ScrollToTop();
    }

    async Task RunChecks()
    {
        ScoreTitle.Text = "Сканирую…";
        var checks = await Task.Run(SystemInfo.Checks);
        ChecksList.ItemsSource = checks;

        int ok = checks.Count(c => c.Ok);
        ScoreOk.Text = ok.ToString();
        ScoreTotal.Text = $"/{checks.Count}";
        int fixable = checks.Count(c => !c.Ok && c.Fix != null);
        (ScoreTitle.Text, ScoreSub.Text) = ok == checks.Count
            ? ("Всё настроено", "Windows готова к игре.")
            : ($"Можно улучшить: {checks.Count - ok}", fixable > 0
                ? $"Исправляется кнопкой: {fixable}."
                : "Подсказки — в списке ниже.");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RunChecks();

    async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: Check { Fix: { } fix } } button) return;
        button.IsEnabled = false;
        string message;
        try { message = await Task.Run(fix); }
        catch (Exception ex) { message = "Не получилось: " + ex.Message; }
        await RunChecks();
        ScoreSub.Text = message;
    }
}
