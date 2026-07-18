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

    public static readonly string[] All = [Admin, Accounting, Hr, Employee, Kiosk, Warehouse];

    /// <summary>Nhãn tiếng Việt để hiển thị trên UI (web/app).</summary>
    public static string Label(string? role) => Normalize(role) switch
    {
        Admin => "Quản trị",
        Accounting => "Kế toán",
        Hr => "Nhân sự",
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
            "hr" or "humanresources" => Hr,
            "employee" or "user" => Employee,
            "kiosk" => Kiosk,
            "warehouse" or "thukho" or "thu kho" or "thủ kho" or "storekeeper" => Warehouse,
            _ => null
        };
    }
}
