using Microsoft.AspNetCore.Authorization;

namespace KetoanMini.Api.Security;

public static class PermissionEndpoints
{
    /// <summary>
    /// Đăng ký MỘT policy cho MỖI quyền trong danh mục. Policy chỉ chấp nhận claim quyền do middleware
    /// dựng lại từ CSDL ở mỗi request — claim quyền KHÔNG nằm trong JWT, nên token cũ không mang theo
    /// quyền cũ được, và DB không đọc được thì không có claim nào ⇒ mọi endpoint đặc quyền trả 403.
    /// </summary>
    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var permission in Permissions.All)
            options.AddPolicy(Permissions.Policy(permission),
                p => p.RequireAuthenticatedUser().RequireClaim(Permissions.ClaimType, permission));
    }

    /// <summary>Chốt cửa một endpoint/nhóm endpoint bằng QUYỀN (không phải tên vai trò).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(Permissions.Policy(permission));

    /// <summary>Cho qua nếu có ÍT NHẤT MỘT trong các quyền (vd: xem được vì là kế toán HOẶC là nhân sự).</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(p => p
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => permissions.Any(perm => ctx.User.HasClaim(Permissions.ClaimType, perm))));
}
