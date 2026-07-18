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
/// Kiểm thử tích hợp Trung tâm tác vụ /api/worklist — Đợt 1, nhiệm vụ 3:
///  • Tổng hợp đúng "việc cần làm" của chính người dùng: đơn chờ mình duyệt + phiếu lương chưa xác nhận.
///  • Mỗi tác vụ có khóa ổn định (không trùng) và tóm tắt số lượng theo loại.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WorklistTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string User = "__test_wl_user__";
    private Guid _employeeId;
    private Guid _requestId;
    private Guid _payslipId;

    public WorklistTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();

        // Bắt đầu từ trạng thái sạch: lần chạy trước có thể bị ngắt giữa chừng và để sót hàng cũ.
        await CleanupAsync(conn);

        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, @u, '', 'Employee', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role='Employee', approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", User).With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        _employeeId = Guid.NewGuid();
        await conn.Cmd("INSERT INTO hr_employees (id, username, full_name) VALUES (@id, @u, 'WL User')")
            .With("@id", _employeeId).With("@u", User).ExecuteNonQueryAsync();

        // Đơn chờ CHÍNH người này duyệt.
        _requestId = Guid.NewGuid();
        await conn.Cmd(
            @"INSERT INTO hr_requests (id, request_no, req_type, title, employee_id, requester_username, payload, status, current_step)
              VALUES (@id, @no, 'booking', 'Cần duyệt', @emp, 'someone_else', '{}'::jsonb, 'Pending', 1)")
            .With("@id", _requestId).With("@no", $"WL-{_requestId.ToString()[..8]}").With("@emp", _employeeId).ExecuteNonQueryAsync();
        await conn.Cmd(
            @"INSERT INTO hr_request_approvals (request_id, step_no, approver_role, approver_username, approver_name, status)
              VALUES (@id, 1, 'Manager', @u, 'WL User', 'Pending')")
            .With("@id", _requestId).With("@u", User).ExecuteNonQueryAsync();

        // Phiếu lương đã phát hành, CHƯA xác nhận.
        _payslipId = Guid.NewGuid();
        await conn.Cmd(
            @"INSERT INTO hr_payslips (id, employee_id, period, work_days, overtime_hours, base_salary, allowance, overtime_pay, deductions, net_pay, note, details, published)
              VALUES (@id, @emp, '2026-06', 26, 0, 5000000, 0, 0, 0, 5000000, '', '{}'::jsonb, TRUE)")
            .With("@id", _payslipId).With("@emp", _employeeId).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await CleanupAsync(conn);
    }

    /// <summary>
    /// Dọn theo USERNAME chứ không theo id ngẫu nhiên của lần chạy này. hr_employees.username là UNIQUE
    /// (ux_hr_employees_username), nên một lần chạy bị ngắt giữa chừng sẽ để lại hàng cũ mà lần sau
    /// không xóa được (id đã khác) → INSERT chết 23505 và test hỏng VĨNH VIỄN cho tới khi dọn tay.
    /// hr_requests/hr_payslips/hr_request_approvals đều ON DELETE CASCADE theo hr_employees(id).
    /// </summary>
    private static async Task CleanupAsync(Npgsql.NpgsqlConnection conn)
    {
        await conn.Cmd("DELETE FROM hr_employees WHERE username=@u").With("@u", User).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", User).ExecuteNonQueryAsync();
    }

    private sealed record Item(string Key, string Kind, string Title, string Description, string Priority, DateTime? DueAt, string Route);
    private sealed record Summary(int Total, int Approvals, int Payslips, int Documents, int Contracts, int Notices, int Overdue);
    private sealed record Result(List<Item> Items, Summary Summary);

    [Fact]
    public async Task Worklist_AggregatesApprovalsAndPayslips_ForCurrentUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", User).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, User, User, "", "Employee", true, "Approved", DateTime.UtcNow));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = await client.GetFromJsonAsync<Result>("/api/worklist");

        Assert.NotNull(result);
        Assert.True(result!.Summary.Approvals >= 1, "Thiếu tác vụ đơn chờ duyệt.");
        Assert.True(result.Summary.Payslips >= 1, "Thiếu tác vụ phiếu lương chưa xác nhận.");
        Assert.Contains(result.Items, i => i.Key == $"approval:{_requestId}");
        Assert.Contains(result.Items, i => i.Key == $"payslip:{_payslipId}");
        // Khóa ổn định → không trùng lặp.
        Assert.Equal(result.Items.Select(i => i.Key).Distinct().Count(), result.Items.Count);
    }
}
