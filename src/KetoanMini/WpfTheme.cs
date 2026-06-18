using WpfMedia = System.Windows.Media;

namespace KetoanMini;

/// <summary>
/// Phiên bản WPF của <see cref="AppTheme"/> — cùng bảng màu/phông, nhưng trả về
/// <see cref="WpfMedia.Brush"/> / <see cref="WpfMedia.Color"/> để dùng cho giao diện WPF.
/// Dùng chung cho các Window/UserControl WPF khi migrate vỏ từ WinForms.
/// </summary>
public static class WpfTheme
{
    // Mỗi brush trả về theo theme đang chọn (Sáng/Tối) để đổi giao diện "live".
    // D = đang ở chế độ tối. Bn(...) chọn brush theo theme (đã cache + Freeze).
    private static bool D => ThemeState.IsDark;
    private static WpfMedia.Brush Bn(string light, string dark) => Cached(D ? dark : light);

    // ── Sidebar ───────────────────────────────────────────────────────────
    public static WpfMedia.Brush SidebarBg      => Bn("#0F172A", "#050608");
    public static WpfMedia.Brush SidebarHover    => Bn("#1E293B", "#101317");
    public static WpfMedia.Brush SidebarActive   => Bn("#2563EB", "#11C5BF");
    public static WpfMedia.Brush SidebarText     => Bn("#94A3B8", "#9AA3AF");
    public static WpfMedia.Brush SidebarSection  => Bn("#475569", "#5F6875");

    // ── Surfaces ──────────────────────────────────────────────────────────
    public static WpfMedia.Brush Background => Bn("#F1F5F9", "#050608");
    public static WpfMedia.Brush Surface    => Bn("#FFFFFF", "#0A0C0F");
    public static WpfMedia.Brush SurfaceAlt  => Bn("#F8FAFC", "#0E1116");
    public static WpfMedia.Brush Border      => Bn("#E2E8F0", "#1C222B");
    public static WpfMedia.Brush GridLine    => Bn("#EEF2F7", "#161B22");
    public static WpfMedia.Brush RowHover     => Bn("#F7FBFF", "#12161C");

    // ── Text ──────────────────────────────────────────────────────────────
    public static WpfMedia.Brush TextPrimary   => Bn("#0F172A", "#F5F7FA");
    public static WpfMedia.Brush TextSecondary => Bn("#64748B", "#A8B0BD");
    public static WpfMedia.Brush TextMuted     => Bn("#94A3B8", "#7D8794");

    // ── Accent / brand ────────────────────────────────────────────────────
    public static WpfMedia.Brush Accent      => Bn("#2563EB", "#11C5BF");
    public static WpfMedia.Brush AccentLight  => Bn("#DBEAFE", "#0E2221");
    public static WpfMedia.Brush AccentHover  => Bn("#1D4ED8", "#18D7D0");

    // ── Semantic ──────────────────────────────────────────────────────────
    public static WpfMedia.Brush Success      => Bn("#10B981", "#22C55E");
    public static WpfMedia.Brush SuccessLight => Bn("#D1FAE5", "#0E1A12");
    public static WpfMedia.Brush Warning      => Bn("#F59E0B", "#F59E0B");
    public static WpfMedia.Brush WarningLight => Bn("#FEF3C7", "#1C1406");
    public static WpfMedia.Brush Danger       => Bn("#EF4444", "#EF4444");
    public static WpfMedia.Brush DangerLight  => Bn("#FEE2E2", "#1A0C0C");
    public static WpfMedia.Brush Purple       => Bn("#8B5CF6", "#8B5CF6");
    public static WpfMedia.Brush PurpleLight  => Bn("#EDE9FE", "#1A1430");

    // ── Login gradient (navy top → navy bottom) ───────────────────────────
    public static readonly WpfMedia.Color NavyTop    = Color("#0F172A");
    public static readonly WpfMedia.Color NavyBottom = Color("#1E3A5F");

    // ── Input border states (khớp LoginInputWrapPanel) ────────────────────
    public static WpfMedia.Brush InputBorderNormal => Bn("#CBD5E1", "#2A323D");
    public static WpfMedia.Brush InputBorderFocus  => Bn("#2563EB", "#11C5BF");
    public static WpfMedia.Brush InputBorderError  => Bn("#EF4444", "#EF4444");

    // ── Fonts ─────────────────────────────────────────────────────────────
    public static readonly WpfMedia.FontFamily Font = new("Segoe UI");

    // Cache brush theo mã hex để không tạo lại mỗi lần đọc thuộc tính.
    private static readonly Dictionary<string, WpfMedia.Brush> _brushCache = new(StringComparer.OrdinalIgnoreCase);

    private static WpfMedia.Brush Cached(string hex)
    {
        if (!_brushCache.TryGetValue(hex, out var brush))
        {
            brush = Brush(hex);
            _brushCache[hex] = brush;
        }

        return brush;
    }

    public static WpfMedia.SolidColorBrush Brush(string hex)
    {
        var brush = new WpfMedia.SolidColorBrush(Color(hex));
        brush.Freeze();
        return brush;
    }

    public static WpfMedia.Color Color(string hex)
        => (WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(hex)!;

    /// <summary>Đổi cỡ phông từ point (như WinForms/GDI+) sang device-independent pixel của WPF.</summary>
    public static double Pt(double pointSize) => pointSize * 96.0 / 72.0;
}
