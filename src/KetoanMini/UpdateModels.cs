namespace KetoanMini;

/// <summary>Một bản phát hành ứng dụng (lịch sử cập nhật lưu trong DB).</summary>
public sealed class AppRelease
{
    public long Id { get; set; }
    public string Version { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";

    /// <summary>Đường dẫn LAN (UNC / thư mục chia sẻ) tới file setup. Có thể trống nếu dùng file nhúng DB.</summary>
    public string SetupPath { get; set; } = "";

    public string SetupFileName { get; set; } = "";
    public long FileSize { get; set; }

    /// <summary>True nếu bản này có file setup được nhúng trực tiếp trong DB (fallback khi không có UNC).</summary>
    public bool HasEmbeddedFile { get; set; }

    /// <summary>True nếu bản này là bắt buộc (kết hợp với công tắc chặn của admin sẽ chặn đăng nhập bản cũ).</summary>
    public bool IsMandatory { get; set; }

    public bool IsPublished { get; set; } = true;
    public DateTime PublishedAt { get; set; } = DateTime.Now;
    public string PublishedBy { get; set; } = "";

    /// <summary>True nếu có nguồn tải file setup (UNC hoặc file nhúng DB).</summary>
    public bool HasSetupSource => HasEmbeddedFile || !string.IsNullOrWhiteSpace(SetupPath);
}

/// <summary>Kết quả kiểm tra phiên bản khi mở app.</summary>
public sealed class VersionCheckResult
{
    /// <summary>Phiên bản đang chạy.</summary>
    public string CurrentVersion { get; set; } = AppVersion.CurrentText;

    /// <summary>Bản phát hành mới nhất tìm thấy trong DB (null nếu chưa có bản nào).</summary>
    public AppRelease? Latest { get; set; }

    /// <summary>True nếu có bản mới hơn bản đang chạy.</summary>
    public bool UpdateAvailable { get; set; }

    /// <summary>
    /// True nếu phải chặn đăng nhập: admin đã bật công tắc chặn VÀ bản đang chạy
    /// cũ hơn bản bắt buộc mới nhất.
    /// </summary>
    public bool MustBlock { get; set; }
}
