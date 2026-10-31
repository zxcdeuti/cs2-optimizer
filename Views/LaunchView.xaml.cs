using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public class LaunchOptionRow(LaunchOption option, bool isOn)
{
    public LaunchOption Option { get; } = option;
    public bool IsOn { get; set; } = isOn;
}

public partial class LaunchView : UserControl
{
    List<LaunchOptionRow> _rows = [];
    string _current = "";

    public LaunchView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _current = LaunchOptions.ReadCurrent() ?? "";
        CurrentText.Text = _current.Length > 0 ? _current : "пусто";

        // Start from what's already in Steam; with nothing set, suggest the defaults.
        _rows = LaunchOptions.All
            .Select(o => new LaunchOptionRow(o, _current.Length == 0 ? o.Default : _current.Contains(o.Flag)))
            .ToList();
        OptionList.ItemsSource = _rows;
        Build();
    }

    void Option_Click(object sender, RoutedEventArgs e) => Build();

    void Build()
    {
        // Keep flags this list doesn't know about, so copying never drops the user's own options.
        var known = LaunchOptions.All.Select(o => o.Flag).ToList();
        var extra = _current;
        foreach (var flag in known) extra = extra.Replace(flag, " ");
        var parts = _rows.Where(r => r.IsOn).Select(r => r.Option.Flag)
            .Concat(extra.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        ResultBox.Text = string.Join(" ", parts);
    }

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ResultBox.Text);
        CopyText.Text = "Скопировано";
        await Task.Delay(1500);
        CopyText.Text = "Копировать строку";
    }

    void OpenSteam_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("steam://nav/games/details/730") { UseShellExecute = true });
}
