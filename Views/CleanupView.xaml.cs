using System.Windows;
using System.Windows.Controls;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class CleanupView : UserControl
{
    List<ProcGroup> _groups = [];

    public CleanupView() => InitializeComponent();

    async void OnLoaded(object sender, RoutedEventArgs e) => await Rescan();

    async Task Rescan()
    {
        _groups = await Task.Run(ProcessCleaner.Scan);
        long max = _groups.Select(g => g.Bytes).DefaultIfEmpty(1).Max();
        foreach (var g in _groups) g.BarWidth = 176.0 * g.Bytes / max;
        ProcList.ItemsSource = _groups;
        var (used, total) = ProcessCleaner.MemoryUsage();
        UsedMem.Text = ProcessCleaner.FormatBytes(used);
        TotalMem.Text = total > 0 ? "/ " + ProcessCleaner.FormatBytes(total) : "";
        UpdateSummary();
    }

    void UpdateSummary()
    {
        var selected = _groups.Where(g => g.IsSelected).ToList();
        SelCount.Text = selected.Count.ToString();
        SelMem.Text = ProcessCleaner.FormatBytes(selected.Sum(g => g.Bytes));
        CloseBtn.IsEnabled = selected.Count > 0;
    }

    void Selection_Click(object sender, RoutedEventArgs e) => UpdateSummary();

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ResultText.Text = "";
        await Rescan();
    }

    async void Close_Click(object sender, RoutedEventArgs e)
    {
        var selected = _groups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0) return;
        var names = string.Join(", ", selected.Select(g => g.Display));
        if (MessageBox.Show(Window.GetWindow(this)!,
                $"Закрыть: {names}?\n\nНесохранённые данные в этих программах могут пропасть.",
                "CS2 Optimizer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        CloseBtn.IsEnabled = false;
        long freed = selected.Sum(g => g.Bytes);
        var (closed, failed) = await Task.Run(() => ProcessCleaner.Close(selected));
        await Rescan();
        ResultText.Text = failed == 0
            ? $"Готово: закрыто процессов — {closed}, освобождено около {ProcessCleaner.FormatBytes(freed)}."
            : $"Закрыто процессов: {closed}. Не удалось закрыть: {failed} (возможно, нужны права администратора).";
    }
}
