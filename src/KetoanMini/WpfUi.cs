using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfData = System.Windows.Data;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

/// <summary>
/// Helper dựng các control WPF dùng lại (nút bo góc, link…) khi migrate vỏ
/// từ WinForms. Tái tạo phong cách của RoundedButton/LinkLabel cũ.
/// </summary>
internal static class WpfUi
{
    private static readonly WpfControls.ControlTemplate FlatButtonTemplate = BuildButtonTemplate();

    /// <summary>Nút bo góc nền đặc (tương đương RoundedButton có BackColor).</summary>
    public static WpfControls.Button FilledButton(string text, WpfMedia.Brush background, WpfMedia.Brush foreground,
        double cornerRadius = 8, double fontPt = 10, bool bold = true)
    {
        var button = NewButton(text, foreground, fontPt, bold, cornerRadius);
        button.Background = background;
        button.BorderBrush = WpfMedia.Brushes.Transparent;
        button.BorderThickness = new Wpf.Thickness(0);
        return button;
    }

    /// <summary>Nút bo góc nền trắng, viền + chữ màu nhấn (tương đương RoundedButton có BorderColor).</summary>
    public static WpfControls.Button OutlineButton(string text, WpfMedia.Brush foreground, WpfMedia.Brush border,
        double cornerRadius = 8, double fontPt = 9.5, bool bold = true)
    {
        var button = NewButton(text, foreground, fontPt, bold, cornerRadius);
        button.Background = WpfMedia.Brushes.White;
        button.BorderBrush = border;
        button.BorderThickness = new Wpf.Thickness(1);
        return button;
    }

    /// <summary>TextBlock dạng liên kết (LinkLabel cũ): màu nhấn, con trỏ tay, có sự kiện click.</summary>
    public static WpfControls.TextBlock LinkText(string text, Action onClick, double fontPt = 9)
    {
        var link = new WpfControls.TextBlock
        {
            Text = text,
            Foreground = WpfTheme.Accent,
            FontFamily = WpfTheme.Font,
            FontSize = WpfTheme.Pt(fontPt),
            Cursor = WpfInput.Cursors.Hand,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        link.MouseLeftButtonUp += (_, _) => onClick();
        return link;
    }

    private static WpfControls.Button NewButton(string text, WpfMedia.Brush foreground, double fontPt, bool bold, double cornerRadius)
    {
        return new WpfControls.Button
        {
            Content = text,
            Foreground = foreground,
            FontFamily = WpfTheme.Font,
            FontSize = WpfTheme.Pt(fontPt),
            FontWeight = bold ? Wpf.FontWeights.Bold : Wpf.FontWeights.Normal,
            Cursor = WpfInput.Cursors.Hand,
            Template = FlatButtonTemplate,
            Tag = cornerRadius,
            SnapsToDevicePixels = true
        };
    }

    private static WpfControls.ControlTemplate BuildButtonTemplate()
    {
        var template = new WpfControls.ControlTemplate(typeof(WpfControls.Button));

        var border = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border), "bd");
        border.SetBinding(WpfControls.Border.BackgroundProperty, TemplatedBinding("Background"));
        border.SetBinding(WpfControls.Border.BorderBrushProperty, TemplatedBinding("BorderBrush"));
        border.SetBinding(WpfControls.Border.BorderThicknessProperty, TemplatedBinding("BorderThickness"));
        // CornerRadius lấy từ Button.Tag (đặt khi tạo nút).
        border.SetBinding(WpfControls.Border.CornerRadiusProperty, new WpfData.Binding
        {
            RelativeSource = new WpfData.RelativeSource(WpfData.RelativeSourceMode.TemplatedParent),
            Path = new Wpf.PropertyPath("Tag"),
            Converter = new CornerRadiusConverter()
        });
        border.SetValue(WpfControls.Border.SnapsToDevicePixelsProperty, true);

        var content = new Wpf.FrameworkElementFactory(typeof(WpfControls.ContentPresenter));
        content.SetValue(WpfControls.ContentPresenter.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Center);
        content.SetValue(WpfControls.ContentPresenter.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        border.AppendChild(content);

        template.VisualTree = border;

        // Hover/press: đổi độ mờ nhẹ giống hiệu ứng sáng/tối ±6% của RoundedButton cũ.
        var hover = new Wpf.Trigger { Property = WpfControls.Control.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Wpf.Setter(WpfControls.Border.OpacityProperty, 0.92, "bd"));
        template.Triggers.Add(hover);

        var pressed = new Wpf.Trigger { Property = WpfControls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Wpf.Setter(WpfControls.Border.OpacityProperty, 0.84, "bd"));
        template.Triggers.Add(pressed);

        var disabled = new Wpf.Trigger { Property = WpfControls.Control.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Wpf.Setter(WpfControls.Border.OpacityProperty, 0.5, "bd"));
        template.Triggers.Add(disabled);

        return template;
    }

    private static WpfData.Binding TemplatedBinding(string path) => new()
    {
        RelativeSource = new WpfData.RelativeSource(WpfData.RelativeSourceMode.TemplatedParent),
        Path = new Wpf.PropertyPath(path)
    };

    private sealed class CornerRadiusConverter : WpfData.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            var radius = value is double d ? d : 8;
            return new Wpf.CornerRadius(radius);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
