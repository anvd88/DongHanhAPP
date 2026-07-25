namespace KetoanMini.Api.Security;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Accounting = "Accounting";
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
        [Admin, Accounting, ChiefAccountant, Hr, Manager, Employee, Kiosk, Warehouse];

    /// <summary>Vai trò được phép cấp THÊM cho một tài khoản (ngoài vai trò chính) qua bảng user_roles.
    /// Cố ý KHÔNG có Admin/Kiosk: nâng lên Admin phải đổi vai trò CHÍNH để còn đối chiếu audit.</summary>
    public static readonly string[] Secondary = [Warehouse, Manager, ChiefAccountant, Accounting, Hr];

    /// <summary>Nhãn tiếng Việt để hiển thị trên UI (web/app).</summary>
    public static string Label(string? role) => Normalize(role) switch
    {
        Admin => "Quản trị",
        Accounting => "Kế toán",
        ChiefAccountant => "Kế toán trưởng",
        Hr => "Nhân sự",
        Manager => "Trưởng phòng",
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
            "accounting" or "ketoan" or "ke toan" => Accounting,
            "chiefaccountant" or "ketoantruong" or "ke toan truong" or "kế toán trưởng" => ChiefAccountant,
            "manager" or "truongphong" or "truong phong" or "trưởng phòng" => Manager,
            "hr" or "humanresources" => Hr,
            "employee" or "user" => Employee,
            "kiosk" => Kiosk,
            "warehouse" or "thukho" or "thu kho" or "thủ kho" or "storekeeper" => Warehouse,
            _ => null
        };
    }
}
