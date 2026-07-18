using System.Net.Http.Headers;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp xuất lịch iCalendar /api/schedule/ical — Đợt 3, nhiệm vụ 8:
///  • Trả tệp text/calendar hợp lệ chứa ca làm của người dùng.
///  • Đổi giờ địa phương (VN, +07) sang UTC đúng trong DTSTART.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ScheduleTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private const string User = "__test_sched_user__";
    private Guid _empId, _shiftId, _assignId;

    public ScheduleTests(ApiFactory factory) => _factory = factory;

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
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE")
            .With("@id", Guid.NewGuid()).With("@u", User).With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        _empId = Guid.NewGuid(); _shiftId = Guid.NewGuid(); _assignId = Guid.NewGuid();
        await conn.Cmd("INSERT INTO hr_employees (id, username, full_name) VALUES (@id, @u, 'Sched User')")
            .With("@id", _empId).With("@u", User).ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_shifts (id, code, name, start_time, end_time) VALUES (@id, 'S', 'Ca Sáng TEST', @st, @et)")
            .With("@id", _shiftId).With("@st", new TimeSpan(8, 0, 0)).With("@et", new TimeSpan(17, 0, 0)).ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_shift_assignments (id, employee_id, shift_id, work_date) VALUES (@id, @emp, @sh, @d)")
            .With("@id", _assignId).With("@emp", _empId).With("@sh", _shiftId).With("@d", new DateOnly(2026, 8, 15)).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await CleanupAsync(conn);
    }

    /// <summary>
    /// Dọn theo USERNAME/tên ca chứ không theo id ngẫu nhiên của lần chạy này: hr_employees.username là
    /// UNIQUE, nên hàng sót lại từ lần chạy bị ngắt làm INSERT lần sau chết 23505 vĩnh viễn. Ca làm không
    /// có ràng buộc UNIQUE nên chỉ bị dồn rác — vẫn dọn theo tên để DB test không phình theo mỗi lần chạy.
    /// hr_shift_assignments ON DELETE CASCADE theo cả hr_employees(id) lẫn hr_shifts(id).
    /// </summary>
    private static async Task CleanupAsync(Npgsql.NpgsqlConnection conn)
    {
        await conn.Cmd("DELETE FROM hr_employees WHERE username=@u").With("@u", User).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM hr_shifts WHERE name='Ca Sáng TEST'").ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", User).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Ical_ExportsShift_WithUtcConversion()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", User).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, User, User, "", "Employee", true, "Approved", DateTime.UtcNow));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.GetAsync("/api/schedule/ical?from=2026-08-01&to=2026-08-31");
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/calendar", res.Content.Headers.ContentType?.MediaType);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VCALENDAR", body);
        Assert.Contains("Ca Sáng TEST", body);
        // 08:00 giờ VN (UTC+7) → 01:00 UTC ngày 15/08.
        Assert.Contains("DTSTART:20260815T010000Z", body);
    }
}
