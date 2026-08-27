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
/// Gán phiếu xuất kho cho lái xe, và việc gộp "giao hàng + thu tiền" của cùng một khách.
///
/// Những bất biến được canh ở đây là loại dễ vỡ âm thầm nhất: gán lại lái xe mà đẻ thêm việc thứ
/// hai thì lái xe cũ vẫn thấy việc cũ trên máy và hai người cùng đi giao một phiếu.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryAssignmentTests
{
    private readonly ApiFactory _factory;
    public DeliveryAssignmentTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DraftVoucher_CannotBeAssigned_BecauseNoPaperExistsYet()
    {
        var world = await SetupAsync();
        var draft = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: false);

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.PostAsJsonAsync($"/api/documents/{draft}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("chưa in", body.GetProperty("message").GetString() ?? "");
    }

    /// <summary>
    /// Lái xe chưa chấm công hôm nay thì chưa nhận chuyến được: giao phiếu cho người còn chưa tới
    /// công ty thì tờ phiếu nằm im tới chiều mà kế toán vẫn tưởng hàng đang trên đường. Giao diện đã
    /// làm mờ tên họ, nhưng chốt phải nằm ở máy chủ vì app native có bản cũ.
    /// </summary>
    [Fact]
    public async Task ADriverWhoHasNotCheckedInToday_CannotBeGivenAVoucher()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);
        var absent = "__da_off_" + Guid.NewGuid().ToString("N")[..10];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
            await using var conn = await db.OpenAsync();
            // Cố ý KHÔNG gọi CheckInAsync: đây chính là điều kiện đang kiểm.
            await MakeUser(conn, tokens, absent, AppRoles.Driver, world.DriverDepartmentId, Guid.NewGuid());
        }

        using var accountant = Client(world.AccountantToken);
        var response = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = absent });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("chưa chấm công", body.GetProperty("message").GetString() ?? "");
        // Danh sách chọn vẫn liệt kê người đó, kèm chú thích — chỉ là không chọn được.
        var list = await accountant.GetFromJsonAsync<JsonElement>("/api/delivery-assignments/drivers");
        var row = list.GetProperty("drivers").EnumerateArray()
            .Single(d => d.GetProperty("username").GetString() == absent);
        Assert.False(row.GetProperty("selectable").GetBoolean());
        Assert.Equal("Chưa chấm công", row.GetProperty("attendanceNote").GetString());
    }

    [Fact]
    public async Task Reassigning_UpdatesTheSameTask_AndNeverCreatesASecondOne()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);

        using var accountant = Client(world.AccountantToken);

        // Gán lần đầu.
        var first = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, await DeliveryTaskCountAsync(document));

        // Gán lại cho lái xe khác: vẫn phải là ĐÚNG MỘT việc, và người nhận đã đổi.
        var second = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.SecondDriverUsername });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await DeliveryTaskCountAsync(document));
        Assert.Equal(world.SecondDriverUsername, await TaskAssigneeAsync(document));

        // Lái xe đầu tiên KHÔNG còn thấy việc này nữa.
        using var firstDriver = Client(world.DriverToken);
        var inbox = await LoadInboxAsync(firstDriver);
        Assert.DoesNotContain(inbox, task =>
            task.TryGetProperty("delivery", out var d)
            && d.ValueKind == JsonValueKind.Object
            && d.GetProperty("documentId").GetGuid() == document);
    }

    /// <summary>
    /// Xe hỏng giữa đường là chuyện thường: lái xe đã NHẬN CHUYẾN vẫn phải đổi được người. Nhưng tờ
    /// phiếu giấy đang trong tay người cũ nên bắt buộc có lý do, và việc phải sạch tiến độ cũ.
    /// </summary>
    [Fact]
    public async Task ADriverWhoAlreadyStartedTheTrip_CanStillBeReplaced_ButOnlyWithAReason()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);

        using var accountant = Client(world.AccountantToken);
        var first = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });
        var taskId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();

        // Lái xe nhận chuyến và đi được nửa đường.
        using var firstDriver = Client(world.DriverToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await firstDriver.PostAsJsonAsync($"/api/tasks/{taskId}/start", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await firstDriver.PostAsJsonAsync($"/api/tasks/{taskId}/progress", new { progress = 60 })).StatusCode);
        Assert.Equal("in_progress", await TaskStatusAsync(document));

        // Không có lý do ⇒ chặn, và trạng thái không suy suyển.
        var noReason = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.SecondDriverUsername });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.True((await noReason.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("needsReason").GetBoolean());
        Assert.Equal(world.DriverUsername, await TaskAssigneeAsync(document));

        // Có lý do ⇒ đổi được, vẫn ĐÚNG MỘT việc, và việc trở lại vạch xuất phát cho người mới.
        var handover = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.SecondDriverUsername, reason = "Xe hỏng giữa đường" });
        Assert.Equal(HttpStatusCode.OK, handover.StatusCode);
        Assert.Equal(1, await DeliveryTaskCountAsync(document));
        Assert.Equal(world.SecondDriverUsername, await TaskAssigneeAsync(document));
        Assert.Equal("assigned", await TaskStatusAsync(document));
        Assert.Equal(0, await TaskProgressAsync(taskId));

        // Lý do phải đọng lại ở nhật ký việc, không chỉ hiện lên rồi biến mất.
        var reassignEvent = await LastEventAsync(taskId, "reassigned");
        Assert.Contains("Xe hỏng giữa đường", reassignEvent);

        // Lái xe cũ mất việc khỏi máy và KHÔNG nộp nghiệm thu chuyến này được nữa.
        Assert.DoesNotContain(await LoadInboxAsync(firstDriver), task =>
            task.TryGetProperty("delivery", out var d)
            && d.ValueKind == JsonValueKind.Object
            && d.GetProperty("documentId").GetGuid() == document);
        var lateSubmit = await firstDriver.PostAsJsonAsync($"/api/tasks/{taskId}/submit", new { note = "Đã giao" });
        Assert.Equal(HttpStatusCode.Forbidden, lateSubmit.StatusCode);
    }

    /// <summary>Đã nộp nghiệm thu = hàng tới khách rồi: đổi người lúc này chỉ làm sai sổ.</summary>
    [Fact]
    public async Task OnceTheDriverHasSubmittedTheDelivery_NobodyElseCanTakeItOver()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);

        using var accountant = Client(world.AccountantToken);
        var first = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });
        var taskId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();

        using var firstDriver = Client(world.DriverToken);
        await firstDriver.PostAsJsonAsync($"/api/tasks/{taskId}/start", new { });
        await firstDriver.PostAsJsonAsync($"/api/tasks/{taskId}/submit", new { note = "Đã giao" });

        var tooLate = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.SecondDriverUsername, reason = "Đổi thử" });
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Equal(world.DriverUsername, await TaskAssigneeAsync(document));

        // Cờ máy chủ trả về phải khớp với chính điều vừa chặn — giao diện dựa vào đó để khoá nút.
        var state = await accountant.GetFromJsonAsync<JsonElement>($"/api/documents/{document}/delivery");
        Assert.False(state.GetProperty("canChange").GetBoolean());
        Assert.NotEqual("", state.GetProperty("lockMessage").GetString());
    }

    [Fact]
    public async Task SwitchingToCustomerPickup_ClosesTheDriverTask()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);

        using var accountant = Client(world.AccountantToken);
        await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });

        var pickup = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "pickup" });
        Assert.Equal(HttpStatusCode.OK, pickup.StatusCode);

        // Ràng buộc CSDL: "khách lấy tại kho" không được đứng tên lái xe nào.
        var (mode, driverUsername, taskId) = await DeliveryColumnsAsync(document);
        Assert.Equal("pickup", mode);
        Assert.Equal("", driverUsername);
        Assert.Null(taskId);
        Assert.Equal("cancelled", await TaskStatusAsync(document));
    }

    [Fact]
    public async Task OnlyRealDrivers_AndOnlyAuthorisedCallers_CanBeInvolved()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);

        // Nhân viên thường không phải lái xe ⇒ không nhận được phiếu.
        using var accountant = Client(world.AccountantToken);
        var notADriver = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.StrangerUsername });
        Assert.Equal(HttpStatusCode.BadRequest, notADriver.StatusCode);

        // Lái xe không có quyền kế toán/giao việc ⇒ không tự gán phiếu cho mình.
        using var driver = Client(world.DriverToken);
        var selfAssign = await driver.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });
        Assert.Equal(HttpStatusCode.Forbidden, selfAssign.StatusCode);
    }

    [Fact]
    public async Task DeliveryAndCashCollection_ForTheSameCustomer_ArriveAsOneMergedItem()
    {
        var world = await SetupAsync();
        var document = await InsertDocumentAsync(world.CustomerId, "Khách " + world.Suffix, issued: true);
        var orderId = await InsertCollectionOrderAsync(world, 5_000_000m);

        using var accountant = Client(world.AccountantToken);
        var assigned = await accountant.PostAsJsonAsync($"/api/documents/{document}/delivery",
            new { mode = "driver", driverUsername = world.DriverUsername });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        using var driver = Client(world.DriverToken);
        var list = await driver.GetFromJsonAsync<JsonElement>("/api/tasks");
        var deliveryTask = list.GetProperty("inbox").EnumerateArray().Single(task =>
            task.TryGetProperty("delivery", out var d)
            && d.ValueKind == JsonValueKind.Object
            && d.GetProperty("documentId").GetGuid() == document);

        // Khoản thu của CHÍNH khách này phải nằm ngay trong thẻ việc giao hàng.
        var collection = deliveryTask.GetProperty("delivery").GetProperty("collection");
        Assert.Equal(JsonValueKind.Object, collection.ValueKind);
        Assert.Equal(orderId, collection.GetProperty("id").GetGuid());
        Assert.Equal(5_000_000m, collection.GetProperty("expectedAmount").GetDecimal());

        // Và KHÔNG được lặp lại ở danh sách lệnh thu rời, nếu không lái xe tưởng phải thu hai lần.
        var standalone = list.GetProperty("collections").EnumerateArray()
            .Select(c => c.GetProperty("id").GetGuid())
            .ToList();
        Assert.DoesNotContain(orderId, standalone);
    }

    [Fact]
    public async Task CashCollectionWithoutAnyDeliveryVoucher_StillShowsUpInTheDriverWorkList()
    {
        var world = await SetupAsync();
        var orderId = await InsertCollectionOrderAsync(world, 1_250_000m);

        using var driver = Client(world.DriverToken);
        var list = await driver.GetFromJsonAsync<JsonElement>("/api/tasks");

        var standalone = list.GetProperty("collections").EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == orderId);
        Assert.Equal(1_250_000m, standalone.GetProperty("expectedAmount").GetDecimal());
        Assert.Equal(1, list.GetProperty("summary").GetProperty("collectionsStandalone").GetInt32());
    }

    // ── Hạ tầng dựng dữ liệu ─────────────────────────────────────────────────────────────────

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<List<JsonElement>> LoadInboxAsync(HttpClient client)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/tasks");
        return list.GetProperty("inbox").EnumerateArray().ToList();
    }

    private sealed record World(string Suffix, Guid CustomerId, Guid DriverDepartmentId,
        Guid DriverEmployeeId, string AccountantUsername, string DriverUsername,
        string SecondDriverUsername, string StrangerUsername,
        string AccountantToken, string DriverToken);

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var accountingDept = Guid.NewGuid();
        var driverDept = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var accountant = "__da_acc_" + suffix;
        var driver = "__da_drv_" + suffix;
        var driver2 = "__da_drv2_" + suffix;
        var stranger = "__da_emp_" + suffix;
        var driverEmployee = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,TRUE)")
            .With("@id", accountingDept).With("@code", "DAK" + suffix[..5]).With("@name", "Kế toán " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,FALSE)")
            .With("@id", driverDept).With("@code", "DAL" + suffix[..5]).With("@name", "Vận tải " + suffix)
            .ExecuteNonQueryAsync();
        await conn.Cmd("INSERT INTO customers (id,name,phone,is_active) VALUES (@id,@name,@phone,TRUE)")
            .With("@id", customerId).With("@name", "Khách " + suffix).With("@phone", "0911000000")
            .ExecuteNonQueryAsync();

        var accountantToken = await MakeUser(conn, tokens, accountant, AppRoles.Accounting, accountingDept, Guid.NewGuid());
        var driverToken = await MakeUser(conn, tokens, driver, AppRoles.Driver, driverDept, driverEmployee);
        await MakeUser(conn, tokens, driver2, AppRoles.Driver, driverDept, Guid.NewGuid());
        await MakeUser(conn, tokens, stranger, AppRoles.Employee, driverDept, Guid.NewGuid());

        // Không giao chuyến được cho người CHƯA chấm công (xem WorkforceAvailability). Các test ở đây
        // nói về việc gán/đổi người nên phải cho cả hai lái xe "đã đến công ty" trước.
        await CheckInAsync(conn, driver);
        await CheckInAsync(conn, driver2);

        return new(suffix, customerId, driverDept, driverEmployee, accountant, driver, driver2, stranger,
            accountantToken, driverToken);
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
            """).With("@id", employeeId).With("@code", "DA" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .With("@position", role == AppRoles.Driver ? "Lái xe" : "Nhân viên")
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:da:" + Guid.NewGuid().ToString("N")[..16]);
    }

    /// <summary>Ghi một lượt chấm công VÀO cho hôm nay — đủ để tài khoản được coi là có mặt.</summary>
    private static async Task CheckInAsync(NpgsqlConnection conn, string username) =>
        await conn.Cmd("""
            INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
            VALUES (@u, @u, 'Vào', 0.99, CURRENT_TIMESTAMP, 'test')
            """).With("@u", username).ExecuteNonQueryAsync();

    /// <summary>
    /// Chèn thẳng phiếu xuất kho để không phải chạm vào máy in thật. Bất biến của schema vẫn được
    /// tôn trọng: phiếu đã phát hành BẮT BUỘC có số, phiếu nháp bắt buộc không có số.
    /// </summary>
    private async Task<Guid> InsertDocumentAsync(Guid customerId, string customerName, bool issued)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO documents (id,voucher_no,doc_date,customer_id,customer_name,document_type,content,issued_at)
            VALUES (@id,@no,CURRENT_DATE,@cid,@cname,'document','Giao hàng test',@issued)
            """)
            .With("@id", id)
            .With("@no", issued ? "PX" + Guid.NewGuid().ToString("N")[..10] : "")
            .With("@cid", customerId).With("@cname", customerName)
            .With("@issued", issued ? DateTime.UtcNow : (object)DBNull.Value)
            .ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> InsertCollectionOrderAsync(World world, decimal amount)
    {
        var id = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("""
            INSERT INTO cash_collection_orders
                (id,order_no,customer_id,customer_name,driver_employee_id,driver_username,driver_name,
                 expected_amount,scheduled_date,handover_due_at,status,created_by)
            VALUES (@id,@no,@cid,@cname,@deid,@du,@dn,@amount,CURRENT_DATE,
                    CURRENT_TIMESTAMP + INTERVAL '1 day','Assigned',@by)
            """)
            .With("@id", id).With("@no", "LT" + Guid.NewGuid().ToString("N")[..10])
            .With("@cid", world.CustomerId).With("@cname", "Khách " + world.Suffix)
            .With("@deid", world.DriverEmployeeId).With("@du", world.DriverUsername)
            .With("@dn", world.DriverUsername).With("@amount", amount)
            .With("@by", world.AccountantUsername)
            .ExecuteNonQueryAsync();
        return id;
    }

    private async Task<int> DeliveryTaskCountAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd(
            "SELECT COUNT(*) FROM work_tasks WHERE source_kind='delivery' AND source_document_id=@id")
            .With("@id", documentId).ExecuteScalarAsync() ?? 0);
    }

    private async Task<string> TaskAssigneeAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd(
            "SELECT assignee_username FROM work_tasks WHERE source_kind='delivery' AND source_document_id=@id")
            .With("@id", documentId).ExecuteScalarAsync() as string ?? "";
    }

    private async Task<string> TaskStatusAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd(
            "SELECT status FROM work_tasks WHERE source_kind='delivery' AND source_document_id=@id")
            .With("@id", documentId).ExecuteScalarAsync() as string ?? "";
    }

    private async Task<int> TaskProgressAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return Convert.ToInt32(await conn.Cmd("SELECT progress FROM work_tasks WHERE id=@id")
            .With("@id", taskId).ExecuteScalarAsync() ?? 0);
    }

    private async Task<string> LastEventAsync(Guid taskId, string kind)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        return await conn.Cmd("""
            SELECT note FROM work_task_events WHERE task_id=@id AND kind=@kind
            ORDER BY id DESC LIMIT 1
            """).With("@id", taskId).With("@kind", kind).ExecuteScalarAsync() as string ?? "";
    }

    private async Task<(string Mode, string DriverUsername, Guid? TaskId)> DeliveryColumnsAsync(Guid documentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await using var r = await conn.Cmd(
            "SELECT delivery_mode, delivery_driver_username, delivery_task_id FROM documents WHERE id=@id")
            .With("@id", documentId).ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        var taskOrdinal = r.GetOrdinal("delivery_task_id");
        return (r.Str("delivery_mode"), r.Str("delivery_driver_username"),
            r.IsDBNull(taskOrdinal) ? null : r.GetGuid(taskOrdinal));
    }
}
