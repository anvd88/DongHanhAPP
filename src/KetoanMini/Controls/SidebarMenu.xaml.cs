using Wpf = System.Windows;
using WpfAnimation = System.Windows.Media.Animation;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfShapes = System.Windows.Shapes;
using WpfThreading = System.Windows.Threading;

namespace KetoanMini;

public sealed class SidebarNavigationEventArgs : EventArgs
{
    public SidebarNavigationEventArgs(string key) => Key = key;
    public string Key { get; }
}

public partial class SidebarMenu : WpfControls.UserControl
{
    private const double IndicatorVerticalBleed = 2.5;
    private const double NormalBorderOpacity = 0.62;
    private const double HoverBorderOpacity = 0.88;

    public static readonly Wpf.DependencyProperty IsMenuItemSelectedProperty =
        Wpf.DependencyProperty.RegisterAttached(
            "IsMenuItemSelected",
            typeof(bool),
            typeof(SidebarMenu),
            new Wpf.PropertyMetadata(false));

    private readonly Dictionary<string, SidebarMenuEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly WpfMedia.RadialGradientBrush _selectionPointerBorderBrush = CreatePointerBorderBrush();
    private bool _isBuilt;
    private bool _indicatorPlaced;
    private bool _pointerBorderActive;
    private CancellationTokenSource? _selectionAnimationCts;
    private SidebarMenuEntry? _selectedEntry;

    public event EventHandler<SidebarNavigationEventArgs>? NavigationRequested;

    public bool IsAdmin { get; set; }

    public string ActiveKey { get; private set; } = "dashboard";

    public SidebarMenu()
    {
        InitializeComponent();
        VersionText.Text = $"Phiên bản {AppVersion.CurrentText}";
        Loaded += (_, _) => BuildMenu();
        SizeChanged += (_, _) => QueueSnapIndicatorToActive();
    }

    public static bool GetIsMenuItemSelected(Wpf.DependencyObject obj)
        => (bool)obj.GetValue(IsMenuItemSelectedProperty);

    public static void SetIsMenuItemSelected(Wpf.DependencyObject obj, bool value)
        => obj.SetValue(IsMenuItemSelectedProperty, value);

    public void SetActive(string key)
    {
        ActiveKey = key;

        if (!_isBuilt || !_entries.TryGetValue(key, out var entry))
            return;

        _ = MoveSelectionIndicatorAsync(entry.Button);
    }

    private void BuildMenu()
    {
        if (_isBuilt) return;
        _isBuilt = true;

        MenuPanel.Children.Clear();
        _entries.Clear();

        AddMenuItem("dashboard", "Tổng quan", IconData.Dashboard, featured: true);
        AddGroupHeader("NGHIỆP VỤ");
        AddMenuItem("ketoan", "Kế toán", IconData.Accounting);
        AddMenuItem("kho", "Hàng tồn kho", IconData.Inventory);
        AddMenuItem("banhang", "Bán hàng", IconData.Sales);
        AddMenuItem("muahang", "Mua hàng", IconData.Purchase);
        AddMenuItem("giacong", "Gia công", IconData.Production);
        AddMenuItem("taisan", "Tài sản cố định", IconData.Asset);
        AddGroupHeader("QUẢN LÝ");
        if (IsAdmin)
            AddMenuItem("nhansu", "Nhân sự", IconData.Users);
        AddMenuItem("baocao", "Báo cáo", IconData.Report);
        AddMenuItem("danhmuc", "Danh mục", IconData.Catalog);
        AddMenuItem("congno", "Công nợ", IconData.Debt);
        AddGroupHeader("HỆ THỐNG");
        AddMenuItem("caidat", "Cài đặt", IconData.Settings);
        AddMenuItem("saoluu", "Sao lưu", IconData.Backup);
        AddMenuItem("lichhen", "Lịch hẹn", IconData.Calendar);
        AddMenuItem("tichhop", "Tích hợp", IconData.Integration);
        if (IsAdmin)
            AddMenuItem("capnhat", "Cập nhật", IconData.Update);

        SetActive(ActiveKey);
        MenuHost.SizeChanged += (_, _) => QueueSnapIndicatorToActive();
    }

    private void AddGroupHeader(string text)
    {
        MenuPanel.Children.Add(new WpfControls.TextBlock
        {
            Text = text,
            Style = (Wpf.Style)FindResource("GroupHeaderStyle")
        });
    }

    private void AddMenuItem(string key, string title, string iconData, bool featured = false)
    {
        var button = new WpfControls.Button
        {
            Style = (Wpf.Style)FindResource(featured ? "SidebarFeaturedButtonStyle" : "SidebarMenuButtonStyle")
        };

        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(featured ? 34 : 28) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(10) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var icon = new WpfShapes.Path
        {
            Data = WpfMedia.Geometry.Parse(iconData),
            Stroke = MutableBrush("#B8CBF3"),
            StrokeThickness = 1.9,
            StrokeStartLineCap = WpfMedia.PenLineCap.Round,
            StrokeEndLineCap = WpfMedia.PenLineCap.Round,
            StrokeLineJoin = WpfMedia.PenLineJoin.Round,
            Stretch = WpfMedia.Stretch.Uniform,
            Width = featured ? 17 : 15,
            Height = featured ? 17 : 15,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };

        var iconBox = new WpfControls.Border
        {
            Width = featured ? 34 : 28,
            Height = featured ? 34 : 28,
            CornerRadius = new Wpf.CornerRadius(featured ? 10 : 8),
            Background = MutableBrush(featured ? "#1C315E" : "#16284E"),
            BorderBrush = MutableBrush(featured ? "#31558D" : "#27467A"),
            BorderThickness = new Wpf.Thickness(1),
            Child = icon
        };
        grid.Children.Add(iconBox);

        var label = new WpfControls.TextBlock
        {
            Text = title,
            Foreground = MutableBrush("#F1F5FF"),
            FontSize = featured ? 14.5 : 13.2,
            FontWeight = featured ? Wpf.FontWeights.SemiBold : Wpf.FontWeights.Medium,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis
        };
        WpfControls.Grid.SetColumn(label, 2);
        grid.Children.Add(label);

        button.Content = grid;
        button.Click += (_, _) => NavigationRequested?.Invoke(this, new SidebarNavigationEventArgs(key));
        button.MouseEnter += (_, _) => OnMenuItemMouseEnter(key);
        button.MouseMove += (_, e) => OnMenuItemMouseMove(key, e.GetPosition(SelectionIndicator));
        button.MouseLeave += (_, _) => OnMenuItemMouseLeave(key);
        button.SizeChanged += (_, _) =>
        {
            if (string.Equals(ActiveKey, key, StringComparison.OrdinalIgnoreCase))
                QueueSnapIndicatorToActive();
        };

        MenuPanel.Children.Add(button);
        _entries[key] = new SidebarMenuEntry(key, button, iconBox, icon, label);
    }

    private async Task MoveSelectionIndicatorAsync(Wpf.FrameworkElement target)
    {
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(() => MoveSelectionIndicatorAsync(target)).Task.Unwrap();
            return;
        }

        if (!TryGetEntry(target, out var targetEntry))
            return;

        if (!target.IsLoaded || target.ActualHeight <= 0 || MenuHost.ActualHeight <= 0)
        {
            await Dispatcher.InvokeAsync(() => { }, WpfThreading.DispatcherPriority.Loaded);
            if (!target.IsLoaded || target.ActualHeight <= 0)
                return;
        }

        var cts = ResetSelectionAnimation();

        try
        {
            var targetBounds = target.TransformToAncestor(MenuHost)
                .TransformBounds(new Wpf.Rect(0, 0, target.ActualWidth, target.ActualHeight));
            var targetY = targetBounds.Top - IndicatorVerticalBleed;
            var targetHeight = Math.Max(1, targetBounds.Height + (IndicatorVerticalBleed * 2));

            if (!_indicatorPlaced || SelectionIndicator.Opacity <= 0.01)
            {
                SnapIndicatorTo(targetY, targetHeight);
                SelectionIndicator.Opacity = 0.96;
                SelectionBorderBrush.Opacity = NormalBorderOpacity;
                _indicatorPlaced = true;
                ApplySelectedEntry(targetEntry, animate: false);
                return;
            }

            var currentY = SelectionTranslate.Y;
            var distance = targetY - currentY;
            if (Math.Abs(distance) < 0.5 && Math.Abs(SelectionIndicator.Height - targetHeight) < 0.5)
            {
                ApplySelectedEntry(targetEntry, animate: true);
                return;
            }

            var direction = Math.Sign(distance);
            if (direction == 0)
                direction = 1;
            var overshoot = Math.Clamp(Math.Abs(distance) * 0.07, 2.0, 7.0) * direction;
            var moveEase = new WpfAnimation.QuinticEase { EasingMode = WpfAnimation.EasingMode.EaseInOut };
            var settleEase = new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut };

            await AnimateSelectionPhaseAsync(
                y: currentY,
                height: Math.Max(1, SelectionIndicator.Height),
                scaleX: 1.045,
                scaleY: 1.18,
                opacity: 0.88,
                borderOpacity: 0.62,
                milliseconds: 90,
                easing: new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut },
                cts.Token);

            await AnimateSelectionPhaseAsync(
                y: targetY + overshoot,
                height: targetHeight,
                scaleX: 1.025,
                scaleY: 1.1,
                opacity: 0.91,
                borderOpacity: 0.58,
                milliseconds: 315,
                easing: moveEase,
                cts.Token);

            await AnimateSelectionPhaseAsync(
                y: targetY,
                height: targetHeight,
                scaleX: 1,
                scaleY: 1,
                opacity: 0.96,
                borderOpacity: NormalBorderOpacity,
                milliseconds: 145,
                easing: settleEase,
                cts.Token);

            StopSelectionAnimationsKeepCurrent();
            ApplySelectedEntry(targetEntry, animate: true);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_selectionAnimationCts, cts))
                StopSelectionAnimationsKeepCurrent();
        }
        finally
        {
            if (ReferenceEquals(_selectionAnimationCts, cts))
            {
                _selectionAnimationCts.Dispose();
                _selectionAnimationCts = null;
            }
        }
    }

    private CancellationTokenSource ResetSelectionAnimation()
    {
        _selectionAnimationCts?.Cancel();
        _selectionAnimationCts?.Dispose();
        StopSelectionAnimationsKeepCurrent();
        StopPointerBorder(resetOpacity: true);
        _selectionAnimationCts = new CancellationTokenSource();
        return _selectionAnimationCts;
    }

    private async Task AnimateSelectionPhaseAsync(
        double y,
        double height,
        double scaleX,
        double scaleY,
        double opacity,
        double borderOpacity,
        int milliseconds,
        WpfAnimation.IEasingFunction easing,
        CancellationToken cancellationToken)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        BeginDoubleAnimation(SelectionTranslate, WpfMedia.TranslateTransform.YProperty, SelectionTranslate.Y, y, duration, easing);
        BeginDoubleAnimation(SelectionScale, WpfMedia.ScaleTransform.ScaleXProperty, SelectionScale.ScaleX, scaleX, duration, easing);
        BeginDoubleAnimation(SelectionScale, WpfMedia.ScaleTransform.ScaleYProperty, SelectionScale.ScaleY, scaleY, duration, easing);
        BeginDoubleAnimation(SelectionIndicator, Wpf.FrameworkElement.HeightProperty, SelectionIndicator.Height, height, duration, easing);
        BeginDoubleAnimation(SelectionIndicator, Wpf.UIElement.OpacityProperty, SelectionIndicator.Opacity, opacity, duration, easing);
        BeginDoubleAnimation(SelectionBorderBrush, WpfMedia.Brush.OpacityProperty, SelectionBorderBrush.Opacity, borderOpacity, duration, easing);
        await Task.Delay(duration, cancellationToken);
    }

    private void StopSelectionAnimationsKeepCurrent()
    {
        var y = SelectionTranslate.Y;
        var scaleX = SelectionScale.ScaleX;
        var scaleY = SelectionScale.ScaleY;
        var height = SelectionIndicator.Height;
        var opacity = SelectionIndicator.Opacity;
        var borderOpacity = SelectionBorderBrush.Opacity;

        SelectionTranslate.BeginAnimation(WpfMedia.TranslateTransform.YProperty, null);
        SelectionScale.BeginAnimation(WpfMedia.ScaleTransform.ScaleXProperty, null);
        SelectionScale.BeginAnimation(WpfMedia.ScaleTransform.ScaleYProperty, null);
        SelectionIndicator.BeginAnimation(Wpf.FrameworkElement.HeightProperty, null);
        SelectionIndicator.BeginAnimation(Wpf.UIElement.OpacityProperty, null);
        SelectionBorderBrush.BeginAnimation(WpfMedia.Brush.OpacityProperty, null);

        SelectionTranslate.Y = y;
        SelectionScale.ScaleX = scaleX;
        SelectionScale.ScaleY = scaleY;
        SelectionIndicator.Height = Math.Max(1, height);
        SelectionIndicator.Opacity = opacity;
        SelectionBorderBrush.Opacity = borderOpacity;
    }

    private void ApplySelectedEntry(SidebarMenuEntry selectedEntry, bool animate)
    {
        _selectedEntry = selectedEntry;

        foreach (var entry in _entries.Values)
        {
            var selected = ReferenceEquals(entry, selectedEntry);
            SetIsMenuItemSelected(entry.Button, selected);
            ApplyEntryVisualState(entry, selected, hover: false, animate);
        }
    }

    private void OnMenuItemMouseEnter(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;

        if (GetIsMenuItemSelected(entry.Button))
        {
            var ease = new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut };
            BeginDoubleAnimation(SelectionScale, WpfMedia.ScaleTransform.ScaleXProperty, SelectionScale.ScaleX, 1.02, TimeSpan.FromMilliseconds(170), ease);
            BeginDoubleAnimation(SelectionScale, WpfMedia.ScaleTransform.ScaleYProperty, SelectionScale.ScaleY, 1.055, TimeSpan.FromMilliseconds(170), ease);
            BeginDoubleAnimation(SelectionBorderBrush, WpfMedia.Brush.OpacityProperty, SelectionBorderBrush.Opacity, HoverBorderOpacity, TimeSpan.FromMilliseconds(170), ease);
            StartPointerBorder();
            return;
        }

        ApplyEntryVisualState(entry, selected: false, hover: true, animate: true);
    }

    private void OnMenuItemMouseMove(string key, Wpf.Point pointer)
    {
        if (!_entries.TryGetValue(key, out var entry) || !GetIsMenuItemSelected(entry.Button))
            return;

        if (SelectionIndicator.ActualWidth <= 0 || SelectionIndicator.ActualHeight <= 0)
            return;

        var x = Math.Clamp(pointer.X / SelectionIndicator.ActualWidth, 0, 1);
        var y = Math.Clamp(pointer.Y / SelectionIndicator.ActualHeight, 0, 1);
        var center = new Wpf.Point(x, y);
        _selectionPointerBorderBrush.Center = center;
        _selectionPointerBorderBrush.GradientOrigin = center;
        _selectionPointerBorderBrush.RadiusX = 0.13 + (0.06 * Math.Abs(0.5 - x));
        _selectionPointerBorderBrush.RadiusY = 0.42 + (0.2 * Math.Abs(0.5 - y));

        if (!_pointerBorderActive)
            StartPointerBorder();
    }

    private void OnMenuItemMouseLeave(string key)
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;

        if (GetIsMenuItemSelected(entry.Button))
        {
            var ease = new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut };
            BeginDoubleAnimation(SelectionScale, WpfMedia.ScaleTransform.ScaleXProperty, SelectionScale.ScaleX, 1, TimeSpan.FromMilliseconds(260), ease);
            BeginDoubleAnimation(SelectionScale, WpfMedia.ScaleTransform.ScaleYProperty, SelectionScale.ScaleY, 1, TimeSpan.FromMilliseconds(260), ease);
            BeginDoubleAnimation(SelectionBorderBrush, WpfMedia.Brush.OpacityProperty, SelectionBorderBrush.Opacity, NormalBorderOpacity, TimeSpan.FromMilliseconds(260), ease);
            FadeOutPointerBorder();
            return;
        }

        ApplyEntryVisualState(entry, selected: false, hover: false, animate: true);
    }

    private void ApplyEntryVisualState(SidebarMenuEntry entry, bool selected, bool hover, bool animate)
    {
        entry.Button.Background = selected ? WpfMedia.Brushes.Transparent : hover ? MutableBrush("#1AFFFFFF") : WpfMedia.Brushes.Transparent;
        entry.Button.BorderBrush = selected ? WpfMedia.Brushes.Transparent : hover ? MutableBrush("#2EDDF5FF") : WpfMedia.Brushes.Transparent;

        var iconBackground = selected ? "#0016284E" : hover ? "#203B70" : "#16284E";
        var iconBorder = selected ? "#0027467A" : hover ? "#3F6BA5" : "#27467A";
        var iconStroke = selected ? "#FFFFFF" : hover ? "#E8F7FF" : "#B8CBF3";
        var textForeground = selected ? "#FFFFFF" : hover ? "#FFFFFF" : "#F1F5FF";

        AnimateBrush(entry.IconBox.Background, Color(iconBackground), animate ? 180 : 0);
        AnimateBrush(entry.IconBox.BorderBrush, Color(iconBorder), animate ? 180 : 0);
        AnimateBrush(entry.Icon.Stroke, Color(iconStroke), animate ? 180 : 0);
        AnimateBrush(entry.Text.Foreground, Color(textForeground), animate ? 180 : 0);
        entry.Text.FontWeight = selected ? Wpf.FontWeights.SemiBold : Wpf.FontWeights.Medium;
    }

    private void QueueSnapIndicatorToActive()
    {
        if (!_isBuilt || !_entries.ContainsKey(ActiveKey))
            return;

        Dispatcher.BeginInvoke(new Action(SnapIndicatorToActive), WpfThreading.DispatcherPriority.Loaded);
    }

    private void SnapIndicatorToActive()
    {
        if (!_entries.TryGetValue(ActiveKey, out var entry) || entry.Button.ActualHeight <= 0 || MenuHost.ActualHeight <= 0)
            return;

        var bounds = entry.Button.TransformToAncestor(MenuHost)
            .TransformBounds(new Wpf.Rect(0, 0, entry.Button.ActualWidth, entry.Button.ActualHeight));
        SnapIndicatorTo(bounds.Top - IndicatorVerticalBleed, Math.Max(1, bounds.Height + (IndicatorVerticalBleed * 2)));
        SelectionIndicator.Opacity = 0.96;
        SelectionBorderBrush.Opacity = NormalBorderOpacity;
        _indicatorPlaced = true;

        if (_selectedEntry is null || !ReferenceEquals(_selectedEntry, entry))
            ApplySelectedEntry(entry, animate: false);
    }

    private void SnapIndicatorTo(double y, double height)
    {
        StopSelectionAnimationsKeepCurrent();
        SelectionTranslate.Y = y;
        SelectionScale.ScaleX = 1;
        SelectionScale.ScaleY = 1;
        SelectionIndicator.Height = height;
        SelectionBorderBrush.Opacity = NormalBorderOpacity;
        StopPointerBorder(resetOpacity: true);
    }

    private void StartPointerBorder()
    {
        _pointerBorderActive = true;
        _selectionPointerBorderBrush.BeginAnimation(WpfMedia.Brush.OpacityProperty, null);
        SelectionIndicator.BorderBrush = _selectionPointerBorderBrush;

        BeginDoubleAnimation(
            _selectionPointerBorderBrush,
            WpfMedia.Brush.OpacityProperty,
            _selectionPointerBorderBrush.Opacity,
            0.96,
            TimeSpan.FromMilliseconds(180),
            new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut });
    }

    private void FadeOutPointerBorder()
    {
        var fade = new WpfAnimation.DoubleAnimation(_selectionPointerBorderBrush.Opacity, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut },
            FillBehavior = WpfAnimation.FillBehavior.HoldEnd
        };
        fade.Completed += (_, _) => StopPointerBorder(resetOpacity: true);
        _selectionPointerBorderBrush.BeginAnimation(WpfMedia.Brush.OpacityProperty, fade, WpfAnimation.HandoffBehavior.SnapshotAndReplace);
    }

    private void StopPointerBorder(bool resetOpacity)
    {
        _selectionPointerBorderBrush.BeginAnimation(WpfMedia.Brush.OpacityProperty, null);
        if (resetOpacity)
            _selectionPointerBorderBrush.Opacity = 0;

        _pointerBorderActive = false;
        SelectionIndicator.BorderBrush = SelectionBorderBrush;
    }

    private bool TryGetEntry(Wpf.FrameworkElement target, out SidebarMenuEntry entry)
    {
        entry = _entries.Values.FirstOrDefault(item => ReferenceEquals(item.Button, target))!;
        return entry is not null;
    }

    private static void BeginDoubleAnimation(
        Wpf.DependencyObject target,
        Wpf.DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        WpfAnimation.IEasingFunction? easing)
    {
        var animation = new WpfAnimation.DoubleAnimation(from, to, duration)
        {
            EasingFunction = easing,
            FillBehavior = WpfAnimation.FillBehavior.HoldEnd
        };

        if (target is Wpf.UIElement element)
            element.BeginAnimation(property, animation, WpfAnimation.HandoffBehavior.SnapshotAndReplace);
        else if (target is WpfAnimation.Animatable animatable)
            animatable.BeginAnimation(property, animation, WpfAnimation.HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateBrush(WpfMedia.Brush? brush, WpfMedia.Color to, int milliseconds)
    {
        if (brush is not WpfMedia.SolidColorBrush solid || solid.IsFrozen)
            return;

        if (milliseconds <= 0)
        {
            solid.BeginAnimation(WpfMedia.SolidColorBrush.ColorProperty, null);
            solid.Color = to;
            return;
        }

        var animation = new WpfAnimation.ColorAnimation(solid.Color, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new WpfAnimation.CubicEase { EasingMode = WpfAnimation.EasingMode.EaseOut },
            FillBehavior = WpfAnimation.FillBehavior.HoldEnd
        };
        solid.BeginAnimation(WpfMedia.SolidColorBrush.ColorProperty, animation, WpfAnimation.HandoffBehavior.SnapshotAndReplace);
    }

    private static WpfMedia.Brush Brush(string hex)
    {
        var brush = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    private static WpfMedia.SolidColorBrush MutableBrush(string hex)
        => new(Color(hex));

    private static WpfMedia.RadialGradientBrush CreatePointerBorderBrush()
    {
        var brush = new WpfMedia.RadialGradientBrush
        {
            MappingMode = WpfMedia.BrushMappingMode.RelativeToBoundingBox,
            Center = new Wpf.Point(0.5, 0.5),
            GradientOrigin = new Wpf.Point(0.5, 0.5),
            RadiusX = 0.22,
            RadiusY = 0.62,
            Opacity = 0
        };
        brush.GradientStops.Add(new WpfMedia.GradientStop(Color("#FFFFFFFF"), 0));
        brush.GradientStops.Add(new WpfMedia.GradientStop(Color("#D8F5FFFF"), 0.16));
        brush.GradientStops.Add(new WpfMedia.GradientStop(Color("#7BBFEAFF"), 0.38));
        brush.GradientStops.Add(new WpfMedia.GradientStop(Color("#244D76A8"), 1));
        return brush;
    }

    private static WpfMedia.Color Color(string hex)
        => (WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(hex)!;

    private sealed record SidebarMenuEntry(
        string Key,
        WpfControls.Button Button,
        WpfControls.Border IconBox,
        WpfShapes.Path Icon,
        WpfControls.TextBlock Text);

    private static class IconData
    {
        public const string Dashboard = "M3,3 L10,3 L10,10 L3,10 Z M14,3 L21,3 L21,10 L14,10 Z M3,14 L10,14 L10,21 L3,21 Z M14,14 L21,14 L21,21 L14,21 Z";
        public const string Accounting = "M5,4 L19,4 L19,20 L5,20 Z M8,8 L16,8 M8,12 L16,12 M8,16 L12,16";
        public const string Inventory = "M4,8 L12,4 L20,8 L12,12 Z M4,8 L4,16 L12,20 L20,16 L20,8 M12,12 L12,20";
        public const string Sales = "M5,19 L19,19 M7,16 L7,10 M12,16 L12,5 M17,16 L17,8";
        public const string Purchase = "M5,7 L8,7 L10,16 L18,16 M10,10 L19,10 L17,14 L10,14 M11,20 A1,1 0 1 1 11,18 A1,1 0 1 1 11,20 M17,20 A1,1 0 1 1 17,18 A1,1 0 1 1 17,20";
        public const string Production = "M12,3 L14,7 L18,7 L16,11 L19,14 L15,16 L15,21 L10,21 L10,16 L6,14 L9,11 L7,7 L11,7 Z";
        public const string Asset = "M12,3 L20,8 L20,18 L12,22 L4,18 L4,8 Z M12,3 L12,12 M4,8 L12,12 L20,8";
        public const string Users = "M8,11 A4,4 0 1 1 8,3 A4,4 0 1 1 8,11 M2,21 C2,16 5,14 8,14 C11,14 14,16 14,21 M17,10 A3,3 0 1 1 17,4 M15,15 C18,15 21,17 21,21";
        public const string Report = "M5,4 L19,4 L19,20 L5,20 Z M8,16 L8,12 M12,16 L12,8 M16,16 L16,10";
        public const string Catalog = "M5,6 L19,6 M5,12 L19,12 M5,18 L19,18";
        public const string Settings = "M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8 M12,3 L12,6 M12,18 L12,21 M3,12 L6,12 M18,12 L21,12 M5.5,5.5 L7.6,7.6 M16.4,16.4 L18.5,18.5 M18.5,5.5 L16.4,7.6 M7.6,16.4 L5.5,18.5";
        public const string Backup = "M6,5 L18,5 L18,19 L6,19 Z M8,5 L8,11 L16,11 L16,5 M9,16 L15,16";
        public const string Update = "M12,5 L12,19 M6,11 L12,5 L18,11 M5,20 L19,20";
        public const string Debt = "M5,5 L19,5 L19,19 L5,19 Z M8,9 L16,9 M8,13 L16,13 M8,17 L12,17 M15,3 L15,7";
        public const string Calendar = "M6,4 L6,8 M18,4 L18,8 M4,7 L20,7 L20,20 L4,20 Z M8,11 L10,11 M13,11 L15,11 M8,15 L10,15 M13,15 L15,15";
        public const string Integration = "M7,7 A3,3 0 1 1 7,13 A3,3 0 1 1 7,7 M17,3 A3,3 0 1 1 17,9 A3,3 0 1 1 17,3 M17,15 A3,3 0 1 1 17,21 A3,3 0 1 1 17,15 M10,10 L14,7 M10,12 L14,17";
    }
}
