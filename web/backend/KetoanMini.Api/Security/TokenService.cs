using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using KetoanMini.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace KetoanMini.Api.Security;

public sealed class TokenService(IConfiguration config)
{
    private readonly IConfigurationSection _jwt = config.GetSection("Jwt");

    /// <summary>Hạn của phiên web (cookie) — ngắn hơn nhiều token app, xem AuthCookies.WebExpireHours.</summary>
    public DateTime WebExpiresAt() => DateTime.UtcNow.AddHours(AuthCookies.WebExpireHours(config));

    /// <summary>
    /// Token cho PHIÊN WEB (đặt vào cookie HttpOnly): cùng nội dung claim nhưng hạn ngắn.
    /// Trình duyệt hay là máy dùng chung, nên không để phiên web sống 365 ngày như app riêng của một người.
    /// </summary>
    public string CreateWebToken(UserDto user, string? sid = null)
        => CreateToken(user, sid, WebExpiresAt());

    /// <summary>
    /// GIA HẠN TRƯỢT phiên web: dựng token mới từ chính danh tính của request hiện tại. Cố ý KHÔNG
    /// chép claim vai trò/quyền sang — chúng được dựng lại từ CSDL ở mỗi request, nên chép sang chỉ
    /// tạo ra một bản sao cũ có thể sai.
    /// </summary>
    public string RenewWebToken(ClaimsPrincipal principal)
    {
        var claims = new List<Claim>();
        foreach (var type in new[] { ClaimTypes.NameIdentifier, ClaimTypes.Name, "fullName", "sid" })
            if (principal.FindFirst(type) is { } c) claims.Add(new Claim(type, c.Value));
        return Write(claims, WebExpiresAt());
    }

    public string CreateToken(UserDto user, string? sid = null, DateTime? expiresAt = null)
    {
        var role = AppRoles.Normalize(user.Role) ?? AppRoles.Employee;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, role),
            new("fullName", user.FullName),
        };
        // Vai trò PHỤ (đa vai trò): thêm mỗi vai trò khác vai trò chính thành một claim Role riêng để
        // IsInRole hoạt động cho cả vai trò phụ (vd "Warehouse"/Thủ kho). Chốt nghiệp vụ vẫn kiểm tra
        // lại theo DB nên cấp/thu quyền có hiệu lực ngay; JWT chỉ là lớp tiện lợi cho các policy theo role.
        foreach (var extra in user.Roles)
        {
            var norm = AppRoles.Normalize(extra);
            if (norm is not null && !string.Equals(norm, role, StringComparison.Ordinal)
                && !claims.Any(c => c.Type == ClaimTypes.Role && c.Value == norm))
                claims.Add(new Claim(ClaimTypes.Role, norm));
        }
        // Gắn định danh phiên/thiết bị (sid) vào token để có thể thu hồi từ xa (đăng xuất thiết bị).
        if (!string.IsNullOrWhiteSpace(sid))
            claims.Add(new Claim("sid", sid.Trim()));

        var hours = int.TryParse(_jwt["ExpireHours"], out var h) ? h : 12;
        return Write(claims, expiresAt ?? DateTime.UtcNow.AddHours(hours));
    }

    private string Write(IEnumerable<Claim> claims, DateTime expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt["Key"]!));
        var token = new JwtSecurityToken(
            issuer: _jwt["Issuer"],
            audience: _jwt["Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
