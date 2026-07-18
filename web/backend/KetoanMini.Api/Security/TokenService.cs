using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using KetoanMini.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace KetoanMini.Api.Security;

public sealed class TokenService(IConfiguration config)
{
    private readonly IConfigurationSection _jwt = config.GetSection("Jwt");

    public string CreateToken(UserDto user, string? sid = null)
    {
        var role = AppRoles.Normalize(user.Role) ?? AppRoles.Employee;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

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
        var token = new JwtSecurityToken(
            issuer: _jwt["Issuer"],
            audience: _jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(hours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
