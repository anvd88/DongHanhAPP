using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Nhật ký hoạt động: nhật ký vốn CHỈ admin xem được. Từ 2026-07-17 phòng kế toán được tra cứu thêm
/// phần tiền (phiếu chi/hoàn phạt/lệnh thu tiền) để đối chiếu thu chi. Bộ test này chốt rằng cửa nới ra đó
/// KHÔNG rộng hơn dự tính: kế toán không thể lách qua tham số group/entity để đọc nhật ký khác.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditScopeTests
{
    private readonly ApiFactory _factory;
    public AuditScopeTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Cashier_SeesOnlyMoneyEntries_AndCannotWidenScopeViaParameters()
    {
        var world = await SetupAsync();
        try
        {
            var cashier = Client(world.CashierToken);

            // Không truyền lọc gì: vẫn chỉ được phần tiền.
            var all = await ItemsAsync(cashier, "/api/audit?pageSize=200");
            Assert.NotEmpty(all);
            Assert.All(all, e => Assert.Contains(e.GetProperty("entity").GetString(),
                new[] { "PayoutVoucher", "PenaltyRefund", "CashCollection" }));

            // Cố lọc sang nhật ký đăng nhập bằng entity → không được dòng nào (không phải "bỏ qua bộ lọc").
            Assert.Empty(await ItemsAsync(cashier, "/api/audit?entity=Auth&pageSize=200"));
            // Cố lọc bằng nhóm nghiệp vụ khác → cũng rỗng.
            Assert.Empty(await ItemsAsync(cashier, "/api/audit?group=auth&pageSize=200"));
            Assert.Empty(await ItemsAsync(cashier, "/api/audit?group=attendance&pageSize=200"));
            // Tìm kiếm tự do không được kéo dòng ngoài phạm vi về.
            Assert.All(await ItemsAsync(cashier, "/api/audit?search=" + world.AuthMarker + "&pageSize=200"),
                e => Assert.Contains(e.GetProperty("entity").GetString(), new[] { "PayoutVoucher", "PenaltyRefund", "CashCollection" }));

            // Gợi ý lọc cũng không lộ đối tượng ngoài phạm vi.
            var filters = await (await cashier.GetAsync("/api/audit/filters")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(filters.GetProperty("canSeeAll").GetBoolean());
            Assert.All(filters.GetProperty("entities").EnumerateArray(),
                e => Assert.Contains(e.GetString(), new[] { "PayoutVoucher", "PenaltyRefund", "CashCollection" }));

            // Xuất tệp phải chịu đúng phạm vi: không được lách bằng /export.
            var export = await cashier.GetAsync("/api/audit/export?format=csv&group=auth");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            var csv = await export.Content.ReadAsStringAsync();
            Assert.DoesNotContain(world.AuthMarker, csv, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task Admin_SeesEverything_ButPlainEmployeeIsRefused()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            var adminItems = await ItemsAsync(admin, "/api/audit?pageSize=200&search=" + world.AuthMarker);
            Assert.Contains(adminItems, e => e.GetProperty("entity").GetString() == "Auth");

            var filters = await (await admin.GetAsync("/api/audit/filters")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(filters.GetProperty("canSeeAll").GetBoolean());

            // Nhân viên thường: nhật ký vẫn đóng hoàn toàn.
            Assert.Equal(HttpStatusCode.Forbidden, (await Client(world.WorkerToken).GetAsync("/api/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(world.WorkerToken).GetAsync("/api/audit/filters")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(world.WorkerToken).GetAsync("/api/audit/export?format=csv")).StatusCode);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task MonthFilter_KeepsOnlyThatMonth_AndIsOfferedFromRealData()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);

            // Tháng của bản ghi cũ (đã chèn lúc setup) chỉ trả về đúng bản ghi đó.
            var oldMonth = world.OldEntryMonth;
            var items = await ItemsAsync(admin, $"/api/audit?month={oldMonth}&pageSize=200&search={world.OldMarker}");
            Assert.NotEmpty(items);
            Assert.All(items, e => Assert.StartsWith(oldMonth,
                e.GetProperty("occurredAt").GetDateTime().ToString("yyyy-MM"), StringComparison.Ordinal));

            // Tháng khác thì không thấy bản ghi đó nữa.
            Assert.Empty(await ItemsAsync(admin, $"/api/audit?month={world.OtherMonth}&pageSize=200&search={world.OldMarker}"));

            // Tháng không hợp lệ bị bỏ qua (coi như không lọc) chứ không làm hỏng request.
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit?month=abc")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit?month=2026-13")).StatusCode);

            // Danh sách tháng gợi ý lấy từ dữ liệu thật → phải có tháng của bản ghi vừa chèn.
            var filters = await (await admin.GetAsync("/api/audit/filters")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(filters.GetProperty("months").EnumerateArray(), m => m.GetString() == oldMonth);
            Assert.Contains(filters.GetProperty("groups").EnumerateArray(),
                gr => gr.GetProperty("key").GetString() == "payout");
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    // ---------------- Dựng & dọn dữ liệu test ----------------

    private sealed record World(string CashierToken, string AdminToken, string WorkerToken,
        string AuthMarker, string OldMarker, string OldEntryMonth, string OtherMonth,
        Guid AccountingDeptId, Guid OtherDeptId, string[] Usernames);

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var accountingDeptId = Guid.NewGuid();
        var otherDeptId = Guid.NewGuid();
        var cashierUser = $"__au_cashier_{suffix}";
        var adminUser = $"__au_admin_{suffix}";
        var workerUser = $"__au_worker_{suffix}";
        var authMarker = $"__au_authmark_{suffix}";
        var oldMarker = $"__au_oldmark_{suffix}";

        // Bản ghi "cũ" đặt ở tháng trước để test lọc tháng không phụ thuộc thời điểm chạy test.
        var oldAt = DateTime.Now.AddMonths(-1);
        var oldMonth = oldAt.ToString("yyyy-MM");
        var otherMonth = DateTime.Now.AddMonths(-2).ToString("yyyy-MM");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        await conn.Cmd("INSERT INTO hr_departments (id, code, name, is_accounting) VALUES (@id, @c, @n, TRUE)")
            .With("@id", accountingDeptId).With("@c", "AKT" + suffix[..4]).With("@n", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_departments (id, code, name, is_accounting) VALUES (@id, @c, @n, FALSE)")
            .With("@id", otherDeptId).With("@c", "AKH" + suffix[..4]).With("@n", "Kho " + suffix)
            .ExecuteNonQueryAsync();

        var cashierToken = await MakeUserAsync(conn, tokens, cashierUser, AppRoles.Accounting, accountingDeptId, suffix);
        var adminToken = await MakeUserAsync(conn, tokens, adminUser, AppRoles.Admin, accountingDeptId, suffix);
        var workerToken = await MakeUserAsync(conn, tokens, workerUser, AppRoles.Employee, otherDeptId, suffix);

        // Nhật ký mẫu: hai dòng tiền (kế toán được xem) + một dòng đăng nhập (kế toán KHÔNG được xem).
        await AddLogAsync(conn, DateTime.Now, cashierUser, "Lập phiếu chi", "PayoutVoucher", "PC90001", "Chi tiền mặt test");
        await AddLogAsync(conn, oldAt, cashierUser, "Duyệt hoàn tiền phạt", "PenaltyRefund", "HP90001", oldMarker);
        await AddLogAsync(conn, DateTime.Now, workerUser, "Đăng nhập", "Auth", workerUser, authMarker);

        return new World(cashierToken, adminToken, workerToken, authMarker, oldMarker, oldMonth, otherMonth,
            accountingDeptId, otherDeptId, [cashierUser, adminUser, workerUser]);
    }

    private static Task AddLogAsync(Npgsql.NpgsqlConnection conn, DateTime at, string username,
        string action, string entity, string entityName, string details)
        => conn.Cmd("""
            INSERT INTO audit_logs (occurred_at, username, action, entity, entity_name, details)
            VALUES (@at, @u, @a, @e, @en, @d)
            """)
            .With("@at", at).With("@u", username).With("@a", action).With("@e", entity)
            .With("@en", entityName).With("@d", details).ExecuteNonQueryAsync();

    private static async Task<string> MakeUserAsync(Npgsql.NpgsqlConnection conn, TokenService tokens,
        string username, string role, Guid departmentId, string suffix)
    {
        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @u, '', @r, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", userId).With("@u", username).With("@r", role)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id, employee_code, user_id, username, full_name, department_id, status)
            VALUES (@id, @code, @uid, @u, @u, @dept, 'Active')
            """)
            .With("@id", Guid.NewGuid()).With("@code", "A" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId).ExecuteNonQueryAsync();
        return tokens.CreateToken(
            new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:au:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task CleanupAsync(World world)
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("DELETE FROM audit_logs WHERE username = ANY(@u)").With("@u", world.Usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM hr_employees WHERE username = ANY(@u)").With("@u", world.Usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username = ANY(@u)").With("@u", world.Usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)").With("@u", world.Usernames)
                .ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM hr_departments WHERE id = ANY(@d)")
                .With("@d", new[] { world.AccountingDeptId, world.OtherDeptId }).ExecuteNonQueryAsync();
        }
        catch { /* dọn dẹp best-effort */ }
    }

    private static async Task<List<JsonElement>> ItemsAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray().ToList();
    }

    private HttpClient Client(string token)
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }
}
