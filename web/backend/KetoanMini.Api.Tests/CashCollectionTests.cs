using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CashCollectionTests
{
    private readonly ApiFactory _factory;
    public CashCollectionTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DriverCollection_OnlyPostsDebtAfterMatchingAccountantCount_ExactlyOnce()
    {
        var world = await SetupAsync();
        Guid orderId = Guid.Empty;
        try
        {
            using var accountant = Client(world.AccountantToken);
            using var cashier = Client(world.CashierToken);
            using var driver = Client(world.DriverToken);
            using var stranger = Client(world.StrangerToken);

            // Bộ chọn khách hàng của nghiệp vụ chỉ cấp dữ liệu cần thiết; không trả địa chỉ.
            var customerChoicesResponse = await accountant.GetAsync("/api/cash-collections/customers");
            Assert.Equal(HttpStatusCode.OK, customerChoicesResponse.StatusCode);
            var customerChoices = await customerChoicesResponse.Content.ReadFromJsonAsync<JsonElement>();
            var customerChoice = customerChoices.EnumerateArray()
                .Single(x => x.GetProperty("id").GetGuid() == world.CustomerId);
            Assert.Equal("0900000000", customerChoice.GetProperty("phone").GetString());
            Assert.False(customerChoice.TryGetProperty("address", out _));
            Assert.Equal(HttpStatusCode.Forbidden,
                (await driver.GetAsync("/api/cash-collections/customers")).StatusCode);

            var driverChoices = await (await accountant.GetAsync("/api/cash-collections/drivers"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(driverChoices.EnumerateArray(), x =>
                x.GetProperty("id").GetGuid() == world.DriverEmployeeId);
            Assert.DoesNotContain(driverChoices.EnumerateArray(), x =>
                x.GetProperty("id").GetGuid() == world.StrangerEmployeeId);

            // Nhân viên thường không thể bị chọn làm người giữ tiền, kể cả gọi API trực tiếp.
            var assignOrdinaryEmployee = await accountant.PostAsJsonAsync("/api/cash-collections", new
            {
                customerId = world.CustomerId,
                driverEmployeeId = world.StrangerEmployeeId,
                expectedAmount = 800_000,
                scheduledDate = DateOnly.FromDateTime(DateTime.Today),
                handoverDueAt = DateTime.UtcNow.AddDays(1),
                note = "Không được giao nhân viên thường",
            });
            Assert.Equal(HttpStatusCode.BadRequest, assignOrdinaryEmployee.StatusCode);

            var created = await accountant.PostAsJsonAsync("/api/cash-collections", new
            {
                customerId = world.CustomerId,
                driverEmployeeId = world.DriverEmployeeId,
                expectedAmount = 800_000,
                scheduledDate = DateOnly.FromDateTime(DateTime.Today),
                handoverDueAt = DateTime.UtcNow.AddDays(1),
                note = "Thu tiền công nợ test",
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            orderId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var mine = await (await driver.GetAsync("/api/cash-collections?scope=mine")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(mine.EnumerateArray(), x => x.GetProperty("id").GetGuid() == orderId);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await stranger.GetAsync("/api/cash-collections?scope=mine")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await stranger.PostAsJsonAsync($"/api/cash-collections/{orderId}/accept", new { })).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent,
                (await driver.PostAsJsonAsync($"/api/cash-collections/{orderId}/accept", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await driver.PostAsJsonAsync($"/api/cash-collections/{orderId}/collect", new
                {
                    lines = new[]
                    {
                        new { denomination = 500_000, quantity = 1 },
                        new { denomination = 100_000, quantity = 3 },
                    },
                })).StatusCode);

            // Kế toán chỉ tạo/theo dõi lệnh; không được đứng vai Thủ quỹ để nhận và kiểm đếm.
            Assert.Equal(HttpStatusCode.Forbidden,
                (await accountant.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
                {
                    lines = new[]
                    {
                        new { denomination = 500_000, quantity = 1 },
                        new { denomination = 100_000, quantity = 3 },
                    },
                })).StatusCode);
            Assert.Equal(0, await PaymentCountAsync(orderId));

            // Thủ quỹ đếm thiếu: ghi lần kiểm đếm và trạng thái sai lệch, tuyệt đối chưa giảm công nợ.
            var mismatch = await cashier.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
            {
                lines = new[] { new { denomination = 500_000, quantity = 1 } },
            });
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Equal(0, await PaymentCountAsync(orderId));

            // Đếm lại khớp: cùng transaction hoàn tất lệnh + tạo đúng một khoản thu công nợ.
            var completed = await cashier.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
            {
                lines = new[]
                {
                    new { denomination = 500_000, quantity = 1 },
                    new { denomination = 100_000, quantity = 3 },
                },
            });
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            Assert.Equal(1, await PaymentCountAsync(orderId));

            // Bấm lại/retry sau khi hoàn tất không được nhân đôi khoản thu.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await cashier.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
                {
                    lines = new[] { new { denomination = 100_000, quantity = 8 } },
                })).StatusCode);
            Assert.Equal(1, await PaymentCountAsync(orderId));

            var detail = await (await accountant.GetAsync($"/api/cash-collections/{orderId}"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Completed", detail.GetProperty("order").GetProperty("status").GetString());
            var actions = detail.GetProperty("events").EnumerateArray()
                .Select(x => x.GetProperty("action").GetString()).ToArray();
            Assert.Equal(new[] { "created", "accepted", "collected", "variance_detected", "completed" }, actions);

            var debt = await (await accountant.GetAsync($"/api/debts/{world.CustomerId}"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(debt.GetProperty("transactions").EnumerateArray(), x =>
                x.GetProperty("kind").GetString() == "payment" && x.GetProperty("credit").GetDecimal() == 800_000m);

            // Nhật ký tiền là append-only ở tầng DB.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            var eventId = detail.GetProperty("events")[0].GetProperty("id").GetGuid();
            var ex = await Assert.ThrowsAsync<PostgresException>(() => conn.Cmd(
                    "UPDATE cash_collection_events SET note='tampered' WHERE id=@id")
                .With("@id", eventId).ExecuteNonQueryAsync());
            Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await CleanupAsync(world, orderId);
        }
    }

    [Fact]
    public async Task VarianceResolution_SeparatesActors_AndRequiresChiefApprovalBeforePostingDebt()
    {
        var world = await SetupAsync();
        Guid orderId = Guid.Empty;
        try
        {
            using var accountant = Client(world.AccountantToken);
            using var cashier = Client(world.CashierToken);
            using var driver = Client(world.DriverToken);
            using var chief = Client(world.ChiefToken);

            // Dù được cấp kiêm nhiệm Driver, người tạo không được tự chọn/giao lệnh cho chính mình.
            await GrantRoleAsync(world.AccountantUsername, AppRoles.Driver);
            var choices = await (await accountant.GetAsync("/api/cash-collections/drivers"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.DoesNotContain(choices.EnumerateArray(), x =>
                x.GetProperty("id").GetGuid() == world.AccountantEmployeeId);
            var selfAssigned = await accountant.PostAsJsonAsync("/api/cash-collections", new
            {
                customerId = world.CustomerId,
                driverEmployeeId = world.AccountantEmployeeId,
                expectedAmount = 800_000,
                scheduledDate = DateOnly.FromDateTime(DateTime.Today),
                handoverDueAt = DateTime.UtcNow.AddDays(1),
                note = "Không được tự giao",
            });
            Assert.Equal(HttpStatusCode.BadRequest, selfAssigned.StatusCode);

            var created = await accountant.PostAsJsonAsync("/api/cash-collections", new
            {
                customerId = world.CustomerId,
                driverEmployeeId = world.DriverEmployeeId,
                expectedAmount = 800_000,
                scheduledDate = DateOnly.FromDateTime(DateTime.Today),
                handoverDueAt = DateTime.UtcNow.AddDays(1),
                note = "Kiểm soát sai lệch",
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            orderId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NoContent,
                (await driver.PostAsJsonAsync($"/api/cash-collections/{orderId}/accept", new { })).StatusCode);

            // Thu lệch dự kiến bắt buộc phải có lý do.
            var noReason = await driver.PostAsJsonAsync($"/api/cash-collections/{orderId}/collect", new
            {
                lines = new[] { new { denomination = 100_000, quantity = 7 } },
            });
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            var collected = await driver.PostAsJsonAsync($"/api/cash-collections/{orderId}/collect", new
            {
                lines = new[] { new { denomination = 100_000, quantity = 7 } },
                reason = "Khách thanh toán một phần",
            });
            Assert.Equal(HttpStatusCode.OK, collected.StatusCode);

            // Cấp thêm Cashier cũng không giúp người tạo tự nhận tiền của chính lệnh mình tạo.
            await GrantRoleAsync(world.AccountantUsername, AppRoles.Cashier);
            var creatorReceives = await accountant.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
            {
                lines = new[] { new { denomination = 100_000, quantity = 7 } },
            });
            Assert.Equal(HttpStatusCode.BadRequest, creatorReceives.StatusCode);
            Assert.Equal(0, await PaymentCountAsync(orderId));

            // Thủ quỹ đếm khác tài xế: Kế toán trưởng trả về cho tài xế khai lại.
            Assert.Equal(HttpStatusCode.Conflict,
                (await cashier.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
                {
                    lines = new[] { new { denomination = 100_000, quantity = 6 } },
                })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await accountant.PostAsJsonAsync($"/api/cash-collections/{orderId}/resolve", new
                {
                    action = "return_to_driver", reason = "Không có quyền xử lý",
                })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await cashier.PostAsJsonAsync($"/api/cash-collections/{orderId}/resolve", new
                {
                    action = "return_to_driver", reason = "Thủ quỹ không được duyệt",
                })).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await chief.PostAsJsonAsync($"/api/cash-collections/{orderId}/resolve", new
                {
                    action = "return_to_driver", reason = "Yêu cầu hai bên kiểm đếm lại",
                })).StatusCode);

            // Tài xế khai lại đúng tiền thực tế; dù thủ quỹ khớp, vẫn chưa ghi công nợ vì lệch dự kiến.
            Assert.Equal(HttpStatusCode.OK,
                (await driver.PostAsJsonAsync($"/api/cash-collections/{orderId}/collect", new
                {
                    lines = new[] { new { denomination = 100_000, quantity = 7 } },
                    reason = "Khách chỉ thanh toán 700.000 đồng",
                })).StatusCode);
            var expectedVariance = await cashier.PostAsJsonAsync($"/api/cash-collections/{orderId}/receive", new
            {
                lines = new[] { new { denomination = 100_000, quantity = 7 } },
            });
            Assert.Equal(HttpStatusCode.Conflict, expectedVariance.StatusCode);
            Assert.Equal(0, await PaymentCountAsync(orderId));

            var approved = await chief.PostAsJsonAsync($"/api/cash-collections/{orderId}/resolve", new
            {
                action = "approve_actual", reason = "Đã đối chiếu khách thanh toán một phần",
            });
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.Equal(700_000m, (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amount").GetDecimal());
            Assert.Equal(1, await PaymentCountAsync(orderId));
            Assert.Equal(HttpStatusCode.BadRequest,
                (await chief.PostAsJsonAsync($"/api/cash-collections/{orderId}/resolve", new
                {
                    action = "approve_actual", reason = "Không được duyệt lặp",
                })).StatusCode);
            Assert.Equal(1, await PaymentCountAsync(orderId));

            var detail = await (await chief.GetAsync($"/api/cash-collections/{orderId}"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Completed", detail.GetProperty("order").GetProperty("status").GetString());
            var actions = detail.GetProperty("events").EnumerateArray()
                .Select(x => x.GetProperty("action").GetString()).ToArray();
            Assert.Contains("variance_returned", actions);
            Assert.Contains("recollected", actions);
            Assert.Contains("expected_variance_detected", actions);
            Assert.Contains("variance_resolved", actions);
            Assert.Equal(2, detail.GetProperty("counts").EnumerateArray()
                .Count(x => x.GetProperty("stage").GetString() == "driver"));
        }
        finally
        {
            await CleanupAsync(world, orderId);
        }
    }

    private sealed record World(Guid AccountingDepartmentId, Guid DriverDepartmentId, Guid CustomerId,
        Guid AccountantEmployeeId, Guid ChiefEmployeeId, Guid DriverEmployeeId, Guid StrangerEmployeeId,
        string AccountantUsername, string ChiefUsername, string CashierUsername, string DriverUsername,
        string StrangerUsername, string AccountantToken, string ChiefToken, string CashierToken,
        string DriverToken, string StrangerToken);

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var accountingDept = Guid.NewGuid();
        var driverDept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var accountant = "__cc_accountant_" + suffix;
        var chief = "__cc_chief_" + suffix;
        var cashier = "__cc_cashier_" + suffix;
        var driver = "__cc_driver_" + suffix;
        var stranger = "__cc_stranger_" + suffix;
        var accountantEmployee = Guid.NewGuid();
        var chiefEmployee = Guid.NewGuid();
        var driverEmployee = Guid.NewGuid();
        var strangerEmployee = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", accountingDept).With("@code", "CCK" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,FALSE)")
            .With("@id", driverDept).With("@code", "CCL" + suffix[..5]).With("@name", "Lái xe " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,phone,is_active) VALUES (@id,@name,@phone,TRUE)")
            .With("@id", customerId).With("@name", "Khách thu tiền " + suffix).With("@phone", "0900000000")
            .ExecuteNonQueryAsync();

        var accountantToken = await MakeUser(conn, tokens, accountant, AppRoles.Accounting, accountingDept, accountantEmployee);
        var chiefToken = await MakeUser(conn, tokens, chief, AppRoles.ChiefAccountant, accountingDept, chiefEmployee);
        var cashierToken = await MakeUser(conn, tokens, cashier, AppRoles.Cashier, accountingDept, Guid.NewGuid());
        var driverToken = await MakeUser(conn, tokens, driver, AppRoles.Driver, driverDept, driverEmployee);
        var strangerToken = await MakeUser(conn, tokens, stranger, AppRoles.Employee, driverDept, strangerEmployee);
        return new(accountingDept, driverDept, customerId, accountantEmployee, chiefEmployee, driverEmployee,
            strangerEmployee, accountant, chief, cashier, driver, stranger, accountantToken, chiefToken,
            cashierToken, driverToken, strangerToken);
    }

    private static async Task<string> MakeUser(NpgsqlConnection conn, TokenService tokens, string username,
        string role, Guid departmentId, Guid employeeId)
    {
        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id,username,full_name,email,role,password_hash,is_active,
                approval_status,approved_at,approved_by,created_at,is_deleted)
            VALUES (@id,@u,@u,'',@role,@hash,TRUE,'Approved',CURRENT_TIMESTAMP,'test',CURRENT_TIMESTAMP,FALSE)
            """).With("@id", userId).With("@u", username).With("@role", role)
            .With("@hash", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id,employee_code,user_id,username,full_name,department_id,status,position)
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active',@position)
            """).With("@id", employeeId).With("@code", "CC" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .With("@position", role == AppRoles.Driver ? "Lái xe" : role == AppRoles.Accounting ? "Kế toán" :
                role == AppRoles.ChiefAccountant ? "Kế toán trưởng" :
                role == AppRoles.Cashier ? "Thủ quỹ" : "Nhân viên")
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:cc:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task<int> PaymentCountAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT COUNT(*) FROM payments WHERE source_kind='cash_collection' AND source_id=@id")
            .With("@id", orderId).ExecuteScalarAsync() ?? 0);
    }

    private async Task GrantRoleAsync(string username, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO user_roles (username,role,granted_by,granted_at)
            VALUES (@u,@r,'cash-collection-test',CURRENT_TIMESTAMP)
            ON CONFLICT (username,role) DO UPDATE SET expires_at=NULL
            """).With("@u", username).With("@r", role).ExecuteNonQueryAsync();
        await conn.Cmd("""
            UPDATE app_users SET authorization_version=COALESCE(authorization_version,1)+1
            WHERE lower(username)=lower(@u)
            """).With("@u", username).ExecuteNonQueryAsync();
    }

    private async Task CleanupAsync(World world, Guid orderId)
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            if (orderId != Guid.Empty)
            {
                await conn.Cmd("DELETE FROM cash_count_lines WHERE session_id IN (SELECT id FROM cash_count_sessions WHERE order_id=@id)")
                    .With("@id", orderId).ExecuteNonQueryAsync();
                await conn.Cmd("DELETE FROM cash_count_sessions WHERE order_id=@id").With("@id", orderId).ExecuteNonQueryAsync();
                await conn.Cmd("UPDATE cash_collection_orders SET payment_id=NULL WHERE id=@id").With("@id", orderId).ExecuteNonQueryAsync();
                await conn.Cmd("DELETE FROM payments WHERE source_kind='cash_collection' AND source_id=@id").With("@id", orderId).ExecuteNonQueryAsync();
                await conn.Cmd("DELETE FROM cash_collection_orders WHERE id=@id").With("@id", orderId).ExecuteNonQueryAsync();
            }
            await conn.Cmd("DELETE FROM customers WHERE id=@id").With("@id", world.CustomerId).ExecuteNonQueryAsync();
            var usernames = new[]
            {
                world.AccountantUsername, world.ChiefUsername, world.CashierUsername, world.DriverUsername,
                world.StrangerUsername,
            };
            await conn.Cmd("DELETE FROM user_roles WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM hr_employees WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM user_sessions WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM app_users WHERE username=ANY(@u)").With("@u", usernames).ExecuteNonQueryAsync();
            await conn.Cmd("DELETE FROM hr_departments WHERE id=ANY(@ids)")
                .With("@ids", new[] { world.AccountingDepartmentId, world.DriverDepartmentId }).ExecuteNonQueryAsync();
        }
        catch { /* dọn dữ liệu test best-effort; event tài chính cố ý được giữ lại */ }
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
