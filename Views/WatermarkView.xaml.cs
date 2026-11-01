using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class WatermarkView : UserControl
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    Telemetry? _telemetry;
    OverlaySettings _settings = OverlaySettings.Load();
    bool _ready;

    public WatermarkView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = OverlaySettings.Load();
        _ready = false;
        Check(LookHost, _settings.Look);
        Check(LayoutHost, _settings.Layout);
        Check(CornerHost, _settings.Corner);
        (FpsToggle.IsChecked, CpuToggle.IsChecked, GpuToggle.IsChecked) = (_settings.ShowFps, _settings.ShowCpu, _settings.ShowGpu);
        (LogoToggle.IsChecked, LabelsToggle.IsChecked) = (_settings.ShowLogo, _settings.ShowLabels);
        TagBox.Text = _settings.Tag;
        (ScaleSlider.Value, OpacitySlider.Value, OffsetSlider.Value) = (_settings.Scale, _settings.Opacity, _settings.Offset);
        _ready = true;

        // The preview has no FPS of its own: the main window isn't a game.
        _telemetry ??= new Telemetry(withFps: false);
        Refresh();
        _timer.Start();

        if (WatermarkHotkey.Instance is { } hk)
        {
            hk.StateChanged -= ShowHotkey;
            hk.StateChanged += ShowHotkey;
        }
        ShowHotkey();
    }

    // ── hotkey ─────────────────────────────────────────────────────────────────

    bool _recording;

    void ShowHotkey()
    {
        var s = AppSettings.Current;
        HotkeyToggle.IsChecked = s.HotkeyEnabled;
        if (!_recording) HotkeyText.Text = KeyCombo.Parse(s.Hotkey)?.ToString().Replace("+", " + ") ?? "не задан";
        var problem = WatermarkHotkey.Instance?.Problem;
        HotkeyStatus.Foreground = (Brush)FindResource(problem != null && s.HotkeyEnabled ? "WarnBrush" : "MutedBrush");
        HotkeyStatus.Text = WatermarkHotkey.Instance == null ? "Бинд работает, пока приложение запущено."
            : problem ?? "В игре прячет и показывает ватермарку, а если она выключена — запускает. Работает и из трея.";
    }

    void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        _recording = true;
        if (WatermarkHotkey.Instance is { } hk) hk.Paused = true;
        HotkeyText.Text = "жми сочетание…";
        HotkeyBtn.Focus();
    }

    void Hotkey_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var mods = System.Windows.Input.Keyboard.Modifiers;
        if (key == System.Windows.Input.Key.Escape) { StopRecording(); return; }
        if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt
            or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift)
        {
            HotkeyText.Text = new KeyCombo(mods, System.Windows.Input.Key.None).ToString().Replace("+None", " + …");
            return;
        }
        if (!KeyCombo.IsAllowed(mods, key))
        {
            HotkeyStatus.Text = "Одиночная буква сработает в чате — добавь Ctrl, Alt или Shift, либо возьми F-клавишу.";
            HotkeyStatus.Foreground = (Brush)FindResource("WarnBrush");
            return;
        }
        AppSettings.Save(AppSettings.Current with { Hotkey = new KeyCombo(mods, key).ToString(), HotkeyEnabled = true });
        StopRecording();
    }

    void Hotkey_LostFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (_recording) StopRecording();
    }

    void StopRecording()
    {
        _recording = false;
        if (WatermarkHotkey.Instance is { } hk) hk.Paused = false; // re-registers and refreshes the status
        ShowHotkey();
    }

    void HotkeyToggle_Click(object sender, RoutedEventArgs e) =>
        AppSettings.Save(AppSettings.Current with { HotkeyEnabled = HotkeyToggle.IsChecked == true });

    static void Check(Panel host, string tag)
    {
        foreach (var chip in host.Children.OfType<RadioButton>())
            chip.IsChecked = (string)chip.Tag == tag;
    }

    void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    void Refresh()
    {
        if (_telemetry != null) Preview.Show(_telemetry.Sample(), _settings);
        Preview.HorizontalAlignment = _settings.Corner.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Preview.VerticalAlignment = _settings.Corner.StartsWith("Bottom") ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Preview.Margin = new Thickness(Math.Min(_settings.Offset, 40));

        bool running = OverlayWindow.IsRunning();
        bool stuck = running && RecorderLog.IsStuck();
        StatusDot.Fill = (Brush)FindResource(stuck ? "WarnBrush" : running ? "AccentBrush" : "DimBrush");
        StatusText.Text = stuck ? "НЕ ОТВЕЧАЕТ" : running ? "РАБОТАЕТ" : "ВЫКЛЮЧЕНА";
        StatusSub.Text = running ? "Плашка на экране поверх всех окон." : "Включи — плашка появится поверх всех окон и игр.";
        ToggleText.Text = running ? "Выключить" : "Включить";
        ToggleBtn.Style = (Style)FindResource(running ? "SecondaryButton" : "PrimaryButton");
    }

    void Save(OverlaySettings settings)
    {
        if (!_ready) return;
        _settings = settings;
        _settings.Save();
        Refresh();
    }

    void Choice_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag, GroupName: var group }) return;
        Save(group switch
        {
            "look" => _settings with { Look = tag },
            "layout" => _settings with { Layout = tag },
            _ => _settings with { Corner = tag },
        });
    }

    void Option_Click(object sender, RoutedEventArgs e) => Save(_settings with
    {
        ShowFps = FpsToggle.IsChecked == true, ShowCpu = CpuToggle.IsChecked == true, ShowGpu = GpuToggle.IsChecked == true,
        ShowLogo = LogoToggle.IsChecked == true, ShowLabels = LabelsToggle.IsChecked == true,
    });

    void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Sliders fire while InitializeComponent is still creating their siblings.
        if (!_ready) return;
        Save(_settings with { Scale = (int)ScaleSlider.Value, Opacity = (int)OpacitySlider.Value, Offset = (int)OffsetSlider.Value });
    }

    void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color }) Save(_settings with { Accent = color });
    }

    void Tag_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) Save(_settings with { Tag = TagBox.Text.Trim() });
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (OverlayControl.IsRunning) OverlayControl.Stop();
        else OverlayControl.Start(AppSettings.Current.OverlayAsAdmin);
        _settings = OverlaySettings.Load();
        Refresh();
    }
}
