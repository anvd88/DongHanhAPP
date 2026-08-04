namespace KetoanMini.Api.Security;

public static class AppRoles
{
    public const string Admin = "Admin";
    /// <summary>Ban giám đốc — xem dữ liệu/báo cáo toàn công ty, không quản trị hệ thống.</summary>
    public const string Executive = "Executive";
    public const string Accounting = "Accounting";
    /// <summary>Kế toán tiền lương — lập, cập nhật và phát hành phiếu lương; không nhận toàn bộ quyền Kế toán.</summary>
    public const string Payroll = "Payroll";
    /// <summary>Thủ quỹ — thực hiện chi tiền sau khi phiếu đã được kế toán trưởng duyệt.</summary>
    public const string Cashier = "Cashier";
    public const string Hr = "HR";
    public const string Employee = "Employee";
    public const string Kiosk = "Kiosk";
    /// <summary>Thủ kho — người có thẩm quyền giao việc &amp; nghiệm thu. Thường là vai trò THỨ HAI
    /// cấp thêm cho một tài khoản (một người có thể vừa là Kế toán/Nhân viên vừa là Thủ kho).</summary>
    public const string Warehouse = "Warehouse";
    /// <summary>Kế toán trưởng — kế toán có thêm quyền DUYỆT chứng từ. Xem Permissions.RolePermissions.</summary>
    public const string ChiefAccountant = "ChiefAccountant";
    /// <summary>Trưởng phòng — duyệt đơn &amp; giao việc trong phạm vi phòng ban của mình.</summary>
    public const string Manager = "Manager";

    public static readonly string[] All =
        [Admin, Executive, ChiefAccountant, Accounting, Payroll, Cashier, Warehouse, Hr, Manager, Employee, Kiosk];

    /// <summary>Vai trò có thể gắn cho hồ sơ nhân sự. Kiosk là danh tính kỹ thuật, không phải chức vụ.</summary>
    public static readonly string[] Assignable =
        [Admin, Executive, ChiefAccountant, Accounting, Payroll, Cashier, Warehouse, Hr, Manager, Employee];

    /// <summary>Vai trò được phép cấp THÊM cho một tài khoản (ngoài vai trò chính) qua bảng user_roles.
    /// Cố ý KHÔNG có Admin/Kiosk: nâng lên Admin phải đổi vai trò CHÍNH để còn đối chiếu audit.</summary>
    public static readonly string[] Secondary = [Warehouse, Manager, ChiefAccountant, Accounting, Payroll, Cashier, Hr];

    /// <summary>
    /// Thứ tự chọn vai trò chính khi một nhân sự kiêm nhiệm nhiều chức vụ. Quyền thực tế luôn là hợp
    /// của mọi vai trò; thứ tự này chỉ làm cho cột vai trò chính, giao diện mặc định và dữ liệu cũ ổn định.
    /// Ưu tiên vai trò có hành động nghiệp vụ đặc biệt trước vai trò chỉ đọc.
    /// </summary>
    public static int PrimaryPriority(string? role) => Normalize(role) switch
    {
        Admin => 1000,
        ChiefAccountant => 900,
        Accounting => 800,
        Payroll => 750,
        Cashier => 700,
        Hr => 600,
        Manager => 500,
        Warehouse => 400,
        Executive => 300,
        Employee => 100,
        Kiosk => 0,
        _ => -1,
    };

    public static bool IsPrivileged(string? role)
        => Normalize(role) is { } normalized && normalized is not Employee;

    /// <summary>Nhãn tiếng Việt để hiển thị trên UI (web/app).</summary>
    public static string Label(string? role) => Normalize(role) switch
    {
        Admin => "Quản trị hệ thống",
        Executive => "Ban giám đốc",
        Accounting => "Kế toán",
        Payroll => "Kế toán tiền lương",
        ChiefAccountant => "Kế toán trưởng",
        Cashier => "Thủ quỹ",
        Hr => "Quản lý nhân sự",
        Manager => "Quản lý",
        Employee => "Nhân viên",
        Kiosk => "Kiosk",
        Warehouse => "Thủ kho",
        _ => role ?? ""
    };

    public static string? Normalize(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        return role.Trim().ToLowerInvariant() switch
        {
            "admin" => Admin,
            "executive" or "director" or "giamdoc" or "giam doc" or "giám đốc" or "ban giám đốc" => Executive,
            "accounting" or "ketoan" or "ke toan" => Accounting,
            "payroll" or "payrollaccountant" or "ketoantienluong" or "ke toan tien luong" or "kế toán tiền lương" => Payroll,
            "chiefaccountant" or "ketoantruong" or "ke toan truong" or "kế toán trưởng" => ChiefAccountant,
            "cashier" or "thuquy" or "thu quy" or "thủ quỹ" => Cashier,
            "manager" or "truongphong" or "truong phong" or "trưởng phòng" => Manager,
            "hr" or "humanresources" => Hr,
            "employee" or "user" => Employee,
            "kiosk" => Kiosk,
            "warehouse" or "thukho" or "thu kho" or "thủ kho" or "storekeeper" => Warehouse,
            _ => null
        };
    }
}
