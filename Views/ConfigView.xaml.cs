using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class ConfigView : UserControl
{
    Dictionary<string, ConfigEntry> _state = [];
    bool _built;

    public ConfigView() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        _state = ConfigItems.Load();
        foreach (var item in ConfigItems.All) Rows.Children.Add(BuildRow(item));
        Refresh();
    }

    UIElement BuildRow(ConfigItem item)
    {
        var entry = _state[item.Cvar];
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        var include = new CheckBox { Style = (Style)FindResource("Tick"), IsChecked = entry.Included, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        grid.Children.Add(include);

        var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = item.Title, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = item.Hint, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        text.Children.Add(new TextBlock { Text = item.Cvar, Style = (Style)FindResource("Label"), Margin = new Thickness(0, 6, 0, 0) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement control;
        if (item.IsSwitch)
        {
            var toggle = new CheckBox
            {
                Style = (Style)FindResource("Toggle"), Padding = new Thickness(0), IsChecked = entry.Value == item.On,
                Content = new TextBlock { Text = entry.Value == item.On ? "Вкл" : "Выкл", Foreground = (Brush)FindResource("MutedBrush") },
            };
            toggle.Click += (_, _) =>
            {
                ((TextBlock)toggle.Content).Text = toggle.IsChecked == true ? "Вкл" : "Выкл";
                Update(item.Cvar, value: toggle.IsChecked == true ? item.On! : item.Off!);
            };
            control = toggle;
        }
        else
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            var value = new TextBlock { Style = (Style)FindResource("Num"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider
            {
                Minimum = item.Min, Maximum = item.Max, TickFrequency = item.Step,
                Value = double.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : item.Default,
            };
            void ShowValue() => value.Text = item.Cvar.StartsWith("fps_max") && slider.Value == 0 ? "∞" : ConfigItems.Format(item, slider.Value);
            ShowValue();
            slider.ValueChanged += (_, _) => { ShowValue(); Update(item.Cvar, value: ConfigItems.Format(item, slider.Value)); };
            Grid.SetColumn(value, 1);
            row.Children.Add(slider);
            row.Children.Add(value);
            control = row;
        }
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 2);
        grid.Children.Add(control);

        include.Click += (_, _) =>
        {
            control.Opacity = include.IsChecked == true ? 1 : 0.35;
            Update(item.Cvar, included: include.IsChecked == true);
        };
        control.Opacity = entry.Included ? 1 : 0.35;

        return new Border { Child = grid, BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 15, 0, 15) };
    }

    void Update(string cvar, bool? included = null, string? value = null)
    {
        var e = _state[cvar];
        _state[cvar] = new ConfigEntry(included ?? e.Included, value ?? e.Value);
        ConfigItems.Save(_state);
        Refresh();
    }

    void Refresh() => Preview.Text = string.Join("\n", ConfigItems.Lines(_state));

    async void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(Preview.Text);
        CopyText.Text = "Скопировано";
        await Task.Delay(1500);
        CopyText.Text = "Копировать";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = Autoexec.WriteBlock("конфиг", ConfigItems.Lines(_state));
            HintText.Text = $"Сохранено в {file}. Если какая-то команда в консоли пишет Unknown command — Valve её переименовала, напиши, поправлю.";
            HintText.Foreground = (Brush)FindResource("OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HintText.Text = "Не удалось записать autoexec.cfg: " + ex.Message;
            HintText.Foreground = (Brush)FindResource("DangerBrush");
        }
    }
}
