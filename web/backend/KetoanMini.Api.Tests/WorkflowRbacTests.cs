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
/// Integration coverage for the permission-based workflow gates. These tests intentionally combine
/// technical permissions with the business membership rule used by accounting cash flows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WorkflowRbacTests(ApiFactory factory)
{
    [Fact]
    public async Task Penalties_HrCanManage_EmployeeIsSelfOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var deptId = Guid.NewGuid();
        var usernames = new[] { $"__rbac_pen_hr_{suffix}", $"__rbac_pen_emp_{suffix}" };
        Guid? penaltyId = null;

        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
            await using var conn = await db.OpenAsync();
            await CreateDepartmentAsync(conn, deptId, suffix, accounting: false);
            var hr = await CreateUserAsync(conn, tokens, usernames[0], AppRoles.Hr, deptId);
            var employee = await CreateUserAsync(conn, tokens, usernames[1], AppRoles.Employee, deptId);

            var hrClient = Client(hr.Token);
            var employeeClient = Client(employee.Token);
            var body = new
            {
                employeeId = employee.EmployeeId,
                penaltyType = "fine",
                penaltyDate = "2026-08-03",
                amount = 250_000m,
                installments = 1,
                startPeriod = "2026-08",
                reason = "Kiểm thử phân quyền",
                note = "",
                status = "Active",
            };

            Assert.Equal(HttpStatusCode.Forbidden,
                (await employeeClient.PostAsJsonAsync("/api/penalties", body)).StatusCode);

            var created = await hrClient.PostAsJsonAsync("/api/penalties", body);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            penaltyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            Assert.Equal(HttpStatusCode.Forbidden,
                (await employeeClient.GetAsync("/api/penalties?scope=all")).StatusCode);
            var mine = await (await employeeClient.GetAsync("/api/penalties?scope=mine"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(mine.EnumerateArray(), p => p.GetProperty("id").GetGuid() == penaltyId);

            var all = await (await hrClient.GetAsync("/api/penalties?scope=all"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(all.EnumerateArray(), p => p.GetProperty("id").GetGuid() == penaltyId);
        }
        finally
        {
            await CleanupAsync(usernames, [deptId], penaltyId is null ? [] : [penaltyId.Value], [], []);
        }
    }

    [Fact]
    public async Task PenaltyRefunds_SeparateReadApproveAndPay_AndRequireAccountingMembership()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var accountingDept = Guid.NewGuid();
        var otherDept = Guid.NewGuid();
        var usernames = new[]
        {
            $"__rbac_ref_acc_{suffix}", $"__rbac_ref_chief_{suffix}", $"__rbac_ref_cash_{suffix}",
            $"__rbac_ref_admin_{suffix}", $"__rbac_ref_worker_{suffix}",
        };
        var refundId = Guid.NewGuid();

        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
            await using var conn = await db.OpenAsync();
            await CreateDepartmentAsync(conn, accountingDept, suffix + "a", accounting: true);
            await CreateDepartmentAsync(conn, otherDept, suffix + "o", accounting: false);

            var accounting = await CreateUserAsync(conn, tokens, usernames[0], AppRoles.Accounting, accountingDept);
            var chief = await CreateUserAsync(conn, tokens, usernames[1], AppRoles.ChiefAccountant, accountingDept);
            var cashier = await CreateUserAsync(conn, tokens, usernames[2], AppRoles.Cashier, accountingDept);
            var outsiderAdmin = await CreateUserAsync(conn, tokens, usernames[3], AppRoles.Admin, otherDept);
            var worker = await CreateUserAsync(conn, tokens, usernames[4], AppRoles.Employee, otherDept);

            await conn.Cmd("""
                INSERT INTO hr_penalty_refunds
                    (id, refund_no, employee_id, amount, reason, status, created_by)
                VALUES (@id, @no, @emp, 400000, 'Kiểm thử tách nhiệm vụ', 'PendingAccounting', 'test')
                """).With("@id", refundId).With("@no", "HP" + suffix[..6]).With("@emp", worker.EmployeeId)
                .ExecuteNonQueryAsync();

            var accountingClient = Client(accounting.Token);
            var chiefClient = Client(chief.Token);
            var cashierClient = Client(cashier.Token);
            var outsiderAdminClient = Client(outsiderAdmin.Token);

            Assert.Equal(HttpStatusCode.OK,
                (await accountingClient.GetAsync("/api/penalty-refunds?scope=queue")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await outsiderAdminClient.GetAsync("/api/penalty-refunds?scope=all")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await accountingClient.PostAsJsonAsync($"/api/penalty-refunds/{refundId}/approve",
                    new { payoutMethod = "cash", note = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await outsiderAdminClient.PostAsJsonAsync($"/api/penalty-refunds/{refundId}/approve",
                    new { payoutMethod = "cash", note = "" })).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent,
                (await chiefClient.PostAsJsonAsync($"/api/penalty-refunds/{refundId}/approve",
                    new { payoutMethod = "cash", note = "Đã kiểm tra" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await accountingClient.PostAsync($"/api/penalty-refunds/{refundId}/mark-paid", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await cashierClient.PostAsync($"/api/penalty-refunds/{refundId}/mark-paid", null)).StatusCode);

            var status = await conn.Cmd("SELECT status FROM hr_penalty_refunds WHERE id=@id")
                .With("@id", refundId).ExecuteScalarAsync() as string;
            Assert.Equal("Paid", status);
        }
        finally
        {
            await CleanupAsync(usernames, [accountingDept, otherDept], [], [refundId], []);
        }
    }

    [Fact]
    public async Task Attendance_HrCanManage_ManagerIsScopedAndEmployeeReadsOnlyOwnAssignmentsAndTimesheet()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var deptId = Guid.NewGuid();
        var outsideDeptId = Guid.NewGuid();
        var usernames = new[]
        {
            $"__rbac_att_hr_{suffix}", $"__rbac_att_mgr_{suffix}",
            $"__rbac_att_one_{suffix}", $"__rbac_att_out_{suffix}",
        };
        Guid? shiftId = null;
        var siteName = "RBAC QR " + suffix;

        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
            await using var conn = await db.OpenAsync();
            await CreateDepartmentAsync(conn, deptId, suffix, accounting: false);
            await CreateDepartmentAsync(conn, outsideDeptId, suffix + "o", accounting: false);
            var hr = await CreateUserAsync(conn, tokens, usernames[0], AppRoles.Hr, deptId);
            var manager = await CreateUserAsync(conn, tokens, usernames[1], AppRoles.Manager, deptId);
            var first = await CreateUserAsync(conn, tokens, usernames[2], AppRoles.Employee, deptId);
            var outside = await CreateUserAsync(conn, tokens, usernames[3], AppRoles.Employee, outsideDeptId);
            await conn.Cmd("UPDATE hr_employees SET access_role='dept_manager' WHERE id=@id")
                .With("@id", manager.EmployeeId).ExecuteNonQueryAsync();

            var hrClient = Client(hr.Token);
            var managerClient = Client(manager.Token);
            var firstClient = Client(first.Token);
            var shiftBody = new
            {
                code = "RB" + suffix[..4], name = "Ca RBAC " + suffix,
                startTime = "08:00", endTime = "17:00", breakMinutes = 60,
                lateGraceMinutes = 5, standardHours = 8, isOvernight = false,
            };

            Assert.Equal(HttpStatusCode.Forbidden,
                (await firstClient.PostAsJsonAsync("/api/shifts", shiftBody)).StatusCode);
            var created = await hrClient.PostAsJsonAsync("/api/shifts", shiftBody);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            shiftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            foreach (var (employeeId, date) in new[]
                     {
                         (first.EmployeeId, "2031-01-10"),
                         (outside.EmployeeId, "2031-01-11"),
                     })
            {
                var assigned = await hrClient.PostAsJsonAsync("/api/shifts/assignments", new
                {
                    employeeId, shiftId, workDate = date, note = "RBAC",
                });
                Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
            }

            var own = await (await firstClient.GetAsync(
                    "/api/shifts/assignments?from=2031-01-01&to=2031-01-31"))
                .Content.ReadFromJsonAsync<JsonElement>();
            var ownRows = own.EnumerateArray().ToArray();
            Assert.Single(ownRows);
            Assert.Equal(first.EmployeeId, ownRows[0].GetProperty("employeeId").GetGuid());
            Assert.Equal(HttpStatusCode.Forbidden,
                (await firstClient.GetAsync(
                    $"/api/shifts/assignments?from=2031-01-01&to=2031-01-31&employeeId={outside.EmployeeId}"))
                .StatusCode);

            var managerRows = await (await managerClient.GetAsync(
                    "/api/shifts/assignments?from=2031-01-01&to=2031-01-31"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(managerRows.EnumerateArray(), row =>
                row.GetProperty("employeeId").GetGuid() == first.EmployeeId);
            Assert.DoesNotContain(managerRows.EnumerateArray(), row =>
                row.GetProperty("employeeId").GetGuid() == outside.EmployeeId);
            Assert.Equal(HttpStatusCode.OK,
                (await managerClient.GetAsync(
                    $"/api/shifts/assignments?from=2031-01-01&to=2031-01-31&employeeId={first.EmployeeId}"))
                .StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await managerClient.GetAsync(
                    $"/api/shifts/assignments?from=2031-01-01&to=2031-01-31&employeeId={outside.EmployeeId}"))
                .StatusCode);

            var all = await (await hrClient.GetAsync(
                    "/api/shifts/assignments?from=2031-01-01&to=2031-01-31"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(all.EnumerateArray().Count() >= 2);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await firstClient.GetAsync($"/api/timesheet/employee/{outside.EmployeeId}?month=2031-01")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await managerClient.GetAsync($"/api/timesheet/employee/{first.EmployeeId}?month=2031-01")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await managerClient.GetAsync($"/api/timesheet/employee/{outside.EmployeeId}?month=2031-01")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await hrClient.GetAsync($"/api/timesheet/employee/{outside.EmployeeId}?month=2031-01")).StatusCode);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await firstClient.PostAsJsonAsync("/api/chamcong/qr-sites",
                    new { name = siteName, projectName = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await hrClient.PostAsJsonAsync("/api/chamcong/qr-sites",
                    new { name = siteName, projectName = "" })).StatusCode);
        }
        finally
        {
            await CleanupAsync(usernames, [deptId, outsideDeptId], [], [], shiftId is null ? [] : [shiftId.Value], siteName);
        }
    }

    private sealed record TestUser(Guid UserId, Guid EmployeeId, string Token);

    private static async Task CreateDepartmentAsync(NpgsqlConnection conn, Guid id, string suffix, bool accounting)
        => await conn.Cmd("INSERT INTO hr_departments (id, code, name, is_accounting) VALUES (@id,@code,@name,@acc)")
            .With("@id", id).With("@code", "R" + Guid.NewGuid().ToString("N")[..10])
            .With("@name", "RBAC Department " + suffix).With("@acc", accounting).ExecuteNonQueryAsync();

    private static async Task<TestUser> CreateUserAsync(
        NpgsqlConnection conn, TokenService tokens, string username, string role, Guid departmentId)
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id,@u,@u,'',@role,@ph,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", role)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id, employee_code, user_id, username, full_name, department_id, status)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active')
            """).With("@id", employeeId).With("@code", "R" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId).ExecuteNonQueryAsync();
        var token = tokens.CreateToken(
            new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:rbac:" + Guid.NewGuid().ToString("N")[..16]);
        return new TestUser(userId, employeeId, token);
    }

    private HttpClient Client(string token)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task CleanupAsync(
        string[] usernames, Guid[] departmentIds, Guid[] penaltyIds, Guid[] refundIds, Guid[] shiftIds,
        string? qrSiteName = null)
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            if (penaltyIds.Length > 0)
                await conn.Cmd("DELETE FROM hr_penalties WHERE id=ANY(@ids)").With("@ids", penaltyIds).ExecuteNonQueryAsync();
            if (refundIds.Length > 0)
                await conn.Cmd("DELETE FROM hr_penalty_refunds WHERE id=ANY(@ids)").With("@ids", refundIds).ExecuteNonQueryAsync();
            if (!string.IsNullOrWhiteSpace(qrSiteName))
                await conn.Cmd("DELETE FROM cham_cong_qr_sites WHERE name=@name").With("@name", qrSiteName).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM hr_employees WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            if (shiftIds.Length > 0)
                await conn.Cmd("DELETE FROM hr_shifts WHERE id=ANY(@ids)").With("@ids", shiftIds).ExecuteNonQueryAsync();
            if (departmentIds.Length > 0)
                await conn.Cmd("DELETE FROM hr_departments WHERE id=ANY(@ids)").With("@ids", departmentIds).ExecuteNonQueryAsync();
        }
        catch
        {
            // Best-effort cleanup: unique test identities prevent cross-test interference.
        }
    }
}
