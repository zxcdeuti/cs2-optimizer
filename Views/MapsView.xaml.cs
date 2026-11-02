using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CS2Optimizer.Services;

namespace CS2Optimizer.Views;

public partial class MapsView : UserControl
{
    string _mapId = MapRadar.Pool[0].Id;
    RadarMap? _radar;
    string _layer = "default";
    PlaceMark? _selected;
    string _query = "";
    string? _pendingFocus;
    bool _built;
    int _loadId;

    public MapsView() => InitializeComponent();

    bool Ru => LangRu.IsChecked == true;

    string RuName(PlaceMark p) => Callouts.RadarName(_mapId, p);

    string EnName(PlaceMark p) => Callouts.RadarEnName(_mapId, p);

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;
        if (!AppSettings.Current.MapLabelsRu) LangEn.IsChecked = true;
        foreach (var (id, name) in MapRadar.Pool)
        {
            var chip = new RadioButton { Style = (Style)FindResource("Chip"), GroupName = "map", Tag = id, Content = name, IsChecked = id == _mapId };
            chip.Checked += async (_, _) => await ShowMap(id);
            MapHost.Children.Add(chip);
        }
        await ShowMap(_mapId);
    }

    /// <summary>Opens a map from outside (the assistant), optionally focusing a place by name.</summary>
    public async void Open(string mapId, string? focus = null)
    {
        _pendingFocus = focus;
        if (!_built) { _mapId = mapId; return; } // OnLoaded opens it
        SearchBox.Text = "";
        var chip = MapHost.Children.OfType<RadioButton>().FirstOrDefault(c => (string)c.Tag == mapId);
        if (chip == null) return;
        if (chip.IsChecked == true) await ShowMap(mapId); // Checked won't fire again
        else chip.IsChecked = true;
    }

    async Task ShowMap(string id)
    {
        int load = ++_loadId;
        _mapId = id;
        _radar = null;
        _selected = null;
        _view = Matrix.Identity;
        _shownPng = null;
        MapTitle.Text = MapRadar.Pool.First(p => p.Id == id).Name;
        MapSub.Text = "";
        RadarImage.Source = null;
        Marks.Children.Clear();
        PlaceList.Children.Clear();
        InfoCard.Visibility = Visibility.Collapsed;
        LayerHost.Visibility = Visibility.Collapsed;
        StatusText.Text = "Загружаю радар из файлов игры…";

        RadarMap radar;
        try { radar = await Task.Run(() => MapRadar.Load(id)); }
        catch (Exception ex)
        {
            if (load == _loadId) StatusText.Text = "Не получилось достать карту из игры: " + ex.Message;
            return;
        }
        if (load != _loadId) return; // the user already clicked another map

        _radar = radar;
        StatusText.Text = "";
        MapSub.Text = $"Зон на карте: {radar.Places.Count}. Радар из файлов твоей CS2.";

        LayerHost.Children.Clear();
        // Start on the level with the most labels: on Train nearly every zone counts as the lower one.
        _layer = radar.Layers.Keys.OrderByDescending(l => radar.Places.Count(p => p.Layer == l)).ThenBy(l => l == "default" ? 0 : 1).First();
        if (radar.Layers.Count > 1)
        {
            foreach (var layer in radar.Layers.Keys)
            {
                var chip = new RadioButton
                {
                    Style = (Style)FindResource("Chip"), GroupName = "layer", Tag = layer, IsChecked = layer == _layer,
                    Content = layer == "default" ? "Верхний уровень" : layer == "lower" ? "Нижний уровень" : layer,
                    Background = (Brush)FindResource("BgBrush"),
                };
                chip.Checked += (_, _) => { _layer = layer; Render(); };
                LayerHost.Children.Add(chip);
            }
            LayerHost.Visibility = Visibility.Visible;
        }

        Render();
        if (_pendingFocus is { } focus)
        {
            _pendingFocus = null;
            var place = radar.Places.FirstOrDefault(p => Matches(p, focus));
            if (place != null) { ZoomTo(place, 2.2); Select(place); }
        }
    }

    void Render(bool rebuildList = true)
    {
        if (_radar == null) return;
        var png = _radar.Layers[_layer];
        if (_shownPng != png)
        {
            // Zoom and selection re-render the labels often; decode the radar only when it changes.
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(png);
            bmp.EndInit();
            RadarImage.Source = bmp;
            _shownPng = png;
        }

        var crop = _radar.Crop ?? [0, 0, 1, 1];
        _zoom = (crop[2] - crop[0]) / _view.M11;
        Frame.Width = Frame.Height = (crop[2] - crop[0]) * 1024;
        Board.Margin = new Thickness(-crop[0] * 1024, -crop[1] * 1024, 0, 0);
        Board.RenderTransform = new MatrixTransform(_view);

        Marks.Children.Clear();
        foreach (var p in _radar.Places.Where(p => p.Layer == _layer))
            Marks.Children.Add(Mark(p));
        Dispatcher.InvokeAsync(Declutter, System.Windows.Threading.DispatcherPriority.Loaded);
        if (rebuildList) BuildList();
    }

    double _zoom = 1;
    string? _shownPng;
    Matrix _view = Matrix.Identity;
    Point? _dragFrom;
    bool _dragging;

    // ── zoom & pan ─────────────────────────────────────────────────────────────

    /// <summary>Zooms so the place sits in the middle of the frame.</summary>
    void ZoomTo(PlaceMark p, double scale)
    {
        var crop = _radar?.Crop ?? [0, 0, 1, 1];
        double frame = (crop[2] - crop[0]) * 1024;
        // Frame centre expressed in the board's own coordinates (the board is shifted by the crop margin).
        double cx = crop[0] * 1024 + frame / 2, cy = crop[1] * 1024 + frame / 2;
        _view = new Matrix(scale, 0, 0, scale, cx - scale * p.X * 1024, cy - scale * p.Y * 1024);
    }

    void Frame_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_radar == null) return;
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        double next = Math.Clamp(_view.M11 * factor, 1, 6);
        factor = next / _view.M11;
        if (Math.Abs(factor - 1) < 0.001) return;
        var at = e.GetPosition(Board);
        _view.ScaleAtPrepend(factor, factor, at.X, at.Y);
        if (next <= 1.001) _view = Matrix.Identity; // fully out: snap back to the whole map
        Render();
        e.Handled = true;
    }

    void Frame_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { _view = Matrix.Identity; Render(); return; }
        _dragFrom = e.GetPosition(Frame);
        _dragging = false;
    }

    void Frame_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed || _view.M11 <= 1.001) return;
        var now = e.GetPosition(Frame);
        var delta = now - from;
        // Only start panning after a small move, so clicks on labels still select them.
        if (!_dragging && delta.Length < 4) return;
        if (!_dragging) { _dragging = true; Frame.CaptureMouse(); Frame.Cursor = Cursors.SizeAll; }
        _view.Translate(delta.X, delta.Y);
        _dragFrom = now;
        Board.RenderTransform = new MatrixTransform(_view);
    }

    void Frame_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = null;
        if (!_dragging) return;
        _dragging = false;
        Frame.ReleaseMouseCapture();
        Frame.Cursor = null;
    }

    /// <summary>Nudges overlapping labels up or down until they stop covering each other; selected one stays put.</summary>
    void Declutter()
    {
        var placed = new List<Rect>();
        var labels = Marks.Children.OfType<Border>()
            .Where(l => l.Tag is PlaceMark && l.ActualWidth > 0)
            .OrderByDescending(l => ((PlaceMark)l.Tag).Key == _selected?.Key)
            .ThenBy(l => ((PlaceMark)l.Tag).Y);
        foreach (var label in labels)
        {
            var p = (PlaceMark)label.Tag;
            double w = label.ActualWidth, h = label.ActualHeight;
            double x = p.X * 1024 - w / 2, y0 = p.Y * 1024 - h / 2;
            var rect = new Rect(x, y0, w, h);
            foreach (var step in new[] { 0, 1, -1, 2, -2, 3, -3 })
            {
                rect = new Rect(x, y0 + step * (h + 1), w, h);
                if (!placed.Any(r => r.IntersectsWith(rect))) break;
            }
            placed.Add(rect);
            Canvas.SetLeft(label, rect.X);
            Canvas.SetTop(label, rect.Y);
        }
    }

    UIElement Mark(PlaceMark p)
    {
        bool selected = p == _selected, hit = _query.Length == 0 || Matches(p, _query);
        // Sized in radar pixels, so scale with the crop to keep the on-screen size steady.
        double k = _zoom;
        var text = new TextBlock
        {
            Text = Ru ? RuName(p) : EnName(p), FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = Math.Max(7, 16 * k), FontWeight = FontWeights.SemiBold,
            Foreground = selected ? (Brush)FindResource("AccentInkBrush") : Brushes.White,
        };
        var label = new Border
        {
            Child = text, Tag = p, Padding = new Thickness(6 * k, 1.5 * k, 6 * k, 2.5 * k), CornerRadius = new CornerRadius(2 * k), Cursor = Cursors.Hand,
            Background = selected ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x15, 0x1D)),
            BorderBrush = selected ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromRgb(0x3D, 0x4A, 0x5D)),
            BorderThickness = new Thickness(k),
            Opacity = hit ? 1 : 0.22,
            ToolTip = Ru ? EnName(p) : RuName(p),
        };
        if (selected) Panel.SetZIndex(label, 10);
        // Rough spot until Declutter places it with the real size.
        Canvas.SetLeft(label, p.X * 1024);
        Canvas.SetTop(label, p.Y * 1024);
        label.MouseLeftButtonUp += (_, _) => Select(p);
        return label;
    }

    void BuildList()
    {
        if (_radar == null) return;
        PlaceList.Children.Clear();
        if (ModeBook.IsChecked == true) { BuildBook(); return; }
        var places = _radar.Places
            .Where(p => _query.Length == 0 || Matches(p, _query))
            .GroupBy(p => p.Key).Select(g => g.First())
            .OrderBy(p => Ru ? RuName(p) : EnName(p)).ToList();

        ListTitle.Text = _query.Length == 0 ? "Все зоны" : places.Count > 0 ? $"Найдено: {places.Count}" : NotHere();
        foreach (var p in places)
        {
            var row = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            if (_radar.Layers.Count > 1)
            {
                var layer = new TextBlock { Text = p.Layer == "default" ? "Верх" : "Низ", Style = (Style)FindResource("ColHead") };
                DockPanel.SetDock(layer, Dock.Right);
                row.Children.Add(layer);
            }
            row.Children.Add(new TextBlock
            {
                Text = Ru ? RuName(p) : EnName(p), FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource(p.Key == _selected?.Key ? "AccentBrush" : "TextBrush"),
            });
            var border = new Border { Child = row, BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8, 0, 8) };
            border.MouseLeftButtonUp += (_, _) => Select(p);
            PlaceList.Children.Add(border);
        }
    }

    /// <summary>Players' callouts for this map by area; clicking one finds it on the radar when the game has that zone.</summary>
    void BuildBook()
    {
        var q = _query.Trim().ToLowerInvariant().Replace('ё', 'е');
        var spots = Callouts.All.Where(c => c.Map == _mapId && (q.Length == 0
            || c.En.ToLowerInvariant().Contains(q) || c.Ru.ToLowerInvariant().Replace('ё', 'е').Contains(q))).ToList();
        ListTitle.Text = spots.Count == 0 ? (q.Length > 0 ? NotHere() : "Для этой карты справочника нет") : $"Колаутов: {spots.Count}";

        foreach (var zone in Callouts.Zones)
        {
            var inZone = spots.Where(c => c.Zone == zone).ToList();
            if (inZone.Count == 0) continue;
            bool site = zone is "A" or "B";
            PlaceList.Children.Add(new TextBlock
            {
                Text = site ? $"Точка {zone}" : zone, Style = (Style)FindResource("Label"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 2),
                Foreground = (Brush)FindResource(site ? "AccentBrush" : "MutedBrush"),
            });
            foreach (var c in inZone)
            {
                var place = _radar!.Places.FirstOrDefault(p => Callouts.SamePlace(c, p.Key));
                var box = new StackPanel { Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var line = new WrapPanel();
                line.Children.Add(new TextBlock { Text = c.En, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 14, FontWeight = FontWeights.SemiBold });
                line.Children.Add(new TextBlock { Text = c.Ru, FontSize = 12.5, Foreground = (Brush)FindResource("InfoBrush"), Margin = new Thickness(8, 1, 0, 0) });
                if (place != null)
                    line.Children.Add(new TextBlock { Text = "на радаре", Style = (Style)FindResource("ColHead"), Margin = new Thickness(8, 1, 0, 0) });
                box.Children.Add(line);
                box.Children.Add(new TextBlock { Text = c.Description, Style = (Style)FindResource("Muted"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
                var border = new Border { Child = box, BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 8) };
                border.MouseLeftButtonUp += (_, _) => ShowCallout(c, place);
                PlaceList.Children.Add(border);
            }
        }
    }

    void ShowCallout(Callout c, PlaceMark? place)
    {
        if (place != null)
        {
            ZoomTo(place, 2.2);
            Select(place);
            return;
        }
        // Not a zone the game marks: show the description without a spot on the radar.
        _selected = null;
        Render(rebuildList: false);
        InfoEn.Text = c.En;
        InfoRu.Text = c.Ru;
        InfoSlang.Text = $"{c.Zone}. Отдельной зоны на радаре нет.";
        InfoSlang.Visibility = Visibility.Visible;
        InfoDesc.Text = c.Description;
        InfoDesc.Visibility = Visibility.Visible;
        InfoCard.Visibility = Visibility.Visible;
    }

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_radar != null) BuildList();
    }

    /// <summary>With no hit on this map, point at maps where the handbook knows the name.</summary>
    string NotHere()
    {
        var elsewhere = Callouts.Search(_query).Select(c => Callouts.MapName(c.Map)).Distinct().ToList();
        return elsewhere.Count > 0 ? $"Здесь нет, есть на: {string.Join(", ", elsewhere)}" : "Ничего не найдено";
    }

    void Select(PlaceMark p)
    {
        _selected = p;
        if (p.Layer != _layer)
        {
            foreach (var chip in LayerHost.Children.OfType<RadioButton>())
                chip.IsChecked = (string)chip.Tag == p.Layer; // Checked re-renders
        }
        else Render(rebuildList: ModeBook.IsChecked != true); // keep the handbook's scroll position

        var slang = Callouts.ForPlace(_mapId, p.Key);
        InfoEn.Text = EnName(p);
        InfoRu.Text = RuName(p);
        InfoSlang.Text = slang != null ? "Как говорят: " + slang.Ru : "";
        InfoSlang.Visibility = slang != null ? Visibility.Visible : Visibility.Collapsed;
        InfoDesc.Text = slang?.Description ?? "";
        InfoDesc.Visibility = slang != null ? Visibility.Visible : Visibility.Collapsed;
        InfoCard.Visibility = Visibility.Visible;
    }

    /// <summary>Matches official names (EN/RU/key) and the handbook's slang for this zone.</summary>
    bool Matches(PlaceMark p, string query)
    {
        var q = query.Trim().ToLowerInvariant().Replace('ё', 'е');
        if (q.Length == 0) return true;
        bool Has(string s) => s.ToLowerInvariant().Replace('ё', 'е').Contains(q);
        var slang = Callouts.ForPlace(_mapId, p.Key);
        return Has(p.En) || Has(p.Ru) || Has(p.Key) || (slang != null && (Has(slang.Ru) || Has(slang.En)))
               || Callouts.All.Any(c => c.Map == _mapId && (Has(c.Ru) || Has(c.En)) && Callouts.SamePlace(c, p.Key));
    }

    void Lang_Checked(object sender, RoutedEventArgs e)
    {
        if (_radar != null) Render();
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text;
        SearchHint.Visibility = _query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }
}
