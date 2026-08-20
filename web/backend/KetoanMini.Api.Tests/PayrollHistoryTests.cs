using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Lịch sử phiếu lương là sổ sự kiện bất biến: bản nháp cũng phải có dấu vết, mỗi lần đổi trạng thái
/// tăng đúng một revision, và nhân viên xác nhận phiếu cũng là một sự kiện nghiệp vụ.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PayrollHistoryTests(ApiFactory factory)
{
    [Fact]
    public async Task Draft_Publish_AndAcknowledge_AreVersionedAndVisibleInTimeline()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            var employee = Client(world.EmployeeToken);

            var draft = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = false,
                adjustments = Array.Empty<object>(),
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Bản nháp đầu tiên",
            });
            Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
            var payslipId = (await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            // Nhân viên chỉ xem phiếu đã phát hành của chính mình qua self API; không được đọc audit/snapshot nháp.
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(
                $"/api/payroll/payslips/history?employeeId={world.EmployeeId}&period={world.Period}")).StatusCode);
            var employeeDraftView = await (await employee.GetAsync("/api/payroll/my-payslips"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(employeeDraftView.EnumerateArray());

            var afterDraft = await ReadHistory(admin, world);
            Assert.Equal("Draft", afterDraft.GetProperty("payslip").GetProperty("status").GetString());
            var draftEvents = afterDraft.GetProperty("history").EnumerateArray().ToArray();
            Assert.Single(draftEvents);
            Assert.Equal("DraftCreated", draftEvents[0].GetProperty("action").GetString());
            Assert.Equal(1, draftEvents[0].GetProperty("revision").GetInt32());
            Assert.Equal("Draft", draftEvents[0].GetProperty("statusAfter").GetString());
            Assert.Equal(2_000_000m, draftEvents[0].GetProperty("snapshot").GetProperty("netPay").GetDecimal());

            var publish = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = true,
                adjustments = Array.Empty<object>(),
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Phát hành chính thức",
            });
            Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
            Assert.Equal(payslipId, (await publish.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

            var afterPublish = await ReadHistory(admin, world);
            Assert.Equal("Published", afterPublish.GetProperty("payslip").GetProperty("status").GetString());
            var employeePublishedView = await (await employee.GetAsync("/api/payroll/my-payslips"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(employeePublishedView.EnumerateArray(), p => p.GetProperty("id").GetGuid() == payslipId);
            var publishEvents = afterPublish.GetProperty("history").EnumerateArray().ToArray();
            Assert.Equal(2, publishEvents.Length);
            Assert.Equal("Published", publishEvents[0].GetProperty("action").GetString());
            Assert.Equal("Draft", publishEvents[0].GetProperty("statusBefore").GetString());
            Assert.Equal("Published", publishEvents[0].GetProperty("statusAfter").GetString());
            Assert.Equal(2, publishEvents[0].GetProperty("revision").GetInt32());

            var acknowledged = await employee.PostAsync($"/api/payroll/my-payslips/{payslipId}/ack", null);
            Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);

            var final = await ReadHistory(admin, world);
            Assert.Equal("Acknowledged", final.GetProperty("payslip").GetProperty("status").GetString());
            var finalEvents = final.GetProperty("history").EnumerateArray().ToArray();
            Assert.Equal(3, finalEvents.Length);
            Assert.Equal("Acknowledged", finalEvents[0].GetProperty("action").GetString());
            Assert.Equal(world.EmployeeUsername, finalEvents[0].GetProperty("actor").GetString());
            Assert.Equal(3, finalEvents[0].GetProperty("revision").GetInt32());

            // Xác nhận lặp là idempotent: không bịa thêm một lần xác nhận.
            Assert.Equal(HttpStatusCode.NoContent,
                (await employee.PostAsync($"/api/payroll/my-payslips/{payslipId}/ack", null)).StatusCode);
            Assert.Equal(3, (await ReadHistory(admin, world)).GetProperty("history").GetArrayLength());
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task LegacyEmployeePayslipEndpoint_TracksPublicationTimestampForRequirementGate()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            var employee = Client(world.EmployeeToken);
            var published = await admin.PostAsJsonAsync($"/api/hr/employees/{world.EmployeeId}/payslips", new
            {
                period = world.Period,
                workDays = 22,
                overtimeHours = 0,
                baseSalary = 2_000_000,
                allowance = 0,
                overtimePay = 0,
                deductions = 0,
                note = "Phát hành từ màn nhân sự cũ",
                published = true,
            });
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);

            using var scope = factory.Services.CreateScope();
            await using var conn = await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
            var publishedAt = await conn.Cmd(
                    "SELECT published_at FROM hr_payslips WHERE employee_id=@emp AND period=@period")
                .With("@emp", world.EmployeeId).With("@period", world.Period).ExecuteScalarAsync();
            Assert.NotNull(publishedAt);

            var requirement = await employee.GetFromJsonAsync<JsonElement>("/api/payroll/my-payslips/requirement");
            Assert.Equal(1, requirement.GetProperty("pendingCount").GetInt32());

            var unpublished = await admin.PostAsJsonAsync($"/api/hr/employees/{world.EmployeeId}/payslips", new
            {
                period = world.Period,
                workDays = 22,
                overtimeHours = 0,
                baseSalary = 2_000_000,
                allowance = 0,
                overtimePay = 0,
                deductions = 0,
                note = "Đưa về nháp",
                published = false,
            });
            Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
            Assert.IsType<DBNull>(await conn.Cmd(
                    "SELECT published_at FROM hr_payslips WHERE employee_id=@emp AND period=@period")
                .With("@emp", world.EmployeeId).With("@period", world.Period).ExecuteScalarAsync());
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task PayslipAcknowledgement_RejectsAStaleDisplayedRevision()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            var employee = Client(world.EmployeeToken);
            var created = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = true,
                adjustments = Array.Empty<object>(),
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Bản nhân viên đang xem",
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var firstList = await employee.GetFromJsonAsync<JsonElement>("/api/payroll/my-payslips");
            var first = Assert.Single(firstList.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
            var staleRevision = first.GetProperty("revisionToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(staleRevision));

            var edited = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = true,
                adjustments = new[] { new { label = "Điều chỉnh sau khi mở phiếu", amount = 300_000, kind = "earning" } },
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Số liệu đã thay đổi",
            });
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

            var staleAck = await employee.PostAsync(
                $"/api/payroll/my-payslips/{id}/ack?expectedRevision={Uri.EscapeDataString(staleRevision!)}", null);
            Assert.Equal(HttpStatusCode.Conflict, staleAck.StatusCode);

            var refreshedList = await employee.GetFromJsonAsync<JsonElement>("/api/payroll/my-payslips");
            var refreshed = Assert.Single(refreshedList.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
            var refreshedRevision = refreshed.GetProperty("revisionToken").GetString();
            Assert.NotEqual(staleRevision, refreshedRevision);
            var currentAck = await employee.PostAsync(
                $"/api/payroll/my-payslips/{id}/ack?expectedRevision={Uri.EscapeDataString(refreshedRevision!)}", null);
            Assert.Equal(HttpStatusCode.NoContent, currentAck.StatusCode);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task HistoryRows_CannotBeUpdatedOrDeleted()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = false,
                adjustments = Array.Empty<object>(),
                approvedOvertimeDates = Array.Empty<string>(),
            })).StatusCode);

            using var scope = factory.Services.CreateScope();
            await using var conn = await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
            var historyId = (Guid)(await conn.Cmd("SELECT id FROM hr_payslip_history WHERE employee_id=@emp AND period=@period")
                .With("@emp", world.EmployeeId).With("@period", world.Period).ExecuteScalarAsync())!;

            var error = await Assert.ThrowsAsync<PostgresException>(() => conn.Cmd(
                    "UPDATE hr_payslip_history SET actor='tampered' WHERE id=@id")
                .With("@id", historyId).ExecuteNonQueryAsync());
            Assert.Equal("P0001", error.SqlState);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task PublishedPayslip_CanBeDeletedAndReissued_WithoutLosingHistory()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            var employee = Client(world.EmployeeToken);
            var created = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = true,
                adjustments = Array.Empty<object>(),
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Phiếu trước điều chỉnh",
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var oldId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            // Màn tổng hợp tháng nhìn thấy phiếu đã phát hành; nhân viên thường không được đọc sổ toàn công ty.
            var monthBefore = await admin.GetFromJsonAsync<JsonElement>(
                $"/api/payroll/payslips/published?period={world.Period}&page=1&pageSize=50");
            Assert.Contains(monthBefore.GetProperty("items").EnumerateArray(),
                x => x.GetProperty("id").GetGuid() == oldId);
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(
                $"/api/payroll/payslips/published?period={world.Period}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.DeleteAsync(
                $"/api/hr/payslips/{oldId}")).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(
                $"/api/hr/payslips/{oldId}")).StatusCode);

            var afterDelete = await ReadHistory(admin, world);
            Assert.False(afterDelete.TryGetProperty("payslip", out var deletedCurrent)
                         && deletedCurrent.ValueKind != JsonValueKind.Null);
            var deletedEvents = afterDelete.GetProperty("history").EnumerateArray().ToArray();
            Assert.Equal(2, deletedEvents.Length);
            Assert.Equal("Deleted", deletedEvents[0].GetProperty("action").GetString());
            Assert.Equal("Published", deletedEvents[0].GetProperty("statusBefore").GetString());
            Assert.Equal("Deleted", deletedEvents[0].GetProperty("statusAfter").GetString());
            Assert.Equal(2, deletedEvents[0].GetProperty("revision").GetInt32());
            Assert.DoesNotContain((await (await employee.GetAsync("/api/payroll/my-payslips"))
                .Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(),
                x => x.GetProperty("id").GetGuid() == oldId);

            var reissued = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = true,
                adjustments = new[] { new { label = "Điều chỉnh truy lĩnh", amount = 500_000, kind = "earning" } },
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Phiếu điều chỉnh",
            });
            Assert.Equal(HttpStatusCode.OK, reissued.StatusCode);
            var reissuedBody = await reissued.Content.ReadFromJsonAsync<JsonElement>();
            var newId = reissuedBody.GetProperty("id").GetGuid();
            Assert.NotEqual(oldId, newId);
            Assert.Equal(2_500_000m, reissuedBody.GetProperty("netPay").GetDecimal());

            var final = await ReadHistory(admin, world);
            Assert.Equal(newId, final.GetProperty("payslip").GetProperty("id").GetGuid());
            var finalEvents = final.GetProperty("history").EnumerateArray().ToArray();
            Assert.Equal(3, finalEvents.Length);
            Assert.Equal(3, finalEvents[0].GetProperty("revision").GetInt32());
            Assert.Equal("PublishedCreated", finalEvents[0].GetProperty("action").GetString());

            var monthAfter = await admin.GetFromJsonAsync<JsonElement>(
                $"/api/payroll/payslips/published?period={world.Period}&search={Uri.EscapeDataString("Nhân viên lương")}&status=pending");
            var adjusted = Assert.Single(monthAfter.GetProperty("items").EnumerateArray());
            Assert.Equal(newId, adjusted.GetProperty("id").GetGuid());
            Assert.Equal(2_500_000m, adjusted.GetProperty("netPay").GetDecimal());
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task PayslipRequirement_UsesPublicationDateBoundary_AndBlocksSelfAttendanceUntilAcknowledged()
    {
        var world = await SetupAsync();
        try
        {
            var admin = Client(world.AdminToken);
            var employee = Client(world.EmployeeToken);
            var published = await admin.PostAsJsonAsync("/api/payroll/payslips", new
            {
                employeeId = world.EmployeeId,
                period = world.Period,
                published = true,
                adjustments = Array.Empty<object>(),
                approvedOvertimeDates = Array.Empty<string>(),
                note = "Kiểm thử hạn xác nhận",
            });
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            var payslipId = (await published.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            using var scope = factory.Services.CreateScope();
            await using var conn = await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();

            // Phát hành ngày D-1: vẫn được dùng trọn ngày hiện tại, chưa khóa trước 00:00 ngày D+2.
            await conn.Cmd("""
                UPDATE hr_payslips
                   SET published_at=(((CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date - 1 + time '12:00')
                       AT TIME ZONE 'Asia/Ho_Chi_Minh')
                 WHERE id=@id
                """).With("@id", payslipId).ExecuteNonQueryAsync();
            var warning = await employee.GetFromJsonAsync<JsonElement>("/api/payroll/my-payslips/requirement");
            Assert.Equal(1, warning.GetProperty("pendingCount").GetInt32());
            Assert.False(warning.GetProperty("mustAcknowledge").GetBoolean());
            Assert.False(warning.GetProperty("payslip").GetProperty("overdue").GetBoolean());

            // Phát hành ngày D-2: mốc khóa là 00:00 hôm nay, nên giờ hiện tại chắc chắn đã quá hạn.
            await conn.Cmd("""
                UPDATE hr_payslips
                   SET published_at=(((CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Ho_Chi_Minh')::date - 2 + time '12:00')
                       AT TIME ZONE 'Asia/Ho_Chi_Minh')
                 WHERE id=@id
                """).With("@id", payslipId).ExecuteNonQueryAsync();
            var locked = await employee.GetFromJsonAsync<JsonElement>("/api/payroll/my-payslips/requirement");
            Assert.True(locked.GetProperty("mustAcknowledge").GetBoolean());
            Assert.Equal(1, locked.GetProperty("overdueCount").GetInt32());

            // Chặn trước cả bước kiểm tra token QR, do đó không thể ghi công bằng đường dự phòng.
            var blockedAttendance = await employee.PostAsJsonAsync("/api/chamcong/qr", new { token = "invalid-test-token" });
            Assert.Equal(HttpStatusCode.OK, blockedAttendance.StatusCode);
            Assert.Equal("payslip_required",
                (await blockedAttendance.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

            Assert.Equal(HttpStatusCode.NoContent,
                (await employee.PostAsync($"/api/payroll/my-payslips/{payslipId}/ack", null)).StatusCode);
            var unlocked = await employee.GetFromJsonAsync<JsonElement>("/api/payroll/my-payslips/requirement");
            Assert.False(unlocked.GetProperty("mustAcknowledge").GetBoolean());
            Assert.Equal(0, unlocked.GetProperty("pendingCount").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await employee.PostAsJsonAsync("/api/chamcong/qr", new { token = "invalid-test-token" })).StatusCode);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    private sealed record World(Guid EmployeeId, string EmployeeUsername, string Period,
        string AdminUsername, string AdminToken, string EmployeeToken);

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var adminUsername = $"__payroll_admin_{suffix}";
        var employeeUsername = $"__payroll_employee_{suffix}";
        var period = $"{DateTime.UtcNow.Year:D4}-{DateTime.UtcNow.Month:D2}";
        var employeeId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        var adminToken = await AddUser(conn, tokens, adminUsername, "Quản trị lương", AppRoles.Admin, null);
        var employeeToken = await AddUser(conn, tokens, employeeUsername, "Nhân viên lương", AppRoles.Employee, employeeId);
        await conn.Cmd("""
            INSERT INTO hr_salaries (id, employee_id, base_salary, allowance, overtime_rate, components, note, updated_by)
            VALUES (@id, @emp, 2000000, 0, 0, '[]', '', @by)
            """).With("@id", Guid.NewGuid()).With("@emp", employeeId).With("@by", adminUsername)
            .ExecuteNonQueryAsync();
        return new World(employeeId, employeeUsername, period, adminUsername, adminToken, employeeToken);
    }

    private static async Task<string> AddUser(NpgsqlConnection conn, TokenService tokens, string username,
        string fullName, string role, Guid? employeeId)
    {
        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @name, '', @role, @hash, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """).With("@id", userId).With("@u", username).With("@name", fullName).With("@role", role)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        if (employeeId is Guid id)
            await conn.Cmd("""
                INSERT INTO hr_employees (id, employee_code, user_id, username, full_name, status)
                VALUES (@id, @code, @uid, @u, @name, 'Active')
                """).With("@id", id).With("@code", "E" + Guid.NewGuid().ToString("N")[..8])
                .With("@uid", userId).With("@u", username).With("@name", fullName).ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, fullName, "", role, true, "Approved", DateTime.UtcNow));
    }

    private async Task CleanupAsync(World world)
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            await using var conn = await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
            await conn.Cmd("DELETE FROM hr_employees WHERE id=@id").With("@id", world.EmployeeId).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username IN (@admin,@employee)")
                .With("@admin", world.AdminUsername).With("@employee", world.EmployeeUsername).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username IN (@admin,@employee)")
                .With("@admin", world.AdminUsername).With("@employee", world.EmployeeUsername).ExecuteNonQueryAsync();
        }
        catch { /* dọn test best-effort; lịch sử cố ý được giữ lại */ }
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> ReadHistory(HttpClient admin, World world)
    {
        var response = await admin.GetAsync(
            $"/api/payroll/payslips/history?employeeId={world.EmployeeId}&period={world.Period}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
