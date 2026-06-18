namespace KetoanMini;

/// <summary>Chế độ giao diện: Sáng (light) hoặc Tối (dark/OLED).</summary>
public enum UiTheme
{
    Light,
    Dark
}

/// <summary>
/// Shared light/dark theme state for the WPF shell. The selected mode is saved
/// in LocalAppData and reapplied on startup; MainWindow refreshes live colors
/// after <see cref="Current"/> changes.
/// </summary>
public static class ThemeState
{
    public static UiTheme Current { get; set; } = UiTheme.Light;

    public static bool IsDark => Current == UiTheme.Dark;

    public static void Toggle() => Current = IsDark ? UiTheme.Light : UiTheme.Dark;

    private static string FilePath
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Path.GetTempPath();
            }

            return Path.Combine(baseDir, "KetoanMini", "ui.theme");
        }
    }

    /// <summary>Đọc lựa chọn theme đã lưu (gọi lúc khởi động app).</summary>
    public static void Load()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path) &&
                string.Equals(File.ReadAllText(path).Trim(), "dark", StringComparison.OrdinalIgnoreCase))
            {
                Current = UiTheme.Dark;
            }
        }
        catch
        {
            // Không để lỗi đọc cấu hình chặn khởi động.
        }
    }

    /// <summary>Lưu lựa chọn theme hiện tại.</summary>
    public static void Save()
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, IsDark ? "dark" : "light");
        }
        catch
        {
            // Không để lỗi ghi cấu hình ảnh hưởng thao tác đổi theme.
        }
    }
}
