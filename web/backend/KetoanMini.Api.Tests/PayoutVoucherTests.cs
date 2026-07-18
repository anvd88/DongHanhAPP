using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Phiếu chi tiền mặt: chốt hai bất biến của nghiệp vụ —
/// (1) chỉ role Accounting THUỘC phòng ban is_accounting mới lập/duyệt chi được (Admin cũng không);
/// (2) không có chữ ký điện tử của người nhận (quét QR) thì KHÔNG duyệt chi được.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PayoutVoucherTests
{
    private readonly ApiFactory _factory;
    public PayoutVoucherTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CashPayout_RequiresRecipientQrSignature_BeforeAccountantCanApprove()
    {
        var world = await SetupAsync();
        try
        {
            var cashier = Client(world.CashierToken);
            var worker = Client(world.WorkerToken);

            // --- Lập phiếu chi tay (VD: đưa tiền cho nhân viên đi mua dầu) ---
            var created = await cashier.PostAsJsonAsync("/api/payout-vouchers", new
            {
                sourceKind = "manual",
                categoryId = world.FuelCategoryId,
                employeeId = world.WorkerEmployeeId,
                amount = 500_000,
                reason = "Mua dầu chạy máy phát",
                note = "",
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var voucherId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var fresh = await FindVoucherAsync(cashier, voucherId);
            Assert.Equal("AwaitingScan", fresh.GetProperty("status").GetString());
            var qrValue = fresh.GetProperty("qrValue").GetString()!;
            Assert.StartsWith("ketoanmini-payout:", qrValue, StringComparison.Ordinal);

            // --- CHỐT CHỐNG GIAN LẬN: chưa ai ký nhận thì không duyệt chi được ---
            var tooEarly = await cashier.PostAsJsonAsync($"/api/payout-vouchers/{voucherId}/approve", new { });
            Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);

            // --- Người khác quét hộ: bị từ chối và KHÔNG lộ số tiền/tên người nhận ---
            var stranger = await cashier.PostAsJsonAsync("/api/qr/resolve", ResolveBody(qrValue));
            Assert.Equal(HttpStatusCode.OK, stranger.StatusCode);
            var strangerEnvelope = await stranger.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(strangerEnvelope.GetProperty("actions").EnumerateArray());
            var strangerText = strangerEnvelope.GetProperty("presentation").GetProperty("message").GetString() ?? "";
            Assert.DoesNotContain("500", strangerText, StringComparison.Ordinal);
            Assert.DoesNotContain(world.WorkerFullName, strangerText, StringComparison.Ordinal);

            // --- Đúng người nhận quét: server trả hộp thoại xác nhận kèm vé quyết định ---
            var resolved = await worker.PostAsJsonAsync("/api/qr/resolve", ResolveBody(qrValue));
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
            var envelope = await resolved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(envelope.GetProperty("unhandled").GetBoolean());
            var decisionToken = envelope.GetProperty("decisionToken").GetString()!;
            Assert.Contains("500.000", envelope.GetProperty("presentation").GetProperty("message").GetString()!,
                StringComparison.Ordinal);
            Assert.Contains(envelope.GetProperty("actions").EnumerateArray(),
                a => a.GetProperty("id").GetString() == "payout_confirm" &&
                     a.GetProperty("type").GetString() == "server_decision");

            // Vé của người này không dùng được ở tài khoản khác.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await cashier.PostAsJsonAsync("/api/qr/decision",
                    new { decisionToken, actionId = "payout_confirm" })).StatusCode);

            // --- Người nhận ký nhận ---
            var confirm = await worker.PostAsJsonAsync("/api/qr/decision", new { decisionToken, actionId = "payout_confirm" });
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Equal("Confirmed", (await FindVoucherAsync(cashier, voucherId)).GetProperty("status").GetString());

            // Ký nhận lần hai bằng vé cũ không đổi được gì (mã QR đã bị thu hồi).
            Assert.Equal(HttpStatusCode.BadRequest,
                (await worker.PostAsJsonAsync("/api/qr/decision",
                    new { decisionToken, actionId = "payout_confirm" })).StatusCode);

            // --- Giờ mới duyệt chi được ---
            Assert.Equal(HttpStatusCode.NoContent,
                (await cashier.PostAsJsonAsync($"/api/payout-vouchers/{voucherId}/approve", new { })).StatusCode);
            var paid = await FindVoucherAsync(cashier, voucherId);
            Assert.Equal("Paid", paid.GetProperty("status").GetString());
            // Chi xong thì mã QR phải biến mất khỏi phiếu (API bỏ hẳn trường null — xem DefaultIgnoreCondition).
            Assert.True(!paid.TryGetProperty("qrValue", out var leftoverQr) ||
                        leftoverQr.ValueKind is JsonValueKind.Null);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task OnlyAccountingRoleInsideAccountingDepartment_CanCreateVouchers()
    {
        var world = await SetupAsync();
        try
        {
            object body = new
            {
                sourceKind = "manual",
                categoryId = world.FuelCategoryId,
                employeeId = world.WorkerEmployeeId,
                amount = 100_000,
                reason = "Thử quyền",
            };

            // Nhân viên thường (dù NGỒI ở phòng kế toán) → không được.
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(world.WorkerToken).PostAsJsonAsync("/api/payout-vouchers", body)).StatusCode);

            // Admin → cố ý KHÔNG được chi tiền (tách quyền quản trị với quyền chi).
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(world.AdminToken).PostAsJsonAsync("/api/payout-vouchers", body)).StatusCode);

            // Có role Accounting nhưng KHÔNG thuộc phòng kế toán → không được.
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Client(world.OutsiderAccountantToken).PostAsJsonAsync("/api/payout-vouchers", body)).StatusCode);

            // Đủ cả hai điều kiện → được.
            Assert.Equal(HttpStatusCode.OK,
                (await Client(world.CashierToken).PostAsJsonAsync("/api/payout-vouchers", body)).StatusCode);
        }
        finally
        {
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task VoucherFromApprovedAppeal_TakesAmountFromRefund_AndSettlesItOnApproval()
    {
        var world = await SetupAsync();
        var refundId = Guid.NewGuid();
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync();
                await conn.Cmd("""
                    INSERT INTO hr_penalty_refunds (id, refund_no, employee_id, penalty_no, appeal_request_no,
                        amount, reason, status, created_by)
                    VALUES (@id, @no, @emp, 'PH99999', 'DT99999', 1234500, 'Hoàn tiền phạt do khiếu nại được duyệt',
                        'PendingAccounting', 'test')
                    """)
                    .With("@id", refundId).With("@no", "HP" + Guid.NewGuid().ToString("N")[..6])
                    .With("@emp", world.WorkerEmployeeId).ExecuteNonQueryAsync();
            }

            var cashier = Client(world.CashierToken);

            // Khoản hoàn phải xuất hiện trong danh sách nguồn để kế toán chọn.
            var sources = await (await cashier.GetAsync("/api/payout-vouchers/sources/refunds"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(sources.EnumerateArray(), s => s.GetProperty("id").GetGuid() == refundId);

            // Chọn đơn → số tiền LẤY TỪ ĐƠN, không tin số client gửi lên.
            var created = await cashier.PostAsJsonAsync("/api/payout-vouchers", new
            {
                sourceKind = "refund",
                sourceId = refundId,
                amount = 1, // cố tình sai
            });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var voucherId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

            var voucher = await FindVoucherAsync(cashier, voucherId);
            Assert.Equal(1234500m, voucher.GetProperty("amount").GetDecimal());
            Assert.Equal("penalty-refund", voucher.GetProperty("categoryCode").GetString());

            // Lập phiếu = chốt hình thức "chi tiền mặt" cho khoản hoàn đó.
            Assert.Equal(("Approved", "cash"), await RefundStateAsync(refundId));

            // Cùng một khoản hoàn không lập được hai phiếu.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await cashier.PostAsJsonAsync("/api/payout-vouchers",
                    new { sourceKind = "refund", sourceId = refundId })).StatusCode);

            // Người nhận ký nhận rồi kế toán duyệt chi → khoản hoàn coi như đã trả xong.
            var qrValue = voucher.GetProperty("qrValue").GetString()!;
            var worker = Client(world.WorkerToken);
            var envelope = await (await worker.PostAsJsonAsync("/api/qr/resolve", ResolveBody(qrValue)))
                .Content.ReadFromJsonAsync<JsonElement>();
            await worker.PostAsJsonAsync("/api/qr/decision", new
            {
                decisionToken = envelope.GetProperty("decisionToken").GetString(),
                actionId = "payout_confirm",
            });
            Assert.Equal(HttpStatusCode.NoContent,
                (await cashier.PostAsJsonAsync($"/api/payout-vouchers/{voucherId}/approve", new { })).StatusCode);
            Assert.Equal(("Paid", "cash"), await RefundStateAsync(refundId));
        }
        finally
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync();
                await conn.Cmd("DELETE FROM hr_penalty_refunds WHERE id=@id").With("@id", refundId).ExecuteNonQueryAsync();
            }
            await CleanupAsync(world);
        }
    }

    [Fact]
    public async Task CancellingVoucher_ReturnsRefundToAccountingQueue()
    {
        var world = await SetupAsync();
        var refundId = Guid.NewGuid();
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync();
                await conn.Cmd("""
                    INSERT INTO hr_penalty_refunds (id, refund_no, employee_id, penalty_no, appeal_request_no,
                        amount, reason, status, created_by)
                    VALUES (@id, @no, @emp, 'PH99998', 'DT99998', 250000, 'Hoàn tiền phạt', 'PendingAccounting', 'test')
                    """)
                    .With("@id", refundId).With("@no", "HP" + Guid.NewGuid().ToString("N")[..6])
                    .With("@emp", world.WorkerEmployeeId).ExecuteNonQueryAsync();
            }

            var cashier = Client(world.CashierToken);
            var created = await cashier.PostAsJsonAsync("/api/payout-vouchers",
                new { sourceKind = "refund", sourceId = refundId });
            var voucherId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            Assert.Equal(("Approved", "cash"), await RefundStateAsync(refundId));

            Assert.Equal(HttpStatusCode.NoContent,
                (await cashier.PostAsJsonAsync($"/api/payout-vouchers/{voucherId}/cancel",
                    new { reason = "Nhân viên chọn cộng vào lương" })).StatusCode);

            // Hủy phiếu phải trả khoản hoàn về hàng chờ, nếu không tiền của nhân viên sẽ kẹt vĩnh viễn.
            Assert.Equal(("PendingAccounting", ""), await RefundStateAsync(refundId));
            Assert.Equal("Cancelled", (await FindVoucherAsync(cashier, voucherId)).GetProperty("status").GetString());
        }
        finally
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Database>();
                await using var conn = await db.OpenAsync();
                await conn.Cmd("DELETE FROM hr_penalty_refunds WHERE id=@id").With("@id", refundId).ExecuteNonQueryAsync();
            }
            await CleanupAsync(world);
        }
    }

    // ---------------- Dựng & dọn dữ liệu test ----------------

    private sealed record World(
        string Suffix, Guid AccountingDeptId, Guid OtherDeptId, Guid WorkerEmployeeId, string WorkerFullName,
        Guid FuelCategoryId, string CashierToken, string WorkerToken, string AdminToken, string OutsiderAccountantToken,
        string[] Usernames);

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var accountingDeptId = Guid.NewGuid();
        var otherDeptId = Guid.NewGuid();
        var workerEmployeeId = Guid.NewGuid();
        var workerName = "NV Kho " + suffix;

        var cashierUser = $"__pv_cashier_{suffix}";
        var workerUser = $"__pv_worker_{suffix}";
        var adminUser = $"__pv_admin_{suffix}";
        var outsiderUser = $"__pv_outsider_{suffix}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        await conn.Cmd("INSERT INTO hr_departments (id, code, name, is_accounting) VALUES (@id, @c, @n, TRUE)")
            .With("@id", accountingDeptId).With("@c", "KT" + suffix[..4]).With("@n", "Phòng Kế Toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_departments (id, code, name, is_accounting) VALUES (@id, @c, @n, FALSE)")
            .With("@id", otherDeptId).With("@c", "KHO" + suffix[..4]).With("@n", "Phòng Kho " + suffix)
            .ExecuteNonQueryAsync();

        var cashierToken = await MakeUserAsync(conn, tokens, cashierUser, AppRoles.Accounting, accountingDeptId, "KT " + suffix, Guid.NewGuid());
        // Nhân viên nhận tiền: ngồi ở phòng kho, KHÔNG có quyền kế toán.
        var workerToken = await MakeUserAsync(conn, tokens, workerUser, AppRoles.Employee, otherDeptId, workerName, workerEmployeeId);
        var adminToken = await MakeUserAsync(conn, tokens, adminUser, AppRoles.Admin, accountingDeptId, "Admin " + suffix, Guid.NewGuid());
        var outsiderToken = await MakeUserAsync(conn, tokens, outsiderUser, AppRoles.Accounting, otherDeptId, "KT ngoài " + suffix, Guid.NewGuid());

        var fuelCategoryId = (Guid)(await conn.Cmd("SELECT id FROM hr_payout_categories WHERE code='fuel'")
            .ExecuteScalarAsync())!;

        return new World(suffix, accountingDeptId, otherDeptId, workerEmployeeId, workerName, fuelCategoryId,
            cashierToken, workerToken, adminToken, outsiderToken,
            [cashierUser, workerUser, adminUser, outsiderUser]);
    }

    private static async Task<string> MakeUserAsync(Npgsql.NpgsqlConnection conn, TokenService tokens,
        string username, string role, Guid departmentId, string fullName, Guid employeeId)
    {
        var userId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active,
                approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @n, '', @r, @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            """)
            .With("@id", userId).With("@u", username).With("@n", fullName).With("@r", role)
            .With("@ph", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO hr_employees (id, employee_code, user_id, username, full_name, department_id, status)
            VALUES (@id, @code, @uid, @u, @n, @dept, 'Active')
            """)
            .With("@id", employeeId).With("@code", "E" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@n", fullName).With("@dept", departmentId)
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(
            new UserDto(userId, username, fullName, "", role, true, "Approved", DateTime.UtcNow),
            "app:pv:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task CleanupAsync(World world)
    {
        try
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await conn.Cmd("""
                DELETE FROM hr_payout_vouchers WHERE employee_id IN
                    (SELECT id FROM hr_employees WHERE username = ANY(@u))
                """).With("@u", world.Usernames).ExecuteNonQueryAsync();
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

    private async Task<(string Status, string PayoutMethod)> RefundStateAsync(Guid refundId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await using var r = await conn.Cmd("SELECT status, payout_method FROM hr_penalty_refunds WHERE id=@id")
            .With("@id", refundId).ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.Str("status"), r.Str("payout_method"));
    }

    private static async Task<JsonElement> FindVoucherAsync(HttpClient client, Guid voucherId)
    {
        var list = await (await client.GetAsync("/api/payout-vouchers?scope=all")).Content
            .ReadFromJsonAsync<JsonElement>();
        foreach (var v in list.EnumerateArray())
            if (v.GetProperty("id").GetGuid() == voucherId)
                return v;
        throw new Xunit.Sdk.XunitException($"Không tìm thấy phiếu chi {voucherId} trong sổ.");
    }

    private HttpClient Client(string token)
    {
        var c = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static object ResolveBody(string value) => new
    {
        value,
        protocolVersion = 1,
        capabilities = new[] { "server_decision", "open_https_url", "dismiss" },
        clientVersionCode = 1,
    };
}
