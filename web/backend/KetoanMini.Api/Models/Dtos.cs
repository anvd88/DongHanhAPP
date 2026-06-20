namespace KetoanMini.Api.Models;

// ----- Auth -----
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, UserDto User);
public record HeartbeatRequest(string? Sid);
public record UpdateProfileRequest(string FullName);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserDto(Guid Id, string Username, string FullName, string Role, bool IsActive,
    string ApprovalStatus, DateTime? CreatedAt)
{
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
    public bool IsPending => string.Equals(ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase);
}

// ----- Dashboard -----
public record DashboardDto(int ActiveCustomers, int TotalDocuments, decimal TotalPayments,
    decimal MonthRevenue, int Month, int Year, List<RecentDocDto> Recent);
public record RecentDocDto(Guid Id, string VoucherNo, DateOnly Date, string CustomerName, string Content, decimal Total);

// ----- Documents -----
public record DocumentListItemDto(Guid Id, string VoucherNo, DateOnly Date, string CustomerName, string Content, decimal Total);
public record DocumentLineDto(string LineContent, string Spec, decimal Quantity, decimal UnitPrice, string Note)
{
    public decimal Amount => Quantity * UnitPrice;
}
public record DocumentDetailDto(Guid Id, string VoucherNo, DateOnly Date, string CustomerName, string Content, string Note, List<DocumentLineDto> Lines);
public record SaveDocumentRequest(string VoucherNo, DateOnly Date, string CustomerName, string Content, string Note, List<DocumentLineDto> Lines);

// ----- Reports -----
public record ReportsDto(decimal TotalPayments, decimal MonthRevenue, int TotalDocuments, int ActiveCustomers, List<MonthlyRowDto> Monthly);
public record MonthlyRowDto(int Year, int Month, int DocumentCount, int PaymentCount, decimal Total);

// ----- Audit -----
public record AuditDto(DateTime OccurredAt, string Username, string Action, string Entity, string EntityName, string Details);

// ----- Customers -----
public record CustomerDto(Guid Id, string Name, string TaxCode, string Phone, string Address, bool IsActive);

// ----- Users (Nhân sự) -----
public record UserAdminDto(Guid Id, string Username, string FullName, string Role, bool IsActive,
    string ApprovalStatus, DateTime? CreatedAt, bool IsOnline, DateTime? LastSeen);
public record CreateUserRequest(string Username, string FullName, string Password, string Role);
public record SetLockRequest(bool Locked);
public record ResetPasswordResponse(string Code);

// ----- Gia công -----
public record GiaCongLineDto(long Id, string LoaiDong, string MaHang, string TenHang, string QuyCach, string DonViTinh,
    decimal SoLuong, decimal DonGiaGiaCong, string TrangThaiDong, string GhiChu)
{
    public decimal ThanhTien => SoLuong * DonGiaGiaCong;
}
public record GiaCongListItemDto(long Id, string MaPhieu, string LoaiPhieu, string DoiTac, string NhanVienPhuTrach,
    DateOnly NgayLap, DateOnly? HanHoanThanh, string TrangThai, int TienDo, int BuocHienTai, int SoMatHang, decimal TongGiaTri);
public record GiaCongDetailDto(long Id, string MaPhieu, string LoaiPhieu, string DoiTac, string NhanVienPhuTrach,
    DateOnly NgayLap, DateOnly? HanHoanThanh, string TrangThai, int TienDo, int BuocHienTai, string GhiChu, List<GiaCongLineDto> Lines);
public record SaveGiaCongRequest(string LoaiPhieu, string DoiTac, string NhanVienPhuTrach, DateOnly NgayLap,
    DateOnly? HanHoanThanh, string TrangThai, int TienDo, int BuocHienTai, string GhiChu, List<GiaCongLineDto> Lines);

// ----- Chấm công khuôn mặt -----
public record FaceEngineStatusDto(string Engine, bool IsReal, double MatchThreshold);
public record DangKyKhuonMatRequest(string Username, string FullName, string ImageBase64);
public record FaceNguoiDungDto(string Username, string FullName, int SoMau, DateTime? CreatedAt);
public record FaceRegistrationLogDto(long Id, string Username, string FullName, DateTime CreatedAt, string CreatedBy);
public record NhanDienRequest(string ImageBase64);
public record NhanDienResult(bool Matched, string? Username, string? FullName, double Similarity,
    string? Loai, DateTime? OccurredAt, string Message);
public record ChamCongLogDto(long Id, string Username, string FullName, string Loai, double Similarity,
    DateTime OccurredAt, string GhiChu);

// ----- Releases (Cập nhật) -----
public record ReleaseDto(long Id, string Version, string ReleaseNotes, bool IsMandatory, bool IsPublished, DateTime PublishedAt, string PublishedBy);
