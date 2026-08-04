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
