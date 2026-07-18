using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp Danh bạ &amp; sơ đồ tổ chức /api/directory — Đợt 2, nhiệm vụ 6:
///  • Tìm kiếm không dấu.
///  • Phân quyền liên hệ: quản lý xem SĐT nhân viên mình, nhưng KHÔNG xem SĐT người ngoài nhóm.
///  • Sơ đồ tổ chức theo manager_id (quản lý chứa nhân viên trong Reports).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DirectoryTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string Mgr = "__test_dir_mgr__";
    private const string Sub = "__test_dir_sub__";
    private const string Other = "__test_dir_other__";
    private Guid _mgrId, _subId, _otherId;

    public DirectoryTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        // Bắt đầu từ trạng thái sạch: lần chạy trước có thể bị ngắt giữa chừng và để sót hàng cũ.
        await CleanupAsync(conn);
        foreach (var u in new[] { Mgr, Sub, Other }) await Upsert(conn, u);

        _mgrId = Guid.NewGuid(); _subId = Guid.NewGuid(); _otherId = Guid.NewGuid();
        await Emp(conn, _mgrId, Mgr, "Nguyễn Quản Lý DIRTEST", "0900000001", null);
        await Emp(conn, _subId, Sub, "Trần Nhân Viên DIRTEST", "0900000002", _mgrId);
        await Emp(conn, _otherId, Other, "Lê Người Khác DIRTEST", "0900000003", null);
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await CleanupAsync(conn);
    }

    /// <summary>
    /// Dọn theo USERNAME chứ không theo id ngẫu nhiên của lần chạy này: hr_employees.username là UNIQUE,
    /// nên hàng sót lại từ lần chạy bị ngắt sẽ làm INSERT lần sau chết 23505 vĩnh viễn.
    /// Xóa nhân viên cấp dưới trước để manager_id (REFERENCES hr_employees ON DELETE SET NULL) không cản.
    /// </summary>
    private static async Task CleanupAsync(Npgsql.NpgsqlConnection conn)
    {
        await conn.Cmd("DELETE FROM hr_employees WHERE username = ANY(@u)")
            .With("@u", new[] { Sub, Other, Mgr }).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)")
            .With("@u", new[] { Mgr, Sub, Other }).ExecuteNonQueryAsync();
    }

    private static async Task Upsert(Npgsql.NpgsqlConnection conn, string username) =>
        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, @u, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role='Employee', approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", username).With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

    private static async Task Emp(Npgsql.NpgsqlConnection conn, Guid id, string username, string fullName, string phone, Guid? managerId) =>
        await conn.Cmd(
            @"INSERT INTO hr_employees (id, username, full_name, phone, manager_id, status)
              VALUES (@id, @u, @n, @p, @m, 'Active')")
            .With("@id", id).With("@u", username).With("@n", fullName).With("@p", phone)
            .With("@m", (object?)managerId ?? DBNull.Value).ExecuteNonQueryAsync();

    private async Task<HttpClient> ClientAsAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", username).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, username, username, "", "Employee", true, "Approved", DateTime.UtcNow));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record DirItem(Guid Id, string FullName, string Position, Guid? ManagerId, string? Phone, bool CanSeeContact, bool Online);
    private sealed record OrgNode(Guid Id, string FullName, string Position, string? DepartmentName, List<OrgNode> Reports);

    [Fact]
    public async Task Directory_NonAccentSearch_And_ContactPermission()
    {
        var mgr = await ClientAsAsync(Mgr); // vai trò Employee (không phải Admin/HR)

        // Tìm không dấu "dirtest" khớp tên có dấu "DIRTEST".
        var items = await mgr.GetFromJsonAsync<List<DirItem>>("/api/directory?search=dirtest");
        Assert.NotNull(items);

        var sub = Assert.Single(items!, i => i.Id == _subId);
        var other = Assert.Single(items!, i => i.Id == _otherId);

        // Quản lý XEM được SĐT của nhân viên mình…
        Assert.True(sub.CanSeeContact);
        Assert.Equal("0900000002", sub.Phone);
        // …nhưng KHÔNG xem được SĐT người ngoài nhóm.
        Assert.False(other.CanSeeContact);
        Assert.Null(other.Phone);
    }

    [Fact]
    public async Task OrgChart_NestsSubordinateUnderManager()
    {
        var mgr = await ClientAsAsync(Mgr);
        var roots = await mgr.GetFromJsonAsync<List<OrgNode>>("/api/directory/org-chart");
        Assert.NotNull(roots);

        // Nút quản lý là gốc (không có quản lý) và chứa nhân viên trong Reports.
        var mgrNode = Assert.Single(roots!, n => n.Id == _mgrId);
        Assert.Contains(mgrNode.Reports, n => n.Id == _subId);
    }
}
