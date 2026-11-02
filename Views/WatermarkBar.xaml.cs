using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class WatermarkBar : UserControl
{
    static readonly FontFamily Hud = new("Bahnschrift");
    static readonly Brush Ink = Frozen("#F2F2F3");
    static readonly Brush Dim = Frozen("#8E9BAD");
    static readonly Brush Warm = Frozen("#FF9360");
    static readonly Brush Hot = Frozen("#F2564B");

    OverlaySettings? _applied;
    TextBlock? _fps, _cpuLoad, _cpuTemp, _gpuLoad, _gpuTemp;
    Brush _labelBrush = Dim;

    public WatermarkBar() => InitializeComponent();

    public void Show(Stats s, OverlaySettings settings)
    {
        Apply(settings);
        if (_fps != null) _fps.Text = s.Fps?.ToString() ?? "—";
        if (_cpuLoad != null) _cpuLoad.Text = $"{s.CpuLoad:0}%";
        if (_gpuLoad != null) _gpuLoad.Text = s.GpuLoad is { } g ? $"{g:0}%" : "—";
        SetTemp(_cpuTemp, s.CpuTemp);
        SetTemp(_gpuTemp, s.GpuTemp);
    }

    /// <summary>Rebuilds the visual tree only when the settings actually changed.</summary>
    void Apply(OverlaySettings s)
    {
        if (s == _applied) return;
        _applied = s;
        _fps = _cpuLoad = _cpuTemp = _gpuLoad = _gpuTemp = null;

        var accent = Frozen(s.Accent);
        double k = s.Scale / 100.0;
        Root.LayoutTransform = new ScaleTransform(k, k);

        var bg = new SolidColorBrush(Color.FromArgb((byte)(s.Opacity * 255 / 100), 0x10, 0x15, 0x1D));
        Root.Effect = null;
        Root.BorderBrush = Frozen("#3D4A5D");
        _labelBrush = s.Look == "Clear" ? Ink : Dim; // nothing behind the labels but the game
        switch (s.Look)
        {
            case "Clear":
                Root.Background = Brushes.Transparent;
                Root.BorderThickness = new Thickness(0);
                Root.CornerRadius = new CornerRadius(0);
                Root.Padding = new Thickness(2);
                Root.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 1 };
                break;
            case "Capsule":
                Root.Background = bg;
                Root.BorderThickness = new Thickness(1);
                Root.CornerRadius = new CornerRadius(999);
                Root.Padding = s.Layout == "Column" ? new Thickness(14, 10, 14, 10) : new Thickness(6, 5, 16, 5);
                break;
            default:
                Root.Background = bg;
                Root.BorderThickness = new Thickness(1);
                Root.CornerRadius = new CornerRadius(3);
                Root.Padding = s.Layout == "Column" ? new Thickness(12, 10, 14, 10) : new Thickness(5, 5, 13, 5);
                break;
        }

        Root.Child = s.Layout == "Column" ? BuildColumn(s, accent) : BuildRow(s, accent);
    }

    UIElement BuildRow(OverlaySettings s, Brush accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (s.ShowLogo) row.Children.Add(Logo(s));
        if (s.Tag.Length > 0)
            row.Children.Add(new TextBlock
            {
                Text = s.Tag, FontFamily = Hud, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(row.Children.Count > 0 ? 9 : 0, 0, 0, 0),
            });

        // The first metric after the logo/tag gets a gap; every next one gets a divider.
        void AddGroup(params UIElement[] parts)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal };
            if (row.Children.Count > 0 && (row.Children[^1] is StackPanel))
                group.Children.Add(new Rectangle { Width = 1, Height = 14, Margin = new Thickness(11, 0, 11, 0), Fill = Frozen("#3D4A5D") });
            else if (row.Children.Count > 0)
                group.Margin = new Thickness(11, 0, 0, 0);
            foreach (var p in parts) group.Children.Add(p);
            row.Children.Add(group);
        }

        if (s.ShowFps)
        {
            _fps = Value(17, accent, 30);
            AddGroup(s.ShowLabels ? [_fps, Label("FPS", new Thickness(5, 2, 0, 0))] : [_fps]);
        }
        if (s.ShowCpu)
        {
            (_cpuLoad, _cpuTemp) = (Value(15, Ink, 34), Value(15, Ink, 32, left: 5));
            AddGroup(s.ShowLabels ? [Label("CPU", new Thickness(0, 1, 7, 0)), _cpuLoad, _cpuTemp] : [_cpuLoad, _cpuTemp]);
        }
        if (s.ShowGpu)
        {
            (_gpuLoad, _gpuTemp) = (Value(15, Ink, 34), Value(15, Ink, 32, left: 5));
            AddGroup(s.ShowLabels ? [Label("GPU", new Thickness(0, 1, 7, 0)), _gpuLoad, _gpuTemp] : [_gpuLoad, _gpuTemp]);
        }
        return row;
    }

    UIElement BuildColumn(OverlaySettings s, Brush accent)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void AddRow(UIElement first, UIElement? second = null, UIElement? third = null, int span = 1)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int r = grid.RowDefinitions.Count - 1;
            foreach (var (el, col) in new[] { (first, 0), (second, 1), (third, 2) })
            {
                if (el == null) continue;
                Grid.SetRow(el, r);
                Grid.SetColumn(el, col);
                if (col == 0) Grid.SetColumnSpan(el, span);
                grid.Children.Add(el);
            }
        }

        if (s.ShowLogo || s.Tag.Length > 0)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            if (s.ShowLogo) head.Children.Add(Logo(s));
            if (s.Tag.Length > 0)
                head.Children.Add(new TextBlock
                {
                    Text = s.Tag, FontFamily = Hud, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(s.ShowLogo ? 8 : 0, 0, 0, 0),
                });
            AddRow(head, span: 3);
        }

        TextBlock RowLabel(string text) => Label(s.ShowLabels ? text : "", new Thickness(0, 1, s.ShowLabels ? 14 : 0, 0));
        if (s.ShowFps) AddRow(RowLabel("FPS"), _fps = Value(17, accent, 34));
        if (s.ShowCpu) AddRow(RowLabel("CPU"), _cpuLoad = Value(15, Ink, 34), _cpuTemp = Value(15, Ink, 32, left: 8));
        if (s.ShowGpu) AddRow(RowLabel("GPU"), _gpuLoad = Value(15, Ink, 34), _gpuTemp = Value(15, Ink, 32, left: 8));
        return grid;
    }

    /// <summary>The user's own icon, cut to the chosen shape, or the app's mark.</summary>
    static FrameworkElement Logo(OverlaySettings s)
    {
        double size = Math.Clamp(s.LogoSize, 14, 40);
        if (WatermarkLogo.Load(s.LogoFile) is not { } own)
            return new Image { Source = (ImageSource)Application.Current.FindResource("AppLogoSmall"), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };

        if (s.LogoShape == "Original")
        {
            // Height is what fits the bar; a wide logo may run longer, up to a limit.
            var image = new Image { Source = own, Height = size, MaxWidth = size * 4, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        var picture = new ImageBrush(own) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
        return new Border
        {
            Width = size, Height = size, Background = picture, VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(s.LogoShape == "Circle" ? size / 2 : size * 0.22),
        };
    }

    static TextBlock Value(double size, Brush brush, double minWidth, double left = 0)
    {
        var t = new TextBlock
        {
            Text = "—", FontFamily = Hud, FontWeight = FontWeights.SemiBold, FontSize = size, Foreground = brush,
            MinWidth = minWidth, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(left, 0, 0, 0),
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    TextBlock Label(string text, Thickness margin) => new()
    {
        Text = text, FontFamily = Hud, FontSize = 10.5, Foreground = _labelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = margin,
    };

    static void SetTemp(TextBlock? block, double? celsius)
    {
        if (block == null) return;
        block.Visibility = celsius == null ? Visibility.Collapsed : Visibility.Visible;
        if (celsius is not { } c) return;
        block.Text = $"{c:0}°";
        block.Foreground = c >= 85 ? Hot : c >= 72 ? Warm : Ink;
    }

    static Brush Frozen(string hex)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
