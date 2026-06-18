using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using WpfShapes = System.Windows.Shapes;

namespace KetoanMini;

public sealed class SidebarNavigationEventArgs : EventArgs
{
    public SidebarNavigationEventArgs(string key) => Key = key;
    public string Key { get; }
}

public partial class SidebarMenu : WpfControls.UserControl
{
    private readonly Dictionary<string, SidebarMenuEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _isBuilt;

    public event EventHandler<SidebarNavigationEventArgs>? NavigationRequested;

    public bool IsAdmin { get; set; }

    public string ActiveKey { get; private set; } = "dashboard";

    public SidebarMenu()
    {
        InitializeComponent();
        VersionText.Text = $"Phiên bản {AppVersion.CurrentText}";
        Loaded += (_, _) => BuildMenu();
    }

    public void SetActive(string key)
    {
        ActiveKey = key;
        foreach (var entry in _entries.Values)
        {
            var active = string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase);
            entry.Button.Tag = active ? "Active" : null;
            entry.IconBox.Background = active ? Brush("#5EA0FF") : Brush("#16284E");
            entry.IconBox.BorderBrush = active ? Brush("#9DC5FF") : Brush("#27467A");
            entry.Icon.Stroke = active ? WpfMedia.Brushes.White : Brush("#B8CBF3");
            entry.Text.Foreground = active ? WpfMedia.Brushes.White : Brush("#F1F5FF");
            entry.Text.FontWeight = active ? Wpf.FontWeights.SemiBold : Wpf.FontWeights.Medium;
        }
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
            Stroke = Brush("#B8CBF3"),
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
            Background = Brush(featured ? "#1C315E" : "#16284E"),
            BorderBrush = Brush(featured ? "#31558D" : "#27467A"),
            BorderThickness = new Wpf.Thickness(1),
            Child = icon
        };
        grid.Children.Add(iconBox);

        var label = new WpfControls.TextBlock
        {
            Text = title,
            Foreground = Brush("#F1F5FF"),
            FontSize = featured ? 14.5 : 13.2,
            FontWeight = featured ? Wpf.FontWeights.SemiBold : Wpf.FontWeights.Medium,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis
        };
        WpfControls.Grid.SetColumn(label, 2);
        grid.Children.Add(label);

        button.Content = grid;
        button.Click += (_, _) => NavigationRequested?.Invoke(this, new SidebarNavigationEventArgs(key));

        MenuPanel.Children.Add(button);
        _entries[key] = new SidebarMenuEntry(key, button, iconBox, icon, label);
    }

    private static WpfMedia.Brush Brush(string hex)
    {
        var brush = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

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
