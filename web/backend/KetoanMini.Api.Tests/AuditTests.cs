using System.Net;
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
/// Kiểm thử tích hợp cho Nhật ký hệ thống /api/audit — Đợt 1, nhiệm vụ 2:
///  • Chỉ Admin xem được (Employee → 403).
///  • Trả envelope phân trang { items, total, page, pageSize } + lọc theo đối tượng.
///  • Che dữ liệu nhạy cảm trong trường trước/sau (password → ***).
///  • Xuất CSV áp dụng bộ lọc.
/// Việc test chạy được cũng xác nhận /api/audit KHÔNG bị trùng route lúc khởi động app.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string Admin = "__test_audit_admin__";
    private const string Emp = "__test_audit_emp__";
    private const string Marker = "__TestAudit__"; // entity đánh dấu để lọc + dọn chính xác.

    public AuditTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();

        await Upsert(conn, Admin, "Admin");
        await Upsert(conn, Emp, "Employee");

        // 3 dòng nhật ký test: 2 dòng thường + 1 dòng có dữ liệu nhạy cảm ở before/after.
        await Insert(conn, "Tạo phiếu", "Chi tiết A", null, null);
        await Insert(conn, "Sửa phiếu", "Chi tiết B", null, null);
        await Insert(conn, "Đổi lương", "Điều chỉnh lương",
            """{"password":"supersecret-xyz","luongCung":5000000}""",
            """{"password":"newsecret-abc","luongCung":8000000}""");
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM audit_logs WHERE entity = @m").With("@m", Marker).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)")
            .With("@u", new[] { Admin, Emp }).ExecuteNonQueryAsync();
    }

    private static async Task Upsert(Npgsql.NpgsqlConnection conn, string username, string role) =>
        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES
                 (@id, @u, @u, '', @role, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role=@role, approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", username).With("@role", role)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

    private async Task Insert(Npgsql.NpgsqlConnection conn, string action, string details, string? before, string? after) =>
        await conn.Cmd(
            @"INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details, before_data, after_data)
              VALUES (CURRENT_TIMESTAMP, @u, @a, @e, 'muc-test', @d, @before::jsonb, @after::jsonb)")
            .With("@u", Admin).With("@a", action).With("@e", Marker).With("@d", details)
            .With("@before", (object?)before ?? DBNull.Value).With("@after", (object?)after ?? DBNull.Value)
            .ExecuteNonQueryAsync();

    private async Task<HttpClient> ClientAsAsync(string username, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", username).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, username, username, "", role, true, "Approved", DateTime.UtcNow));
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record AuditItem(long Id, DateTime OccurredAt, string Username, string Action,
        string Entity, string EntityName, string Details, string? Before, string? After);
    private sealed record AuditPage(List<AuditItem> Items, long Total, int Page, int PageSize);

    [Fact]
    public async Task Audit_NonAdmin_Returns403()
    {
        var client = await ClientAsAsync(Emp, "Employee");
        var res = await client.GetAsync("/api/audit");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Audit_Admin_ReturnsPagedEnvelope_AndEntityFilter()
    {
        var client = await ClientAsAsync(Admin, "Admin");
        var page = await client.GetFromJsonAsync<AuditPage>($"/api/audit?entity={Marker}&pageSize=2&page=1");

        Assert.NotNull(page);
        Assert.Equal(1, page!.Page);
        Assert.Equal(2, page.PageSize);
        Assert.True(page.Total >= 3, $"Kỳ vọng >=3 dòng đánh dấu, thực tế {page.Total}.");
        Assert.Equal(2, page.Items.Count);                       // trang chỉ có pageSize dòng
        Assert.All(page.Items, i => Assert.Equal(Marker, i.Entity)); // đúng bộ lọc đối tượng
    }

    [Fact]
    public async Task Audit_MasksSensitiveBeforeAfter()
    {
        var client = await ClientAsAsync(Admin, "Admin");
        var page = await client.GetFromJsonAsync<AuditPage>($"/api/audit?entity={Marker}&search=Đổi lương&pageSize=50");

        var row = Assert.Single(page!.Items, i => i.Action == "Đổi lương");
        // Bí mật bị che, nhưng dữ liệu nghiệp vụ (lương) vẫn còn để kiểm tra.
        Assert.Contains("***", row.Before);
        Assert.DoesNotContain("supersecret-xyz", row.Before);
        Assert.DoesNotContain("newsecret-abc", row.After);
        Assert.Contains("5000000", row.Before);
        Assert.Contains("8000000", row.After);
    }

    [Fact]
    public async Task Audit_ExportCsv_ReturnsCsvFile()
    {
        var client = await ClientAsAsync(Admin, "Admin");
        var res = await client.GetAsync($"/api/audit/export?format=csv&entity={Marker}");
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Thời gian", body);      // dòng tiêu đề
        Assert.Contains("Đổi lương", body);       // dữ liệu đã lọc
        Assert.DoesNotContain("supersecret-xyz", body); // vẫn che bí mật khi xuất
    }
}
