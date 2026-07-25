namespace KetoanMini.Api.Security;

/// <summary>
/// DANH SÁCH QUYỀN CHUẨN của toàn hệ thống — nguồn duy nhất. Endpoint/menu/nút bấm phải kiểm tra
/// QUYỀN (permission) chứ không kiểm tra tên vai trò, để thêm vai trò mới (Kế toán trưởng, Trưởng
/// phòng…) chỉ là sửa <see cref="RolePermissions"/> ở đây chứ không phải đi sửa hàng chục chỗ.
///
/// Quy ước tên: "module.hành_động" — module là danh từ số nhiều, hành động là động từ.
/// KHÔNG viết chuỗi quyền trực tiếp trong endpoint: luôn dùng hằng số trong lớp này.
/// </summary>
public static class Permissions
{
    // ── Tài khoản & phân quyền ────────────────────────────────────────────────
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    // ── Hệ thống ──────────────────────────────────────────────────────────────
    public const string SystemSettingsManage = "system.settings.manage";
    public const string SystemReleasesManage = "system.releases.manage";

    // ── Nhật ký hoạt động ─────────────────────────────────────────────────────
    /// <summary>Xem nhật ký hoạt động. Phạm vi (toàn bộ hay chỉ phần tiền) do server chốt riêng.</summary>
    public const string AuditRead = "audit.read";

    // ── Kế toán / chứng từ ────────────────────────────────────────────────────
    public const string AccountingAccess = "accounting.access";
    public const string VouchersRead = "vouchers.read";
    public const string VouchersCreate = "vouchers.create";
    public const string VouchersUpdate = "vouchers.update";
    public const string VouchersApprove = "vouchers.approve";
    public const string VouchersCancel = "vouchers.cancel";
    /// <summary>Phiếu chi tiền mặt: xem sổ. Lập/duyệt còn đòi thuộc phòng ban is_accounting (server chốt).</summary>
    public const string PayoutRead = "payout.read";

    // ── Báo cáo ───────────────────────────────────────────────────────────────
    public const string ReportRead = "report.read";
    public const string ReportExport = "report.export";

    // ── Chấm công ─────────────────────────────────────────────────────────────
    /// <summary>Tự chấm công &amp; xem bảng công của chính mình.</summary>
    public const string AttendanceSelf = "attendance.self";
    public const string AttendanceRead = "attendance.read";
    public const string AttendanceManage = "attendance.manage";
    /// <summary>Máy kiosk chấm công ẩn danh.</summary>
    public const string AttendanceKiosk = "attendance.kiosk";

    // ── Bảng lương ────────────────────────────────────────────────────────────
    public const string PayrollRead = "payroll.read";
    public const string PayrollManage = "payroll.manage";

    // ── Nhân sự ───────────────────────────────────────────────────────────────
    /// <summary>Vào khu nhân sự với dữ liệu của chính mình (hồ sơ, đơn từ, quyền lợi).</summary>
    public const string HrSelfAccess = "hr.self.access";
    public const string HrRead = "hr.read";
    public const string HrManage = "hr.manage";

    // ── Đơn từ ────────────────────────────────────────────────────────────────
    public const string RequestsSelf = "requests.self";
    public const string RequestsApprove = "requests.approve";
    public const string RequestsManage = "requests.manage";

    // ── Phạt / kỷ luật ────────────────────────────────────────────────────────
    public const string PenaltyRead = "penalty.read";
    public const string PenaltyManage = "penalty.manage";

    // ── Giao việc & nghiệm thu ────────────────────────────────────────────────
    public const string TasksSelf = "tasks.self";
    public const string TasksAssign = "tasks.assign";

    // ── Cổng thông tin công ty ────────────────────────────────────────────────
    public const string PortalRead = "portal.read";
    public const string PortalManage = "portal.manage";

    // ── Trò chuyện ────────────────────────────────────────────────────────────
    public const string ChatAccess = "chat.access";

    /// <summary>Loại claim mang quyền trong ClaimsPrincipal (do middleware dựng lại từ DB mỗi request).</summary>
    public const string ClaimType = "perm";

    /// <summary>Mọi quyền hợp lệ — dùng để đăng ký policy lúc khởi động &amp; kiểm tra chính tả.</summary>
    public static readonly string[] All =
    [
        UsersRead, UsersManage, RolesManage,
        SystemSettingsManage, SystemReleasesManage,
        AuditRead,
        AccountingAccess, VouchersRead, VouchersCreate, VouchersUpdate, VouchersApprove, VouchersCancel, PayoutRead,
        ReportRead, ReportExport,
        AttendanceSelf, AttendanceRead, AttendanceManage, AttendanceKiosk,
        PayrollRead, PayrollManage,
        HrSelfAccess, HrRead, HrManage,
        RequestsSelf, RequestsApprove, RequestsManage,
        PenaltyRead, PenaltyManage,
        TasksSelf, TasksAssign,
        PortalRead, PortalManage,
        ChatAccess,
    ];

    /// <summary>Quyền tối thiểu của MỌI nhân viên đã đăng nhập (trừ máy kiosk).</summary>
    private static readonly string[] BaseEmployee =
    [
        HrSelfAccess, AttendanceSelf, RequestsSelf, TasksSelf, ChatAccess, PortalRead, PenaltyRead,
    ];

    /// <summary>
    /// VAI TRÒ → QUYỀN. Đây là chỗ DUY NHẤT quyết định một vai trò làm được gì. Thêm vai trò mới =
    /// thêm một dòng ở đây (và một nhánh trong <see cref="AppRoles.Normalize"/>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolePermissions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Admin có mọi quyền TRỪ lập/duyệt phiếu chi tiền mặt: theo quy trình nội bộ chỉ thủ quỹ
            // (Kế toán thuộc phòng ban is_accounting) được đụng tiền mặt — xem PayoutVoucherEndpoints.
            [AppRoles.Admin] = All,

            [AppRoles.Employee] = BaseEmployee,

            [AppRoles.Accounting] =
            [
                .. BaseEmployee,
                AccountingAccess, VouchersRead, VouchersCreate, VouchersUpdate, VouchersCancel,
                PayoutRead, ReportRead, ReportExport, AuditRead,
            ],

            // Kế toán trưởng = kế toán + được DUYỆT chứng từ và xem bảng lương.
            [AppRoles.ChiefAccountant] =
            [
                .. BaseEmployee,
                AccountingAccess, VouchersRead, VouchersCreate, VouchersUpdate, VouchersApprove, VouchersCancel,
                PayoutRead, ReportRead, ReportExport, AuditRead, PayrollRead,
            ],

            [AppRoles.Hr] =
            [
                .. BaseEmployee,
                HrRead, HrManage, AttendanceRead, AttendanceManage,
                RequestsApprove, RequestsManage, PayrollRead, PenaltyManage, ReportRead,
            ],

            // Trưởng phòng: duyệt đơn của phòng mình + giao việc. Phạm vi dữ liệu (phòng ban) do
            // server ép riêng qua AccessScope, quyền ở đây chỉ mở CỬA chứ không mở PHẠM VI.
            [AppRoles.Manager] =
            [
                .. BaseEmployee,
                HrRead, RequestsApprove, TasksAssign, AttendanceRead, ReportRead,
            ],

            // Thủ kho: vai trò PHỤ, chỉ thêm quyền giao việc & nghiệm thu.
            [AppRoles.Warehouse] = [.. BaseEmployee, TasksAssign],

            // Máy kiosk: chỉ chấm công ẩn danh, không có gì khác.
            [AppRoles.Kiosk] = [AttendanceKiosk],
        };

    /// <summary>
    /// Gộp quyền của TẤT CẢ vai trò một tài khoản đang giữ (vai trò chính + phụ). Vai trò lạ/không
    /// chuẩn hóa được bị bỏ qua — không có vai trò hợp lệ nào ⇒ không có quyền nào (đóng mặc định).
    /// </summary>
    public static IReadOnlySet<string> For(IEnumerable<string?> roles)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in roles)
        {
            if (AppRoles.Normalize(raw) is not { } role) continue;
            if (!RolePermissions.TryGetValue(role, out var perms)) continue;
            foreach (var p in perms) result.Add(p);
        }
        return result;
    }

    /// <summary>Tên policy ASP.NET tương ứng một quyền (đăng ký ở Program.cs).</summary>
    public static string Policy(string permission) => $"perm:{permission}";

    /// <summary>
    /// Nhãn tiếng Việt cho một quyền — CHỈ để hiển thị (ví dụ liệt kê "vai trò này làm được gì" trong
    /// màn quản lý tài khoản). Không dùng để chốt cửa. Quyền lạ trả về chính chuỗi khóa.
    /// </summary>
    public static string Label(string permission) => permission switch
    {
        UsersRead => "Xem danh sách người dùng",
        UsersManage => "Quản lý tài khoản & phân quyền",
        RolesManage => "Quản lý vai trò",
        SystemSettingsManage => "Cấu hình hệ thống",
        SystemReleasesManage => "Quản lý bản cập nhật APK",
        AuditRead => "Xem nhật ký hoạt động",
        AccountingAccess => "Vào khu kế toán",
        VouchersRead => "Xem chứng từ",
        VouchersCreate => "Lập chứng từ",
        VouchersUpdate => "Sửa chứng từ",
        VouchersApprove => "Duyệt chứng từ",
        VouchersCancel => "Hủy chứng từ",
        PayoutRead => "Xem sổ phiếu chi tiền mặt",
        ReportRead => "Xem báo cáo",
        ReportExport => "Xuất báo cáo",
        AttendanceSelf => "Tự chấm công & xem bảng công của mình",
        AttendanceRead => "Xem chấm công nhân viên",
        AttendanceManage => "Quản lý chấm công",
        AttendanceKiosk => "Máy kiosk chấm công ẩn danh",
        PayrollRead => "Xem bảng lương",
        PayrollManage => "Quản lý bảng lương",
        HrSelfAccess => "Xem hồ sơ/đơn từ của mình",
        HrRead => "Xem dữ liệu nhân sự",
        HrManage => "Quản lý nhân sự",
        RequestsSelf => "Gửi đơn từ",
        RequestsApprove => "Duyệt đơn từ",
        RequestsManage => "Quản lý đơn từ",
        PenaltyRead => "Xem phạt/kỷ luật của mình",
        PenaltyManage => "Quản lý phạt/kỷ luật",
        TasksSelf => "Nhận & báo cáo việc được giao",
        TasksAssign => "Giao việc & nghiệm thu",
        PortalRead => "Xem cổng thông tin",
        PortalManage => "Quản trị cổng thông tin",
        ChatAccess => "Trò chuyện",
        _ => permission,
    };
}
