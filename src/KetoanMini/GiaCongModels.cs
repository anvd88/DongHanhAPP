using System.Text.Json.Serialization;

namespace KetoanMini;

public sealed class GiaCongPhieu
{
    public long Id { get; set; }
    public string MaPhieu { get; set; } = "";
    public string LoaiPhieu { get; set; } = "Xuất gia công";
    public string DoiTac { get; set; } = "";
    public string NhanVienPhuTrach { get; set; } = "";
    public DateOnly NgayLap { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? HanHoanThanh { get; set; }
    public string TrangThai { get; set; } = GiaCongTrangThai.DangXuLy;
    public int TienDo { get; set; }
    public int BuocHienTai { get; set; } = 1;
    public string GhiChu { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Navigation — populated by GetPhieuById
    public List<GiaCongHangHoa> HangHoaList { get; set; } = [];

    [JsonIgnore]
    public int SoMatHang { get; set; }

    [JsonIgnore]
    public decimal TongGiaTri { get; set; }

    [JsonIgnore]
    public string TrangThaiColor => GiaCongTrangThai.GetStatusHexColor(TrangThai);
}

public sealed class GiaCongHangHoa
{
    public long Id { get; set; }
    public long PhieuId { get; set; }
    public string LoaiDong { get; set; } = GiaCongLoaiDong.NguyenLieu;
    public string MaHang { get; set; } = "";
    public string TenHang { get; set; } = "";
    public string DonViTinh { get; set; } = "";
    public decimal SoLuong { get; set; }
    public decimal DonGiaGiaCong { get; set; }
    public string GhiChu { get; set; } = "";
    public string TrangThaiDong { get; set; } = GiaCongTrangThaiDong.Cho;

    [JsonIgnore]
    public decimal ThanhTien => SoLuong * DonGiaGiaCong;
}

public static class GiaCongTrangThai
{
    public const string DangXuLy = "Đang xử lý";
    public const string HoanThanh = "Hoàn thành";
    public const string ChoDauTac = "Chờ đối tác";
    public const string Huy = "Hủy";

    public static readonly IReadOnlyList<string> AllValues =
    [
        DangXuLy,
        HoanThanh,
        ChoDauTac,
        Huy,
    ];

    public static Color GetStatusColor(string trangThai) => trangThai switch
    {
        DangXuLy => Color.FromArgb(59, 130, 246),   // blue
        HoanThanh => Color.FromArgb(34, 197, 94),   // green
        ChoDauTac => Color.FromArgb(234, 179, 8),   // amber
        Huy        => Color.FromArgb(239, 68, 68),  // red
        _          => Color.Gray,
    };

    // Returns a CSS-style hex color string, e.g. "#3B82F6"
    public static string GetStatusHexColor(string trangThai)
    {
        var c = GetStatusColor(trangThai);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}

public static class GiaCongLoaiDong
{
    public const string NguyenLieu = "Nguyên liệu";
    public const string ThanhPham  = "Thành phẩm";
    public const string HaoHut     = "Hao hụt";

    public static readonly IReadOnlyList<string> AllValues =
    [
        NguyenLieu,
        ThanhPham,
        HaoHut,
    ];
}

public static class GiaCongTrangThaiDong
{
    public const string DaGiao   = "Đã giao";
    public const string DangMay  = "Đang may";
    public const string GhiNhan  = "Ghi nhận";
    public const string Cho      = "Chờ";

    public static readonly IReadOnlyList<string> AllValues =
    [
        DaGiao,
        DangMay,
        GhiNhan,
        Cho,
    ];
}
