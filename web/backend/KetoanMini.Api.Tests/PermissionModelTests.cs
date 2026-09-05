using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Bảng VAI TRÒ → QUYỀN là chỗ duy nhất quyết định ai làm được gì, nên nó phải được khoá bằng test:
/// một lần sửa nhầm ở đó là mở toang cả hệ thống mà không có triệu chứng gì khác. Các test dưới đây
/// không cần CSDL — chúng chấm chính cái bảng đó.
/// </summary>
public sealed class PermissionMapTests
{
    [Fact]
    public void MoiVaiTro_ChiCapQuyenCoTrongDanhMuc()
    {
        foreach (var (role, perms) in Permissions.RolePermissions)
            foreach (var p in perms)
                Assert.True(Permissions.All.Contains(p), $"Vai trò {role} cấp quyền lạ '{p}' (gõ sai tên?).");
    }

    [Fact]
    public void MoiVaiTroHopLe_DeuCoTrongBangQuyen()
    {
        foreach (var role in AppRoles.All)
            Assert.True(Permissions.RolePermissions.ContainsKey(role),
                $"Vai trò {role} chưa khai báo quyền — tài khoản giữ vai trò này sẽ không làm được gì.");
    }

    [Fact]
    public void Admin_CoMoiQuyenKyThuat_NhungKhongTuThamGiaThuTienMat()
    {
        var admin = Permissions.For([AppRoles.Admin]);
        // Giữ tiền mặt là việc theo CHỨC DANH, không phải đặc quyền quản trị: Admin chỉ tham gia khi
        // đồng thời giữ vai trò Kế toán/Thủ quỹ/Lái xe. cashfund.manage (ghi bút toán tay vào quỹ)
        // nằm cùng nhóm đó — Permissions.cs đã loại nó khỏi Admin, danh sách này phải khớp.
        var collectionPermissions = new[]
        {
            Permissions.CollectionsSelf, Permissions.CollectionsReadAll, Permissions.CollectionsCreate,
            Permissions.CollectionsReceive, Permissions.CollectionsResolve, Permissions.CashFundManage,
        };
        Assert.Empty(Permissions.All.Except(collectionPermissions).Except(admin));
        Assert.All(collectionPermissions, permission => Assert.DoesNotContain(permission, admin));
    }

    [Fact]
    public void Kiosk_ChiChamCong_KhongChamVaoGiKhac()
    {
        var perms = Permissions.For([AppRoles.Kiosk]);
        Assert.Contains(Permissions.AttendanceKiosk, perms);
        Assert.DoesNotContain(Permissions.HrSelfAccess, perms);
        Assert.DoesNotContain(Permissions.UsersManage, perms);
    }

    [Theory]
    [InlineData(AppRoles.Employee)]
    [InlineData(AppRoles.Executive)]
    [InlineData(AppRoles.Accounting)]
    [InlineData(AppRoles.ChiefAccountant)]
    [InlineData(AppRoles.Cashier)]
    [InlineData(AppRoles.Hr)]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.Warehouse)]
    [InlineData(AppRoles.Driver)]
    [InlineData(AppRoles.Kiosk)]
    public void ChiAdmin_MoiQuanTriDuocTaiKhoanVaCauHinh(string role)
    {
        var perms = Permissions.For([role]);
        Assert.DoesNotContain(Permissions.UsersManage, perms);
        Assert.DoesNotContain(Permissions.SystemSettingsManage, perms);
    }

    /// <summary>Kế toán trưởng khác kế toán ĐÚNG ở chỗ được duyệt chứng từ — nếu không thì thêm vai trò
    /// này chẳng để làm gì, mà tệ hơn là kế toán thường tự duyệt được chứng từ mình lập.</summary>
    [Fact]
    public void KeToanTruong_DuyetDuocChungTu_KeToanThuongThiKhong()
    {
        Assert.DoesNotContain(Permissions.VouchersApprove, Permissions.For([AppRoles.Accounting]));
        Assert.Contains(Permissions.VouchersApprove, Permissions.For([AppRoles.ChiefAccountant]));
    }

    [Fact]
    public void KeToanTruong_LuonKeThuaDayDuMoiQuyenCuaKeToan()
    {
        var accounting = Permissions.For([AppRoles.Accounting]);
        var chief = Permissions.For([AppRoles.ChiefAccountant]);

        Assert.Empty(accounting.Except(chief));
        Assert.Contains(Permissions.PayoutCreate, chief);
        Assert.Contains(Permissions.PayoutApprove, chief);
    }

    [Fact]
    public void ThuQuy_ChiDuocThucChi_KhongTuLapHayTuDuyet()
    {
        var cashier = Permissions.For([AppRoles.Cashier]);

        Assert.Contains(Permissions.PayoutRead, cashier);
        Assert.Contains(Permissions.PayoutPay, cashier);
        Assert.DoesNotContain(Permissions.PayoutCreate, cashier);
        Assert.DoesNotContain(Permissions.PayoutApprove, cashier);
    }

    [Fact]
    public void BanGiamDoc_XemToanCongTy_NhungKhongQuanTriHayTuDuyetTien()
    {
        var executive = Permissions.For([AppRoles.Executive]);

        Assert.Contains(Permissions.CompanyScopeAll, executive);
        Assert.Contains(Permissions.ReportRead, executive);
        Assert.Contains(Permissions.VouchersRead, executive);
        Assert.DoesNotContain(Permissions.UsersManage, executive);
        Assert.DoesNotContain(Permissions.VouchersApprove, executive);
        Assert.DoesNotContain(Permissions.PayoutApprove, executive);
        Assert.DoesNotContain(Permissions.PayoutPay, executive);
        Assert.DoesNotContain(Permissions.CollectionsReadAll, executive);
    }

    [Fact]
    public void LenhThuTien_TachDungQuyenKeToanLaiXeVaThuQuy()
    {
        var driver = Permissions.For([AppRoles.Driver]);
        Assert.Contains(Permissions.CollectionsSelf, driver);
        Assert.DoesNotContain(Permissions.CollectionsReadAll, driver);
        Assert.DoesNotContain(Permissions.CollectionsCreate, driver);
        Assert.DoesNotContain(Permissions.CollectionsReceive, driver);
        Assert.DoesNotContain(Permissions.CollectionsResolve, driver);

        foreach (var accountingRole in new[] { AppRoles.Accounting, AppRoles.ChiefAccountant })
        {
            var permissions = Permissions.For([accountingRole]);
            Assert.Contains(Permissions.CollectionsSelf, permissions);
            Assert.Contains(Permissions.CollectionsReadAll, permissions);
            Assert.Contains(Permissions.CollectionsCreate, permissions);
            Assert.DoesNotContain(Permissions.CollectionsReceive, permissions);
            if (accountingRole == AppRoles.ChiefAccountant)
                Assert.Contains(Permissions.CollectionsResolve, permissions);
            else
                Assert.DoesNotContain(Permissions.CollectionsResolve, permissions);
        }

        var cashier = Permissions.For([AppRoles.Cashier]);
        Assert.Contains(Permissions.CollectionsSelf, cashier);
        Assert.Contains(Permissions.CollectionsReadAll, cashier);
        Assert.Contains(Permissions.CollectionsReceive, cashier);
        Assert.DoesNotContain(Permissions.CollectionsCreate, cashier);
        Assert.DoesNotContain(Permissions.CollectionsResolve, cashier);

        foreach (var forbiddenRole in new[]
                 {
                     AppRoles.Employee, AppRoles.Admin, AppRoles.Executive, AppRoles.Payroll,
                     AppRoles.Hr, AppRoles.Manager, AppRoles.Warehouse, AppRoles.Kiosk,
                 })
        {
            var permissions = Permissions.For([forbiddenRole]);
            Assert.DoesNotContain(Permissions.CollectionsSelf, permissions);
            Assert.DoesNotContain(Permissions.CollectionsReadAll, permissions);
            Assert.DoesNotContain(Permissions.CollectionsCreate, permissions);
            Assert.DoesNotContain(Permissions.CollectionsReceive, permissions);
            Assert.DoesNotContain(Permissions.CollectionsResolve, permissions);
        }
    }

    [Fact]
    public void DaVaiTro_GopQuyenChuKhongThayThe()
    {
        var perms = Permissions.For([AppRoles.Accounting, AppRoles.Warehouse]);
        Assert.Contains(Permissions.AccountingAccess, perms); // của vai trò chính
        Assert.Contains(Permissions.TasksAssign, perms);      // của vai trò phụ
    }

    /// <summary>Vai trò rác (dữ liệu cũ, gõ tay vào DB) không được biến thành quyền nào — đóng mặc định.</summary>
    [Fact]
    public void VaiTroLa_KhongCapQuyenNao()
        => Assert.Empty(Permissions.For(["Superuser", "root", "", null]));

    [Fact]
    public void TrangDich_LuonHopVoiQuyen()
    {
        foreach (var role in AppRoles.All)
        {
            var perms = Permissions.For([role]);
            var landing = AccessProfileService.LandingPathFor(AccessProfileService.UiProfileFor(perms), perms);
            Assert.False(string.IsNullOrWhiteSpace(landing), $"Vai trò {role} không có trang đích.");
        }
    }
}

/// <summary>
/// Chốt bằng CẢ HỆ THỐNG THẬT (TestServer + PostgreSQL): quyền phải đi ra từ CSDL ở mỗi request, và
/// KHÔNG có đường nào để client tự khai quyền cho mình.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccessProfileEndpointTests
{
    private readonly ApiFactory _factory;
    public AccessProfileEndpointTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient(string token)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Profile(string Username, string[] Roles, string[] Permissions,
        string Scope, string UiProfile, string LandingPath, int AuthorizationVersion);

    [Fact]
    public async Task NhanVien_NhanDungHoSoTruyCap()
    {
        var res = await NewClient(await _factory.EmployeeTokenAsync()).GetAsync("/api/auth/access-profile");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var p = await res.Content.ReadFromJsonAsync<Profile>();

        Assert.NotNull(p);
        Assert.Equal(["Employee"], p!.Roles);
        Assert.Equal("workspace", p.UiProfile);
        Assert.Equal("self", p.Scope);
        Assert.Contains(Permissions.HrSelfAccess, p.Permissions);
        // Điều quan trọng nhất: hồ sơ KHÔNG được chứa quyền quản trị.
        Assert.DoesNotContain(Permissions.UsersManage, p.Permissions);
        Assert.DoesNotContain(Permissions.AccountingAccess, p.Permissions);
    }

    /// <summary>
    /// JWT được KÝ bởi server, nhưng nếu server tin claim quyền trong đó thì bất kỳ token cũ nào cũng
    /// mang theo quyền cũ mãi mãi. Test này dựng token có sẵn claim "perm=users.manage" và đòi hỏi nó
    /// bị BỎ QUA hoàn toàn: quyền chỉ được dựng lại từ vai trò trong CSDL ở mỗi request.
    /// </summary>
    [Fact]
    public async Task ClaimQuyenGanSanTrongToken_BiBoQua()
    {
        var token = await ForgedPermissionTokenAsync(Permissions.UsersManage, Permissions.SystemSettingsManage);
        var client = NewClient(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);

        var p = await (await client.GetAsync("/api/auth/access-profile")).Content.ReadFromJsonAsync<Profile>();
        Assert.NotNull(p);
        Assert.DoesNotContain(Permissions.UsersManage, p!.Permissions);
    }

    /// <summary>Cấp vai trò phụ ⇒ có hiệu lực NGAY ở request kế tiếp với CHÍNH token cũ (không đăng nhập lại),
    /// và phiên bản phân quyền tăng để client biết phải nạp lại giao diện.</summary>
    [Fact]
    public async Task CapVaiTroPhu_CoHieuLucNgay_VoiTokenCu()
    {
        var token = await _factory.EmployeeTokenAsync();
        var client = NewClient(token);

        var before = await (await client.GetAsync("/api/auth/access-profile")).Content.ReadFromJsonAsync<Profile>();
        Assert.DoesNotContain(Permissions.TasksAssign, before!.Permissions);

        await GrantSecondaryRoleAsync(AppRoles.Warehouse);
        var after = await (await client.GetAsync("/api/auth/access-profile")).Content.ReadFromJsonAsync<Profile>();
        Assert.Contains(Permissions.TasksAssign, after!.Permissions);
        Assert.True(after.AuthorizationVersion > before.AuthorizationVersion);

        await RevokeSecondaryRoleAsync(AppRoles.Warehouse);
        var revoked = await (await client.GetAsync("/api/auth/access-profile")).Content.ReadFromJsonAsync<Profile>();
        Assert.DoesNotContain(Permissions.TasksAssign, revoked!.Permissions);
    }

    /// <summary>Vai trò cấp TẠM hết hạn thì tự mất hiệu lực, không cần ai đi thu hồi.</summary>
    [Fact]
    public async Task VaiTroCapTam_HetHan_TuMatHieuLuc()
    {
        var client = NewClient(await _factory.EmployeeTokenAsync());
        await GrantSecondaryRoleAsync(AppRoles.Warehouse, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var p = await (await client.GetAsync("/api/auth/access-profile")).Content.ReadFromJsonAsync<Profile>();
        Assert.DoesNotContain(AppRoles.Warehouse, p!.Roles);
        Assert.DoesNotContain(Permissions.TasksAssign, p.Permissions);

        await RevokeSecondaryRoleAsync(AppRoles.Warehouse);
    }

    // ---- tiện ích ----

    /// <summary>Token hợp lệ (chữ ký thật, còn hạn) nhưng bị nhét thêm claim quyền — mô phỏng token rò rỉ
    /// hoặc client cố tình dựng token có quyền cao.</summary>
    private async Task<string> ForgedPermissionTokenAsync(params string[] perms)
    {
        await _factory.EmployeeTokenAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username = @u")
            .With("@u", _factory.EmpUser).ExecuteScalarAsync())!;

        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection("Jwt");
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(cfg["Key"]!));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, _factory.EmpUser),
            new(ClaimTypes.Role, AppRoles.Admin),   // vai trò cũng bịa luôn cho đủ bộ
            new("fullName", "Test Employee"),
        };
        claims.AddRange(perms.Select(p => new Claim(Permissions.ClaimType, p)));

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: cfg["Issuer"], audience: cfg["Audience"], claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private async Task GrantSecondaryRoleAsync(string role, DateTime? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd(
            @"INSERT INTO user_roles (username, role, granted_by, granted_at, expires_at)
              VALUES (@u, @r, 'test', CURRENT_TIMESTAMP, @exp)
              ON CONFLICT (username, role) DO UPDATE SET expires_at = EXCLUDED.expires_at")
            .With("@u", _factory.EmpUser).With("@r", role)
            .With("@exp", (object?)expiresAt ?? DBNull.Value).ExecuteNonQueryAsync();
        await conn.Cmd(
            "UPDATE app_users SET authorization_version = COALESCE(authorization_version, 1) + 1 WHERE username = @u")
            .With("@u", _factory.EmpUser).ExecuteNonQueryAsync();
    }

    private async Task RevokeSecondaryRoleAsync(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM user_roles WHERE username = @u AND role = @r")
            .With("@u", _factory.EmpUser).With("@r", role).ExecuteNonQueryAsync();
        await conn.Cmd(
            "UPDATE app_users SET authorization_version = COALESCE(authorization_version, 1) + 1 WHERE username = @u")
            .With("@u", _factory.EmpUser).ExecuteNonQueryAsync();
    }
}
