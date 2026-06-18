using Wpf = System.Windows;
using WpfAnimation = System.Windows.Media.Animation;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

public sealed class LiquidGlassBorder : WpfControls.Border
{
    public static readonly Wpf.DependencyProperty BaseBorderBrushProperty =
        Wpf.DependencyProperty.Register(nameof(BaseBorderBrush), typeof(WpfMedia.Brush), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata(WpfMedia.Brushes.Transparent, OnBaseBorderBrushChanged));

    public static readonly Wpf.DependencyProperty GlowColorProperty =
        Wpf.DependencyProperty.Register(nameof(GlowColor), typeof(WpfMedia.Color), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString("#AA5CA9FF")!));

    public static readonly Wpf.DependencyProperty EnableMouseGlowProperty =
        Wpf.DependencyProperty.Register(nameof(EnableMouseGlow), typeof(bool), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata(true));

    public static readonly Wpf.DependencyProperty GlowRadiusXProperty =
        Wpf.DependencyProperty.Register(nameof(GlowRadiusX), typeof(double), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata(0.22));

    public static readonly Wpf.DependencyProperty GlowRadiusYProperty =
        Wpf.DependencyProperty.Register(nameof(GlowRadiusY), typeof(double), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata(0.42));

    public static readonly Wpf.DependencyProperty ClipChildToCornerRadiusProperty =
        Wpf.DependencyProperty.Register(nameof(ClipChildToCornerRadius), typeof(bool), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata(false, OnClipChildToCornerRadiusChanged));

    public static readonly Wpf.DependencyProperty ClipSelfToCornerRadiusProperty =
        Wpf.DependencyProperty.Register(nameof(ClipSelfToCornerRadius), typeof(bool), typeof(LiquidGlassBorder),
            new Wpf.PropertyMetadata(false, OnClipSelfToCornerRadiusChanged));

    private readonly WpfMedia.RadialGradientBrush _glowBrush;
    private Wpf.FrameworkElement? _clippedChild;
    private WpfMedia.RectangleGeometry? _selfClip;

    public LiquidGlassBorder()
    {
        _glowBrush = new WpfMedia.RadialGradientBrush
        {
            MappingMode = WpfMedia.BrushMappingMode.RelativeToBoundingBox,
            RadiusX = GlowRadiusX,
            RadiusY = GlowRadiusY,
            Opacity = 0
        };
        _glowBrush.GradientStops.Add(new WpfMedia.GradientStop(WpfMedia.Color.FromArgb(210, 255, 255, 255), 0));
        _glowBrush.GradientStops.Add(new WpfMedia.GradientStop(GlowColor, 0.42));
        _glowBrush.GradientStops.Add(new WpfMedia.GradientStop(WpfMedia.Color.FromArgb(0, 255, 255, 255), 1));

        MouseEnter += (_, _) =>
        {
            if (!EnableMouseGlow) return;
            BorderBrush = _glowBrush;
            _glowBrush.BeginAnimation(WpfMedia.Brush.OpacityProperty, new WpfAnimation.DoubleAnimation(0.72, TimeSpan.FromMilliseconds(160)));
        };
        MouseMove += (_, e) =>
        {
            if (!EnableMouseGlow || ActualWidth <= 0 || ActualHeight <= 0) return;
            var p = e.GetPosition(this);
            var center = new Wpf.Point(Math.Clamp(p.X / ActualWidth, 0, 1), Math.Clamp(p.Y / ActualHeight, 0, 1));
            _glowBrush.Center = center;
            _glowBrush.GradientOrigin = center;
            _glowBrush.RadiusX = GlowRadiusX;
            _glowBrush.RadiusY = GlowRadiusY;
            if (_glowBrush.GradientStops.Count > 1)
                _glowBrush.GradientStops[1].Color = GlowColor;
        };
        MouseLeave += (_, _) =>
        {
            if (!EnableMouseGlow) return;
            var animation = new WpfAnimation.DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
            animation.Completed += (_, _) => BorderBrush = BaseBorderBrush;
            _glowBrush.BeginAnimation(WpfMedia.Brush.OpacityProperty, animation);
        };
    }

    public WpfMedia.Brush BaseBorderBrush
    {
        get => (WpfMedia.Brush)GetValue(BaseBorderBrushProperty);
        set => SetValue(BaseBorderBrushProperty, value);
    }

    public WpfMedia.Color GlowColor
    {
        get => (WpfMedia.Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    public bool EnableMouseGlow
    {
        get => (bool)GetValue(EnableMouseGlowProperty);
        set => SetValue(EnableMouseGlowProperty, value);
    }

    public double GlowRadiusX
    {
        get => (double)GetValue(GlowRadiusXProperty);
        set => SetValue(GlowRadiusXProperty, value);
    }

    public double GlowRadiusY
    {
        get => (double)GetValue(GlowRadiusYProperty);
        set => SetValue(GlowRadiusYProperty, value);
    }

    public bool ClipChildToCornerRadius
    {
        get => (bool)GetValue(ClipChildToCornerRadiusProperty);
        set => SetValue(ClipChildToCornerRadiusProperty, value);
    }

    public bool ClipSelfToCornerRadius
    {
        get => (bool)GetValue(ClipSelfToCornerRadiusProperty);
        set => SetValue(ClipSelfToCornerRadiusProperty, value);
    }

    protected override void OnRenderSizeChanged(Wpf.SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ApplyChildClip();
        ApplySelfClip();
    }

    protected override void OnVisualChildrenChanged(Wpf.DependencyObject visualAdded, Wpf.DependencyObject visualRemoved)
    {
        if (_clippedChild is not null)
            _clippedChild.SizeChanged -= OnChildSizeChanged;

        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        _clippedChild = Child as Wpf.FrameworkElement;
        if (_clippedChild is not null)
            _clippedChild.SizeChanged += OnChildSizeChanged;

        ApplyChildClip();
    }

    protected override void OnPropertyChanged(Wpf.DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == CornerRadiusProperty ||
            e.Property == BorderThicknessProperty ||
            e.Property == PaddingProperty)
        {
            ApplyChildClip();
            ApplySelfClip();
        }
    }

    private static void OnBaseBorderBrushChanged(Wpf.DependencyObject d, Wpf.DependencyPropertyChangedEventArgs e)
    {
        if (d is LiquidGlassBorder border && border.BorderBrush != border._glowBrush)
            border.BorderBrush = (WpfMedia.Brush)e.NewValue;
    }

    private static void OnClipChildToCornerRadiusChanged(Wpf.DependencyObject d, Wpf.DependencyPropertyChangedEventArgs e)
    {
        if (d is LiquidGlassBorder border)
            border.ApplyChildClip();
    }

    private static void OnClipSelfToCornerRadiusChanged(Wpf.DependencyObject d, Wpf.DependencyPropertyChangedEventArgs e)
    {
        if (d is LiquidGlassBorder border)
            border.ApplySelfClip();
    }

    private void OnChildSizeChanged(object sender, Wpf.SizeChangedEventArgs e)
        => ApplyChildClip();

    private void ApplyChildClip()
    {
        if (Child is not Wpf.FrameworkElement child)
            return;

        if (!ClipChildToCornerRadius)
        {
            child.Clip = null;
            return;
        }

        var width = child.ActualWidth;
        var height = child.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var corner = CornerRadius;
        var maxCorner = Math.Max(Math.Max(corner.TopLeft, corner.TopRight), Math.Max(corner.BottomRight, corner.BottomLeft));
        var borderInset = Math.Max(Math.Max(BorderThickness.Left, BorderThickness.Top), Math.Max(BorderThickness.Right, BorderThickness.Bottom));
        var radius = Math.Max(0, Math.Min(Math.Min(width, height) / 2, maxCorner - borderInset));

        child.Clip = new WpfMedia.RectangleGeometry(new Wpf.Rect(0, 0, width, height), radius, radius);
    }

    private void ApplySelfClip()
    {
        if (!ClipSelfToCornerRadius)
        {
            if (ReferenceEquals(Clip, _selfClip))
                Clip = null;
            _selfClip = null;
            return;
        }

        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var corner = CornerRadius;
        var maxCorner = Math.Max(Math.Max(corner.TopLeft, corner.TopRight), Math.Max(corner.BottomRight, corner.BottomLeft));
        var radius = Math.Max(0, Math.Min(Math.Min(ActualWidth, ActualHeight) / 2, maxCorner));

        _selfClip ??= new WpfMedia.RectangleGeometry();
        _selfClip.Rect = new Wpf.Rect(0, 0, ActualWidth, ActualHeight);
        _selfClip.RadiusX = radius;
        _selfClip.RadiusY = radius;
        Clip = _selfClip;
    }
}
