namespace KetoanMini;

/// <summary>Chế độ giao diện: Sáng (light) hoặc Tối (dark/OLED).</summary>
public enum UiTheme
{
    Light,
    Dark
}

/// <summary>
/// Trạng thái theme dùng chung cho cả <see cref="AppTheme"/> (WinForms) và
/// <see cref="WpfTheme"/> (WPF). Lưu lựa chọn vào một file nhỏ trong LocalAppData
/// để nhớ qua các lần mở app. Việc đổi màu "live" do MainForm dựng lại giao diện
/// sau khi <see cref="Current"/> đổi.
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
