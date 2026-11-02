using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CS2Optimizer.Views;

/// <summary>The app's panel shape: works like a Border, but its corners are cut off at 45° instead of rounded.</summary>
public class Plate : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = Prop<Brush?>(nameof(Background), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty BorderBrushProperty = Prop<Brush?>(nameof(BorderBrush), null, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty BorderThicknessProperty = Prop(nameof(BorderThickness), 0.0, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty PaddingProperty = Prop(nameof(Padding), new Thickness(), FrameworkPropertyMetadataOptions.AffectsMeasure);
    public static readonly DependencyProperty CutTopRightProperty = Prop(nameof(CutTopRight), 0.0, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty CutBottomLeftProperty = Prop(nameof(CutBottomLeft), 0.0, FrameworkPropertyMetadataOptions.AffectsRender);
    public static readonly DependencyProperty ClipContentProperty = Prop(nameof(ClipContent), false, FrameworkPropertyMetadataOptions.AffectsArrange);

    static DependencyProperty Prop<T>(string name, T value, FrameworkPropertyMetadataOptions options) =>
        DependencyProperty.Register(name, typeof(T), typeof(Plate), new FrameworkPropertyMetadata(value, options));

    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    public double CutTopRight { get => (double)GetValue(CutTopRightProperty); set => SetValue(CutTopRightProperty, value); }
    public double CutBottomLeft { get => (double)GetValue(CutBottomLeftProperty); set => SetValue(CutBottomLeftProperty, value); }

    /// <summary>Also cuts the content, for pictures and anything else that reaches the corners.</summary>
    public bool ClipContent { get => (bool)GetValue(ClipContentProperty); set => SetValue(ClipContentProperty, value); }

    protected override Size MeasureOverride(Size available)
    {
        var p = Padding;
        double w = p.Left + p.Right, h = p.Top + p.Bottom;
        if (Child == null) return new Size(w, h);
        Child.Measure(new Size(Math.Max(0, available.Width - w), Math.Max(0, available.Height - h)));
        return new Size(Child.DesiredSize.Width + w, Child.DesiredSize.Height + h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var p = Padding;
        Child?.Arrange(new Rect(p.Left, p.Top, Math.Max(0, final.Width - p.Left - p.Right), Math.Max(0, final.Height - p.Top - p.Bottom)));
        return final;
    }

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) =>
        ClipContent ? Outline(RenderSize, 0) : base.GetLayoutClip(layoutSlotSize);

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (Background != null) dc.DrawGeometry(Background, null, Outline(size, 0));
        if (BorderBrush == null || BorderThickness <= 0) return;

        // Keeps straight edges on whole pixels at any display scale.
        double half = BorderThickness / 2;
        dc.PushGuidelineSet(new GuidelineSet([half, size.Width - half], [half, size.Height - half]));
        dc.DrawGeometry(null, new Pen(BorderBrush, BorderThickness), Outline(size, half));
        dc.Pop();
    }

    Geometry Outline(Size size, double inset)
    {
        double left = inset, top = inset, right = size.Width - inset, bottom = size.Height - inset;
        double cut = Math.Min(CutTopRight, Math.Min(size.Width, size.Height));
        double low = Math.Min(CutBottomLeft, Math.Min(size.Width, size.Height));
        var shape = new StreamGeometry();
        using (var c = shape.Open())
        {
            c.BeginFigure(new Point(left, top), isFilled: true, isClosed: true);
            c.PolyLineTo([new(right - cut, top), new(right, top + cut), new(right, bottom), new(left + low, bottom), new(left, bottom - low)], isStroked: true, isSmoothJoin: false);
        }
        shape.Freeze();
        return shape;
    }
}
