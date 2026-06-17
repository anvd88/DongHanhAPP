using WpfMedia = System.Windows.Media;

namespace KetoanMini;

/// <summary>
/// Phiên bản WPF của <see cref="AppTheme"/> — cùng bảng màu/phông, nhưng trả về
/// <see cref="WpfMedia.Brush"/> / <see cref="WpfMedia.Color"/> để dùng cho giao diện WPF.
/// Dùng chung cho các Window/UserControl WPF khi migrate vỏ từ WinForms.
/// </summary>
public static class WpfTheme
{
    // ── Sidebar ───────────────────────────────────────────────────────────
    public static readonly WpfMedia.Brush SidebarBg        = Brush("#0F172A");
    public static readonly WpfMedia.Brush SidebarHover     = Brush("#1E293B");
    public static readonly WpfMedia.Brush SidebarActive    = Brush("#2563EB");
    public static readonly WpfMedia.Brush SidebarText      = Brush("#94A3B8");
    public static readonly WpfMedia.Brush SidebarSection   = Brush("#475569");

    // ── Surfaces ──────────────────────────────────────────────────────────
    public static readonly WpfMedia.Brush Background = Brush("#F1F5F9");
    public static readonly WpfMedia.Brush Surface    = Brush("#FFFFFF");
    public static readonly WpfMedia.Brush SurfaceAlt  = Brush("#F8FAFC");
    public static readonly WpfMedia.Brush Border      = Brush("#E2E8F0");

    // ── Text ──────────────────────────────────────────────────────────────
    public static readonly WpfMedia.Brush TextPrimary   = Brush("#0F172A");
    public static readonly WpfMedia.Brush TextSecondary = Brush("#64748B");
    public static readonly WpfMedia.Brush TextMuted     = Brush("#94A3B8");

    // ── Accent / brand ────────────────────────────────────────────────────
    public static readonly WpfMedia.Brush Accent      = Brush("#2563EB");
    public static readonly WpfMedia.Brush AccentLight  = Brush("#DBEAFE");
    public static readonly WpfMedia.Brush AccentHover  = Brush("#1D4ED8");

    // ── Semantic ──────────────────────────────────────────────────────────
    public static readonly WpfMedia.Brush Success      = Brush("#10B981");
    public static readonly WpfMedia.Brush SuccessLight = Brush("#D1FAE5");
    public static readonly WpfMedia.Brush Warning      = Brush("#F59E0B");
    public static readonly WpfMedia.Brush WarningLight = Brush("#FEF3C7");
    public static readonly WpfMedia.Brush Danger       = Brush("#EF4444");
    public static readonly WpfMedia.Brush DangerLight  = Brush("#FEE2E2");
    public static readonly WpfMedia.Brush Purple       = Brush("#8B5CF6");
    public static readonly WpfMedia.Brush PurpleLight  = Brush("#EDE9FE");

    // ── Login gradient (navy top → navy bottom) ───────────────────────────
    public static readonly WpfMedia.Color NavyTop    = Color("#0F172A");
    public static readonly WpfMedia.Color NavyBottom = Color("#1E3A5F");

    // ── Input border states (khớp LoginInputWrapPanel) ────────────────────
    public static readonly WpfMedia.Brush InputBorderNormal = Brush("#CBD5E1");
    public static readonly WpfMedia.Brush InputBorderFocus  = Brush("#2563EB");
    public static readonly WpfMedia.Brush InputBorderError  = Brush("#EF4444");

    // ── Fonts ─────────────────────────────────────────────────────────────
    public static readonly WpfMedia.FontFamily Font = new("Segoe UI");

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
