using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp engine khấu trừ phạt theo SỔ CÁI (hr_penalty_ledger):
///  • Trừ theo lương còn có thể trừ (cap) + chuyển phần thiếu sang kỳ sau (carry-over).
///  • Tổng thực thu KHÔNG BAO GIỜ vượt mức phạt; thu đủ → "Đã tất toán" (Settled), dừng trừ.
///  • Nhiều phạt cùng cạnh tranh lương: cũ trước, phần thiếu chuyển kỳ sau.
///  • Xóa/nháp phiếu lương gỡ ghi sổ và trả "Đã tất toán" về "Còn hiệu lực".
/// Chạy trên DATABASE TEST tách riêng (ApiFactory) nên không đụng dữ liệu vận hành.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PenaltyLedgerTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly string _user = $"__test_pen_{Guid.NewGuid():N}__";
    private readonly string _approver = $"__test_pen_appr_{Guid.NewGuid():N}__";
    private Guid _empId;

    public PenaltyLedgerTests(ApiFactory factory) => _factory = factory;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var db = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<Database>();
        return await db.OpenAsync();
    }

    public async Task InitializeAsync()
    {
        await using var conn = await OpenAsync();
        _empId = Guid.NewGuid();
        await conn.Cmd("INSERT INTO hr_employees (id, username, full_name) VALUES (@id, @u, 'Pen Ledger User')")
            .With("@id", _empId).With("@u", _user).ExecuteNonQueryAsync();
        // Người duyệt đơn khiếu nại (cần app_users để cấp token gọi endpoint approve).
        await conn.Cmd("""
            INSERT INTO app_users (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES (@id, @u, @u, '', 'Manager', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
            ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role='Manager'
            """).With("@id", Guid.NewGuid()).With("@u", _approver).With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = await OpenAsync();
        // CASCADE: xóa nhân viên kéo theo hr_penalties → hr_penalty_ledger, hr_requests, hr_penalty_refunds.
        await conn.Cmd("DELETE FROM hr_employees WHERE id=@id").With("@id", _empId).ExecuteNonQueryAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", _approver).ExecuteNonQueryAsync();
    }

    private async Task<Guid> AddFineAsync(NpgsqlConnection conn, decimal amount, int installments,
        string startPeriod, string status = "Active")
    {
        var id = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO hr_penalties (id, penalty_no, employee_id, penalty_type, penalty_date, amount,
                installments, start_period, reason, status)
            VALUES (@id, @no, @emp, 'fine', @date, @amt, @inst, @start, 'test', @st)
            """)
            .With("@id", id).With("@no", $"PT{id.ToString()[..6]}").With("@emp", _empId)
            .With("@date", DateOnly.Parse(startPeriod + "-01")).With("@amt", amount)
            .With("@inst", installments).With("@start", startPeriod).With("@st", status)
            .ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<string> StatusAsync(NpgsqlConnection conn, Guid penaltyId)
        => (await conn.Cmd("SELECT status FROM hr_penalties WHERE id=@id").With("@id", penaltyId)
            .ExecuteScalarAsync()) as string ?? "";

    /// <summary>Phạt 10tr, lương chỉ 8tr → trừ 8tr kỳ này (không âm lương), 2tr chuyển kỳ sau rồi tất toán.</summary>
    [Fact]
    public async Task Cap_By_Salary_Then_CarryOver_To_Next_Month()
    {
        await using var conn = await OpenAsync();
        var pen = await AddFineAsync(conn, 10_000_000m, 1, "2026-05");

        // Kỳ 05: lương còn 8tr → chỉ trừ được 8tr.
        var (total5, lines5) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-05", 8_000_000m);
        Assert.Equal(8_000_000m, total5);
        await PenaltyEndpoints.RecordDeductionsAsync(conn, _empId, "2026-05", lines5);
        Assert.Equal(8_000_000m, await PenaltyEndpoints.GetCollectedAsync(conn, pen));
        Assert.Equal("Active", await StatusAsync(conn, pen));   // chưa đủ → còn hiệu lực

        // Kỳ 06: còn nợ 2tr, lương dư → trừ nốt 2tr, không thu quá.
        var (total6, lines6) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-06", 8_000_000m);
        Assert.Equal(2_000_000m, total6);
        await PenaltyEndpoints.RecordDeductionsAsync(conn, _empId, "2026-06", lines6);
        Assert.Equal(10_000_000m, await PenaltyEndpoints.GetCollectedAsync(conn, pen));
        Assert.Equal("Settled", await StatusAsync(conn, pen));   // thu đủ → đã tất toán

        // Kỳ 07: đã tất toán → không trừ thêm đồng nào.
        var (total7, _) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-07", 99_000_000m);
        Assert.Equal(0m, total7);
    }

    /// <summary>Đã trừ đủ 10tr; khiếu nại giảm còn 5tr → hoàn 5tr, tất toán, các kỳ sau không trừ (mô phỏng theo công thức appeal).</summary>
    [Fact]
    public async Task Reduce_After_FullyDeducted_Refunds_Excess_And_Settles()
    {
        await using var conn = await OpenAsync();
        var pen = await AddFineAsync(conn, 10_000_000m, 1, "2026-05");
        var (_, lines) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-05", 99_000_000m);
        await PenaltyEndpoints.RecordDeductionsAsync(conn, _empId, "2026-05", lines);

        var collected = await PenaltyEndpoints.GetCollectedAsync(conn, pen);
        Assert.Equal(10_000_000m, collected);

        // Logic khiếu nại "giảm còn 5tr" (ApplyPenaltyAppeal): refund = max(0, collected - newAmount); settled nếu đủ.
        const decimal newAmount = 5_000_000m;
        var refund = Math.Max(0m, collected - newAmount);
        Assert.Equal(5_000_000m, refund);
        var status = collected >= newAmount ? "Settled" : "Active";
        await conn.Cmd("UPDATE hr_penalties SET amount=@amt, status=@st WHERE id=@id")
            .With("@id", pen).With("@amt", newAmount).With("@st", status).ExecuteNonQueryAsync();
        Assert.Equal("Settled", await StatusAsync(conn, pen));

        // Sau khi tất toán: các kỳ sau KHÔNG bị trừ lại (đây là lỗi cũ đã sửa).
        var (totalNext, _) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-06", 99_000_000m);
        Assert.Equal(0m, totalNext);
    }

    /// <summary>Hai phạt cùng cạnh tranh lương hạn hẹp: phạt CŨ được ưu tiên; phần thiếu của phạt mới chuyển kỳ sau.</summary>
    [Fact]
    public async Task Two_Fines_Share_Limited_Salary_OldestFirst()
    {
        await using var conn = await OpenAsync();
        var old = await AddFineAsync(conn, 3_000_000m, 1, "2026-05");
        await Task.Delay(5);                                     // đảm bảo created_at khác nhau
        var recent = await AddFineAsync(conn, 3_000_000m, 1, "2026-05");

        // Lương chỉ đủ 4tr: phạt cũ lấy đủ 3tr, phạt mới chỉ 1tr.
        var (total, lines) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-05", 4_000_000m);
        Assert.Equal(4_000_000m, total);
        await PenaltyEndpoints.RecordDeductionsAsync(conn, _empId, "2026-05", lines);
        Assert.Equal(3_000_000m, await PenaltyEndpoints.GetCollectedAsync(conn, old));
        Assert.Equal(1_000_000m, await PenaltyEndpoints.GetCollectedAsync(conn, recent));
        Assert.Equal("Settled", await StatusAsync(conn, old));
        Assert.Equal("Active", await StatusAsync(conn, recent));

        // Kỳ sau: phạt mới còn nợ 2tr được thu nốt.
        var (total6, _) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-06", 99_000_000m);
        Assert.Equal(2_000_000m, total6);
    }

    /// <summary>Xóa ghi sổ một kỳ (xóa/nháp phiếu lương) trả "Đã tất toán" về "Còn hiệu lực" để thu lại phần đã gỡ.</summary>
    [Fact]
    public async Task Clearing_A_Period_Reactivates_A_Settled_Fine()
    {
        await using var conn = await OpenAsync();
        var pen = await AddFineAsync(conn, 5_000_000m, 1, "2026-05");
        var (_, lines) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-05", 99_000_000m);
        await PenaltyEndpoints.RecordDeductionsAsync(conn, _empId, "2026-05", lines);
        Assert.Equal("Settled", await StatusAsync(conn, pen));

        await PenaltyEndpoints.ClearDeductionsForPeriod(conn, _empId, "2026-05");
        Assert.Equal(0m, await PenaltyEndpoints.GetCollectedAsync(conn, pen));
        Assert.Equal("Active", await StatusAsync(conn, pen));

        // Thu lại được toàn bộ ở kỳ sau.
        var (total, _) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-06", 99_000_000m);
        Assert.Equal(5_000_000m, total);
    }

    /// <summary>
    /// ĐẦU-CUỐI đúng lỗi người dùng báo: phạt 10tr đã trừ đủ → nhân viên khiếu nại giảm còn 5tr, người
    /// duyệt duyệt qua HTTP → sinh khoản hoàn 5tr, phạt "Đã tất toán", các kỳ sau KHÔNG bị trừ lại.
    /// </summary>
    [Fact]
    public async Task Appeal_Reduce_EndToEnd_Refunds_Settles_NoFutureDeduction()
    {
        Guid pen; string penaltyNo;
        await using (var conn = await OpenAsync())
        {
            pen = await AddFineAsync(conn, 10_000_000m, 1, "2026-05");
            penaltyNo = (await conn.Cmd("SELECT penalty_no FROM hr_penalties WHERE id=@id").With("@id", pen)
                .ExecuteScalarAsync()) as string ?? "";
            // Trừ đủ 10tr ở kỳ 05 (ghi sổ) → phạt tất toán.
            var (_, lines) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-05", 99_000_000m);
            await PenaltyEndpoints.RecordDeductionsAsync(conn, _empId, "2026-05", lines);

            // Đơn khiếu nại "giảm còn 5tr", giao đúng người duyệt.
            var reqId = Guid.NewGuid();
            await conn.Cmd("""
                INSERT INTO hr_requests (id, request_no, req_type, title, employee_id, requester_username, payload, status, current_step)
                VALUES (@id, @no, 'penalty_appeal', 'Khiếu nại phạt', @emp, @req,
                        @payload::jsonb, 'Pending', 1)
                """)
                .With("@id", reqId).With("@no", $"AP-{reqId.ToString()[..8]}").With("@emp", _empId).With("@req", _user)
                .With("@payload", $"{{\"penaltyNo\":\"{penaltyNo}\",\"appealKind\":\"reduce\",\"requestedAmount\":5000000}}")
                .ExecuteNonQueryAsync();
            await conn.Cmd("""
                INSERT INTO hr_request_approvals (request_id, step_no, approver_role, approver_username, approver_name, status)
                VALUES (@id, 1, 'Manager', @u, 'Approver', 'Pending')
                """).With("@id", reqId).With("@u", _approver).ExecuteNonQueryAsync();

            var client = await ClientAsAsync(_approver);
            var res = await client.PostAsJsonAsync($"/api/requests/{reqId}/approve",
                new { penaltyOutcome = "reduce", newAmount = 5_000_000 });
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        }

        await using (var conn = await OpenAsync())
        {
            // Phạt: giảm còn 5tr + đã tất toán.
            await using var r = await conn.Cmd("SELECT amount, status FROM hr_penalties WHERE id=@id")
                .With("@id", pen).ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(5_000_000m, r.Dec("amount"));
            Assert.Equal("Settled", r.Str("status"));
        }

        await using (var conn = await OpenAsync())
        {
            // Khoản hoàn 5tr (đã thu 10tr − mức mới 5tr) chờ kế toán.
            var refund = (await conn.Cmd("""
                SELECT COALESCE(SUM(amount),0)::numeric FROM hr_penalty_refunds
                WHERE penalty_id=@id AND status='PendingAccounting'
                """).With("@id", pen).ExecuteScalarAsync());
            Assert.Equal(5_000_000m, Convert.ToDecimal(refund));

            // Không còn trừ ở kỳ sau.
            var (total, _) = await PenaltyEndpoints.ComputeDeductionsAsync(conn, _empId, "2026-06", 99_000_000m);
            Assert.Equal(0m, total);
        }
    }

    private async Task<HttpClient> ClientAsAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var conn = await scope.ServiceProvider.GetRequiredService<Database>().OpenAsync();
        await using var _ = conn;
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", username).ExecuteScalarAsync())!;
        var token = scope.ServiceProvider.GetRequiredService<TokenService>()
            .CreateToken(new UserDto(id, username, username, "", AppRoles.Manager, true, "Approved", DateTime.UtcNow));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
