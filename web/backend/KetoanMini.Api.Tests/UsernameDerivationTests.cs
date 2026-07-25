using KetoanMini.Api.Endpoints;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Khóa quy ước sinh tên đăng nhập từ họ tên + mã nhân viên (dùng khi tạo hồ sơ mới tự tạo tài khoản):
/// TÊN (token cuối) + VIẾT TẮT các token đầu (họ, đệm) + PHẦN SỐ của mã (tối thiểu 2 chữ số), bỏ dấu.
/// </summary>
public sealed class UsernameDerivationTests
{
    [Theory]
    [InlineData("Nguyễn Văn An", "NV0001", "annv01")]     // ví dụ gốc của yêu cầu
    [InlineData("Trần Thị Hương", "NV0012", "huongtt12")]
    [InlineData("Lê Đức", "NV0007", "ducl07")]            // đ → d
    [InlineData("An", "NV0003", "an03")]                  // chỉ có tên
    [InlineData("Phạm Hoàng Long", "NV0100", "longph100")] // số ≥ 3 chữ số không bị cắt
    [InlineData("  Đỗ  Thị  Bích  ", "NV0025", "bichdt25")] // thừa khoảng trắng + dấu
    public void Derives_username_from_name_and_code(string fullName, string code, string expected)
        => Assert.Equal(expected, HrEndpoints.DeriveLoginUsername(fullName, code));

    [Fact]
    public void Falls_back_when_name_empty()
        => Assert.Equal("nv05", HrEndpoints.DeriveLoginUsername("   ", "NV0005"));
}
