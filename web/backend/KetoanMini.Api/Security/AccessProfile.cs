namespace KetoanMini.Api.Security;

/// <summary>Phạm vi dữ liệu người dùng được phép nhìn thấy.</summary>
public enum ScopeKind
{
    /// <summary>Chỉ dữ liệu của chính mình.</summary>
    Self,
    /// <summary>Dữ liệu trong phòng ban của mình.</summary>
    Department,
    /// <summary>Dữ liệu trong địa điểm/chi nhánh của mình.</summary>
    Branch,
    /// <summary>Toàn bộ dữ liệu.</summary>
    All,
}

public sealed record AccessScope(ScopeKind Kind, Guid? DepartmentId, Guid? LocationId)
{
    public static readonly AccessScope SelfOnly = new(ScopeKind.Self, null, null);
    public string Name => Kind switch
    {
        ScopeKind.All => "all",
        ScopeKind.Department => "department",
        ScopeKind.Branch => "branch",
        _ => "self",
    };
}

/// <summary>
/// HỒ SƠ TRUY CẬP — thứ DUY NHẤT client được dùng để dựng giao diện. Backend tính lại từ CSDL mỗi
/// lần được hỏi, nên client không thể tự "khai" quyền cho mình: sửa localStorage/URL chỉ đổi được
/// những gì hiện ra, không đổi được những gì server cho phép làm.
/// </summary>
/// <param name="AuthorizationVersion">Tăng mỗi lần quyền của tài khoản thay đổi. Client so sánh để
/// biết cần nạp lại hồ sơ; server dùng để ghi audit "quyền cũ → quyền mới".</param>
public sealed record AccessProfileDto(
    string Username,
    string FullName,
    string PrimaryRole,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> RoleLabels,
    IReadOnlyList<string> Permissions,
    string Scope,
    Guid? DepartmentId,
    Guid? LocationId,
    string UiProfile,
    string LandingPath,
    int AuthorizationVersion);
