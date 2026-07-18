using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp cho luồng PHÊ DUYỆT ĐƠN (RequestEndpoints.Decide) — Đợt 1, nhiệm vụ 1:
///  • Từ chối BẮT BUỘC nêu lý do → thiếu lý do trả 400.
///  • Sai người duyệt → 403 (không lộ đơn cho người ngoài luồng duyệt).
///  • Hai thiết bị cùng duyệt MỘT đơn → đúng một 204, phần còn lại 409 (chống xử lý trùng).
/// Chạy full app qua TestServer + PostgreSQL thật (giống SecurityTests). Mỗi test tự seed và dọn dữ liệu.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RequestApprovalTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;

    // Tài khoản dùng riêng cho bộ test này (dọn sạch ở DisposeAsync).
    private const string Approver = "__test_approver__";
    private const string Requester = "__test_requester__";
    private const string Outsider = "__test_outsider__";

    private readonly List<Guid> _requestIds = new();
    private Guid _employeeId;

    public RequestApprovalTests(ApiFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ---------- Seed / dọn dẹp ----------

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();

        // Bắt đầu từ trạng thái sạch: lần chạy trước có thể bị ngắt giữa chừng và để sót hàng cũ.
        await CleanupAsync(conn);

        foreach (var u in new[] { Approver, Requester, Outsider })
            await conn.Cmd(
                @"INSERT INTO app_users
                     (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
                  VALUES
                     (@id, @u, @u, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
                  ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role='Employee', approval_status='Approved'")
                .With("@id", Guid.NewGuid()).With("@u", u).With("@ph", PasswordHasher.Hash("test-pass"))
                .ExecuteNonQueryAsync();

        // Một hồ sơ nhân sự để hr_requests.employee_id tham chiếu (chỉ id là bắt buộc, còn lại có mặc định).
        _employeeId = Guid.NewGuid();
        await conn.Cmd("INSERT INTO hr_employees (id, username, full_name) VALUES (@id, @u, 'Test Requester')")
            .With("@id", _employeeId).With("@u", Requester).ExecuteNonQueryAsync();
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
    /// hr_requests ON DELETE CASCADE theo hr_employees(id) nên xóa nhân viên là kéo theo đơn từ.
    /// </summary>
    private static async Task CleanupAsync(Npgsql.NpgsqlConnection conn)
    {
        await conn.Cmd("DELETE FROM hr_employees WHERE username = ANY(@u)")
            .With("@u", new[] { Approver, Requester, Outsider }).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username = ANY(@u)")
            .With("@u", new[] { Approver, Requester, Outsider }).ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Tạo một đơn loại "booking" (KHÔNG có tác động phụ khi duyệt) với đúng MỘT bước chờ duyệt
    /// giao cho <paramref name="approverUsername"/>. Trả về id đơn.
    /// </summary>
    private async Task<Guid> SeedPendingRequestAsync(string approverUsername)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = Guid.NewGuid();
        await conn.Cmd(
            @"INSERT INTO hr_requests (id, request_no, req_type, title, employee_id, requester_username, payload, status, current_step)
              VALUES (@id, @no, 'booking', 'Test booking', @emp, @req, '{}'::jsonb, 'Pending', 1)")
            .With("@id", id).With("@no", $"TEST-{id.ToString()[..8]}")
            .With("@emp", _employeeId).With("@req", Requester).ExecuteNonQueryAsync();
        await conn.Cmd(
            @"INSERT INTO hr_request_approvals (request_id, step_no, approver_role, approver_username, approver_name, status)
              VALUES (@id, 1, 'Manager', @u, 'Approver', 'Pending')")
            .With("@id", id).With("@u", approverUsername).ExecuteNonQueryAsync();
        _requestIds.Add(id);
        return id;
    }

    private async Task<string> TokenForAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", username).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        return tokens.CreateToken(new UserDto(id, username, username, "", "Employee", true, "Approved", DateTime.UtcNow));
    }

    private async Task<HttpClient> ClientAsAsync(string username)
    {
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenForAsync(username));
        return client;
    }

    // ---------- Test ----------

    [Fact]
    public async Task Reject_WithoutReason_Returns400()
    {
        var id = await SeedPendingRequestAsync(Approver);
        var client = await ClientAsAsync(Approver);
        var res = await client.PostAsJsonAsync($"/api/requests/{id}/reject", new { comment = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reject_WithReason_Succeeds()
    {
        var id = await SeedPendingRequestAsync(Approver);
        var client = await ClientAsAsync(Approver);
        var res = await client.PostAsJsonAsync($"/api/requests/{id}/reject", new { comment = "Không hợp lệ" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Approve_WrongApprover_Returns403()
    {
        var id = await SeedPendingRequestAsync(Approver);
        var client = await ClientAsAsync(Outsider);
        var res = await client.PostAsJsonAsync($"/api/requests/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Approve_TwoDevicesConcurrently_ProcessesExactlyOnce()
    {
        var id = await SeedPendingRequestAsync(Approver);
        var token = await TokenForAsync(Approver);

        // Năm "thiết bị" của cùng người duyệt cùng bấm Duyệt gần như đồng thời.
        async Task<HttpStatusCode> ApproveOnce()
        {
            var c = NewClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var r = await c.PostAsJsonAsync($"/api/requests/{id}/approve", new { });
            return r.StatusCode;
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ApproveOnce()));

        // ĐÚNG MỘT lần được xử lý (204) — không có xử lý trùng.
        Assert.Equal(1, results.Count(s => s == HttpStatusCode.NoContent));
        // Mọi lần còn lại đều bị chặn: 409 (thua "giành chỗ" ở bước duyệt) hoặc 400 (đọc đơn sau khi
        // người thắng đã chốt → không còn ở trạng thái chờ duyệt). Cả hai đều nghĩa là "đã có người xử lý".
        Assert.All(results.Where(s => s != HttpStatusCode.NoContent),
            s => Assert.True(s is HttpStatusCode.Conflict or HttpStatusCode.BadRequest,
                $"Lần duyệt trùng phải bị chặn bằng 409/400 nhưng nhận {(int)s}."));
    }
}
