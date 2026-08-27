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
/// Hai bất biến mới quanh "ai đang có mặt hôm nay":
///
///  1. Ô CHỌN NGƯỜI NHẬN VIỆC — người chưa chấm công hoặc đang nghỉ phép (đơn ĐÃ DUYỆT) vẫn hiện
///     tên kèm chú thích nhưng không giao việc được. Chốt nằm ở máy chủ, không chỉ ở giao diện:
///     web mở nhiều tab và app native có bản cũ, cả hai đều gửi thẳng username lên.
///
///  2. BẢNG TIN ĐIỀU HÀNH — cấp quản lý trở lên nhận được dòng chuông "ai vừa gửi đơn từ".
///     Kiểm bằng chính bảng hộp thư web_notifications vì đó là thứ cái chuông đọc.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WorkforceAvailabilityTests
{
    private readonly ApiFactory _factory;
    public WorkforceAvailabilityTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Meta_MarksTheAbsent_AndTheApprovedLeave_AsNotSelectable()
    {
        var world = await SetupAsync();

        using var boss = Client(world.AssignerToken);
        var meta = await boss.GetFromJsonAsync<JsonElement>("/api/tasks/meta");
        var rows = meta.GetProperty("assignees").EnumerateArray()
            .ToDictionary(x => x.GetProperty("username").GetString()!, x => x);

        // Không ai bị GIẤU đi — cả ba đều còn trong danh sách, chỉ khác nhau ở cờ chọn được.
        Assert.True(rows.ContainsKey(world.PresentUser));
        Assert.True(rows.ContainsKey(world.AbsentUser));
        Assert.True(rows.ContainsKey(world.OnLeaveUser));

        Assert.True(rows[world.PresentUser].GetProperty("selectable").GetBoolean());
        Assert.Equal("present", rows[world.PresentUser].GetProperty("attendanceStatus").GetString());

        Assert.False(rows[world.AbsentUser].GetProperty("selectable").GetBoolean());
        Assert.Equal("Chưa chấm công", rows[world.AbsentUser].GetProperty("attendanceNote").GetString());

        Assert.False(rows[world.OnLeaveUser].GetProperty("selectable").GetBoolean());
        Assert.Contains("nghỉ phép", rows[world.OnLeaveUser].GetProperty("attendanceNote").GetString() ?? "");
    }

    [Fact]
    public async Task AssigningToSomeoneWhoHasNotCheckedIn_IsRefusedByTheServer()
    {
        var world = await SetupAsync();
        using var boss = Client(world.AssignerToken);

        var refused = await boss.PostAsJsonAsync("/api/tasks",
            new { title = "Kiểm kê kho", assigneeUsername = world.AbsentUser });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("chưa chấm công",
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString() ?? "");

        var onLeave = await boss.PostAsJsonAsync("/api/tasks",
            new { title = "Kiểm kê kho", assigneeUsername = world.OnLeaveUser });
        Assert.Equal(HttpStatusCode.Conflict, onLeave.StatusCode);

        // Người đã chấm công vẫn nhận việc bình thường — chốt mới không được chặn nhầm ai cả.
        var ok = await boss.PostAsJsonAsync("/api/tasks",
            new { title = "Kiểm kê kho", assigneeUsername = world.PresentUser });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /// <summary>
    /// Đi công tác (đơn đã duyệt) KHÔNG phải là nghỉ: người đó vẫn làm việc, chỉ là không đứng trước
    /// camera ở công ty được. Chặn họ là chặn nhầm — đây là ngoại lệ duy nhất của luật "chưa chấm công".
    /// </summary>
    [Fact]
    public async Task ApprovedBusinessTrip_StaysSelectable_EvenWithoutACheckIn()
    {
        var world = await SetupAsync();
        await InsertRequestAsync(world.TripEmployeeId, world.TripUser, "business_trip", "Approved");

        using var boss = Client(world.AssignerToken);
        var meta = await boss.GetFromJsonAsync<JsonElement>("/api/tasks/meta");
        var row = meta.GetProperty("assignees").EnumerateArray()
            .Single(x => x.GetProperty("username").GetString() == world.TripUser);

        Assert.True(row.GetProperty("selectable").GetBoolean());
        Assert.Equal("Đang công tác", row.GetProperty("attendanceNote").GetString());

        var ok = await boss.PostAsJsonAsync("/api/tasks",
            new { title = "Gửi báo giá cho khách", assigneeUsername = world.TripUser });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    /// <summary>
    /// Đơn nghỉ CHƯA DUYỆT chưa phải là nghỉ. Nếu chặn ở đây thì bất kỳ ai cũng tự loại mình khỏi
    /// danh sách nhận việc chỉ bằng cách nộp một lá đơn — chỉ ghi chú thêm để người giao cân nhắc.
    /// </summary>
    [Fact]
    public async Task PendingLeave_OnlyAddsANote_ItDoesNotBlockAssignment()
    {
        var world = await SetupAsync();
        await InsertRequestAsync(world.PresentEmployeeId, world.PresentUser, "leave", "Pending");

        using var boss = Client(world.AssignerToken);
        var meta = await boss.GetFromJsonAsync<JsonElement>("/api/tasks/meta");
        var row = meta.GetProperty("assignees").EnumerateArray()
            .Single(x => x.GetProperty("username").GetString() == world.PresentUser);

        Assert.True(row.GetProperty("selectable").GetBoolean());
        Assert.Contains("đơn nghỉ chờ duyệt", row.GetProperty("attendanceNote").GetString() ?? "");
    }

    /// <summary>
    /// Nhân viên gửi đơn ⇒ quản lý nhân sự thấy một dòng trên chuông web, kể cả khi bước duyệt đầu
    /// tiên không thuộc về họ. Người GỬI không tự nhận thông báo về việc mình vừa bấm.
    /// </summary>
    [Fact]
    public async Task SubmittingARequest_ShowsUpOnTheManagementBell()
    {
        var world = await SetupAsync();

        using var employee = Client(world.PresentToken);
        var created = await employee.PostAsJsonAsync("/api/requests", new
        {
            type = "leave",
            title = "Xin nghỉ phép",
            payload = new { fromDate = "2099-01-05", toDate = "2099-01-06", days = 2, reason = "test" },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var notice = await ReadNotificationAsync(world.HrUser, "request");
        Assert.NotNull(notice);
        Assert.Contains(world.PresentUser, notice!.Value.Title + notice.Value.Body);

        // Chính người gửi thì không.
        Assert.Null(await ReadNotificationAsync(world.PresentUser, "request"));
    }

    // ── Dàn dựng ────────────────────────────────────────────────────────────────

    private sealed record World(
        string Suffix, Guid DepartmentId,
        string AssignerUser, string AssignerToken,
        string PresentUser, string PresentToken, Guid PresentEmployeeId,
        string AbsentUser, string OnLeaveUser,
        string TripUser, Guid TripEmployeeId,
        string HrUser);

    private async Task<World> SetupAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var department = Guid.NewGuid();
        var assigner = "__wa_boss_" + suffix;
        var present = "__wa_in_" + suffix;
        var absent = "__wa_out_" + suffix;
        var onLeave = "__wa_off_" + suffix;
        var trip = "__wa_trip_" + suffix;
        var hr = "__wa_hr_" + suffix;
        var presentEmployee = Guid.NewGuid();
        var tripEmployee = Guid.NewGuid();
        var leaveEmployee = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("INSERT INTO hr_departments (id,code,name,is_accounting) VALUES (@id,@code,@name,FALSE)")
            .With("@id", department).With("@code", "WA" + suffix[..5]).With("@name", "Kho " + suffix)
            .ExecuteNonQueryAsync();

        // Admin để phạm vi giao việc là TOÀN BỘ nhân sự; phạm vi phòng ban đã có test riêng.
        var assignerToken = await MakeUser(conn, tokens, assigner, AppRoles.Admin, department, Guid.NewGuid());
        var presentToken = await MakeUser(conn, tokens, present, AppRoles.Employee, department, presentEmployee);
        await MakeUser(conn, tokens, absent, AppRoles.Employee, department, Guid.NewGuid());
        await MakeUser(conn, tokens, onLeave, AppRoles.Employee, department, leaveEmployee);
        await MakeUser(conn, tokens, trip, AppRoles.Employee, department, tripEmployee);
        await MakeUser(conn, tokens, hr, AppRoles.Hr, department, Guid.NewGuid());

        await conn.Cmd("""
            INSERT INTO cham_cong_log (username, full_name, loai, similarity, occurred_at, ghi_chu)
            VALUES (@u, @u, 'Vào', 0.99, CURRENT_TIMESTAMP, 'test')
            """).With("@u", present).ExecuteNonQueryAsync();

        await InsertRequestAsync(conn, leaveEmployee, onLeave, "leave", "Approved");

        return new(suffix, department, assigner, assignerToken, present, presentToken, presentEmployee,
            absent, onLeave, trip, tripEmployee, hr);
    }

    /// <summary>Đơn phủ ĐÚNG ngày hôm nay theo giờ VN — đúng thứ mà WorkforceAvailability đối chiếu.</summary>
    private async Task InsertRequestAsync(Guid employeeId, string username, string type, string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await InsertRequestAsync(conn, employeeId, username, type, status);
    }

    private static async Task InsertRequestAsync(NpgsqlConnection conn, Guid employeeId, string username,
        string type, string status)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).ToString("yyyy-MM-dd");
        await conn.Cmd("""
            INSERT INTO hr_requests (id, request_no, req_type, title, employee_id, requester_username,
                payload, status, current_step)
            VALUES (@id, @no, @type, 'Test', @emp, @u,
                jsonb_build_object('fromDate', @day, 'toDate', @day, 'days', 1, 'reason', 'test'),
                @status, 1)
            """)
            .With("@id", Guid.NewGuid()).With("@no", "WA" + Guid.NewGuid().ToString("N")[..8])
            .With("@type", type).With("@emp", employeeId).With("@u", username)
            .With("@day", today).With("@status", status)
            .ExecuteNonQueryAsync();
    }

    private async Task<(string Title, string Body)?> ReadNotificationAsync(string username, string category)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await using var r = await conn.Cmd("""
            SELECT title, body FROM web_notifications
            WHERE lower(username)=lower(@u) AND category=@c
            ORDER BY id DESC LIMIT 1
            """).With("@u", username).With("@c", category).ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.Str("title"), r.Str("body")) : null;
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
            VALUES (@id,@code,@uid,@u,@u,@dept,'Active','Nhân viên')
            """).With("@id", employeeId).With("@code", "WA" + Guid.NewGuid().ToString("N")[..8])
            .With("@uid", userId).With("@u", username).With("@dept", departmentId)
            .ExecuteNonQueryAsync();
        return tokens.CreateToken(new UserDto(userId, username, username, "", role, true, "Approved", DateTime.UtcNow),
            "app:wa:" + Guid.NewGuid().ToString("N")[..16]);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
