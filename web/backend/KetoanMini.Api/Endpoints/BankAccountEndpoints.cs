using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Tài khoản ngân hàng của nhân viên (dùng để nhận lương/thanh toán). Mỗi nhân viên có thể lưu nhiều
/// thẻ, một thẻ đặt làm mặc định. Frontend hiển thị mỗi tài khoản như một thẻ ngân hàng, nền tự đồng bộ
/// theo thương hiệu ngân hàng. Trước mắt chỉ hỗ trợ Vietcombank &amp; Sacombank (danh sách <see cref="Banks"/>).
/// Gắn với hr_employees.id như các module nhân sự khác; bảng tự tạo lúc khởi động.
/// </summary>
public static class BankAccountEndpoints
{
    /// <summary>Ngân hàng hỗ trợ: mã → tên đầy đủ + tên hiển thị. Frontend dựng danh sách chọn từ đây.</summary>
    public static readonly (string Code, string Name, string ShortName)[] Banks =
    {
        ("vietcombank", "Ngân hàng TMCP Ngoại thương Việt Nam", "Vietcombank"),
        ("sacombank", "Ngân hàng TMCP Sài Gòn Thương Tín", "Sacombank"),
    };

    private static bool IsKnownBank(string? code) => Array.Exists(Banks, b => b.Code == code);
    private static string NormBank(string? code) => IsKnownBank(code) ? code!.Trim() : Banks[0].Code;

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS hr_bank_accounts (
                id uuid PRIMARY KEY,
                employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE CASCADE,
                bank varchar(32) NOT NULL DEFAULT 'vietcombank',
                account_number varchar(40) NOT NULL DEFAULT '',
                account_holder varchar(200) NOT NULL DEFAULT '',
                branch varchar(200) NOT NULL DEFAULT '',
                is_default boolean NOT NULL DEFAULT FALSE,
                note text NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_hr_bank_accounts_emp ON hr_bank_accounts (employee_id, is_default DESC, created_at);
            """).ExecuteNonQueryAsync(ct);
    }

    public static void MapBankAccounts(this WebApplication app)
    {
        var g = app.MapGroup("/api/bank-accounts").RequireAuthorization();

        g.MapGet("/banks", () =>
            Results.Ok(Array.ConvertAll(Banks, b => new { code = b.Code, name = b.Name, shortName = b.ShortName })));

        // Danh sách thẻ: mặc định của chính mình; admin có thể xem của nhân viên khác qua ?employeeId=.
        g.MapGet("/", async (ClaimsPrincipal u, Database db, Guid? employeeId) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await ResolveEmployee(conn, u, employeeId);
            if (empId is null) return Results.Forbid();

            var list = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT b.id, b.employee_id, b.bank, b.account_number, b.account_holder, b.branch,
                       b.is_default, b.note,
                       e.full_name AS emp_name, e.employee_code
                FROM hr_bank_accounts b JOIN hr_employees e ON e.id = b.employee_id
                WHERE b.employee_id = @emp
                ORDER BY b.is_default DESC, b.created_at
                """).With("@emp", empId.Value).ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(ReadAccount(r));
            return Results.Ok(list);
        });

        g.MapPost("/", async (SaveBankAccountReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await ResolveEmployee(conn, u, req.EmployeeId);
            if (empId is null) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.AccountNumber))
                return Results.BadRequest(new { message = "Vui lòng nhập số tài khoản." });

            // Tự điền chủ tài khoản theo tên nhân viên (viết hoa) nếu để trống — đồng bộ với hồ sơ.
            var holder = (req.AccountHolder ?? "").Trim();
            if (string.IsNullOrEmpty(holder))
                holder = ((await conn.Cmd("SELECT full_name FROM hr_employees WHERE id=@id")
                    .With("@id", empId.Value).ExecuteScalarAsync() as string) ?? "").ToUpperInvariant();

            // Thẻ đầu tiên của nhân viên luôn là mặc định.
            var count = Convert.ToInt64(await conn.Cmd("SELECT COUNT(*) FROM hr_bank_accounts WHERE employee_id=@e")
                .With("@e", empId.Value).ExecuteScalarAsync());
            var makeDefault = req.IsDefault || count == 0;

            var id = Guid.NewGuid();
            if (makeDefault)
                await ClearDefault(conn, empId.Value);
            await conn.Cmd("""
                INSERT INTO hr_bank_accounts (id, employee_id, bank, account_number, account_holder, branch, is_default, note)
                VALUES (@id, @emp, @bank, @num, @holder, @branch, @def, @note)
                """)
                .With("@id", id).With("@emp", empId.Value).With("@bank", NormBank(req.Bank))
                .With("@num", req.AccountNumber.Trim()).With("@holder", holder)
                .With("@branch", (req.Branch ?? "").Trim()).With("@def", makeDefault)
                .With("@note", req.Note ?? "").ExecuteNonQueryAsync();

            await SignalEmployee(hub, db, u, "Thêm tài khoản ngân hàng", id.ToString());
            return Results.Ok(new { id });
        });

        g.MapPut("/{id:guid}", async (Guid id, SaveBankAccountReq req, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await OwnerOf(conn, id);
            if (empId is null) return Results.NotFound();
            if (!await CanManage(conn, u, empId.Value)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.AccountNumber))
                return Results.BadRequest(new { message = "Vui lòng nhập số tài khoản." });

            if (req.IsDefault) await ClearDefault(conn, empId.Value);
            var n = await conn.Cmd("""
                UPDATE hr_bank_accounts SET bank=@bank, account_number=@num, account_holder=@holder,
                    branch=@branch, is_default=@def, note=@note, updated_at=CURRENT_TIMESTAMP
                WHERE id=@id
                """)
                .With("@id", id).With("@bank", NormBank(req.Bank))
                .With("@num", req.AccountNumber.Trim()).With("@holder", (req.AccountHolder ?? "").Trim())
                .With("@branch", (req.Branch ?? "").Trim()).With("@def", req.IsDefault)
                .With("@note", req.Note ?? "").ExecuteNonQueryAsync();
            if (n == 0) return Results.NotFound();

            await SignalEmployee(hub, db, u, "Cập nhật tài khoản ngân hàng", id.ToString());
            return Results.NoContent();
        });

        // Đặt một thẻ làm mặc định (gỡ mặc định của các thẻ còn lại của nhân viên đó).
        g.MapPost("/{id:guid}/default", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await OwnerOf(conn, id);
            if (empId is null) return Results.NotFound();
            if (!await CanManage(conn, u, empId.Value)) return Results.Forbid();

            await ClearDefault(conn, empId.Value);
            await conn.Cmd("UPDATE hr_bank_accounts SET is_default=TRUE, updated_at=CURRENT_TIMESTAMP WHERE id=@id")
                .With("@id", id).ExecuteNonQueryAsync();
            await SignalEmployee(hub, db, u, "Đặt tài khoản ngân hàng mặc định", id.ToString());
            return Results.NoContent();
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db, IHubContext<ChangesHub> hub) =>
        {
            await using var conn = await db.OpenAsync();
            var empId = await OwnerOf(conn, id);
            if (empId is null) return Results.NotFound();
            if (!await CanManage(conn, u, empId.Value)) return Results.Forbid();

            var wasDefault = Convert.ToBoolean(
                await conn.Cmd("SELECT is_default FROM hr_bank_accounts WHERE id=@id").With("@id", id).ExecuteScalarAsync());
            await conn.Cmd("DELETE FROM hr_bank_accounts WHERE id=@id").With("@id", id).ExecuteNonQueryAsync();
            // Nếu vừa xóa thẻ mặc định, tự nâng thẻ cũ nhất còn lại lên làm mặc định.
            if (wasDefault)
                await conn.Cmd("""
                    UPDATE hr_bank_accounts SET is_default=TRUE
                    WHERE id = (SELECT id FROM hr_bank_accounts WHERE employee_id=@e ORDER BY created_at LIMIT 1)
                    """).With("@e", empId.Value).ExecuteNonQueryAsync();

            await SignalEmployee(hub, db, u, "Xóa tài khoản ngân hàng", id.ToString());
            return Results.NoContent();
        });
    }

    // ---- Trợ giúp ----

    /// <summary>Nhân viên đích cho thao tác đọc/tạo: admin có thể truyền employeeId; còn lại là chính mình.</summary>
    private static async Task<Guid?> ResolveEmployee(NpgsqlConnection conn, ClaimsPrincipal u, Guid? employeeId)
    {
        if (employeeId is { } given && given != Guid.Empty)
            return u.IsAdmin() ? given : null; // chỉ admin được xem/tạo hộ nhân viên khác
        return await HrEndpoints.EnsureEmployeeForUser(conn, u.Username());
    }

    private static async Task<Guid?> OwnerOf(NpgsqlConnection conn, Guid accountId)
        => await conn.Cmd("SELECT employee_id FROM hr_bank_accounts WHERE id=@id")
            .With("@id", accountId).ExecuteScalarAsync() is Guid g ? g : null;

    /// <summary>Admin quản lý mọi thẻ; nhân viên chỉ quản lý thẻ của chính mình.</summary>
    private static async Task<bool> CanManage(NpgsqlConnection conn, ClaimsPrincipal u, Guid employeeId)
    {
        if (u.IsAdmin()) return true;
        var owner = await conn.Cmd("SELECT username FROM hr_employees WHERE id=@id")
            .With("@id", employeeId).ExecuteScalarAsync() as string;
        return string.Equals(owner, u.Username(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task ClearDefault(NpgsqlConnection conn, Guid employeeId)
        => conn.Cmd("UPDATE hr_bank_accounts SET is_default=FALSE WHERE employee_id=@e AND is_default=TRUE")
            .With("@e", employeeId).ExecuteNonQueryAsync();

    private static object ReadAccount(NpgsqlDataReader r) => new
    {
        id = r.Guid("id"),
        employeeId = r.Guid("employee_id"),
        employeeName = r.Str("emp_name"),
        employeeCode = r.Str("employee_code"),
        bank = r.Str("bank"),
        accountNumber = r.Str("account_number"),
        accountHolder = r.Str("account_holder"),
        branch = r.Str("branch"),
        isDefault = r.Bool("is_default"),
        note = r.Str("note"),
    };

    private static async Task SignalEmployee(IHubContext<ChangesHub> hub, Database db,
        ClaimsPrincipal u, string action, string name)
    {
        await db.RecordAudit(u.Username(), action, "BankAccount", name, $"{action} (web).");
        // Clients.All ĐÃ gồm cả nhân viên chủ tài khoản, nên không gửi thêm bản riêng cho họ nữa:
        // trước đây máy của người đó nhận "data"/"hr" HAI lần và tải lại dữ liệu thừa một lượt.
        await hub.Clients.All.SendAsync("changed", "data");
        await hub.Clients.All.SendAsync("changed", "hr");
    }

    public record SaveBankAccountReq(Guid? EmployeeId, string? Bank, string? AccountNumber,
        string? AccountHolder, string? Branch, bool IsDefault, string? Note);
}
