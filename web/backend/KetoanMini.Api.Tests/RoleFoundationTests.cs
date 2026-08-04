using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class RoleFoundationTests(ApiFactory factory)
{
    [Fact]
    public async Task Migration_DuocGhiNhan_VaSeedDayDuVaiTroChucVu()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();

        await HrEndpoints.EnsureTables(db);
        await HrEndpoints.EnsureTables(db); // lần hai phải là no-op, không seed trùng

        await using var conn = await db.OpenAsync();
        var migrationCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@v")
            .With("@v", RoleFoundationMigration.Version).ExecuteScalarAsync());
        var expansionCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@v")
            .With("@v", RoleCatalogExpansionMigration.Version).ExecuteScalarAsync());
        var multiPositionCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@v")
            .With("@v", EmployeePositionMigration.Version).ExecuteScalarAsync());
        var catalogExpansion2Count = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@v")
            .With("@v", JobPositionCatalogExpansionMigration.Version).ExecuteScalarAsync());
        var legacyBackfillCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@v")
            .With("@v", LegacyRolePositionBackfillMigration.Version).ExecuteScalarAsync());
        var canonicalCorrectionCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=@v")
            .With("@v", CanonicalRolePositionCorrectionMigration.Version).ExecuteScalarAsync());
        var roleCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM system_roles WHERE code=ANY(@roles)")
            .With("@roles", AppRoles.All).ExecuteScalarAsync());
        var positionCount = Convert.ToInt32(await conn.Cmd(
                "SELECT COUNT(*) FROM hr_job_positions WHERE is_system=TRUE")
            .ExecuteScalarAsync());
        var directorRole = (string?)await conn.Cmd(
                "SELECT default_role FROM hr_job_positions WHERE code='DIRECTOR'")
            .ExecuteScalarAsync();

        Assert.Equal(1, migrationCount);
        Assert.Equal(1, expansionCount);
        Assert.Equal(1, multiPositionCount);
        Assert.Equal(1, catalogExpansion2Count);
        Assert.Equal(1, legacyBackfillCount);
        Assert.Equal(1, canonicalCorrectionCount);
        Assert.Equal(AppRoles.All.Length, roleCount);
        Assert.True(positionCount >= 65, $"Danh mục chức vụ hệ thống còn thiếu: chỉ có {positionCount} mục.");
        Assert.Equal(AppRoles.Executive, directorRole);
        Assert.NotEqual(AppRoles.Admin, directorRole);
        var hasPrimaryIndex = await conn.Cmd("""
            SELECT EXISTS(
                SELECT 1 FROM pg_indexes
                WHERE tablename='hr_employee_positions' AND indexname='ux_hr_employee_positions_primary'
            )
            """).ExecuteScalarAsync() is bool indexed && indexed;
        Assert.True(hasPrimaryIndex);
    }

    [Fact]
    public async Task TaoHoSo_BangChucVuKeToanTruong_TuTaoTaiKhoanDungRole()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var admin = $"__role_admin_{suffix}__";
        var employeeUsername = $"__chief_{suffix}__";
        Guid employeeId = default;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        var adminId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users
                (id, username, full_name, email, role, password_hash, is_active,
                 approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, 'Role test admin', '', @role, @ph, TRUE,
                    'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", adminId).With("@u", admin).With("@role", AppRoles.Admin)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        try
        {
            var positionId = (Guid)(await conn.Cmd(
                    "SELECT id FROM hr_job_positions WHERE code='CHIEF_ACCOUNTANT'")
                .ExecuteScalarAsync())!;
            var departmentId = (Guid)(await conn.Cmd(
                    "SELECT id FROM hr_departments WHERE is_accounting=TRUE ORDER BY created_at LIMIT 1")
                .ExecuteScalarAsync())!;

            var token = tokens.CreateToken(new UserDto(
                adminId, admin, "Role test admin", "", AppRoles.Admin, true, "Approved", DateTime.UtcNow));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Gửi role/position giả khác với catalog để chốt rằng server chỉ tin positionId.
            var response = await client.PostAsJsonAsync("/api/hr/employees", new
            {
                username = employeeUsername,
                fullName = "Kế toán trưởng kiểm thử",
                departmentId,
                positionId,
                position = "Nhân viên",
                role = AppRoles.Employee,
                createAccount = true,
                status = "Active",
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            employeeId = body.GetProperty("id").GetGuid();
            Assert.True(body.GetProperty("accountCreated").GetBoolean());
            var temporaryPassword = body.GetProperty("password").GetString();
            Assert.NotNull(temporaryPassword);
            Assert.Equal(16, temporaryPassword!.Length);
            Assert.NotEqual("123", temporaryPassword);

            await using var r = await conn.Cmd("""
                SELECT u.role, e.position_id, e.position, e.access_role
                FROM hr_employees e JOIN app_users u ON lower(u.username)=lower(e.username)
                WHERE e.id=@id
                """).With("@id", employeeId).ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(AppRoles.ChiefAccountant, r.Str("role"));
            Assert.Equal(positionId, r.Guid("position_id"));
            Assert.Equal("Kế toán trưởng", r.Str("position"));
            Assert.Equal("staff", r.Str("access_role"));
        }
        finally
        {
            if (employeeId != default)
                await conn.Cmd("DELETE FROM hr_employees WHERE id=@id")
                    .With("@id", employeeId).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=ANY(@users)")
                .With("@users", new[] { employeeUsername, admin }).ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task QuanLy_ChiXemDashboardNhanSuTrongPhongCuaMinh()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var manager = $"__manager_{suffix}__";
        var inside = $"__inside_{suffix}__";
        var outside = $"__outside_{suffix}__";
        var deptInside = Guid.NewGuid();
        var deptOutside = Guid.NewGuid();
        var managerEmployeeId = Guid.NewGuid();
        var insideId = Guid.NewGuid();
        var outsideId = Guid.NewGuid();

        using var serviceScope = factory.Services.CreateScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<Database>();
        var tokens = serviceScope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        var managerUserId = Guid.NewGuid();

        await conn.Cmd("""
            INSERT INTO app_users
                (id, username, full_name, email, role, password_hash, is_active,
                 approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, 'Quản lý kiểm thử', '', @role, @ph, TRUE,
                    'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE);
            INSERT INTO hr_departments(id, code, name) VALUES
                (@deptIn, @codeIn, @nameIn), (@deptOut, @codeOut, @nameOut);
            INSERT INTO hr_employees
                (id, employee_code, user_id, username, full_name, department_id, position,
                 position_id, access_role, status)
            VALUES
                (@managerEmp, @managerCode, @userId, @manager, 'Quản lý kiểm thử', @deptIn,
                 'Trưởng phòng', (SELECT id FROM hr_job_positions WHERE code='DEPARTMENT_HEAD'),
                 'dept_manager', 'Active'),
                (@insideId, @insideCode, NULL, @inside, @insideName, @deptIn, 'Nhân viên',
                 (SELECT id FROM hr_job_positions WHERE code='EMPLOYEE'), 'staff', 'Active'),
                (@outsideId, @outsideCode, NULL, @outside, @outsideName, @deptOut, 'Nhân viên',
                 (SELECT id FROM hr_job_positions WHERE code='EMPLOYEE'), 'staff', 'Active');
            """)
            .With("@id", managerUserId).With("@u", manager).With("@role", AppRoles.Manager)
            .With("@ph", PasswordHasher.Hash("test-pass"))
            .With("@deptIn", deptInside).With("@deptOut", deptOutside)
            .With("@codeIn", $"DIN-{suffix}").With("@codeOut", $"DOUT-{suffix}")
            .With("@nameIn", $"Phòng trong {suffix}").With("@nameOut", $"Phòng ngoài {suffix}")
            .With("@managerEmp", managerEmployeeId).With("@managerCode", $"M-{suffix}")
            .With("@userId", managerUserId).With("@manager", manager)
            .With("@insideId", insideId).With("@insideCode", $"I-{suffix}").With("@inside", inside)
            .With("@insideName", $"Nhân viên trong {suffix}")
            .With("@outsideId", outsideId).With("@outsideCode", $"O-{suffix}").With("@outside", outside)
            .With("@outsideName", $"Nhân viên ngoài {suffix}")
            .ExecuteNonQueryAsync();

        try
        {
            var token = tokens.CreateToken(new UserDto(
                managerUserId, manager, "Quản lý kiểm thử", "", AppRoles.Manager, true, "Approved", DateTime.UtcNow));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var employeesResponse = await client.GetAsync("/api/hr/employees");
            Assert.Equal(HttpStatusCode.OK, employeesResponse.StatusCode);
            var employeesJson = await employeesResponse.Content.ReadAsStringAsync();
            Assert.Contains(inside, employeesJson, StringComparison.Ordinal);
            Assert.DoesNotContain(outside, employeesJson, StringComparison.Ordinal);

            var attendanceResponse = await client.GetAsync("/api/hr/manager/attendance");
            Assert.Equal(HttpStatusCode.OK, attendanceResponse.StatusCode);
            var attendanceJson = await attendanceResponse.Content.ReadAsStringAsync();
            Assert.Contains($"Nhân viên trong {suffix}", attendanceJson, StringComparison.Ordinal);
            Assert.DoesNotContain($"Nhân viên ngoài {suffix}", attendanceJson, StringComparison.Ordinal);

            foreach (var path in new[]
                     {
                         "/api/hr/manager/summary",
                         "/api/hr/manager/contracts/expiring",
                         "/api/hr/manager/reports",
                         "/api/hr/manager/alerts",
                     })
            {
                var response = await client.GetAsync(path);
                Assert.True(response.IsSuccessStatusCode,
                    $"{path} trả {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }
        finally
        {
            await conn.Cmd("DELETE FROM hr_employees WHERE id=ANY(@ids)")
                .With("@ids", new[] { managerEmployeeId, insideId, outsideId }).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM hr_departments WHERE id=ANY(@ids)")
                .With("@ids", new[] { deptInside, deptOutside }).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=@u")
                .With("@u", manager).ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task NhieuChucVu_HopRolePhamVi_DongBoVaKhongChoRoleBiLech()
    {
        var actor = await CreateActorAsync(AppRoles.Admin);
        var employeeUsername = "__multi_" + Guid.NewGuid().ToString("N")[..14];
        Guid employeeId = default;
        Guid employeeUserId = default;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        try
        {
            var positions = await PositionIdsAsync(conn, "EMPLOYEE", "ACCOUNTANT", "DEPARTMENT_HEAD",
                "CHIEF_ACCOUNTANT", "CASHIER");
            var departmentId = (Guid)(await conn.Cmd(
                    "SELECT id FROM hr_departments ORDER BY is_accounting DESC, created_at LIMIT 1")
                .ExecuteScalarAsync())!;

            var create = await actor.Client.PostAsJsonAsync("/api/hr/employees", new
            {
                username = employeeUsername,
                fullName = "Nhân sự kiêm nhiệm",
                departmentId,
                positionId = positions["EMPLOYEE"],
                positionIds = new[] { positions["EMPLOYEE"], positions["ACCOUNTANT"], positions["DEPARTMENT_HEAD"] },
                createAccount = true,
                status = "Active",
            });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            employeeId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var detailResponse = await actor.Client.GetAsync($"/api/hr/employees/{employeeId}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, detail.GetProperty("positions").GetArrayLength());
            Assert.Equal(3, detail.GetProperty("positionIds").GetArrayLength());
            Assert.Equal(positions["EMPLOYEE"], detail.GetProperty("positionId").GetGuid());
            var primaryCount = detail.GetProperty("positions").EnumerateArray()
                .Count(p => p.GetProperty("isPrimary").GetBoolean());
            Assert.Equal(1, primaryCount);

            int versionBefore;
            await using (var r = await conn.Cmd("""
                SELECT u.id, u.role, u.authorization_version, e.access_role,
                       COALESCE((SELECT string_agg(role, ',' ORDER BY role) FROM user_roles WHERE username=u.username), '') AS extras
                FROM hr_employees e JOIN app_users u ON u.id=e.user_id
                WHERE e.id=@employee
                """).With("@employee", employeeId).ExecuteReaderAsync())
            {
                Assert.True(await r.ReadAsync());
                employeeUserId = r.Guid("id");
                Assert.Equal(AppRoles.Accounting, r.Str("role"));
                Assert.Equal("Employee,Manager", r.Str("extras"));
                Assert.Equal("dept_manager", r.Str("access_role"));
                versionBefore = r.Int("authorization_version");
                Assert.True(versionBefore > 1);
            }

            var directPrimary = await actor.Client.PostAsJsonAsync($"/api/users/{employeeUserId}/role",
                new { role = AppRoles.Employee, reason = "test drift" });
            Assert.Equal(HttpStatusCode.Conflict, directPrimary.StatusCode);
            var directSecondary = await actor.Client.PostAsJsonAsync($"/api/users/{employeeUserId}/secondary-role",
                new { role = AppRoles.Warehouse, grant = true, reason = "test drift" });
            Assert.Equal(HttpStatusCode.Conflict, directSecondary.StatusCode);
            var directDelete = await actor.Client.DeleteAsync($"/api/users/{employeeUserId}");
            Assert.Equal(HttpStatusCode.Conflict, directDelete.StatusCode);

            var update = await actor.Client.PutAsJsonAsync($"/api/hr/employees/{employeeId}", new
            {
                employeeCode = "MULTI-" + Guid.NewGuid().ToString("N")[..8],
                username = employeeUsername,
                fullName = "Nhân sự kiêm nhiệm",
                departmentId,
                positionId = positions["CASHIER"],
                positionIds = new[] { positions["EMPLOYEE"], positions["CHIEF_ACCOUNTANT"], positions["CASHIER"] },
                status = "Active",
            });
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

            await using (var r = await conn.Cmd("""
                SELECT u.role, u.authorization_version, e.access_role,
                       COALESCE((SELECT string_agg(role, ',' ORDER BY role) FROM user_roles WHERE username=u.username), '') AS extras
                FROM hr_employees e JOIN app_users u ON u.id=e.user_id WHERE e.id=@employee
                """).With("@employee", employeeId).ExecuteReaderAsync())
            {
                Assert.True(await r.ReadAsync());
                Assert.Equal(AppRoles.ChiefAccountant, r.Str("role"));
                Assert.Equal("Cashier,Employee", r.Str("extras"));
                Assert.Equal("staff", r.Str("access_role"));
                Assert.True(r.Int("authorization_version") > versionBefore);
            }

            var delete = await actor.Client.DeleteAsync($"/api/hr/employees/{employeeId}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            employeeId = default;
            await using (var r = await conn.Cmd("""
                SELECT is_active, role,
                       (SELECT COUNT(*) FROM user_roles WHERE username=u.username) AS extra_count
                FROM app_users u WHERE id=@id
                """).With("@id", employeeUserId).ExecuteReaderAsync())
            {
                Assert.True(await r.ReadAsync());
                Assert.False(r.Bool("is_active"));
                Assert.Equal(AppRoles.Employee, r.Str("role"));
                Assert.Equal(0, r.Int("extra_count"));
            }
        }
        finally
        {
            if (employeeId != default)
                await conn.Cmd("DELETE FROM hr_employees WHERE id=@id").With("@id", employeeId).ExecuteNonQueryAsync();
            await CleanupUsersAsync(conn, employeeUsername, actor.Username);
            actor.Client.Dispose();
        }
    }

    [Fact]
    public async Task ValidationPositionIds_VaHrKhongTheLeoThangDacQuyen()
    {
        var admin = await CreateActorAsync(AppRoles.Admin);
        var hr = await CreateActorAsync(AppRoles.Hr);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var createdUsernames = new List<string>();
        try
        {
            var positions = await PositionIdsAsync(conn, "EMPLOYEE", "ACCOUNTANT", "DEPARTMENT_HEAD",
                "BOARD_MANAGEMENT", "BRANCH_DIRECTOR");
            var departmentId = (Guid)(await conn.Cmd("SELECT id FROM hr_departments ORDER BY created_at LIMIT 1")
                .ExecuteScalarAsync())!;

            async Task<HttpResponseMessage> Invalid(object body)
                => await admin.Client.PostAsJsonAsync("/api/hr/employees", body);

            var empty = await Invalid(new
            {
                username = "__invalid_empty_" + Guid.NewGuid().ToString("N")[..8], fullName = "Invalid",
                departmentId, positionIds = Array.Empty<Guid>(), createAccount = false,
            });
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

            var duplicate = await Invalid(new
            {
                username = "__invalid_dup_" + Guid.NewGuid().ToString("N")[..8], fullName = "Invalid",
                departmentId, positionId = positions["EMPLOYEE"],
                positionIds = new[] { positions["EMPLOYEE"], positions["EMPLOYEE"] }, createAccount = false,
            });
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

            var wrongPrimary = await Invalid(new
            {
                username = "__invalid_primary_" + Guid.NewGuid().ToString("N")[..8], fullName = "Invalid",
                departmentId, positionId = positions["DEPARTMENT_HEAD"],
                positionIds = new[] { positions["EMPLOYEE"], positions["ACCOUNTANT"] }, createAccount = false,
            });
            Assert.Equal(HttpStatusCode.BadRequest, wrongPrimary.StatusCode);

            var inactiveId = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_job_positions
                    (id, code, name, default_role, default_access_role, is_system, is_active, sort_order)
                VALUES (@id, @code, 'Chức vụ ngừng dùng', 'Employee', 'staff', FALSE, FALSE, 9999)
                """).With("@id", inactiveId).With("@code", "INACTIVE_" + Guid.NewGuid().ToString("N")[..10])
                .ExecuteNonQueryAsync();
            var inactive = await Invalid(new
            {
                username = "__invalid_inactive_" + Guid.NewGuid().ToString("N")[..8], fullName = "Invalid",
                departmentId, positionId = inactiveId, positionIds = new[] { inactiveId }, createAccount = false,
            });
            Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
            await conn.Cmd("DELETE FROM hr_job_positions WHERE id=@id").With("@id", inactiveId).ExecuteNonQueryAsync();

            var ordinaryUsername = "__hr_employee_" + Guid.NewGuid().ToString("N")[..10];
            createdUsernames.Add(ordinaryUsername);
            var ordinary = await hr.Client.PostAsJsonAsync("/api/hr/employees", new
            {
                username = ordinaryUsername, fullName = "Nhân viên thường", departmentId,
                positionId = positions["EMPLOYEE"], positionIds = new[] { positions["EMPLOYEE"] },
                createAccount = true, status = "Active",
            });
            Assert.Equal(HttpStatusCode.OK, ordinary.StatusCode);
            Assert.Equal(AppRoles.Employee, await conn.Cmd("SELECT role FROM app_users WHERE username=@u")
                .With("@u", ordinaryUsername).ExecuteScalarAsync());

            var privilegedUsername = "__hr_manager_" + Guid.NewGuid().ToString("N")[..10];
            createdUsernames.Add(privilegedUsername);
            var privileged = await hr.Client.PostAsJsonAsync("/api/hr/employees", new
            {
                username = privilegedUsername, fullName = "Quản lý giả", departmentId,
                positionId = positions["DEPARTMENT_HEAD"], positionIds = new[] { positions["DEPARTMENT_HEAD"] },
                createAccount = true, status = "Active",
            });
            Assert.Equal(HttpStatusCode.Forbidden, privileged.StatusCode);

            // HR vẫn sửa được các trường hồ sơ của người đang giữ vai trò đặc quyền nếu KHÔNG đổi
            // tập quyền; nhưng không thể lén cộng thêm một vai trò đặc quyền mới.
            var managedUsername = "__managed_same_" + Guid.NewGuid().ToString("N")[..10];
            createdUsernames.Add(managedUsername);
            var managedCreate = await admin.Client.PostAsJsonAsync("/api/hr/employees", new
            {
                username = managedUsername, fullName = "Quản lý hiện hữu", departmentId,
                positionId = positions["DEPARTMENT_HEAD"], positionIds = new[] { positions["DEPARTMENT_HEAD"] },
                createAccount = true, status = "Active",
            });
            Assert.Equal(HttpStatusCode.OK, managedCreate.StatusCode);
            var managedBody = await managedCreate.Content.ReadFromJsonAsync<JsonElement>();
            var managedId = managedBody.GetProperty("id").GetGuid();
            var managedCode = managedBody.GetProperty("employeeCode").GetString();

            var samePrivileges = await hr.Client.PutAsJsonAsync($"/api/hr/employees/{managedId}", new
            {
                employeeCode = managedCode, username = managedUsername, fullName = "Quản lý đã sửa hồ sơ",
                departmentId, positionId = positions["DEPARTMENT_HEAD"],
                positionIds = new[] { positions["DEPARTMENT_HEAD"] }, status = "Active",
            });
            Assert.Equal(HttpStatusCode.NoContent, samePrivileges.StatusCode);

            var escalation = await hr.Client.PutAsJsonAsync($"/api/hr/employees/{managedId}", new
            {
                employeeCode = managedCode, username = managedUsername, fullName = "Quản lý đã sửa hồ sơ",
                departmentId, positionId = positions["DEPARTMENT_HEAD"],
                positionIds = new[] { positions["DEPARTMENT_HEAD"], positions["ACCOUNTANT"] }, status = "Active",
            });
            Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);

            var scopedUsername = "__scope_change_" + Guid.NewGuid().ToString("N")[..10];
            createdUsernames.Add(scopedUsername);
            var scopedCreate = await admin.Client.PostAsJsonAsync("/api/hr/employees", new
            {
                username = scopedUsername, fullName = "Điều hành phạm vi", departmentId,
                positionId = positions["BOARD_MANAGEMENT"], positionIds = new[] { positions["BOARD_MANAGEMENT"] },
                createAccount = true, status = "Active",
            });
            Assert.Equal(HttpStatusCode.OK, scopedCreate.StatusCode);
            var scopedBody = await scopedCreate.Content.ReadFromJsonAsync<JsonElement>();
            var scopedId = scopedBody.GetProperty("id").GetGuid();
            var scopedCode = scopedBody.GetProperty("employeeCode").GetString();
            var versionBefore = Convert.ToInt32(await conn.Cmd(
                    "SELECT authorization_version FROM app_users WHERE username=@u")
                .With("@u", scopedUsername).ExecuteScalarAsync());

            var scopeUpdate = await admin.Client.PutAsJsonAsync($"/api/hr/employees/{scopedId}", new
            {
                employeeCode = scopedCode, username = scopedUsername, fullName = "Điều hành phạm vi",
                departmentId, positionId = positions["BRANCH_DIRECTOR"],
                positionIds = new[] { positions["BRANCH_DIRECTOR"] }, status = "Active",
            });
            Assert.Equal(HttpStatusCode.NoContent, scopeUpdate.StatusCode);
            await using (var r = await conn.Cmd("""
                SELECT u.role, u.authorization_version, e.access_role
                FROM app_users u JOIN hr_employees e ON e.user_id=u.id
                WHERE u.username=@username
                """).With("@username", scopedUsername).ExecuteReaderAsync())
            {
                Assert.True(await r.ReadAsync());
                Assert.Equal(AppRoles.Executive, r.Str("role"));
                Assert.Equal("location_manager", r.Str("access_role"));
                Assert.True(r.Int("authorization_version") > versionBefore);
            }
        }
        finally
        {
            await conn.Cmd("DELETE FROM hr_employees WHERE username=ANY(@users)")
                .With("@users", createdUsernames.ToArray()).ExecuteNonQueryAsync();
            await CleanupUsersAsync(conn, createdUsernames.Append(admin.Username).Append(hr.Username).ToArray());
            admin.Client.Dispose();
            hr.Client.Dispose();
        }
    }

    [Fact]
    public async Task Migration006_BaoToanVaiTroPhuCuThanhChucVuKiemNhiem()
    {
        var username = "__legacy_roles_" + Guid.NewGuid().ToString("N")[..12];
        var employeeId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        try
        {
            await conn.Cmd("""
                INSERT INTO app_users
                    (id, username, full_name, email, role, password_hash, is_active,
                     approval_status, approved_at, approved_by, created_at, is_deleted)
                VALUES (@userId, @username, 'Legacy role', '', 'Employee', @hash, TRUE,
                        'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE);
                INSERT INTO user_roles(username, role, granted_by, granted_at)
                VALUES (@username, 'Warehouse', 'legacy-admin', CURRENT_TIMESTAMP);
                INSERT INTO hr_employees
                    (id, employee_code, username, full_name, position, position_id, status)
                VALUES (@employee, @code, @username, 'Legacy role', 'Nhân viên',
                        (SELECT id FROM hr_job_positions WHERE code='EMPLOYEE'), 'Active');
                """).With("@userId", Guid.NewGuid()).With("@username", username)
                .With("@hash", PasswordHasher.Hash("test-pass")).With("@employee", employeeId)
                .With("@code", "LEG-" + Guid.NewGuid().ToString("N")[..8]).ExecuteNonQueryAsync();

            // Mô phỏng đúng dữ liệu tồn tại trước migration rồi chạy lại migration idempotent trên DB test.
            await conn.Cmd("DELETE FROM schema_migrations WHERE version=ANY(@versions)")
                .With("@versions", new[]
                {
                    LegacyRolePositionBackfillMigration.Version,
                    CanonicalRolePositionCorrectionMigration.Version,
                }).ExecuteNonQueryAsync();
            await LegacyRolePositionBackfillMigration.ApplyAsync(conn);
            await CanonicalRolePositionCorrectionMigration.ApplyAsync(conn);

            var assignedRoles = new List<(string Role, string Code, bool Primary)>();
            await using var r = await conn.Cmd("""
                SELECT p.default_role, p.code, ep.is_primary
                FROM hr_employee_positions ep
                JOIN hr_job_positions p ON p.id=ep.position_id
                WHERE ep.employee_id=@employee
                ORDER BY p.default_role
                """).With("@employee", employeeId).ExecuteReaderAsync();
            while (await r.ReadAsync())
                assignedRoles.Add((r.Str("default_role"), r.Str("code"), r.Bool("is_primary")));
            Assert.Equal(2, assignedRoles.Count);
            Assert.Contains(assignedRoles, x => x.Role == AppRoles.Employee && x.Code == "EMPLOYEE" && x.Primary);
            Assert.Contains(assignedRoles, x => x.Role == AppRoles.Warehouse && x.Code == "STOREKEEPER" && !x.Primary);
        }
        finally
        {
            await conn.Cmd("DELETE FROM hr_employees WHERE id=@id").With("@id", employeeId).ExecuteNonQueryAsync();
            await CleanupUsersAsync(conn, username);
        }
    }

    private sealed record TestActor(string Username, Guid UserId, HttpClient Client);

    private async Task<TestActor> CreateActorAsync(string role)
    {
        var username = $"__actor_{role.ToLowerInvariant()}_{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO app_users
                (id, username, full_name, email, role, password_hash, is_active,
                 approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @username, @name, '', @role, @hash, TRUE,
                    'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """).With("@id", userId).With("@username", username).With("@name", username)
            .With("@role", role).With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        var token = tokens.CreateToken(new UserDto(
            userId, username, username, "", role, true, "Approved", DateTime.UtcNow));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestActor(username, userId, client);
    }

    private static async Task<Dictionary<string, Guid>> PositionIdsAsync(Npgsql.NpgsqlConnection conn, params string[] codes)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var r = await conn.Cmd("SELECT id, code FROM hr_job_positions WHERE code=ANY(@codes)")
            .With("@codes", codes).ExecuteReaderAsync();
        while (await r.ReadAsync()) result[r.Str("code")] = r.Guid("id");
        Assert.Equal(codes.Length, result.Count);
        return result;
    }

    private static async Task CleanupUsersAsync(Npgsql.NpgsqlConnection conn, params string[] usernames)
    {
        await conn.Cmd("DELETE FROM user_roles WHERE username=ANY(@users)")
            .With("@users", usernames).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM user_sessions WHERE username=ANY(@users)")
            .With("@users", usernames).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=ANY(@users)")
            .With("@users", usernames).ExecuteNonQueryAsync();
    }
}
