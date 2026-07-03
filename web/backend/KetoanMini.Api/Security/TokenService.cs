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
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("fullName", user.FullName),
        };
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
