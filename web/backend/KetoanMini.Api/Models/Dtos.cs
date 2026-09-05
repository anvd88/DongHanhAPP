namespace KetoanMini.Api.Models;

// ----- Auth -----
// Client: "apk"/"android"/"native" = đăng nhập từ app native (KHÔNG bị chặn bởi cờ tắt đăng nhập web).
// Bỏ trống/null = trình duyệt web (chịu ràng buộc cờ "bật/tắt đăng nhập trên web" của tài khoản).
public record LoginRequest(string Username, string Password, string? Sid = null, string? Client = null);
public record LoginBootstrapRequest(string? Sid);
public record LoginBootstrapResponse(bool Ready, DateTime ExpiresAt, string Protocol, bool SecureTransport);
// Quên mật khẩu bằng khuôn mặt: username + mật khẩu mới + loạt ảnh quét.
// Backend so 1:1 với mẫu khuôn mặt đã đăng ký của đúng username này.
public record FacePasswordResetRequest(string Username, string NewPassword, List<string> Images, string? Client = null);
// Khôi phục mật khẩu bằng mã do admin cấp (thay cho reset khuôn mặt): username + mã + mật khẩu mới.
public record RecoveryResetRequest(string Username, string Code, string NewPassword);
// Chỉ KIỂM TRA mã khôi phục ở bước 2 của màn hình quên mật khẩu (chưa đổi mật khẩu).
public record RecoveryVerifyRequest(string Username, string Code);
// Trả về mã khôi phục vừa tạo cho admin xem một lần (không lưu bản rõ ở server).
/// <param name="Code">
/// Chỉ có giá trị khi mã được CẤP TAY — quản trị viên phải đọc cho người dùng. Khi máy chủ đã gửi
/// qua thư điện tử hay Zalo thì trường này là null: chỉ chủ tài khoản cần biết mã.
/// </param>
public record RecoveryCodeResponse(string? Code, string Channel, bool Delivered, string Message, string? SentTo);

/// <summary>Ép kênh gửi mã; để trống là để máy chủ tự chọn kênh tốt nhất đang bật.</summary>
public record IssueRecoveryCodeRequest(string? Channel);

/// <summary>Người dùng tự xin mã ở màn quên mật khẩu (chỉ chạy khi đã bật một kênh gửi tự động).</summary>
public record RequestRecoveryCodeRequest(string? Username);
/// <summary>Token CHỈ có với client native (ứng dụng Android). Với trình duyệt, phiên nằm trong cookie
/// HttpOnly và trường này là null — cố ý, để JavaScript không cầm được token (xem Security/AuthCookies.cs).</summary>
public record LoginResponse(string? Token, UserDto User);
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

// ── Mã bảo mật 6 số của ứng dụng (lưu Ở MÁY CHỦ, thiết bị không giữ bản sao nào) ─────────────────
/// <summary>Trạng thái mã bảo mật của chính tài khoản đang đăng nhập.</summary>
/// <param name="HasPin">Đã tạo mã chưa (hỏi máy chủ, không phải hỏi thiết bị).</param>
/// <param name="LockedForSeconds">Còn bao nhiêu giây bị khoá thử lại; 0 = không khoá. Trả số giây
/// CÒN LẠI chứ không phải mốc thời gian tuyệt đối để đồng hồ điện thoại lệch cũng không sai.</param>
/// <param name="AttemptsBeforeLock">Còn mấy lần thử trước khi bị khoá tạm.</param>
public record AppPinStatusDto(bool HasPin, long LockedForSeconds, int AttemptsBeforeLock);
/// <summary>Tạo mã lần đầu (bỏ trống <paramref name="CurrentPin"/>) hoặc đổi mã (phải kèm mã cũ).</summary>
public record AppPinSetRequest(string Pin, string? CurrentPin);
public record AppPinVerifyRequest(string Pin);
/// <summary>Quên mã: xác minh mật khẩu tài khoản rồi xoá mã cũ để tạo mã mới.</summary>
public record AppPinResetRequest(string Password);
// Cài đặt đăng nhập của tài khoản: cho phép đăng nhập bản web hay không (app native luôn dùng được).
public record AccountLoginSettingsDto(bool WebLoginEnabled);
public record AccountLoginSettingsPatch(bool WebLoginEnabled);
public record UserPreferencesDto(bool WaterReminderEnabled, bool EyeReminderEnabled, bool KeepCreateVoucherOpen);
public record UserPreferencePatchRequest(bool? WaterReminderEnabled, bool? EyeReminderEnabled, bool? KeepCreateVoucherOpen);
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

    /// <summary>Đã gửi vector mã hóa nhưng còn chờ HR đối chiếu trực tiếp và kích hoạt.</summary>
    public bool FaceEnrollmentPending { get; init; }

    /// <summary>MỌI vai trò của tài khoản (vai trò chính + vai trò phụ như "Thủ kho"). Client dùng để
    /// hiện/ẩn tính năng giao việc &amp; nghiệm thu. Rỗng ⇒ chỉ có vai trò chính trong <see cref="Role"/>.</summary>
    public IReadOnlyList<string> Roles { get; init; } = System.Array.Empty<string>();

    /// <summary>Quyền hiệu lực do máy chủ suy từ toàn bộ vai trò hiện hành. Client native chỉ dùng
    /// danh sách này để dựng giao diện; mọi request đặc quyền vẫn bị middleware/API kiểm tra lại từ
    /// CSDL. Trả kèm login/me giúp Android không phải tự nhân bản ma trận vai trò và tự động nhận được
    /// quyền kế thừa (ví dụ Kế toán trưởng luôn có toàn bộ quyền Kế toán).</summary>
    public IReadOnlyList<string> Permissions =>
        [.. KetoanMini.Api.Security.Permissions
            .For(Roles.Count > 0 ? Roles : [Role])
            .OrderBy(permission => permission, StringComparer.Ordinal)];

    /// <summary>Có thẩm quyền giao việc &amp; nghiệm thu = có quyền tasks.assign. Suy từ BẢNG vai trò→quyền
    /// (Security/Permissions.cs) chứ không liệt kê tên vai trò ở đây, để thêm vai trò được giao việc
    /// (Trưởng phòng…) không phải nhớ sửa thêm chỗ này.</summary>
    public bool CanAssignTasks =>
        KetoanMini.Api.Security.Permissions.For(Roles.Count > 0 ? Roles : [Role])
            .Contains(KetoanMini.Api.Security.Permissions.TasksAssign);
}

// ----- Dashboard -----
public record DashboardDto(int ActiveCustomers, int TotalDocuments, decimal TotalPayments,
    decimal MonthRevenue, int Month, int Year, List<RecentDocDto> Recent);
public record RecentDocDto(Guid Id, string VoucherNo, DateOnly Date, string CustomerName, string Content, decimal Total);

// ----- Documents -----
public record DocumentListItemDto(Guid Id, string VoucherNo, DateOnly Date, string DocumentType,
    string CustomerName, string Content, decimal Total, string CreatedBy, DateTime? IssuedAt,
    DateTime? CancelledAt, string CancelledBy, string CancelReason,
    // Đường giao hàng của phiếu xuất kho. Mặc định rỗng để sổ chứng từ của khách hàng
    // (không quan tâm giao hàng) dùng lại nguyên bản ghi này mà không phải truy vấn thêm.
    string DeliveryMode = "", string DeliveryDriverName = "",
    // Đối soát khi lái xe nộp phiếu về: trạng thái việc giao hàng + mốc kế toán xác nhận phiếu
    // đã về kho. Có sẵn ở danh sách để kế toán nhìn ra ngay phiếu nào còn treo.
    string DeliveryTaskStatus = "", DateTime? DeliveryReturnedAt = null);
/// <param name="ProductId">
/// Mã hàng trong danh mục, nếu người lập phiếu chọn từ đó. NULL = gõ tay (vẫn hợp lệ: phiếu cũ,
/// hàng lạ, hàng gia công một lần). Có mã thì thống kê bám theo mã chứ không theo chính tả.
/// </param>
/// <param name="SupplierId">
/// Nguồn hàng: cuộn vừa xuất là hàng nhập của nhà cung cấp nào. Chỉ dùng nội bộ — không in ra phiếu
/// và không xuất hiện trong sổ công nợ PDF gửi khách.
/// </param>
public record DocumentLineDto(string LineContent, string Spec, decimal Quantity, decimal UnitPrice, string Note,
    Guid? ProductId = null, Guid? SupplierId = null, string SupplierName = "")
{
    public decimal Amount => Quantity * UnitPrice;
}
public record DocumentDetailDto(Guid Id, string VoucherNo, DateOnly Date, string CustomerName, string Content,
    string Note, List<DocumentLineDto> Lines, DateTime? IssuedAt, DateTime? CancelledAt,
    string CancelledBy, string CancelReason);
public record SaveDocumentRequest(string VoucherNo, DateOnly Date, string CustomerName, string Content, string Note,
    List<DocumentLineDto> Lines, string? DocumentType = null);
public record WarehousePrintRequest(string VoucherNo);
public record CancelDocumentRequest(string? Reason);

// ----- Reports -----
public record ReportsDto(decimal TotalPayments, decimal MonthRevenue, int TotalDocuments, int ActiveCustomers, List<MonthlyRowDto> Monthly);
public record MonthlyRowDto(int Year, int Month, int DocumentCount, int PaymentCount, decimal Total);

// ----- Audit -----
public record AuditDto(DateTime OccurredAt, string Username, string Action, string Entity, string EntityName, string Details);

// ----- Customers -----
public record CustomerDto(Guid Id, string Name, string TaxCode, string Phone, string Address, bool IsActive)
{
    /// <summary>Các tên gọi khác cùng trỏ về khách này. Chỉ danh sách khách hàng trả về; nơi khác để rỗng.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}
public record SaveAliasRequest(string? Alias);
public record SaveCustomerRequest(string Name, string TaxCode, string Phone, string Address);
public record CustomerReportDto(CustomerDto Customer, int DocumentCount, decimal Total, decimal ReceiptTotal,
    decimal PaymentTotal, decimal SalesTotal, List<DocumentListItemDto> Documents);

// ----- Customer debts -----
/// <param name="ReturnsTotal">
/// Hàng khách trả về (theo giá của chính đơn đã bán). Đứng riêng chứ không gộp vào
/// <paramref name="CollectedTotal"/>: trả hàng không phải là trả tiền.
/// </param>
/// <param name="CarriedBalance">
/// Dư nợ mang sang: số dư luỹ kế tính đến ngay trước ngày đầu kỳ đang xem, gồm cả số dư ban đầu do
/// kế toán nhập. Không lọc kỳ thì bằng <paramref name="OpeningBalance"/>.
/// </param>
/// <param name="SalesTotal">Chỉ tính phát sinh trong kỳ đang xem, giống <paramref name="ReturnsTotal"/> và <paramref name="CollectedTotal"/>.</param>
/// <param name="Balance">Dư cuối kỳ = <paramref name="CarriedBalance"/> + bán − trả hàng − đã thu.</param>
public record DebtSummaryDto(CustomerDto Customer, decimal OpeningBalance, DateOnly? OpeningDate,
    string OpeningNote, decimal SalesTotal, decimal ReturnsTotal, decimal CollectedTotal, decimal Balance,
    int InvoiceCount, DateOnly? LastActivityDate, decimal CarriedBalance);
public record DebtOverviewDto(decimal TotalOpeningBalance, decimal TotalSales, decimal TotalReturns,
    decimal TotalCollected, decimal TotalReceivable, int DebtorCount, List<DebtSummaryDto> Customers,
    decimal TotalCarried, DateOnly? From, DateOnly? To);
public record DebtTransactionDto(Guid Id, DateOnly Date, string Reference, string Kind, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance, bool Cancelled);
public record DebtDetailDto(CustomerDto Customer, DebtSummaryDto Summary, List<DebtTransactionDto> Transactions,
    DateOnly? From, DateOnly? To);
/// <summary>Một dòng hàng trên phiếu, dùng cho phần chi tiết mua bán của sổ công nợ in ra.</summary>
public record DebtVoucherLineDto(string Content, string Spec, decimal Quantity, decimal UnitPrice, decimal Amount);
public record DebtVoucherDto(Guid Id, DateOnly Date, string VoucherNo, string Kind, string Content,
    decimal Total, List<DebtVoucherLineDto> Lines);
public record SaveDebtPaymentRequest(decimal Amount, DateOnly Date, string? Note);
public record SaveOpeningBalanceRequest(decimal Amount, DateOnly AsOfDate, string? Note);

// ----- Users (Nhân sự) -----
public record UserAdminDto(Guid Id, string Username, string FullName, string Email, string Role, bool IsActive,
    string ApprovalStatus, DateTime? CreatedAt, bool IsOnline, DateTime? LastSeen, bool Verified, bool IsDiamond,
    IReadOnlyList<string> SecondaryRoles, bool RolesManagedByPositions);
public record CreateUserRequest(string Username, string FullName, string Email, string Password, string Role);
public record SetLockRequest(bool Locked);
// Reason: lý do đổi quyền, ghi vào lịch sử phân quyền (user_role_history) để sau này tra soát được
// vì sao một người từng có quyền đó. Không bắt buộc để không chặn thao tác gấp.
public record SetRoleRequest(string Role, string? Reason = null);
// Cấp/thu một vai trò PHỤ (vd "Warehouse" = Thủ kho) cho tài khoản. Grant=true để cấp, false để thu hồi.
// ExpiresAt: cấp TẠM tới thời điểm này rồi tự hết hiệu lực (ủy quyền khi người phụ trách đi vắng).
public record SetSecondaryRoleRequest(string Role, bool Grant, DateTime? ExpiresAt = null, string? Reason = null);
public record SetVerifiedRequest(bool Verified);
public record SetDiamondRequest(bool IsDiamond);
public record ResetPasswordResponse(string Code);

// ----- Feedback (Phan hoi) -----
public record FeedbackDto(long Id, string Type, string TypeLabel, string ReporterUsername, string ReporterName,
    string TargetName, string Reason, DateTime CreatedAt);
public record AttendanceFeedbackRequest(string TargetName, string? Reason);

// ----- Gia công -----
/// <param name="ProductId">Mã hàng trong danh mục, để phiếu gia công ghép được với phiếu bán và phiếu nhập mua.</param>
public record GiaCongLineDto(long Id, string LoaiDong, string MaHang, string TenHang, string QuyCach, string DonViTinh,
    decimal SoLuong, decimal DonGiaGiaCong, string GhiChu, Guid? ProductId = null)
{
    public decimal ThanhTien => SoLuong * DonGiaGiaCong;
}
public record GiaCongListItemDto(long Id, string MaPhieu, string LoaiPhieu, string DoiTac, string NhanVienPhuTrach,
    DateOnly NgayLap, DateOnly? HanHoanThanh, int SoMatHang,
    decimal TongGiaTri, decimal SoLuongXuat, decimal SoLuongNhap, decimal SoLuongConTaiCongTy,
    decimal TienGiaCongPhaiTra);
public record GiaCongDetailDto(long Id, string MaPhieu, string LoaiPhieu, string DoiTac, string NhanVienPhuTrach,
    DateOnly NgayLap, DateOnly? HanHoanThanh, string GhiChu, List<GiaCongLineDto> Lines, Guid? DoiTacId = null);
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

// Tự đăng ký khuôn mặt (app): mỗi tài khoản chỉ đăng ký MỘT lần, gồm nhiều tư thế (góc) để mẫu bền.
// Mỗi góc là một loạt ảnh; server chọn khung tốt nhất, kiểm tra chất lượng + liveness rồi lưu 1 mẫu/góc.
public record FaceEnrollPose(string Pose, List<string> Images);
public record SelfFaceEnrollRequest(List<FaceEnrollPose> Poses);
public record SelfFaceStatusDto(bool Registered, int SampleCount, DateTime? CreatedAt,
    bool Pending = false, Guid? RequestId = null, string? RequestStatus = null,
    DateTime? RequestedAt = null, string? ReviewNote = null);
public record SelfFaceEnrollResult(string Message, int SampleCount, string Status = "pending", Guid? RequestId = null);
public record FaceEnrollmentRequestDto(Guid Id, string Username, string FullName, string Status,
    int SampleCount, DateTime RequestedAt, DateTime ExpiresAt, string ReviewedBy,
    DateTime? ReviewedAt, string ReviewNote, string IdentityVerificationMethod);
public record FaceEnrollmentApproveRequest(bool IdentityVerified, string VerificationMethod, string? Note,
    List<string>? VerificationImages = null);
public record FaceEnrollmentRejectRequest(string Reason);
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
// ConfirmToken: token do bước XEM TRƯỚC cấp. Có token thì KHÔNG cần gửi lại ảnh — server ghi công theo
// kết quả đã nhận diện, không chạy lại AdaFace/Silent-Face lần hai. Client cũ không gửi trường này vẫn
// chạy đúng luồng gửi lại ảnh như trước (tương thích ngược cho APK đã phát hành).
public record ChamCongBurstRequest(List<string>? Images = null, DateTime? OccurredAt = null, bool SelfOnly = false,
    bool PreviewOnly = false, double? GpsLat = null, double? GpsLng = null,
    bool MotionCheck = false, string? ConfirmToken = null);

// Cấu hình liveness QUAY ĐẦU (challenge-response): Enabled = app yêu cầu quay đầu lúc quét;
// Enforce = chặn nếu biên độ quay quá nhỏ (nghi ảnh tĩnh) hay chỉ ghi log để hiệu chỉnh.
public record MotionConfigDto(bool Enabled, bool Enforce);

// Cấu hình yêu cầu cười dùng chung cho hướng dẫn trên app và bước xác minh lại từ ảnh ở server (0..1).
public record SmileConfigDto(bool Enabled, double Threshold = 0.65);

// Cấu hình kiểm tra MỞ MẮT phía server: Enforce = chặn khi mắt nhắm/lim dim (bestEyeOpen < Threshold);
// mặc định TẮT (chỉ đo) để hiệu chỉnh trước. Threshold theo thang EyeOpenScore (0..1).
public record EyeOpenConfigDto(bool Enforce, double Threshold);

// Một lượt đo Silent-Face (chống ảnh/màn hình): điểm P(real) cao nhất/trung bình/nhì + biên độ quay đầu.
// EyeOpen = độ mở mắt cao nhất của loạt (−1 = không đo được); để hiệu chỉnh ngưỡng chặn nhắm mắt.
public record LivenessMetricDto(DateTime AtUtc, string User, double Best, double Mean, double Second,
    int Frames, double Threshold, bool Passed, double MotionSpan, double EyeOpen = -1);

// Mức chống giả mạo đang chạy thật: Level = Full | Basic | None (xem AntiSpoofLevel).
// None nghĩa là MỌI ảnh được coi là người thật — panel admin phải cảnh báo đỏ.
public record AntiSpoofDto(string Level, string Detail);

// Dữ liệu cho panel "Chống ảnh/màn hình giả": trạng thái model + cấu hình mở mắt + các lượt đo gần nhất.
public record LivenessPanelDto(AntiSpoofDto AntiSpoof, List<LivenessMetricDto> Metrics, EyeOpenConfigDto? EyeOpen = null);

/// <summary>
/// Kết quả chấm công theo loạt ảnh. <see cref="Status"/>:
/// ok | posture (sai tư thế) | eyesclosed | nosmile | lowquality | noface | spoof | unknown.
/// <see cref="Guidance"/> là hướng dẫn sửa tư thế/điều kiện chụp (nếu có).
/// </summary>
/// <remarks>
/// <c>PreviewToken</c> chỉ có ở phản hồi của bước xem trước: client gửi lại token này (thay cho cả loạt
/// ảnh) khi bấm "Xác nhận" để ghi công mà không phải nhận diện lại.
/// </remarks>
public record ChamCongResult(string Status, bool Matched, string? Username, string? FullName,
    double Similarity, string? Loai, DateTime? OccurredAt, double Quality, string Message, string? Guidance,
    string? PreviewToken = null);
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
