using Wpf = System.Windows;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

/// <summary>
/// Shared WPF color and font palette for windows, pages, and reusable controls.
/// The actual color values live in Themes/LightTheme.xaml and Themes/DarkTheme.xaml.
/// </summary>
public static class WpfTheme
{
    private const string LightThemePath = "Themes/LightTheme.xaml";
    private const string DarkThemePath = "Themes/DarkTheme.xaml";

    public static void ApplyCurrentTheme()
    {
        var app = Wpf.Application.Current;
        if (app is null) return;

        var dictionaries = app.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (IsThemeDictionary(source))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new Wpf.ResourceDictionary
        {
            Source = new Uri($"/KetoanMini;component/{(ThemeState.IsDark ? DarkThemePath : LightThemePath)}", UriKind.Relative)
        });
    }

    private static bool IsThemeDictionary(string? source)
    {
        return source is not null &&
            (source.EndsWith(LightThemePath, StringComparison.OrdinalIgnoreCase) ||
             source.EndsWith(DarkThemePath, StringComparison.OrdinalIgnoreCase));
    }

    public static WpfMedia.Brush SidebarBg      => R("Theme.SidebarBg", ThemeState.IsDark ? "#050608" : "#0F172A");
    public static WpfMedia.Brush SidebarHover    => R("Theme.SidebarHover", ThemeState.IsDark ? "#101317" : "#1E293B");
    public static WpfMedia.Brush SidebarActive   => R("Theme.SidebarActive", ThemeState.IsDark ? "#11C5BF" : "#2563EB");
    public static WpfMedia.Brush SidebarText     => R("Theme.SidebarText", ThemeState.IsDark ? "#9AA3AF" : "#94A3B8");
    public static WpfMedia.Brush SidebarSection  => R("Theme.SidebarSection", ThemeState.IsDark ? "#5F6875" : "#475569");

    public static WpfMedia.Brush Background => R("Theme.Background", ThemeState.IsDark ? "#050608" : "#F1F5F9");
    public static WpfMedia.Brush Surface    => R("Theme.Surface", ThemeState.IsDark ? "#0A0C0F" : "#FFFFFF");
    public static WpfMedia.Brush SurfaceAlt  => R("Theme.SurfaceAlt", ThemeState.IsDark ? "#0E1116" : "#F8FAFC");
    public static WpfMedia.Brush Border      => R("Theme.Border", ThemeState.IsDark ? "#1C222B" : "#E2E8F0");
    public static WpfMedia.Brush GridLine    => R("Theme.GridLine", ThemeState.IsDark ? "#161B22" : "#EEF2F7");
    public static WpfMedia.Brush RowHover     => R("Theme.RowHover", ThemeState.IsDark ? "#12161C" : "#F7FBFF");
    public static WpfMedia.Brush WorkCardBg   => R("Theme.WorkCardBg", ThemeState.IsDark ? "#0B0E12" : "#0F172A");

    public static WpfMedia.Brush TextPrimary   => R("Theme.TextPrimary", ThemeState.IsDark ? "#F5F7FA" : "#0F172A");
    public static WpfMedia.Brush TextSecondary => R("Theme.TextSecondary", ThemeState.IsDark ? "#A8B0BD" : "#64748B");
    public static WpfMedia.Brush TextMuted     => R("Theme.TextMuted", ThemeState.IsDark ? "#7D8794" : "#94A3B8");

    public static WpfMedia.Brush Accent      => R("Theme.Accent", ThemeState.IsDark ? "#11C5BF" : "#2563EB");
    public static WpfMedia.Brush AccentLight  => R("Theme.AccentLight", ThemeState.IsDark ? "#0E2221" : "#DBEAFE");
    public static WpfMedia.Brush AccentHover  => R("Theme.AccentHover", ThemeState.IsDark ? "#18D7D0" : "#1D4ED8");

    public static WpfMedia.Brush Success      => R("Theme.Success", ThemeState.IsDark ? "#22C55E" : "#10B981");
    public static WpfMedia.Brush SuccessLight => R("Theme.SuccessLight", ThemeState.IsDark ? "#0E1A12" : "#D1FAE5");
    public static WpfMedia.Brush Warning      => R("Theme.Warning", "#F59E0B");
    public static WpfMedia.Brush WarningLight => R("Theme.WarningLight", ThemeState.IsDark ? "#1C1406" : "#FEF3C7");
    public static WpfMedia.Brush Danger       => R("Theme.Danger", "#EF4444");
    public static WpfMedia.Brush DangerLight  => R("Theme.DangerLight", ThemeState.IsDark ? "#1A0C0C" : "#FEE2E2");
    public static WpfMedia.Brush Purple       => R("Theme.Purple", "#8B5CF6");
    public static WpfMedia.Brush PurpleLight  => R("Theme.PurpleLight", ThemeState.IsDark ? "#1A1430" : "#EDE9FE");

    public static readonly WpfMedia.Color NavyTop = Color("#0F172A");
    public static readonly WpfMedia.Color NavyBottom = Color("#1E3A5F");

    public static WpfMedia.Brush InputBorderNormal => R("Theme.InputBorderNormal", ThemeState.IsDark ? "#2A323D" : "#CBD5E1");
    public static WpfMedia.Brush InputBorderFocus  => R("Theme.InputBorderFocus", ThemeState.IsDark ? "#11C5BF" : "#2563EB");
    public static WpfMedia.Brush InputBorderError  => R("Theme.InputBorderError", "#EF4444");

    public static readonly WpfMedia.FontFamily Font = new("Segoe UI");

    private static readonly Dictionary<string, WpfMedia.Brush> FallbackBrushCache = new(StringComparer.OrdinalIgnoreCase);

    private static WpfMedia.Brush R(string key, string fallbackHex)
    {
        if (Wpf.Application.Current?.TryFindResource(key) is WpfMedia.Brush brush)
        {
            return brush;
        }

        return Cached(fallbackHex);
    }

    private static WpfMedia.Brush Cached(string hex)
    {
        if (!FallbackBrushCache.TryGetValue(hex, out var brush))
        {
            brush = Brush(hex);
            FallbackBrushCache[hex] = brush;
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

    public static double Pt(double pointSize) => pointSize * 96.0 / 72.0;
}
