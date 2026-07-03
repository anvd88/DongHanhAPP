namespace KetoanMini.Api.Models;

// ----- Auth -----
public record LoginRequest(string Username, string Password, string? Sid = null);
// Đăng nhập bằng khuôn mặt: client gửi một loạt ảnh (data URL base64), server tự chọn khung tốt nhất.
public record FaceLoginRequest(List<string> Images, string? Sid = null);
public record LoginResponse(string Token, UserDto User);
public record HeartbeatRequest(string? Sid);
// Thiết bị/phiên đăng nhập của một tài khoản (phục vụ màn "Quản lý thiết bị đăng nhập").
public record DeviceDto(string Sid, string MachineName, string ClientKind, string UserAgent,
    DateTime? StartedAt, DateTime? LastSeen, bool IsActive, bool Revoked, bool Current);
public record UpdateProfileRequest(string FullName, string Email);
public record UpdateAvatarRequest(string ImageDataUrl);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserPreferencesDto(bool WaterReminderEnabled, bool EyeReminderEnabled, bool KeepCreateVoucherOpen,
    bool MessagePreviewEnabled);
public record UserPreferencePatchRequest(bool? WaterReminderEnabled, bool? EyeReminderEnabled, bool? KeepCreateVoucherOpen,
    bool? MessagePreviewEnabled);
public record UserDto(Guid Id, string Username, string FullName, string Email, string Role, bool IsActive,
    string ApprovalStatus, DateTime? CreatedAt)
{
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
    public bool IsPending => string.Equals(ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ảnh đại diện (data URL) lưu riêng cho bản web; null nếu chưa đặt → hiển thị chữ cái đầu.</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>Tích xanh (giống Facebook): Admin luôn có, hoặc được admin cấp thủ công.</summary>
    public bool Verified { get; init; }
    public bool IsDiamond { get; init; }
}

// ----- Dashboard -----
public record DashboardDto(int ActiveCustomers, int TotalDocuments, decimal TotalPayments,
    decimal MonthRevenue, int Month, int Year, List<RecentDocDto> Recent);
public record RecentDocDto(Guid Id, string VoucherNo, DateOnly Date, string CustomerName, string Content, decimal Total);

// ----- Documents -----
public record DocumentListItemDto(Guid Id, string VoucherNo, DateOnly Date, string DocumentType,
    string CustomerName, string Content, decimal Total, string CreatedBy);
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
public record SaveCustomerRequest(string Name, string TaxCode, string Phone, string Address);
public record CustomerReportDto(CustomerDto Customer, int DocumentCount, decimal Total, decimal ReceiptTotal,
    decimal PaymentTotal, decimal SalesTotal, List<DocumentListItemDto> Documents);

// ----- Users (Nhân sự) -----
public record UserAdminDto(Guid Id, string Username, string FullName, string Email, string Role, bool IsActive,
    string ApprovalStatus, DateTime? CreatedAt, bool IsOnline, DateTime? LastSeen, bool Verified, bool IsDiamond);
public record CreateUserRequest(string Username, string FullName, string Email, string Password, string Role);
public record SetLockRequest(bool Locked);
public record SetVerifiedRequest(bool Verified);
public record SetDiamondRequest(bool IsDiamond);
public record ResetPasswordResponse(string Code);

// ----- Chat (Trò chuyện, web-only) -----
public record ChatContactDto(string Username, string DisplayName, string? AvatarUrl, bool IsOnline, bool Verified, bool IsDiamond, string Role);
public record ChatConversationDto(Guid Id, bool IsGroup, string Title, string? Username, string? AvatarUrl,
    bool IsOnline, bool Verified, bool IsDiamond, string Preview, DateTime? LastAt, int Unread, DateTime? LastSeen,
    bool Pinned = false, bool SupportConversation = false);
public record ChatMessageDto(long Id, string SenderUsername, string SenderName, bool Mine, string Body, DateTime CreatedAt,
    DateTime? EditedAt, bool Removed, bool Forwarded, IReadOnlyList<ChatReactionDto>? Reactions = null,
    // Tin nhắn tệp gửi qua LAN: chỉ lưu METADATA (tên/dung lượng/kiểu), KHÔNG lưu nội dung tệp.
    // HasBlob = true khi server đang GIỮ TẠM nội dung tệp (người nhận offline lúc gửi) chờ tải về rồi xóa.
    string Kind = "text", string? FileName = null, long? FileSize = null, string? FileMime = null,
    bool HasBlob = false);
// Một biểu cảm (cảm xúc) gộp theo emoji trên một tin nhắn: số người thả + tôi có thả hay không.
public record ChatReactionDto(string Emoji, int Count, bool Mine);
public record SendMessageRequest(string Body, bool Forwarded = false, bool SendAsSupport = false);
// Ghi lại "đã gửi tệp X" qua LAN — chỉ metadata; nội dung tệp truyền thẳng P2P, không lưu server.
public record SendFileMessageRequest(string FileName, long FileSize, string? FileMime = null);
public record EditMessageRequest(string Body);
public record ReactRequest(string Emoji);
public record SetConversationPinnedRequest(bool Pinned);
public record ChatReportRequest(string? Reason);

// ----- Feedback (Phan hoi) -----
public record FeedbackDto(long Id, string Type, string TypeLabel, string ReporterUsername, string ReporterName,
    string TargetName, string Reason, Guid? ConversationId, DateTime CreatedAt);
public record AttendanceFeedbackRequest(string TargetName, string? Reason);

// Dung lượng DB của mục Trò chuyện (admin xem trong trang Hệ thống).
public record ChatTableUsageDto(string Table, string Label, long Rows, long DataKb, long IndexKb, long TotalKb);
public record ChatDbUsageDto(long TotalKb, long DataKb, long IndexKb, long MessageCount, long ConversationCount,
    long MemberCount, long DatabaseTotalKb, IReadOnlyList<ChatTableUsageDto> Tables);

// ----- Gia công -----
public record GiaCongLineDto(long Id, string LoaiDong, string MaHang, string TenHang, string QuyCach, string DonViTinh,
    decimal SoLuong, decimal DonGiaGiaCong, string GhiChu)
{
    public decimal ThanhTien => SoLuong * DonGiaGiaCong;
}
public record GiaCongListItemDto(long Id, string MaPhieu, string LoaiPhieu, string DoiTac, string NhanVienPhuTrach,
    DateOnly NgayLap, DateOnly? HanHoanThanh, int SoMatHang,
    decimal TongGiaTri, decimal SoLuongXuat, decimal SoLuongNhap, decimal SoLuongConTaiCongTy,
    decimal TienGiaCongPhaiTra);
public record GiaCongDetailDto(long Id, string MaPhieu, string LoaiPhieu, string DoiTac, string NhanVienPhuTrach,
    DateOnly NgayLap, DateOnly? HanHoanThanh, string GhiChu, List<GiaCongLineDto> Lines);
public record SaveGiaCongRequest(string LoaiPhieu, string DoiTac, string NhanVienPhuTrach, DateOnly NgayLap,
    DateOnly? HanHoanThanh, string GhiChu, List<GiaCongLineDto> Lines);
public record GiaCongReportDto(decimal SoLuongXuat, decimal SoLuongNhap, decimal SoLuongConTaiCongTy,
    decimal TienGiaCongPhaiTra, List<GiaCongReportPartnerDto> Partners, List<GiaCongReportItemDto> Items);
public record GiaCongReportPartnerDto(string DoiTac, decimal SoLuongXuat, decimal SoLuongNhap,
    decimal SoLuongConTaiCongTy, decimal TienGiaCongPhaiTra);
public record GiaCongReportItemDto(string DoiTac, string TenHang, string QuyCach, string DonViTinh,
    decimal SoLuongXuat, decimal SoLuongNhap, decimal SoLuongConTaiCongTy, decimal TienGiaCongPhaiTra);

// ----- Chấm công khuôn mặt -----
public record FaceEngineStatusDto(string Engine, double MatchThreshold);
public record DangKyKhuonMatRequest(string Username, string FullName, string ImageBase64);
public record FaceNguoiDungDto(string Username, string FullName, int SoMau, DateTime? CreatedAt);
public record FaceRegistrationLogDto(long Id, string Username, string FullName, DateTime CreatedAt, string CreatedBy);
public record NhanDienRequest(string ImageBase64);
public record FacePoseDto(bool Found, double Yaw, double Pitch);
public record NhanDienResult(bool Matched, string? Username, string? FullName, double Similarity,
    string? Loai, DateTime? OccurredAt, string Message);

/// <summary>
/// Loạt ảnh chụp liên tiếp; server tự chọn khung tốt nhất để phân tích.
/// <see cref="OccurredAt"/> (tùy chọn) là giờ chấm thật khi ĐỒNG BỘ NGOẠI TUYẾN — client mất mạng
/// lúc chấm nên xếp hàng ảnh vào IndexedDB, khi có mạng lại mới gửi lên; server ghi log theo giờ này
/// thay vì giờ nhận. Null (mặc định) = chấm trực tuyến bình thường, dùng giờ server.
/// <see cref="SelfOnly"/> = true: CHỈ chấm công cho chính tài khoản đang đăng nhập (trang HR Nhân sự).
/// Khuôn mặt khớp nhân viên KHÁC ⇒ chặn (status "proxy"), không cho chấm công hộ.
/// </summary>
public record ChamCongBurstRequest(List<string> Images, DateTime? OccurredAt = null, bool SelfOnly = false);

/// <summary>
/// Kết quả chấm công theo loạt ảnh. <see cref="Status"/>:
/// ok | posture (sai tư thế) | lowquality | noface | spoof | unknown.
/// <see cref="Guidance"/> là hướng dẫn sửa tư thế/điều kiện chụp (nếu có).
/// </summary>
public record ChamCongResult(string Status, bool Matched, string? Username, string? FullName,
    double Similarity, string? Loai, DateTime? OccurredAt, double Quality, string Message, string? Guidance);
public record ChamCongLogDto(long Id, string Username, string FullName, string Loai, double Similarity,
    DateTime OccurredAt, string GhiChu);

// ----- Releases (Cập nhật) -----
public record ReleaseDto(long Id, string AppTarget, string Version, int VersionCode, string ReleaseNotes,
    bool IsMandatory, bool IsPublished, DateTime PublishedAt, string PublishedBy,
    string ApkFileName, long ApkSize, string ApkSha256);
