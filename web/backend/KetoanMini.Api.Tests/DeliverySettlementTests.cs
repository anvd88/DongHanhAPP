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
/// Đối soát phiếu khi lái xe nộp phiếu giấy về: kế toán sửa hàng THỰC NHẬN rồi xác nhận phiếu đã
/// về kho, việc của lái xe đóng lại ở 'completed'.
///
/// Việc giao hàng KHÔNG có chặng nghiệm thu (chốt của người dùng 2026-08-24): khách nhận hàng rồi
/// thì tờ phiếu ký nhận quay về kho là bằng chứng, một cú bấm của kế toán đóng việc luôn.
///
/// Những bất biến canh ở đây đều là loại hỏng ngầm không ai thấy ngay:
///   • Đóng việc phải sang thẳng 'completed', không kẹt lại ở 'submitted'.
///   • Sửa số mà không ghi lịch sử ⇒ tháng sau không ai truy được vì sao công nợ lệch tờ phiếu.
///   • Sửa số liệu tiền mà không cần quyền kế toán ⇒ lái xe tự chữa số hàng mình vừa giao.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeliverySettlementTests
{
    private readonly ApiFactory _factory;
    public DeliverySettlementTests(ApiFactory factory) => _factory = factory;

    /// <summary>
    /// Đường thường: lái xe báo đã giao → kế toán thu tờ phiếu → xong. KHÔNG đi qua 'accepted'.
    /// </summary>
    [Fact]
    public async Task ConfirmingReturn_ClosesTheDelivery_WithoutAnySeparateAcceptance()
    {
        var world = await SetupAsync();
        using var driver = Client(world.DriverToken);
        await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/start", new { });
        await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/submit", new { note = "Đã giao" });

        using var accountant = Client(world.AccountantToken);
        var confirmed = await accountant.PostAsJsonAsync(
            $"/api/documents/{world.DocumentId}/settlement/return", new { note = "" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("completed", await TaskStatusAsync(world.TaskId));

        // Không có bước nghiệm thu nào lẻn vào dòng thời gian.
        Assert.DoesNotContain("accepted", await EventKindsAsync(world.TaskId));
    }

    /// <summary>
    /// Lái xe quên bấm "đã giao" là chuyện thường. Tờ phiếu có chữ ký khách về tới kế toán vẫn phải
    /// đóng được việc — trước đây bắt buộc nghiệm thu nên chỉ một nút quên là phiếu kẹt vĩnh viễn.
    /// </summary>
    [Fact]
    public async Task ConfirmingReturn_StillWorks_WhenTheDriverForgotToReportTheDelivery()
    {
        var world = await SetupAsync();
        using var driver = Client(world.DriverToken);
        await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/start", new { });
        Assert.Equal("in_progress", await TaskStatusAsync(world.TaskId));

        using var accountant = Client(world.AccountantToken);
        var confirmed = await accountant.PostAsJsonAsync(
            $"/api/documents/{world.DocumentId}/settlement/return", new { note = "" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("completed", await TaskStatusAsync(world.TaskId));

        // Nhưng nhật ký phải nói rõ nó nhảy chặng, đừng để dòng thời gian nói dối.
        Assert.Contains("chưa báo giao xong", await LastEventNoteAsync(world.TaskId, "completed"));
    }

    /// <summary>
    /// Kế toán KHÔNG có quyền TasksAssign (quyền đó chỉ mở nút "Giao việc mới" cho Thủ kho/Trưởng
    /// phòng), nhưng gán phiếu xuất kho cho lái xe là họ đã trở thành người giao việc giao hàng đó.
    /// Nếu danh sách "Việc tôi giao" lọc theo QUYỀN thay vì theo VIỆC ĐÃ GIAO thì chính người giao
    /// không thấy việc mình giao để đóng — phiếu kẹt vĩnh viễn ở "chờ nộp phiếu".
    /// </summary>
    [Fact]
    public async Task TheAccountantWhoAssigned_SeesTheDeliveryInTheirOutbox_AndCanCloseIt()
    {
        var world = await SetupAsync();
        using var driver = Client(world.DriverToken);
        await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/start", new { });
        await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/submit", new { note = "Đã giao" });

        using var accountant = Client(world.AccountantToken);
        var list = await accountant.GetFromJsonAsync<JsonElement>("/api/tasks");

        // Không có quyền tạo việc mới...
        Assert.False(list.GetProperty("canAssign").GetBoolean());
        // ...nhưng vẫn thấy việc giao hàng mình đã giao, và thấy nó đang chờ NỘP PHIẾU —
        // không phải "chờ nghiệm thu", nếu không kế toán mở màn Công việc ra chẳng có gì để bấm.
        var mine = list.GetProperty("outbox").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == world.TaskId);
        Assert.Equal("submitted", mine.GetProperty("status").GetString());
        var summary = list.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("outboxReview").GetInt32());
        Assert.Equal(1, summary.GetProperty("outboxAwaitingVoucher").GetInt32());

        // Nghiệm thu không còn tồn tại với việc giao hàng.
        var accepted = await accountant.PostAsJsonAsync($"/api/tasks/{world.TaskId}/accept",
            new { note = "Đạt", rating = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, accepted.StatusCode);
        Assert.Contains("không cần nghiệm thu", await MessageAsync(accepted));

        // Đóng việc bằng đúng một cú: xác nhận tờ phiếu đã về kho.
        var confirmed = await accountant.PostAsJsonAsync(
            $"/api/documents/{world.DocumentId}/settlement/return", new { note = "" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("completed", await TaskStatusAsync(world.TaskId));
    }

    /// <summary>Việc của người khác vẫn không được lọt vào "Việc tôi giao" của mình.</summary>
    [Fact]
    public async Task TheOutbox_StillOnlyShowsWhatYouAssignedYourself()
    {
        var world = await SetupAsync();
        var other = await SetupAsync();

        using var accountant = Client(world.AccountantToken);
        var list = await accountant.GetFromJsonAsync<JsonElement>("/api/tasks");
        var ids = list.GetProperty("outbox").EnumerateArray()
            .Select(t => t.GetProperty("id").GetGuid()).ToList();

        Assert.Contains(world.TaskId, ids);
        Assert.DoesNotContain(other.TaskId, ids);
    }

    [Fact]
    public async Task ConfirmingReturn_MovesTheDriverTask_FromSubmittedToCompleted()
    {
        var world = await SetupAsync();
        await RunToSubmittedAsync(world);
        Assert.Equal("submitted", await TaskStatusAsync(world.TaskId));

        using var accountant = Client(world.AccountantToken);
        var confirmed = await accountant.PostAsJsonAsync(
            $"/api/documents/{world.DocumentId}/settlement/return", new { note = "Nhận đủ phiếu" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        Assert.Equal("completed", await TaskStatusAsync(world.TaskId));

        var payload = await confirmed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("flags").GetProperty("returned").GetBoolean());
        Assert.False(payload.GetProperty("flags").GetProperty("canConfirmReturn").GetBoolean());

        // Xác nhận lần hai không được cộng dồn thành hai lần nhận phiếu.
        var again = await accountant.PostAsJsonAsync(
            $"/api/documents/{world.DocumentId}/settlement/return", new { note = "" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task EditingActualGoods_RecordsHistory_AndComputesTheVariance()
    {
        var world = await SetupAsync();
        await RunToSubmittedAsync(world);
        using var accountant = Client(world.AccountantToken);

        // Khách cân lại thiếu 20, và đơn giá lúc xuất bị viết nhầm.
        var saved = await accountant.PutAsJsonAsync($"/api/documents/{world.DocumentId}/settlement", new
        {
            lines = new[] { new { lineNo = 1, quantity = 980m, unitPrice = 12_500m } },
            reason = "Cân lại tại kho khách thiếu 20; đơn giá nhầm 12.000 → 12.500",
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var payload = await saved.Content.ReadFromJsonAsync<JsonElement>();
        var line = payload.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(1_000m, line.GetProperty("issuedQuantity").GetDecimal());
        Assert.Equal(980m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(-20m, line.GetProperty("quantityDiff").GetDecimal());

        var totals = payload.GetProperty("totals");
        Assert.Equal(12_000_000m, totals.GetProperty("issuedTotal").GetDecimal());
        Assert.Equal(12_250_000m, totals.GetProperty("actualTotal").GetDecimal());
        Assert.Equal(250_000m, totals.GetProperty("diffTotal").GetDecimal());

        var history = payload.GetProperty("history").EnumerateArray().Single();
        Assert.Equal(1_000m, history.GetProperty("oldQuantity").GetDecimal());
        Assert.Equal(980m, history.GetProperty("newQuantity").GetDecimal());
        Assert.Equal(12_000m, history.GetProperty("oldUnitPrice").GetDecimal());
        Assert.Equal(12_500m, history.GetProperty("newUnitPrice").GetDecimal());
        Assert.Contains("Cân lại", history.GetProperty("reason").GetString() ?? "");

        // Lưu lại y nguyên thì KHÔNG được đẻ thêm dòng lịch sử, nếu không sổ loãng thành vô dụng.
        var noop = await accountant.PutAsJsonAsync($"/api/documents/{world.DocumentId}/settlement", new
        {
            lines = new[] { new { lineNo = 1, quantity = 980m, unitPrice = 12_500m } },
            reason = "",
        });
        Assert.Equal(HttpStatusCode.OK, noop.StatusCode);
        var after = await noop.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(after.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task EditingWithoutAReason_IsRefused()
    {
        var world = await SetupAsync();
        await RunToSubmittedAsync(world);
        using var accountant = Client(world.AccountantToken);

        var response = await accountant.PutAsJsonAsync($"/api/documents/{world.DocumentId}/settlement", new
        {
            lines = new[] { new { lineNo = 1, quantity = 900m, unitPrice = 12_000m } },
            reason = "   ",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("lý do", await MessageAsync(response));
        Assert.Equal(1_000m, await LineQuantityAsync(world.DocumentId));
    }

    [Fact]
    public async Task TheDriver_CanNeitherEditTheGoods_NorCloseTheirOwnDelivery()
    {
        var world = await SetupAsync();
        await RunToSubmittedAsync(world);
        using var driver = Client(world.DriverToken);

        var edit = await driver.PutAsJsonAsync($"/api/documents/{world.DocumentId}/settlement", new
        {
            lines = new[] { new { lineNo = 1, quantity = 1_500m, unitPrice = 12_000m } },
            reason = "tự sửa",
        });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        var close = await driver.PostAsJsonAsync(
            $"/api/documents/{world.DocumentId}/settlement/return", new { note = "" });
        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);

        Assert.Equal(1_000m, await LineQuantityAsync(world.DocumentId));
        Assert.Equal("submitted", await TaskStatusAsync(world.TaskId));
    }

    [Fact]
    public async Task ACompletedDelivery_CannotBeHandedToAnotherDriver()
    {
        var world = await SetupAsync();
        await RunToSubmittedAsync(world);
        using var accountant = Client(world.AccountantToken);
        await accountant.PostAsJsonAsync($"/api/documents/{world.DocumentId}/settlement/return", new { note = "" });

        var reassign = await accountant.PostAsJsonAsync($"/api/documents/{world.DocumentId}/delivery",
            new { mode = "pickup" });
        Assert.Equal(HttpStatusCode.Conflict, reassign.StatusCode);
        Assert.Equal("completed", await TaskStatusAsync(world.TaskId));
    }

    // ── Hạ tầng dựng dữ liệu ─────────────────────────────────────────────────────────────────

    private sealed record World(Guid DocumentId, Guid TaskId, string AccountantToken, string DriverToken);

    /// <summary>
    /// Dựng sẵn: phiếu ĐÃ PHÁT HÀNH có một dòng hàng + ảnh chụp "hàng xuất đi", đã gán cho lái xe.
    /// Chèn thẳng vào CSDL để khỏi phải chạm máy in thật; ảnh chụp dùng ĐÚNG câu lệnh mà
    /// FinalizeWarehouseIssue chạy sau khi lệnh in thành công.
    /// </summary>
    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var accountingDept = Guid.NewGuid();
        var driverDept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var accountant = "__ds_acc_" + suffix;
        var driver = "__ds_drv_" + suffix;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();

        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", accountingDept).With("@code", "DSK" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,FALSE)")
            .With("@id", driverDept).With("@code", "DSL" + suffix[..5]).With("@name", "Vận tải " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,is_active) VALUES (@id,@name,TRUE)")
            .With("@id", customerId).With("@name", "Khách " + suffix).ExecuteNonQueryAsync();

        var accountantToken = await MakeUser(conn, tokens, accountant, AppRoles.Accounting, accountingDept);
        var driverToken = await MakeUser(conn, tokens, driver, AppRoles.Driver, driverDept);

        // Gán chuyến đòi lái xe ĐÃ CHẤM CÔNG hôm nay (xem WorkforceAvailability). Các test ở đây nói
        // về khâu nộp phiếu/đối soát sau khi đã giao, nên phải cho lái xe "đến công ty" trước.
        await conn.Cmd("""
            INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
            VALUES (@u, @u, 'Vào', 0.99, CURRENT_TIMESTAMP, 'test')
            """).With("@u", driver).ExecuteNonQueryAsync();

        await conn.Cmd("""
            INSERT INTO documents (id,voucher_no,doc_date,customer_id,customer_name,document_type,content,issued_at)
            VALUES (@id,@no,CURRENT_DATE,@cid,@cname,'document','Giao hàng test',CURRENT_TIMESTAMP)
            """)
            .With("@id", documentId).With("@no", "PS" + suffix)
            .With("@cid", customerId).With("@cname", "Khách " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO document_lines (document_id,line_no,line_content,spec,quantity,unit_price,note)
            VALUES (@id,1,'Thép tấm','10mm',1000,12000,'')
            """).With("@id", documentId).ExecuteNonQueryAsync();
        await conn.Cmd("""
            INSERT INTO document_issued_lines (document_id,line_no,line_content,spec,quantity,unit_price,note)
            SELECT l.document_id,l.line_no,l.line_content,l.spec,l.quantity,l.unit_price,l.note
            FROM document_lines l WHERE l.document_id=@id
            ON CONFLICT (document_id,line_no) DO NOTHING
            """).With("@id", documentId).ExecuteNonQueryAsync();

        using var client = Client(accountantToken);
        var assigned = await client.PostAsJsonAsync($"/api/documents/{documentId}/delivery",
            new { mode = "driver", driverUsername = driver });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var body = await assigned.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = body.GetProperty("taskId").GetGuid();

        return new World(documentId, taskId, accountantToken, driverToken);
    }

    /// <summary>Chạy hết đường của lái xe: nhận → báo đã giao. (Không còn chặng nghiệm thu.)</summary>
    private async Task RunToSubmittedAsync(World world)
    {
        using var driver = Client(world.DriverToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/start", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await driver.PostAsJsonAsync($"/api/tasks/{world.TaskId}/submit", new { note = "Đã giao" })).StatusCode);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("message").GetString() ?? "";
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
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active',@position)
            """).With("@id", Guid.NewGuid()).With("@code", "DS" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .With("@position", role == AppRoles.Driver ? "Lái xe" : "Nhân viên")
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:ds:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private async Task<string> TaskStatusAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd("SELECT status FROM work_tasks WHERE id=@id")
            .With("@id", taskId).ExecuteScalarAsync() as string ?? "";
    }

    /// <summary>Mọi loại sự kiện đã xảy ra với một việc — để canh chặng nào KHÔNG được xuất hiện.</summary>
    private async Task<List<string>> EventKindsAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var kinds = new List<string>();
        await using var r = await conn.Cmd("SELECT kind FROM work_task_events WHERE task_id=@id ORDER BY id")
            .With("@id", taskId).ExecuteReaderAsync();
        while (await r.ReadAsync()) kinds.Add(r.Str("kind"));
        return kinds;
    }

    private async Task<string> LastEventNoteAsync(Guid taskId, string kind)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd("""
            SELECT note FROM work_task_events WHERE task_id=@id AND kind=@kind ORDER BY id DESC LIMIT 1
            """).With("@id", taskId).With("@kind", kind).ExecuteScalarAsync() as string ?? "";
    }

    private async Task<decimal> LineQuantityAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToDecimal(await conn.Cmd(
            "SELECT quantity FROM document_lines WHERE document_id=@id AND line_no=1")
            .With("@id", documentId).ExecuteScalarAsync() ?? 0m);
    }
}
