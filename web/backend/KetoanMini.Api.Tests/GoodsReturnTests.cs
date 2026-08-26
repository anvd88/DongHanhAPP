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

/// <summary>
/// Hàng khách trả về — khách không nhận, hoặc nhận rồi trả lại một phần.
///
/// Bất biến đắt nhất ở đây là GIÁ: hệ thống không có bảng giá, cùng một mặt hàng mỗi đơn một giá.
/// Trả nhầm giá của đơn khác là trừ sai công nợ, mà sai kiểu này không ai phát hiện ra cho tới lúc
/// đối chiếu với khách. Kế đó là chống trừ hai lần: một dòng hàng trả chỉ được ghi bằng ĐÚNG MỘT
/// đường (hạ số trên phiếu chưa chốt, hoặc phiếu trả hàng), và không bao giờ trả quá số đã bán.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GoodsReturnTests
{
    private readonly ApiFactory _factory;
    public GoodsReturnTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// Cùng "Thép tấm 10mm" nhưng đơn tháng trước bán 12.000, đơn hôm nay 13.000. Trả hàng của đơn
    /// CŨ phải trừ công nợ theo giá CŨ.
    /// </summary>
    [Fact]
    public async Task ReturningGoodsFromAnOlderVoucher_UsesThatVouchersPrice_NotTheLatestOne()
    {
        var world = await SetupAsync();
        var older = await InsertVoucherAsync(world, "Thép tấm", "10mm", 1_000m, 12_000m, daysAgo: 30, settled: true);
        var newer = await InsertVoucherAsync(world, "Thép tấm", "10mm", 500m, 13_000m, daysAgo: 0, settled: true);
        var debtBefore = await BalanceAsync(world.CustomerId);
        Assert.Equal(12_000_000m + 6_500_000m, debtBefore);

        using var accountant = Client(world.AccountantToken);
        var sources = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/returns/sources?customerId={world.CustomerId}");
        var fromOlder = sources.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("documentId").GetGuid() == older);
        Assert.Equal(12_000m, fromOlder.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(1_000m, fromOlder.GetProperty("remaining").GetDecimal());

        var response = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Khách trả lại hàng tồn",
            lines = new[] { new { sourceDocumentId = older, sourceLineNo = 1, quantity = 100m } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // 100 × 12.000 của đơn cũ — KHÔNG phải 100 × 13.000 của đơn mới nhất.
        Assert.Equal(1_200_000m, body.GetProperty("returnTotal").GetDecimal());

        Assert.Equal(debtBefore - 1_200_000m, await BalanceAsync(world.CustomerId));
        // Đơn gốc giữ nguyên số đã in; hàng trả nằm ở chứng từ riêng.
        Assert.Equal(1_000m, await LineQuantityAsync(older));
        Assert.Equal(1, await ReturnNoteCountAsync(world.CustomerId));
        Assert.Equal(500m, await LineQuantityAsync(newer));
    }

    /// <summary>Trả quá số đã bán = chế ra hàng chưa từng giao. Cộng dồn nhiều lần cũng không được.</summary>
    [Fact]
    public async Task ReturningMoreThanWasSold_IsRefused_EvenAcrossSeveralReturns()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Tôn cuộn", "0.45", 300m, 20_000m, daysAgo: 10, settled: true);
        using var accountant = Client(world.AccountantToken);

        var first = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Trả đợt 1",
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 250m } },
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Trả đợt 2",
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 100m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("chỉ còn 50", await MessageAsync(second));

        // Lần trả thứ hai bị chặn thì công nợ chỉ được giảm đúng một lần.
        Assert.Equal(300m * 20_000m - 250m * 20_000m, await BalanceAsync(world.CustomerId));
        Assert.Equal(1, await ReturnNoteCountAsync(world.CustomerId));
    }

    /// <summary>
    /// Khách không nhận hàng ngay lúc giao, phiếu chưa chốt về kho ⇒ hạ thẳng số trên chính phiếu
    /// đó (chốt của người dùng). Không đẻ phiếu trả, nhưng phải để lại vết ở sổ sửa dòng.
    /// </summary>
    [Fact]
    public async Task ReturningToTheVoucherOnScreen_LowersItInPlace_AndLeavesAnEditTrail()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Thép hộp", "40x80", 200m, 15_000m, daysAgo: 0, settled: false);

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Khách không nhận, hàng quay đầu",
            contextDocumentId = voucher,
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 50m } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("adjustedLines").GetInt32());
        // Không sinh phiếu trả ⇒ không có id (máy chủ lược bỏ trường null khi trả JSON).
        Assert.False(body.TryGetProperty("returnId", out var id) && id.ValueKind != JsonValueKind.Null);

        Assert.Equal(150m, await LineQuantityAsync(voucher));
        Assert.Equal(0, await ReturnNoteCountAsync(world.CustomerId));
        Assert.Equal(150m * 15_000m, await BalanceAsync(world.CustomerId));
        Assert.Contains("Khách trả lại 50", await LastEditReasonAsync(voucher));
    }

    /// <summary>
    /// Phiếu đã xác nhận về kho là đã chốt sổ: dù kế toán đang mở đúng phiếu đó, hàng trả về vẫn
    /// phải đi bằng chứng từ riêng chứ không sửa lùi tờ phiếu khách đã ký.
    /// </summary>
    [Fact]
    public async Task ReturningToASettledVoucher_AlwaysCreatesAReturnNote_EvenFromItsOwnScreen()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Xà gồ", "C200", 80m, 30_000m, daysAgo: 3, settled: true);

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Khách trả lại hàng thừa",
            contextDocumentId = voucher,
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 20m } },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("adjustedLines").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("returnId").ValueKind);

        Assert.Equal(80m, await LineQuantityAsync(voucher));
        Assert.Equal(80m * 30_000m - 20m * 30_000m, await BalanceAsync(world.CustomerId));
    }

    /// <summary>Lập nhầm phải sửa được: hủy phiếu trả thì số đã trả nhả ra và công nợ trở lại.</summary>
    [Fact]
    public async Task CancellingAReturnNote_ReleasesTheQuantity_AndRestoresTheDebt()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Thép ống", "D60", 100m, 25_000m, daysAgo: 5, settled: true);
        using var accountant = Client(world.AccountantToken);

        var created = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Nhầm khách",
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 40m } },
        });
        var returnId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("returnId").GetGuid();
        Assert.Equal(60m * 25_000m, await BalanceAsync(world.CustomerId));

        var cancelled = await accountant.PutAsJsonAsync($"/api/returns/{returnId}/cancel",
            new { reason = "Ghi nhầm phiếu" });
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        Assert.Equal(100m * 25_000m, await BalanceAsync(world.CustomerId));
        var sources = await accountant.GetFromJsonAsync<JsonElement>(
            $"/api/returns/sources?customerId={world.CustomerId}");
        var line = sources.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(100m, line.GetProperty("remaining").GetDecimal());
    }

    /// <summary>Một phiếu trả chỉ của MỘT khách — trộn khách là trừ công nợ nhầm người.</summary>
    [Fact]
    public async Task MixingTwoCustomersInOneReturn_IsRefused()
    {
        var world = await SetupAsync();
        var other = await SetupAsync();
        var mine = await InsertVoucherAsync(world, "Thép tấm", "8mm", 100m, 11_000m, daysAgo: 2, settled: true);
        var theirs = await InsertVoucherAsync(other, "Thép tấm", "8mm", 100m, 11_000m, daysAgo: 2, settled: true);

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "Trả lẫn lộn",
            lines = new[]
            {
                new { sourceDocumentId = mine, sourceLineNo = 1, quantity = 10m },
                new { sourceDocumentId = theirs, sourceLineNo = 1, quantity = 10m },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cùng một khách", await MessageAsync(response));
        Assert.Equal(0, await ReturnNoteCountAsync(world.CustomerId));
        Assert.Equal(0, await ReturnNoteCountAsync(other.CustomerId));
    }

    /// <summary>Hàng trả về đụng thẳng công nợ ⇒ ngoài kế toán thì không ai được chạm.</summary>
    [Fact]
    public async Task OnlyAccountantsCanRecordReturns()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Thép tấm", "6mm", 50m, 10_000m, daysAgo: 1, settled: true);

        using var driver = Client(world.DriverToken);
        var response = await driver.PostAsJsonAsync("/api/returns", new
        {
            reason = "Tự ghi",
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 5m } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(50m * 10_000m, await BalanceAsync(world.CustomerId));
    }

    /// <summary>Không có lý do thì tháng sau không ai truy được vì sao công nợ tụt.</summary>
    [Fact]
    public async Task RecordingAReturnWithoutAReason_IsRefused()
    {
        var world = await SetupAsync();
        var voucher = await InsertVoucherAsync(world, "Thép tấm", "5mm", 50m, 10_000m, daysAgo: 1, settled: true);

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.PostAsJsonAsync("/api/returns", new
        {
            reason = "   ",
            lines = new[] { new { sourceDocumentId = voucher, sourceLineNo = 1, quantity = 5m } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("lý do", await MessageAsync(response));
        Assert.Equal(0, await ReturnNoteCountAsync(world.CustomerId));
    }

    // ── Hạ tầng dựng dữ liệu ─────────────────────────────────────────────────────────────────

    private sealed record World(Guid CustomerId, string CustomerName, string AccountantToken, string DriverToken);

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
    }

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var dept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customerName = "Khách " + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", dept).With("@code", "GR" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", customerId).With("@name", customerName).ExecuteNonQueryAsync();

        var accountant = await MakeUser(conn, tokens, "__gr_acc_" + suffix, AppRoles.Accounting, dept);
        var driver = await MakeUser(conn, tokens, "__gr_drv_" + suffix, AppRoles.Driver, dept);
        return new World(customerId, customerName, accountant, driver);
    }

    private static async Task<string> MakeUser(NpgsqlConnection conn, TokenService tokens,
        string username, string role, Guid departmentId)
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
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", Guid.NewGuid()).With("@code", "GR" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:gr:" + Guid.NewGuid().ToString("N")[..16]);
    }

    /// <param name="settled">Đã xác nhận tờ phiếu về kho (đã chốt sổ) hay chưa.</param>
    private async Task<Guid> InsertVoucherAsync(World world, string content, string spec,
        decimal quantity, decimal unitPrice, int daysAgo, bool settled)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO documents (id,voucher_no,doc_date,customer_id,customer_name,document_type,
                content,issued_at,delivery_returned_at)
            VALUES (@id,@no,CURRENT_DATE - @days,@cid,@cname,'document','Bán hàng',CURRENT_TIMESTAMP,@settled)
            """)
            .With("@id", id).With("@no", "PX" + Guid.NewGuid().ToString("N")[..10])
            .With("@days", daysAgo).With("@cid", world.CustomerId).With("@cname", world.CustomerName)
            .With("@settled", settled ? DateTime.UtcNow : (object)DBNull.Value)
            .ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO document_lines (document_id,line_no,line_content,spec,quantity,unit_price,note)
            VALUES (@id,1,@content,@spec,@q,@p,'')
            """).With("@id", id).With("@content", content).With("@spec", spec)
            .With("@q", quantity).With("@p", unitPrice).ExecuteNonQueryAsync();
        return id;
    }

    private async Task<decimal> BalanceAsync(Guid customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        // Đúng công thức công nợ của /api/debts: bán − hàng trả về − đã thu.
        return (decimal)(await conn.Cmd("""
            SELECT COALESCE(SUM(CASE WHEN d.document_type = 'return' THEN -1 ELSE 1 END
                                    * l.quantity * l.unit_price), 0)
            FROM documents d JOIN document_lines l ON l.document_id = d.id
            WHERE d.customer_id = @cid AND d.cancelled_at IS NULL
              AND d.document_type IN ('document', 'return')
            """).With("@cid", customerId).ExecuteScalarAsync() ?? 0m);
    }

    private async Task<decimal> LineQuantityAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return (decimal)(await conn.Cmd(
            "SELECT quantity FROM document_lines WHERE document_id=@id AND line_no=1")
            .With("@id", documentId).ExecuteScalarAsync() ?? 0m);
    }

    private async Task<int> ReturnNoteCountAsync(Guid customerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("""
            SELECT COUNT(*) FROM documents
            WHERE customer_id=@cid AND document_type='return' AND cancelled_at IS NULL
            """).With("@cid", customerId).ExecuteScalarAsync() ?? 0);
    }

    private async Task<string> LastEditReasonAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd(
            "SELECT reason FROM document_line_edits WHERE document_id=@id ORDER BY id DESC LIMIT 1")
            .With("@id", documentId).ExecuteScalarAsync() as string ?? "";
    }
}
