namespace KetoanMini.Api.Models;

// ----- Auth -----
// Client: "apk"/"android"/"native" = đăng nhập từ app native (KHÔNG bị chặn bởi cờ tắt đăng nhập web).
// Bỏ trống/null = trình duyệt web (chịu ràng buộc cờ "bật/tắt đăng nhập trên web" của tài khoản).
public record LoginRequest(string Username, string Password, string? Sid = null, string? Client = null);
// Quên mật khẩu bằng khuôn mặt: username + mật khẩu mới + loạt ảnh quét.
// Backend so 1:1 với mẫu khuôn mặt đã đăng ký của đúng username này.
public record FacePasswordResetRequest(string Username, string NewPassword, List<string> Images, string? Client = null);
// Khôi phục mật khẩu bằng mã do admin cấp (thay cho reset khuôn mặt): username + mã + mật khẩu mới.
public record RecoveryResetRequest(string Username, string Code, string NewPassword);
// Trả về mã khôi phục vừa tạo cho admin xem một lần (không lưu bản rõ ở server).
public record RecoveryCodeResponse(string Code);
public record LoginResponse(string Token, UserDto User);
public record QrLoginStartRequest(string? Sid, string ClientMode = "desktop_qr");
public record QrLoginStartResponse(
    string QrCode,
    string PollToken,
    DateTime ExpiresAt,
    string ClientMode = "desktop_qr");
public record QrLoginPollRequest(string PollToken);
public record QrLoginConfirmRequest(string QrCode);
public record QrLoginCancelRequest(string PollToken);
public record MobileAppLoginStartRequest(string? Sid, string ClientMode = "mobile_app");
public record MobileAppLoginStartResponse(
    string RequestCode,
    string PollToken,
    DateTime ExpiresAt,
    string ClientMode = "mobile_app");
public record MobileAppLoginCodeRequest(string RequestCode, string ClientMode = "mobile_app");
public record MobileAppLoginPollRequest(string PollToken, string ClientMode = "mobile_app");
public record MobileAppLoginChallengeResponse(
    string RequestCode,
    string Title,
    string Message,
    DateTime ExpiresAt,
    string ClientMode = "mobile_app");
public record QrResolveRequest(string Value, int ProtocolVersion = 1, List<string>? Capabilities = null, int? ClientVersionCode = null);
public record QrDecisionRequest(string DecisionToken, string ActionId);
public record QrPresentationDto(string Title, string Message, string Severity = "info");
public record QrClientActionDto(
    string Id,
    string Type,
    string Label,
    string Style = "secondary",
    string? Url = null,
    bool CloseOnSelect = false);
public record QrActionEnvelope(
    int ProtocolVersion,
    QrPresentationDto Presentation,
    List<QrClientActionDto> Actions,
    string? DecisionToken = null,
    string? DismissActionId = null,
    DateTime? ExpiresAt = null,
    // Máy chủ tiếp nhận mã nhưng không có nghiệp vụ nào cho nó. Ứng dụng mới sẽ tự đọc và hiện nội
    // dung mã ngay trên máy; bản cũ không biết cờ này nên vẫn hiện Presentation như trước.
    bool Unhandled = false);
public record HeartbeatRequest(string? Sid);
// Thiết bị/phiên đăng nhập của một tài khoản (phục vụ màn "Quản lý thiết bị đăng nhập").
public record DeviceDto(string Sid, string MachineName, string ClientKind, string UserAgent,
    DateTime? StartedAt, DateTime? LastSeen, bool IsActive, bool Revoked, bool Current);
public record UpdateProfileRequest(string FullName, string Email);
public record UpdateAvatarRequest(string ImageDataUrl);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record VerifyPasswordRequest(string Password);
// Cài đặt đăng nhập của tài khoản: cho phép đăng nhập bản web hay không (app native luôn dùng được).
public record AccountLoginSettingsDto(bool WebLoginEnabled);
public record AccountLoginSettingsPatch(bool WebLoginEnabled);
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

    /// <summary>Đã đăng ký khuôn mặt (có mẫu trong cham_cong_face) — app dùng để hiện banner nhắc đăng ký.</summary>
    public bool FaceRegistered { get; init; }

    /// <summary>MỌI vai trò của tài khoản (vai trò chính + vai trò phụ như "Thủ kho"). Client dùng để
    /// hiện/ẩn tính năng giao việc &amp; nghiệm thu. Rỗng ⇒ chỉ có vai trò chính trong <see cref="Role"/>.</summary>
    public IReadOnlyList<string> Roles { get; init; } = System.Array.Empty<string>();

    /// <summary>Có thẩm quyền giao việc &amp; nghiệm thu (Admin hoặc giữ vai trò Thủ kho).</summary>
    public bool CanAssignTasks => IsAdmin
        || Roles.Any(r => string.Equals(r, "Warehouse", StringComparison.OrdinalIgnoreCase));
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
    string ApprovalStatus, DateTime? CreatedAt, bool IsOnline, DateTime? LastSeen, bool Verified, bool IsDiamond,
    IReadOnlyList<string> SecondaryRoles);
public record CreateUserRequest(string Username, string FullName, string Email, string Password, string Role);
public record SetLockRequest(bool Locked);
public record SetRoleRequest(string Role);
// Cấp/thu một vai trò PHỤ (vd "Warehouse" = Thủ kho) cho tài khoản. Grant=true để cấp, false để thu hồi.
public record SetSecondaryRoleRequest(string Role, bool Grant);
public record SetVerifiedRequest(bool Verified);
public record SetDiamondRequest(bool IsDiamond);
public record ResetPasswordResponse(string Code);

// ----- Chat (Trò chuyện, web-only) -----
public record ChatContactDto(string Username, string DisplayName, string? AvatarUrl, bool IsOnline, bool Verified, bool IsDiamond, string Role,
    string EmployeeId = "", string EmployeeCode = "", string DepartmentId = "", string DepartmentName = "", string Position = "",
    string Phone = "", string Email = "", string ManagerUsername = "", string ManagerName = "", bool IsDirectManager = false,
    bool SameDepartment = false);
// Đổ chuông / hủy chuông / báo nhỡ cuộc gọi (thoại/video) qua FCM.
public record CallRingRequest(string ToUsername, string CallId, string? Media);
public record CallCancelRequest(string ToUsername, string CallId);
public record CallMissedRequest(string ToUsername, string CallId, string? Media);
public record MissedCallDto(long Id, string FromUsername, string FromName, string Media, string CallId, DateTime CreatedAt);
public record CallHistoryDto(long Id, string PeerUsername, string PeerName, string CallId, string Media, string Direction,
    string Outcome, DateTime? StartedAt, DateTime EndedAt, int DurationSeconds);
public record RecordCallRequest(string PeerUsername, string? PeerName, string CallId, string? Media, string? Direction,
    string? Outcome, long? StartedAtEpochMs, long EndedAtEpochMs);
// TURN credential động cho WebRTC (cấp có hạn giờ qua HMAC — xem /api/chat/call/turn).
public record TurnCredsDto(string[] Urls, string Username, string Credential, int Ttl);
public record ChatConversationDto(Guid Id, bool IsGroup, string Title, string? Username, string? AvatarUrl,
    bool IsOnline, bool Verified, bool IsDiamond, string Preview, DateTime? LastAt, int Unread, DateTime? LastSeen,
    bool Pinned = false, bool SupportConversation = false);
public record ChatMessageDto(long Id, string SenderUsername, string SenderName, bool Mine, string Body, DateTime CreatedAt,
    DateTime? EditedAt, bool Removed, bool Forwarded, IReadOnlyList<ChatReactionDto>? Reactions = null,
    // kind=file: metadata tệp LAN, blob chỉ được giữ tạm. kind=voice: tin thoại có blob bền vững tới khi gỡ.
    // HasBlob cho biết nội dung hiện sẵn sàng tải/phát; tải hoặc đánh dấu đã đọc không được làm mất voice.
    string Kind = "text", string? FileName = null, long? FileSize = null, string? FileMime = null,
    bool HasBlob = false, bool Read = false);
// Một biểu cảm (cảm xúc) gộp theo emoji trên một tin nhắn: số người thả + tôi có thả hay không.
public record ChatReactionDto(string Emoji, int Count, bool Mine);
public record SendMessageRequest(string Body, bool Forwarded = false, bool SendAsSupport = false);
// kind=file cho tệp đính kèm; recorder phải gửi kind=voice. Backend xác thực MIME/đuôi trước khi nhận voice.
public record SendFileMessageRequest(
    string FileName,
    long FileSize,
    string? FileMime = null,
    string? Kind = null,
    string? ClientMessageId = null);
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

// Tự đăng ký khuôn mặt (app): mỗi tài khoản chỉ đăng ký MỘT lần, gồm nhiều tư thế (góc) để mẫu bền.
// Mỗi góc là một loạt ảnh; server chọn khung tốt nhất, kiểm tra chất lượng + liveness rồi lưu 1 mẫu/góc.
public record FaceEnrollPose(string Pose, List<string> Images);
public record SelfFaceEnrollRequest(List<FaceEnrollPose> Poses);
public record SelfFaceStatusDto(bool Registered, int SampleCount, DateTime? CreatedAt);
public record SelfFaceEnrollResult(string Message, int SampleCount);
public record NhanDienResult(bool Matched, string? Username, string? FullName, double Similarity,
    string? Loai, DateTime? OccurredAt, string Message);

/// <summary>
/// Loạt ảnh chụp liên tiếp; server tự chọn khung tốt nhất để phân tích.
/// <see cref="OccurredAt"/> (tùy chọn) là giờ chấm thật khi ĐỒNG BỘ NGOẠI TUYẾN — client mất mạng
/// lúc chấm nên xếp hàng ảnh vào IndexedDB, khi có mạng lại mới gửi lên; server ghi log theo giờ này
/// thay vì giờ nhận. Null (mặc định) = chấm trực tuyến bình thường, dùng giờ server.
/// <see cref="SelfOnly"/> = true: CHỈ chấm công cho chính tài khoản đang đăng nhập (trang HR Nhân sự).
/// Khuôn mặt khớp nhân viên KHÁC ⇒ chặn (status "proxy"), không cho chấm công hộ.
/// <see cref="PreviewOnly"/> = true: CHỈ nhận diện (ai + Vào/Ra dự kiến), KHÔNG ghi nhật ký. Dùng cho
/// luồng sinh trắc học trên app: quét → xem trước "Nhân viên / Giờ vào" → người dùng bấm Xác nhận thì
/// mới gửi lại cùng loạt ảnh với PreviewOnly=false để ghi công thật.
/// </summary>
// MotionCheck=true: loạt ảnh này được chụp KHI người dùng QUAY ĐẦU (chống ảnh tĩnh) ⇒ server kiểm tra
// biên độ góc quay (yaw span). Ngoại tuyến/kiosk giữ hình tĩnh ⇒ false ⇒ bỏ qua kiểm tra chuyển động.
public record ChamCongBurstRequest(List<string> Images, DateTime? OccurredAt = null, bool SelfOnly = false,
    bool PreviewOnly = false, double? GpsLat = null, double? GpsLng = null,
    string? ChallengeId = null, List<int>? SlotIndices = null, bool MotionCheck = false);

/// <summary>
/// Active-flash liveness: server phát chuỗi màu ngẫu nhiên; client hiển thị đúng thứ tự từng
/// <see cref="SlotMs"/>ms (chờ <see cref="SettleMs"/>ms cho màn hình + camera ổn định) rồi gắn nhãn slot
/// cho từng khung gửi lên. <see cref="FlashSlot.Color"/> là mã hex CSS phủ đầy màn hình.
/// </summary>
public record FlashChallengeResponse(string ChallengeId, List<FlashSlot> Slots, int SlotMs, int SettleMs);
public record FlashSlot(int Index, string Color);

// Cấu hình liveness QUAY ĐẦU (challenge-response): Enabled = app yêu cầu quay đầu lúc quét;
// Enforce = chặn nếu biên độ quay quá nhỏ (nghi ảnh tĩnh) hay chỉ ghi log để hiệu chỉnh.
public record MotionConfigDto(bool Enabled, bool Enforce);

// Một lượt đo Silent-Face (chống ảnh/màn hình): điểm P(real) cao nhất/trung bình/nhì + biên độ quay đầu.
public record LivenessMetricDto(DateTime AtUtc, string User, double Best, double Mean, double Second,
    int Frames, double Threshold, bool Passed, double MotionSpan);

/// <summary>
/// Kết quả chấm công theo loạt ảnh. <see cref="Status"/>:
/// ok | posture (sai tư thế) | lowquality | noface | spoof | unknown.
/// <see cref="Guidance"/> là hướng dẫn sửa tư thế/điều kiện chụp (nếu có).
/// </summary>
public record ChamCongResult(string Status, bool Matched, string? Username, string? FullName,
    double Similarity, string? Loai, DateTime? OccurredAt, double Quality, string Message, string? Guidance);
public record ChamCongLogDto(long Id, string Username, string FullName, string Loai, double Similarity,
    DateTime OccurredAt, string GhiChu);

/// <summary>Bản chấm công ngoại tuyến chờ duyệt (kèm cờ rủi ro) hiển thị ở màn quản lý web.</summary>
public record ChamCongOfflineDto(long Id, string Username, string FullName, string Loai, double Similarity,
    double Quality, DateTime OccurredAt, DateTime SyncedAt, int BackdateMinutes, string ClientIp,
    bool OnCompanyLan, double? GpsLat, double? GpsLng, double? DistanceM, bool? InGeofence, string Flags,
    string Status, string ReviewedBy, DateTime? ReviewedAt, string ReviewNote);

public record OfflineReviewRequest(string? Note = null);

/// <summary>Cấu hình chính sách chấm công ngoại tuyến: geofence công ty + ngưỡng lùi giờ.</summary>
public record OfflineConfigDto(double? GeofenceLat, double? GeofenceLng, double GeofenceRadiusM, int MaxBackdateMinutes);
public record QrAttendanceRequest(string Token);
public record CreateQrSiteRequest(string Name, string? ProjectName = null);
public record ApprovalDelegationReq(string ToUsername, DateOnly FromDate, DateOnly ToDate);

// ----- Releases (Cập nhật) -----
public record ReleaseDto(long Id, string AppTarget, string Version, int VersionCode, string ReleaseNotes,
    bool IsMandatory, bool IsPublished, DateTime PublishedAt, string PublishedBy,
    string ApkFileName, long ApkSize, string ApkSha256);
