using System.Security.Claims;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Npgsql;

namespace KetoanMini.Api.Endpoints;

/// <summary>
/// Lệnh thu tiền khách hàng: kế toán giao tài xế đi thu, tài xế xác nhận số tiền theo mệnh giá,
/// thủ quỹ đếm lại và chỉ khi số tiền khớp dự kiến, hoặc sai lệch được Kế toán trưởng duyệt,
/// thì máy chủ mới ghi nhận lệnh là đã nộp đủ tiền.
/// Không thu thập GPS và không sao chép/lưu địa chỉ khách hàng trong lệnh.
/// </summary>
public static class CashCollectionEndpoints
{
    public const string StatusAssigned = "Assigned";
    public const string StatusAccepted = "Accepted";
    public const string StatusPendingHandover = "PendingHandover";
    public const string StatusFailed = "Failed";
    public const string StatusVariance = "Variance";
    public const string StatusCompleted = "Completed";
    public const string StatusCancelled = "Cancelled";

    private const string StageDriver = "driver";
    private const string StageAccountant = "accountant";
    private static readonly HashSet<long> AllowedDenominations =
        [500_000, 200_000, 100_000, 50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100];

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE SEQUENCE IF NOT EXISTS cash_collection_order_seq START 1;

            CREATE TABLE IF NOT EXISTS cash_collection_orders (
                id uuid PRIMARY KEY,
                order_no varchar(32) NOT NULL UNIQUE,
                customer_id uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
                customer_name varchar(256) NOT NULL DEFAULT '',
                customer_phone varchar(64) NOT NULL DEFAULT '',
                driver_employee_id uuid NOT NULL REFERENCES hr_employees(id) ON DELETE RESTRICT,
                driver_username varchar(128) NOT NULL DEFAULT '',
                driver_name varchar(256) NOT NULL DEFAULT '',
                expected_amount numeric(18,0) NOT NULL,
                scheduled_date date NOT NULL,
                handover_due_at timestamptz NOT NULL,
                note text NOT NULL DEFAULT '',
                status varchar(32) NOT NULL DEFAULT 'Assigned',
                created_by varchar(128) NOT NULL DEFAULT '',
                created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                accepted_by varchar(128) NOT NULL DEFAULT '',
                accepted_at timestamptz NULL,
                collected_by varchar(128) NOT NULL DEFAULT '',
                collected_at timestamptz NULL,
                collected_amount numeric(18,0) NULL,
                failed_by varchar(128) NOT NULL DEFAULT '',
                failed_at timestamptz NULL,
                failure_reason text NOT NULL DEFAULT '',
                received_by varchar(128) NOT NULL DEFAULT '',
                received_at timestamptz NULL,
                received_amount numeric(18,0) NULL,
                payment_id uuid NULL REFERENCES payments(id) ON DELETE RESTRICT,
                cancelled_by varchar(128) NOT NULL DEFAULT '',
                cancelled_at timestamptz NULL,
                cancel_reason text NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_cash_collection_orders_driver
                ON cash_collection_orders (driver_username, status, scheduled_date DESC);
            CREATE INDEX IF NOT EXISTS ix_cash_collection_orders_status
                ON cash_collection_orders (status, handover_due_at, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_cash_collection_orders_customer
                ON cash_collection_orders (customer_id, created_at DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_cash_collection_active_customer
                ON cash_collection_orders (customer_id)
                WHERE status IN ('Assigned','Accepted','PendingHandover','Variance');

            CREATE TABLE IF NOT EXISTS cash_count_sessions (
                id uuid PRIMARY KEY,
                order_id uuid NOT NULL REFERENCES cash_collection_orders(id) ON DELETE RESTRICT,
                stage varchar(20) NOT NULL,
                revision integer NOT NULL,
                actor_username varchar(128) NOT NULL DEFAULT '',
                total numeric(18,0) NOT NULL,
                confirmed_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE (order_id, stage, revision)
            );
            CREATE INDEX IF NOT EXISTS ix_cash_count_sessions_order
                ON cash_count_sessions (order_id, stage, revision DESC);

            CREATE TABLE IF NOT EXISTS cash_count_lines (
                id bigserial PRIMARY KEY,
                session_id uuid NOT NULL REFERENCES cash_count_sessions(id) ON DELETE RESTRICT,
                denomination bigint NOT NULL,
                quantity integer NOT NULL,
                subtotal numeric(18,0) NOT NULL,
                UNIQUE (session_id, denomination),
                CHECK (denomination > 0 AND quantity > 0 AND subtotal > 0)
            );

            -- Sổ sự kiện là nguồn kiểm toán bắt buộc và nằm cùng transaction với nghiệp vụ.
            CREATE TABLE IF NOT EXISTS cash_collection_events (
                id uuid PRIMARY KEY,
                order_id uuid NOT NULL,
                action varchar(48) NOT NULL,
                actor_username varchar(128) NOT NULL DEFAULT '',
                before_status varchar(32) NULL,
                after_status varchar(32) NULL,
                note text NOT NULL DEFAULT '',
                event_data jsonb NULL,
                occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_cash_collection_events_order
                ON cash_collection_events (order_id, occurred_at, id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_cash_collection_events_lifecycle
                ON cash_collection_events (order_id, action)
                WHERE action IN ('created','accepted','collected','failed','completed','cancelled');

            CREATE OR REPLACE FUNCTION prevent_cash_collection_event_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'cash_collection_events is append-only';
            END;
            $fn$;
            DROP TRIGGER IF EXISTS trg_cash_collection_events_immutable ON cash_collection_events;
            CREATE TRIGGER trg_cash_collection_events_immutable
                BEFORE UPDATE OR DELETE ON cash_collection_events
                FOR EACH ROW EXECUTE FUNCTION prevent_cash_collection_event_mutation();

            ALTER TABLE payments
                ADD COLUMN IF NOT EXISTS source_kind varchar(32) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS source_id uuid NULL,
                ADD COLUMN IF NOT EXISTS created_by varchar(128) NOT NULL DEFAULT '';
            CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_source
                ON payments (source_kind, source_id)
                WHERE source_id IS NOT NULL AND source_kind <> '';
            """).ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Ai cần biết tiến trình một lệnh thu tiền: người theo dõi toàn bộ lệnh (kế toán, kế toán
    /// trưởng) và THỦ QUỸ đang chờ nhận tiền về két. Quản trị viên được cộng thêm trong
    /// <see cref="PushService.SendToPermissionAsync"/> — Admin cố ý không có quyền collections.*
    /// (xem <see cref="Permissions"/>) nhưng vẫn phải nắm được dòng tiền.
    /// </summary>
    private static readonly string[] CollectionAudience =
        [Permissions.CollectionsReadAll, Permissions.CollectionsReceive];

    /// <summary>
    /// Báo một mốc của lệnh thu tiền cho cả bộ phận. Gọi SAU khi giao dịch đã commit: thông báo là
    /// hệ quả của việc đã ghi xong, không được phép giữ chỗ trong giao dịch tiền bạc.
    /// </summary>
    private static Task AnnounceCollectionAsync(PushService push, string actorUsername,
        string title, string body, string notifId)
        => push.SendToPermissionAsync(CollectionAudience, title, body, notifId,
            target: "CashCollections", link: "/lenh-thu-tien", category: "collection",
            exceptUsername: actorUsername);

    public static void MapCashCollections(this WebApplication app)
    {
        var g = app.MapGroup("/api/cash-collections").RequireAuthorization();

        // Danh sách tối giản dành riêng cho lệnh thu: không đưa địa chỉ vào quy trình này.
        g.MapGet("/customers", async (ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.CollectionsCreate)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            if (!await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return Results.Forbid();
            var rows = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT id, name, phone
                FROM customers
                WHERE is_active=TRUE
                ORDER BY name
                """).ExecuteReaderAsync();
            while (await r.ReadAsync())
                rows.Add(new { id = r.Guid("id"), name = r.Str("name"), phone = r.Str("phone") });
            return Results.Ok(rows);
        });

        g.MapGet("/drivers", async (ClaimsPrincipal u, Database db) =>
        {
            if (!u.Can(Permissions.CollectionsCreate)) return Results.Forbid();
            await using var conn = await db.OpenAsync();
            if (!await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return Results.Forbid();
            var rows = new List<object>();
            await using var r = await conn.Cmd("""
                SELECT e.id, account.username, e.full_name, e.employee_code, e.position
                FROM hr_employees e
                JOIN LATERAL (
                    SELECT candidate.username, candidate.role
                    FROM app_users candidate
                    WHERE candidate.is_deleted=FALSE AND candidate.is_active=TRUE
                      AND candidate.approval_status='Approved'
                      AND (candidate.id=e.user_id
                           OR (e.user_id IS NULL AND lower(candidate.username)=lower(e.username)))
                    ORDER BY (candidate.id=e.user_id) DESC
                    LIMIT 1
                ) account ON TRUE
                WHERE e.status='Active' AND account.username<>''
                  AND lower(account.username)<>lower(@me)
                  AND (account.role=@driverRole OR EXISTS (
                      SELECT 1 FROM user_roles extra
                      WHERE extra.username=account.username AND extra.role=@driverRole
                        AND (extra.expires_at IS NULL OR extra.expires_at>CURRENT_TIMESTAMP)
                  ))
                ORDER BY e.full_name, e.employee_code
                """).With("@driverRole", AppRoles.Driver).With("@me", u.Username()).ExecuteReaderAsync();
            while (await r.ReadAsync())
                rows.Add(new
                {
                    id = r.Guid("id"), username = r.Str("username"), name = r.Str("full_name"),
                    employeeCode = r.Str("employee_code"), position = r.Str("position"),
                });
            return Results.Ok(rows);
        });

        g.MapGet("/", async (ClaimsPrincipal u, Database db, string? scope, string? status) =>
        {
            await using var conn = await db.OpenAsync();
            var all = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase);
            var accountingParticipant = await IsAccountingParticipantAsync(conn, u);
            var driverParticipant = await IsDriverRoleAsync(conn, u);
            if (all && !accountingParticipant) return Results.Forbid();
            if (!all && !driverParticipant) return Results.Forbid();
            var where = new List<string>();
            var parameters = new List<(string, object)>();
            if (!all)
            {
                where.Add("lower(o.driver_username)=lower(@me)");
                parameters.Add(("@me", u.Username()));
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                where.Add("o.status=@status");
                parameters.Add(("@status", status.Trim()));
            }

            var cmd = conn.Cmd(OrderSelect + " WHERE " + (where.Count == 0 ? "TRUE" : string.Join(" AND ", where)) +
                               " ORDER BY CASE WHEN o.status IN ('PendingHandover','Variance') THEN 0 WHEN o.status IN ('Assigned','Accepted') THEN 1 ELSE 2 END, o.handover_due_at, o.created_at DESC");
            foreach (var (name, value) in parameters) cmd.With(name, value);
            var list = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(ReadDto(r, u, driverParticipant, accountingParticipant));
            return Results.Ok(list);
        });

        /// <summary>
        /// LỊCH SỬ ĐI THU của một tháng: ai đi thu, thu của ai, thu được bao nhiêu, ai nhận tiền về quỹ.
        /// Chỉ liệt kê các lệnh đã KẾT THÚC (hoàn tất / không thu được / đã hủy) — lệnh đang chạy vẫn
        /// nằm ở tab điều hành. Lọc theo ngày đi thu và bằng KHOẢNG để index còn dùng được.
        /// </summary>
        g.MapGet("/history", async (ClaimsPrincipal u, Database db, string? month, string? driver) =>
        {
            await using var conn = await db.OpenAsync();
            if (!await IsAccountingParticipantAsync(conn, u)) return Results.Forbid();
            var period = MonthOrCurrent(month);
            MonthRange(period, out var from, out var to);
            var driverFilter = (driver ?? "").Trim();

            var where = new List<string>
            {
                "o.status IN ('Completed','Failed','Cancelled')",
                "o.scheduled_date >= @from AND o.scheduled_date < @to",
            };
            if (driverFilter.Length > 0) where.Add("lower(o.driver_username)=lower(@driver)");

            var cmd = conn.Cmd($"""
                SELECT o.id, o.order_no, o.customer_name, o.customer_phone, o.driver_username, o.driver_name,
                       o.expected_amount, o.collected_amount, o.received_amount, o.scheduled_date,
                       o.handover_due_at, o.status, o.created_by, o.created_at, o.accepted_at, o.collected_at,
                       o.received_by, o.received_at, o.failure_reason, o.cancel_reason, o.payment_id
                FROM cash_collection_orders o
                WHERE {string.Join(" AND ", where)}
                ORDER BY o.scheduled_date DESC, o.order_no DESC
                """).With("@from", DateOnly.FromDateTime(from)).With("@to", DateOnly.FromDateTime(to));
            if (driverFilter.Length > 0) cmd.With("@driver", driverFilter);

            var trips = new List<object>();
            // Gộp theo tài xế ngay tại đây thay vì thêm một vòng truy vấn nữa: số lệnh của một tháng
            // luôn nhỏ, và như vậy hai con số trên màn hình chắc chắn khớp nhau.
            var byDriver = new Dictionary<string, DriverTally>(StringComparer.OrdinalIgnoreCase);
            decimal totalCollected = 0;
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                {
                    var status = r.Str("status");
                    var received = r.IsDBNull(r.GetOrdinal("received_amount")) ? (decimal?)null : r.Dec("received_amount");
                    var collected = r.IsDBNull(r.GetOrdinal("collected_amount")) ? (decimal?)null : r.Dec("collected_amount");
                    var handed = status == StatusCompleted ? (received ?? collected ?? 0) : 0;
                    totalCollected += handed;

                    var username = r.Str("driver_username");
                    if (!byDriver.TryGetValue(username, out var tally))
                        byDriver[username] = tally = new DriverTally(r.Str("driver_name"));
                    tally.Trips++;
                    tally.Collected += handed;
                    if (status == StatusCompleted) tally.Completed++;
                    else if (status == StatusFailed) tally.Failed++;
                    else tally.Cancelled++;

                    trips.Add(new
                    {
                        id = r.Guid("id"),
                        orderNo = r.Str("order_no"),
                        customerName = r.Str("customer_name"),
                        customerPhone = r.Str("customer_phone"),
                        driverUsername = username,
                        driverName = r.Str("driver_name"),
                        expectedAmount = r.Dec("expected_amount"),
                        collectedAmount = collected,
                        receivedAmount = received,
                        scheduledDate = r.DateOnly("scheduled_date"),
                        handoverDueAt = r.Dt("handover_due_at"),
                        status,
                        createdBy = r.Str("created_by"),
                        createdAt = r.Dt("created_at"),
                        acceptedAt = r.DtNull("accepted_at"),
                        collectedAt = r.DtNull("collected_at"),
                        receivedBy = r.Str("received_by"),
                        receivedAt = r.DtNull("received_at"),
                        failureReason = r.Str("failure_reason"),
                        cancelReason = r.Str("cancel_reason"),
                        postedToFund = !r.IsDBNull(r.GetOrdinal("payment_id")),
                    });
                }

            return Results.Ok(new
            {
                month = period,
                totalTrips = trips.Count,
                totalCollected,
                drivers = byDriver
                    .Select(pair => new
                    {
                        username = pair.Key, name = pair.Value.Name, trips = pair.Value.Trips,
                        completed = pair.Value.Completed, failed = pair.Value.Failed,
                        cancelled = pair.Value.Cancelled, collected = pair.Value.Collected,
                    })
                    .OrderByDescending(x => x.collected).ThenBy(x => x.name)
                    .ToList(),
                trips,
            });
        });

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var row = await LoadOrder(conn, id, false);
            if (row is null) return Results.NotFound();
            var accountingParticipant = await IsAccountingParticipantAsync(conn, u);
            var driverParticipant = await IsDriverRoleAsync(conn, u);
            if (!accountingParticipant && !(driverParticipant && IsAssignedDriver(row, u))) return Results.Forbid();
            var counts = await LoadCounts(conn, id);
            var events = await LoadEvents(conn, id);
            return Results.Ok(new { order = row.ToDto(u, driverParticipant, accountingParticipant), counts, events });
        });

        g.MapPost("/", async (CreateCollectionReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            if (!u.Can(Permissions.CollectionsCreate)) return Results.Forbid();
            if (req.ExpectedAmount <= 0 || req.ExpectedAmount != decimal.Truncate(req.ExpectedAmount))
                return Results.BadRequest(new { message = "Số tiền dự kiến phải là số nguyên dương." });
            if (req.ExpectedAmount > 999_999_999_999_999m)
                return Results.BadRequest(new { message = "Số tiền dự kiến vượt giới hạn cho phép." });
            var note = (req.Note ?? "").Trim();
            if (note.Length > 1000) return Results.BadRequest(new { message = "Ghi chú không được vượt quá 1.000 ký tự." });
            if (req.HandoverDueAt <= DateTime.UtcNow)
                return Results.BadRequest(new { message = "Hạn bàn giao phải lớn hơn thời điểm hiện tại." });

            await using var conn = await db.OpenAsync();
            if (!await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();

            CustomerSnap? customer = null;
            await using (var r = await conn.Cmd("SELECT id,name,phone,is_active FROM customers WHERE id=@id", tx)
                .With("@id", req.CustomerId).ExecuteReaderAsync())
                if (await r.ReadAsync() && r.Bool("is_active"))
                    customer = new(r.Guid("id"), r.Str("name"), r.Str("phone"));
            if (customer is null) return Results.BadRequest(new { message = "Khách hàng không tồn tại hoặc đã ngừng hoạt động." });

            DriverSnap? driver = null;
            await using (var r = await conn.Cmd("""
                SELECT e.id,account.username,e.full_name
                FROM hr_employees e
                JOIN LATERAL (
                    SELECT candidate.username,candidate.role
                    FROM app_users candidate
                    WHERE candidate.is_deleted=FALSE AND candidate.is_active=TRUE
                      AND candidate.approval_status='Approved'
                      AND (candidate.id=e.user_id
                           OR (e.user_id IS NULL AND lower(candidate.username)=lower(e.username)))
                    ORDER BY (candidate.id=e.user_id) DESC
                    LIMIT 1
                ) account ON TRUE
                WHERE e.id=@id AND e.status='Active' AND account.username<>''
                  AND (account.role=@driverRole OR EXISTS (
                      SELECT 1 FROM user_roles extra
                      WHERE extra.username=account.username AND extra.role=@driverRole
                        AND (extra.expires_at IS NULL OR extra.expires_at>CURRENT_TIMESTAMP)
                  ))
                """, tx).With("@id", req.DriverEmployeeId).With("@driverRole", AppRoles.Driver).ExecuteReaderAsync())
                if (await r.ReadAsync()) driver = new(r.Guid("id"), r.Str("username"), r.Str("full_name"));
            if (driver is null) return Results.BadRequest(new { message = "Người được giao phải có role Lái xe, tài khoản hoạt động và hồ sơ đang làm việc." });
            if (SameUser(driver.Username, u.Username()))
                return Results.BadRequest(new { message = "Người tạo lệnh không được đồng thời là tài xế thực hiện lệnh." });

            var duplicate = await conn.Cmd("""
                SELECT order_no FROM cash_collection_orders
                WHERE customer_id=@customer AND status IN ('Assigned','Accepted','PendingHandover','Variance')
                LIMIT 1
                """, tx).With("@customer", customer.Id).ExecuteScalarAsync();
            if (duplicate is not null)
                return Results.BadRequest(new { message = $"Khách hàng đang có lệnh {duplicate}; hãy hoàn tất hoặc hủy lệnh cũ trước." });

            var id = Guid.NewGuid();
            var no = await NextOrderNo(conn, tx);
            await conn.Cmd("""
                INSERT INTO cash_collection_orders
                    (id,order_no,customer_id,customer_name,customer_phone,driver_employee_id,
                     driver_username,driver_name,expected_amount,scheduled_date,handover_due_at,note,status,created_by)
                VALUES
                    (@id,@no,@customer,@customerName,@phone,@driver,@driverUser,@driverName,
                     @amount,@scheduled,@due,@note,@status,@by)
                """, tx)
                .With("@id", id).With("@no", no).With("@customer", customer.Id)
                .With("@customerName", customer.Name).With("@phone", customer.Phone)
                .With("@driver", driver.Id).With("@driverUser", driver.Username).With("@driverName", driver.Name)
                .With("@amount", decimal.Truncate(req.ExpectedAmount)).With("@scheduled", req.ScheduledDate)
                .With("@due", req.HandoverDueAt.ToUniversalTime()).With("@note", note)
                .With("@status", StatusAssigned).With("@by", u.Username()).ExecuteNonQueryAsync();
            await AppendEvent(conn, tx, id, "created", u.Username(), null, StatusAssigned,
                $"Giao {driver.Name} thu tiền của {customer.Name}.", new { expectedAmount = req.ExpectedAmount, req.ScheduledDate, req.HandoverDueAt });
            await push.EnqueueToUserAsync(conn, tx, driver.Username, "Bạn có lệnh thu tiền mới",
                $"{no}: {customer.Name} · {req.ExpectedAmount:N0} ₫", $"cash-collection:{id}:assigned", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Tạo lệnh thu tiền", "CashCollection", no,
                $"{customer.Name}; tài xế {driver.Name}; dự kiến {req.ExpectedAmount:N0} đồng.");
            return Results.Ok(new { id, orderNo = no });
        });

        g.MapPost("/{id:guid}/accept", async (Guid id, ClaimsPrincipal u, Database db, PushService push) =>
        {
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            if (!await IsDriverRoleAsync(conn, u, tx)) return Results.Forbid();
            var before = await LoadOrder(conn, id, true, tx);
            if (before is null) return Results.NotFound();
            if (!IsAssignedDriver(before, u)) return Results.Forbid();
            if (SameUser(before.CreatedBy, u.Username())) return Results.Forbid();
            if (before.Status != StatusAssigned)
                return Results.BadRequest(new { message = "Lệnh không còn ở trạng thái chờ tài xế nhận." });
            await conn.Cmd("""
                UPDATE cash_collection_orders SET status=@to,accepted_by=@me,accepted_at=CURRENT_TIMESTAMP,
                    updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status=@from
                """, tx).With("@to", StatusAccepted).With("@me", u.Username()).With("@id", id)
                .With("@from", StatusAssigned).ExecuteNonQueryAsync();
            await AppendEvent(conn, tx, id, "accepted", u.Username(), StatusAssigned, StatusAccepted, "Tài xế nhận lệnh.");
            await push.EnqueueToUserAsync(conn, tx, before.CreatedBy, "Tài xế đã nhận lệnh thu tiền",
                $"{before.OrderNo}: {before.DriverName} đã nhận lệnh.", $"cash-collection:{id}:accepted", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Nhận lệnh thu tiền", "CashCollection", before.OrderNo, before.CustomerName);
            await AnnounceCollectionAsync(push, u.Username(), "Tài xế đã nhận lệnh thu tiền",
                $"{before.OrderNo}: {before.DriverName} đi thu {before.CustomerName}.",
                $"cash-collection:{id}:accepted");
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/fail", async (Guid id, ReasonReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do không thu được tiền." });
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            if (!await IsDriverRoleAsync(conn, u, tx)) return Results.Forbid();
            var before = await LoadOrder(conn, id, true, tx);
            if (before is null) return Results.NotFound();
            if (!IsAssignedDriver(before, u)) return Results.Forbid();
            if (SameUser(before.CreatedBy, u.Username())) return Results.Forbid();
            if (before.Status is not (StatusAssigned or StatusAccepted))
                return Results.BadRequest(new { message = "Lệnh không còn ở trạng thái có thể báo không thu được." });
            await conn.Cmd("""
                UPDATE cash_collection_orders SET status=@to,failed_by=@me,failed_at=CURRENT_TIMESTAMP,
                    failure_reason=@reason,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status=@from
                """, tx).With("@to", StatusFailed).With("@me", u.Username()).With("@reason", reason)
                .With("@id", id).With("@from", before.Status).ExecuteNonQueryAsync();
            await AppendEvent(conn, tx, id, "failed", u.Username(), before.Status, StatusFailed, reason);
            await push.EnqueueToUserAsync(conn, tx, before.CreatedBy, "Không thu được tiền khách hàng",
                $"{before.OrderNo}: {before.CustomerName} · {reason}", $"cash-collection:{id}:failed", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Báo không thu được tiền", "CashCollection", before.OrderNo, reason);
            await AnnounceCollectionAsync(push, u.Username(), "Không thu được tiền khách hàng",
                $"{before.OrderNo}: {before.DriverName} báo không thu được của {before.CustomerName}. Lý do: {reason}",
                $"cash-collection:{id}:failed");
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/collect", async (Guid id, CashCountReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            var computed = ComputeCash(req.Lines);
            if (computed.Error is not null) return Results.BadRequest(new { message = computed.Error });
            await using var conn = await db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            if (!await IsDriverRoleAsync(conn, u, tx)) return Results.Forbid();
            var before = await LoadOrder(conn, id, true, tx);
            if (before is null) return Results.NotFound();
            if (!IsAssignedDriver(before, u)) return Results.Forbid();
            if (SameUser(before.CreatedBy, u.Username())) return Results.Forbid();
            if (before.Status != StatusAccepted)
                return Results.BadRequest(new { message = "Tài xế phải nhận lệnh trước khi xác nhận đã thu tiền." });
            var varianceReason = (req.Reason ?? "").Trim();
            if (varianceReason.Length > 1000)
                return Results.BadRequest(new { message = "Lý do chênh lệch không được vượt quá 1.000 ký tự." });
            var expectedDifference = computed.Total - before.ExpectedAmount;
            if (expectedDifference != 0 && varianceReason.Length == 0)
                return Results.BadRequest(new
                {
                    message = $"Số thực thu lệch {expectedDifference:N0} ₫ so với dự kiến. Vui lòng nhập lý do chênh lệch."
                });
            var driverRevision = await NextCashRevision(conn, tx, id, StageDriver);
            await SaveCashCount(conn, tx, id, StageDriver, driverRevision, u.Username(), computed);
            await conn.Cmd("""
                UPDATE cash_collection_orders SET status=@to,collected_by=@me,collected_at=CURRENT_TIMESTAMP,
                    collected_amount=@amount,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status=@from
                """, tx).With("@to", StatusPendingHandover).With("@me", u.Username())
                .With("@amount", computed.Total).With("@id", id).With("@from", StatusAccepted).ExecuteNonQueryAsync();
            var collectionAction = driverRevision == 1 ? "collected" : "recollected";
            var collectionNote = expectedDifference == 0
                ? $"Đã thu {computed.Total:N0} đồng từ khách hàng."
                : $"Đã thu {computed.Total:N0} đồng, lệch {expectedDifference:N0} đồng so với dự kiến. Lý do: {varianceReason}";
            await AppendEvent(conn, tx, id, collectionAction, u.Username(), StatusAccepted, StatusPendingHandover,
                collectionNote, new
                {
                    total = computed.Total, expectedAmount = before.ExpectedAmount, expectedDifference,
                    varianceReason, revision = driverRevision, denominations = computed.AsDictionary()
                });
            await push.EnqueueToUserAsync(conn, tx, before.CreatedBy, "Tài xế đã thu tiền khách hàng",
                $"{before.OrderNo}: {computed.Total:N0} ₫ đang chờ bàn giao.",
                $"cash-collection:{id}:collected:{driverRevision}", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Xác nhận đã thu tiền", "CashCollection", before.OrderNo,
                $"Đã thu {computed.Total:N0} đồng; chờ bàn giao thủ quỹ.");
            await AnnounceCollectionAsync(push, u.Username(), "Tài xế đã thu tiền khách hàng",
                $"{before.DriverName} đã thu {computed.Total:N0} ₫ của {before.CustomerName} ({before.OrderNo}) — đang chờ bàn giao.",
                $"cash-collection:{id}:collected:{driverRevision}");
            return Results.Ok(new { collectedAmount = computed.Total });
        });

        g.MapPost("/{id:guid}/receive", async (Guid id, CashCountReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            if (!u.Can(Permissions.CollectionsReceive)) return Results.Forbid();
            var computed = ComputeCash(req.Lines);
            if (computed.Error is not null) return Results.BadRequest(new { message = computed.Error });
            await using var conn = await db.OpenAsync();
            if (!await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadOrder(conn, id, true, tx);
            if (before is null) return Results.NotFound();
            // Chốt bất kiêm nhiệm DUY NHẤT còn lại ở bước nhận tiền: người bàn giao không được tự
            // kiểm đếm phần mình nộp. Chốt "người LẬP lệnh không được nhận tiền của lệnh đó" đã được
            // GỠ CÓ CHỦ Ý: văn phòng có lúc chỉ còn một người đủ vai trò, giữ chốt thì lệnh kẹt lại
            // không ai nhận được tiền. ĐỪNG thêm lại — xem CashCollectionTests, chỗ đã ghi rõ lý do.
            if (SameUser(before.DriverUsername, u.Username()))
                return Results.BadRequest(new { message = "Tài xế không được tự nhận và kiểm đếm tiền do mình bàn giao." });
            if (before.Status is not (StatusPendingHandover or StatusVariance))
                return Results.BadRequest(new { message = "Lệnh không ở trạng thái chờ thủ quỹ nhận tiền." });
            if (before.CollectedAmount is null)
                return Results.BadRequest(new { message = "Lệnh chưa có số tiền tài xế xác nhận." });

            var revisionValue = await conn.Cmd("""
                SELECT COALESCE(MAX(revision),0)+1 FROM cash_count_sessions
                WHERE order_id=@id AND stage=@stage
                """, tx).With("@id", id).With("@stage", StageAccountant).ExecuteScalarAsync();
            var revision = Convert.ToInt32(revisionValue ?? 1);
            await SaveCashCount(conn, tx, id, StageAccountant, revision, u.Username(), computed);

            if (computed.Total != before.CollectedAmount.Value)
            {
                await conn.Cmd("""
                    UPDATE cash_collection_orders SET status=@to,received_by=@me,received_amount=@amount,
                        received_at=NULL,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status=@from
                    """, tx).With("@to", StatusVariance).With("@me", u.Username()).With("@amount", computed.Total)
                    .With("@id", id).With("@from", before.Status).ExecuteNonQueryAsync();
                var difference = computed.Total - before.CollectedAmount.Value;
                await AppendEvent(conn, tx, id, "variance_detected", u.Username(), before.Status, StatusVariance,
                    $"Kiểm đếm lệch {difference:N0} đồng.", new { driverTotal = before.CollectedAmount, accountantTotal = computed.Total, difference, revision, denominations = computed.AsDictionary() });
                await push.EnqueueToUserAsync(conn, tx, before.DriverUsername, "Bàn giao tiền đang bị sai lệch",
                    $"{before.OrderNo}: chênh lệch {difference:N0} ₫, cần kiểm đếm lại.", $"cash-collection:{id}:variance:{revision}", "CashCollections");
                await tx.CommitAsync();
                await db.RecordAudit(u.Username(), "Phát hiện lệch tiền bàn giao", "CashCollection", before.OrderNo,
                    $"Tài xế {before.CollectedAmount:N0}; thủ quỹ {computed.Total:N0}; lệch {difference:N0} đồng.");
                await AnnounceCollectionAsync(push, u.Username(), "Bàn giao tiền bị lệch",
                    $"{before.OrderNo}: tài xế {before.CollectedAmount:N0} ₫ / thủ quỹ {computed.Total:N0} ₫ — lệch {difference:N0} ₫.",
                    $"cash-collection:{id}:variance:{revision}");
                return Results.Conflict(new { message = $"Số tiền đang lệch {difference:N0} ₫. Công nợ chưa được cập nhật.", difference });
            }

            var expectedDifference = computed.Total - before.ExpectedAmount;
            if (expectedDifference != 0)
            {
                await conn.Cmd("""
                    UPDATE cash_collection_orders SET status=@to,received_by=@me,received_amount=@amount,
                        received_at=NULL,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status=@from
                    """, tx).With("@to", StatusVariance).With("@me", u.Username()).With("@amount", computed.Total)
                    .With("@id", id).With("@from", before.Status).ExecuteNonQueryAsync();
                await AppendEvent(conn, tx, id, "expected_variance_detected", u.Username(), before.Status, StatusVariance,
                    $"Tiền thực thu khớp bàn giao nhưng lệch {expectedDifference:N0} đồng so với lệnh; chờ Kế toán trưởng xử lý.",
                    new
                    {
                        expectedAmount = before.ExpectedAmount, actualAmount = computed.Total, expectedDifference,
                        revision, denominations = computed.AsDictionary()
                    });
                await push.EnqueueToUserAsync(conn, tx, before.CreatedBy, "Lệnh thu tiền cần duyệt chênh lệch",
                    $"{before.OrderNo}: thực thu lệch {expectedDifference:N0} ₫ so với dự kiến.",
                    $"cash-collection:{id}:expected-variance:{revision}", "CashCollections");
                await tx.CommitAsync();
                await db.RecordAudit(u.Username(), "Ghi nhận thực thu lệch dự kiến", "CashCollection", before.OrderNo,
                    $"Dự kiến {before.ExpectedAmount:N0}; thực thu {computed.Total:N0}; lệch {expectedDifference:N0} đồng.");
                return Results.Conflict(new
                {
                    message = $"Tiền bàn giao đã khớp nhưng lệch {expectedDifference:N0} ₫ so với dự kiến. Công nợ đang chờ Kế toán trưởng duyệt.",
                    difference = expectedDifference,
                    requiresResolution = true,
                });
            }

            var persistedPayment = await CreateDebtPayment(conn, tx, before, computed.Total, u.Username());

            await conn.Cmd("""
                UPDATE cash_collection_orders SET status=@to,received_by=@me,received_at=CURRENT_TIMESTAMP,
                    received_amount=@amount,payment_id=@payment,updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@from
                """, tx).With("@to", StatusCompleted).With("@me", u.Username()).With("@amount", computed.Total)
                .With("@payment", persistedPayment).With("@id", id).With("@from", before.Status).ExecuteNonQueryAsync();
            await AppendEvent(conn, tx, id, "completed", u.Username(), before.Status, StatusCompleted,
                $"Thủ quỹ nhận đủ {computed.Total:N0} đồng — đã nộp đủ tiền.", new { total = computed.Total, paymentId = persistedPayment, revision, denominations = computed.AsDictionary() });
            await push.EnqueueToUserAsync(conn, tx, before.DriverUsername, "Thủ quỹ đã nhận đủ tiền",
                $"{before.OrderNo}: đã nhận đủ {computed.Total:N0} ₫ và cập nhật công nợ.", $"cash-collection:{id}:completed", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Nhận đủ tiền và ghi công nợ", "CashCollection", before.OrderNo,
                $"Nhận {computed.Total:N0} đồng từ {before.DriverName}; payment={persistedPayment}.");
            await AnnounceCollectionAsync(push, u.Username(), "Thủ quỹ đã nhận đủ tiền",
                $"{before.OrderNo}: nhận đủ {computed.Total:N0} ₫ từ {before.DriverName}, công nợ {before.CustomerName} đã cập nhật.",
                $"cash-collection:{id}:completed");
            return Results.Ok(new { paymentId = persistedPayment, amount = computed.Total });
        });

        g.MapPost("/{id:guid}/resolve", async (Guid id, ResolveVarianceReq req, ClaimsPrincipal u,
            Database db, PushService push) =>
        {
            if (!u.Can(Permissions.CollectionsResolve)) return Results.Forbid();
            var action = (req.Action ?? "").Trim().ToLowerInvariant();
            var reason = (req.Reason ?? "").Trim();
            if (action is not ("approve_actual" or "return_to_driver"))
                return Results.BadRequest(new { message = "Hành động xử lý sai lệch không hợp lệ." });
            if (reason.Length == 0)
                return Results.BadRequest(new { message = "Vui lòng nhập lý do xử lý sai lệch." });
            if (reason.Length > 1000)
                return Results.BadRequest(new { message = "Lý do xử lý không được vượt quá 1.000 ký tự." });

            await using var conn = await db.OpenAsync();
            if (!await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadOrder(conn, id, true, tx);
            if (before is null) return Results.NotFound();
            if (before.Status != StatusVariance)
                return Results.BadRequest(new { message = "Lệnh không còn ở trạng thái cần xử lý sai lệch." });
            if (SameUser(before.DriverUsername, u.Username())) return Results.Forbid();
            if (before.ReceivedAmount is null)
                return Results.BadRequest(new { message = "Thủ quỹ chưa có lần kiểm đếm để Kế toán trưởng xử lý." });

            if (action == "return_to_driver")
            {
                var nextDriverRevision = await NextCashRevision(conn, tx, id, StageDriver);
                await conn.Cmd("""
                    UPDATE cash_collection_orders
                    SET status=@to,collected_by='',collected_at=NULL,collected_amount=NULL,
                        received_by='',received_at=NULL,received_amount=NULL,updated_at=CURRENT_TIMESTAMP
                    WHERE id=@id AND status=@from
                    """, tx).With("@to", StatusAccepted).With("@id", id).With("@from", StatusVariance)
                    .ExecuteNonQueryAsync();
                await AppendEvent(conn, tx, id, "variance_returned", u.Username(), StatusVariance, StatusAccepted,
                    $"Kế toán trưởng trả lệnh cho tài xế kiểm đếm và khai lại. Lý do: {reason}",
                    new
                    {
                        driverTotal = before.CollectedAmount, cashierTotal = before.ReceivedAmount,
                        expectedAmount = before.ExpectedAmount, nextDriverRevision, reason
                    });
                await push.EnqueueToUserAsync(conn, tx, before.DriverUsername, "Lệnh thu tiền cần kiểm đếm lại",
                    $"{before.OrderNo}: {reason}", $"cash-collection:{id}:returned:{nextDriverRevision}", "CashCollections");
                await tx.CommitAsync();
                await db.RecordAudit(u.Username(), "Trả lệnh sai lệch cho tài xế", "CashCollection", before.OrderNo, reason);
                return Results.Ok(new { status = StatusAccepted });
            }

            var approvedAmount = before.ReceivedAmount.Value;
            var persistedPayment = await CreateDebtPayment(conn, tx, before, approvedAmount, u.Username());
            await conn.Cmd("""
                UPDATE cash_collection_orders SET status=@to,received_at=CURRENT_TIMESTAMP,
                    payment_id=@payment,updated_at=CURRENT_TIMESTAMP
                WHERE id=@id AND status=@from
                """, tx).With("@to", StatusCompleted).With("@payment", persistedPayment)
                .With("@id", id).With("@from", StatusVariance).ExecuteNonQueryAsync();
            await AppendEvent(conn, tx, id, "variance_resolved", u.Username(), StatusVariance, StatusCompleted,
                $"Kế toán trưởng duyệt số thực nhận {approvedAmount:N0} đồng và ghi công nợ. Lý do: {reason}",
                new
                {
                    approvedAmount, expectedAmount = before.ExpectedAmount, driverTotal = before.CollectedAmount,
                    cashierTotal = before.ReceivedAmount, paymentId = persistedPayment, reason
                });
            await push.EnqueueToUserAsync(conn, tx, before.DriverUsername, "Sai lệch lệnh thu tiền đã được xử lý",
                $"{before.OrderNo}: đã duyệt {approvedAmount:N0} ₫ và cập nhật công nợ.",
                $"cash-collection:{id}:resolved", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Duyệt sai lệch và ghi công nợ", "CashCollection", before.OrderNo,
                $"Duyệt {approvedAmount:N0} đồng; lý do: {reason}; payment={persistedPayment}.");
            return Results.Ok(new { status = StatusCompleted, paymentId = persistedPayment, amount = approvedAmount });
        });

        g.MapPost("/{id:guid}/cancel", async (Guid id, ReasonReq req, ClaimsPrincipal u, Database db, PushService push) =>
        {
            if (!u.Can(Permissions.CollectionsCreate) && !u.Can(Permissions.CollectionsResolve)) return Results.Forbid();
            var reason = (req.Reason ?? "").Trim();
            if (reason.Length == 0) return Results.BadRequest(new { message = "Vui lòng nhập lý do hủy lệnh." });
            await using var conn = await db.OpenAsync();
            if (!await PayoutVoucherEndpoints.IsCashierAsync(conn, u)) return Results.Forbid();
            await using var tx = await conn.BeginTransactionAsync();
            var before = await LoadOrder(conn, id, true, tx);
            if (before is null) return Results.NotFound();
            if (before.Status is not (StatusAssigned or StatusAccepted or StatusFailed))
                return Results.BadRequest(new { message = "Lệnh đã thu tiền hoặc đã kết thúc nên không thể hủy." });
            await conn.Cmd("""
                UPDATE cash_collection_orders SET status=@to,cancelled_by=@me,cancelled_at=CURRENT_TIMESTAMP,
                    cancel_reason=@reason,updated_at=CURRENT_TIMESTAMP WHERE id=@id AND status=@from
                """, tx).With("@to", StatusCancelled).With("@me", u.Username()).With("@reason", reason)
                .With("@id", id).With("@from", before.Status).ExecuteNonQueryAsync();
            await AppendEvent(conn, tx, id, "cancelled", u.Username(), before.Status, StatusCancelled, reason);
            await push.EnqueueToUserAsync(conn, tx, before.DriverUsername, "Lệnh thu tiền đã bị hủy",
                $"{before.OrderNo}: {reason}", $"cash-collection:{id}:cancelled", "CashCollections");
            await tx.CommitAsync();
            await db.RecordAudit(u.Username(), "Hủy lệnh thu tiền", "CashCollection", before.OrderNo, reason);
            return Results.NoContent();
        });
    }

    private const string OrderSelect = """
        SELECT o.*,
               ds.id AS driver_session_id,
               COALESCE((SELECT jsonb_object_agg(l.denomination::text,l.quantity)::text
                         FROM cash_count_lines l WHERE l.session_id=ds.id),'{}') AS driver_breakdown,
               acs.id AS accountant_session_id,
               COALESCE((SELECT jsonb_object_agg(l.denomination::text,l.quantity)::text
                         FROM cash_count_lines l WHERE l.session_id=acs.id),'{}') AS accountant_breakdown
        FROM cash_collection_orders o
        LEFT JOIN LATERAL (
            SELECT id FROM cash_count_sessions s WHERE s.order_id=o.id AND s.stage='driver'
            ORDER BY revision DESC LIMIT 1
        ) ds ON TRUE
        LEFT JOIN LATERAL (
            SELECT id FROM cash_count_sessions s WHERE s.order_id=o.id AND s.stage='accountant'
            ORDER BY revision DESC LIMIT 1
        ) acs ON TRUE
        """;

    private static object ReadDto(NpgsqlDataReader r, ClaimsPrincipal u, bool driverParticipant, bool accountingParticipant)
    {
        var row = ReadCore(r);
        return row.ToDto(u, driverParticipant, accountingParticipant,
            ParseBreakdown(r.Str("driver_breakdown")), ParseBreakdown(r.Str("accountant_breakdown")));
    }

    private static Dictionary<string, int> ParseBreakdown(string json)
        => JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];

    private static OrderCore ReadCore(NpgsqlDataReader r) => new(
        r.Guid("id"), r.Str("order_no"), r.Guid("customer_id"), r.Str("customer_name"), r.Str("customer_phone"),
        r.Guid("driver_employee_id"), r.Str("driver_username"), r.Str("driver_name"), r.Dec("expected_amount"),
        r.DateOnly("scheduled_date"), r.Dt("handover_due_at"), r.Str("note"), r.Str("status"), r.Str("created_by"),
        r.Dt("created_at"), r.Str("accepted_by"), r.DtNull("accepted_at"), r.Str("collected_by"),
        r.DtNull("collected_at"), r.IsDBNull(r.GetOrdinal("collected_amount")) ? null : r.Dec("collected_amount"),
        r.Str("failed_by"), r.DtNull("failed_at"), r.Str("failure_reason"), r.Str("received_by"),
        r.DtNull("received_at"), r.IsDBNull(r.GetOrdinal("received_amount")) ? null : r.Dec("received_amount"),
        r.IsDBNull(r.GetOrdinal("payment_id")) ? null : r.Guid("payment_id"), r.Str("cancelled_by"),
        r.DtNull("cancelled_at"), r.Str("cancel_reason"));

    private static async Task<OrderCore?> LoadOrder(NpgsqlConnection conn, Guid id, bool forUpdate,
        NpgsqlTransaction? tx = null)
    {
        var sql = "SELECT o.* FROM cash_collection_orders o WHERE o.id=@id" + (forUpdate ? " FOR UPDATE" : "");
        await using var r = await (tx is null ? conn.Cmd(sql) : conn.Cmd(sql, tx)).With("@id", id).ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadCore(r) : null;
    }

    private static bool IsAssignedDriver(OrderCore order, ClaimsPrincipal u)
        => SameUser(order.DriverUsername, u.Username());

    private static bool SameUser(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsDriverRoleAsync(NpgsqlConnection conn, ClaimsPrincipal u,
        NpgsqlTransaction? tx = null)
    {
        if (!u.Can(Permissions.CollectionsSelf)) return false;
        var cmd = tx is null ? conn.Cmd("""
            SELECT EXISTS(
                SELECT 1 FROM app_users account
                WHERE lower(account.username)=lower(@username)
                  AND account.is_deleted=FALSE AND account.is_active=TRUE
                  AND account.approval_status='Approved'
                  AND (account.role=@driverRole OR EXISTS (
                      SELECT 1 FROM user_roles extra
                      WHERE extra.username=account.username AND extra.role=@driverRole
                        AND (extra.expires_at IS NULL OR extra.expires_at>CURRENT_TIMESTAMP)
                  ))
            )
            """) : conn.Cmd("""
            SELECT EXISTS(
                SELECT 1 FROM app_users account
                WHERE lower(account.username)=lower(@username)
                  AND account.is_deleted=FALSE AND account.is_active=TRUE
                  AND account.approval_status='Approved'
                  AND (account.role=@driverRole OR EXISTS (
                      SELECT 1 FROM user_roles extra
                      WHERE extra.username=account.username AND extra.role=@driverRole
                        AND (extra.expires_at IS NULL OR extra.expires_at>CURRENT_TIMESTAMP)
                  ))
            )
            """, tx);
        return await cmd.With("@username", u.Username()).With("@driverRole", AppRoles.Driver)
            .ExecuteScalarAsync() is bool allowed && allowed;
    }

    private static async Task<bool> IsAccountingParticipantAsync(NpgsqlConnection conn, ClaimsPrincipal u)
        => u.Can(Permissions.CollectionsReadAll) && await PayoutVoucherEndpoints.IsCashierAsync(conn, u);

    private sealed class DriverTally(string name)
    {
        public string Name { get; } = name;
        public int Trips;
        public int Completed;
        public int Failed;
        public int Cancelled;
        public decimal Collected;
    }

    private static string MonthOrCurrent(string? month)
    {
        var value = (month ?? "").Trim();
        return MonthRange(value, out _, out _) ? value : DateTime.Now.ToString("yyyy-MM");
    }

    /// <summary>"yyyy-MM" → [đầu tháng, đầu tháng sau). Tháng hỏng thì rơi về tháng hiện tại.</summary>
    private static bool MonthRange(string? month, out DateTime start, out DateTime end)
    {
        var parts = (month ?? "").Trim().Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var y) && int.TryParse(parts[1], out var m)
            && y is >= 2000 and <= 9999 && m is >= 1 and <= 12)
        {
            start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Local);
            end = start.AddMonths(1);
            return true;
        }
        start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, DateTimeKind.Local);
        end = start.AddMonths(1);
        return false;
    }

    private static async Task<string> NextOrderNo(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var value = await conn.Cmd("SELECT nextval('cash_collection_order_seq')", tx).ExecuteScalarAsync();
        return $"LTT{DateTime.Now:yyyyMMdd}{Convert.ToInt64(value):00000}";
    }

    private static CashComputation ComputeCash(IReadOnlyList<CashLineReq>? raw)
    {
        if (raw is null || raw.Count == 0) return new([], 0, "Vui lòng nhập số lượng ít nhất một mệnh giá.");
        var merged = new Dictionary<long, int>();
        foreach (var line in raw)
        {
            if (!AllowedDenominations.Contains(line.Denomination))
                return new([], 0, $"Mệnh giá {line.Denomination:N0} đồng không hợp lệ.");
            if (line.Quantity < 0 || line.Quantity > 100_000)
                return new([], 0, "Số tờ phải nằm trong khoảng 0 đến 100.000.");
            if (line.Quantity == 0) continue;
            merged[line.Denomination] = checked(merged.GetValueOrDefault(line.Denomination) + line.Quantity);
        }
        if (merged.Count == 0) return new([], 0, "Tổng số tờ phải lớn hơn 0.");
        var lines = merged.OrderByDescending(x => x.Key)
            .Select(x => new ComputedLine(x.Key, x.Value, checked((decimal)x.Key * x.Value))).ToList();
        var total = lines.Sum(x => x.Subtotal);
        if (total <= 0 || total > 999_999_999_999_999m)
            return new([], 0, "Tổng tiền vượt giới hạn cho phép.");
        return new(lines, total, null);
    }

    private static async Task SaveCashCount(NpgsqlConnection conn, NpgsqlTransaction tx, Guid orderId,
        string stage, int revision, string actor, CashComputation cash)
    {
        var session = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO cash_count_sessions (id,order_id,stage,revision,actor_username,total)
            VALUES (@id,@order,@stage,@revision,@actor,@total)
            """, tx).With("@id", session).With("@order", orderId).With("@stage", stage)
            .With("@revision", revision).With("@actor", actor).With("@total", cash.Total).ExecuteNonQueryAsync();
        foreach (var line in cash.Lines)
            await conn.Cmd("""
                INSERT INTO cash_count_lines (session_id,denomination,quantity,subtotal)
                VALUES (@session,@denomination,@quantity,@subtotal)
                """, tx).With("@session", session).With("@denomination", line.Denomination)
                .With("@quantity", line.Quantity).With("@subtotal", line.Subtotal).ExecuteNonQueryAsync();
    }

    private static async Task<int> NextCashRevision(NpgsqlConnection conn, NpgsqlTransaction tx, Guid orderId,
        string stage)
    {
        var value = await conn.Cmd("""
            SELECT COALESCE(MAX(revision),0)+1 FROM cash_count_sessions
            WHERE order_id=@id AND stage=@stage
            """, tx).With("@id", orderId).With("@stage", stage).ExecuteScalarAsync();
        return Convert.ToInt32(value ?? 1);
    }

    private static async Task<Guid> CreateDebtPayment(NpgsqlConnection conn, NpgsqlTransaction tx,
        OrderCore order, decimal amount, string actor)
    {
        var paymentId = Guid.NewGuid();
        await conn.Cmd("""
            INSERT INTO payments
                (id,customer_id,customer_name,customer_input_name,amount,pay_date,note,source_kind,source_id,created_by)
            VALUES
                (@payment,@customer,@customerName,@customerName,@amount,CURRENT_DATE,@note,'cash_collection',@source,@by)
            ON CONFLICT (source_kind,source_id) WHERE source_id IS NOT NULL AND source_kind<>'' DO NOTHING
            """, tx).With("@payment", paymentId).With("@customer", order.CustomerId)
            .With("@customerName", order.CustomerName).With("@amount", amount)
            .With("@note", $"Thu tiền qua lệnh {order.OrderNo}").With("@source", order.Id).With("@by", actor)
            .ExecuteNonQueryAsync();
        var actualPayment = await conn.Cmd(
                "SELECT id FROM payments WHERE source_kind='cash_collection' AND source_id=@id", tx)
            .With("@id", order.Id).ExecuteScalarAsync();
        if (actualPayment is not Guid persistedPayment)
            throw new InvalidOperationException("Không tạo được khoản thu công nợ từ lệnh thu tiền.");
        return persistedPayment;
    }

    private static async Task AppendEvent(NpgsqlConnection conn, NpgsqlTransaction tx, Guid orderId,
        string action, string actor, string? before, string? after, string note, object? data = null)
    {
        await conn.Cmd("""
            INSERT INTO cash_collection_events
                (id,order_id,action,actor_username,before_status,after_status,note,event_data)
            VALUES
                (@id,@order,@action,@actor,@before,@after,@note,CASE WHEN @data='' THEN NULL ELSE CAST(@data AS jsonb) END)
            """, tx).With("@id", Guid.NewGuid()).With("@order", orderId).With("@action", action)
            .With("@actor", actor).With("@before", (object?)before ?? DBNull.Value)
            .With("@after", (object?)after ?? DBNull.Value).With("@note", note)
            .With("@data", data is null ? "" : JsonSerializer.Serialize(data)).ExecuteNonQueryAsync();
    }

    private static async Task<List<object>> LoadCounts(NpgsqlConnection conn, Guid orderId)
    {
        var result = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT s.id,s.stage,s.revision,s.actor_username,s.total,s.confirmed_at,
                   l.denomination,l.quantity,l.subtotal
            FROM cash_count_sessions s
            JOIN cash_count_lines l ON l.session_id=s.id
            WHERE s.order_id=@id
            ORDER BY s.stage,s.revision,l.denomination DESC
            """).With("@id", orderId).ExecuteReaderAsync();
        Guid? currentId = null;
        string stage = "", actor = "";
        int revision = 0;
        decimal total = 0;
        DateTime confirmedAt = default;
        var lines = new List<object>();
        while (await r.ReadAsync())
        {
            var id = r.Guid("id");
            if (currentId is not null && id != currentId)
            {
                result.Add(new { id = currentId, stage, revision, actor, total, confirmedAt, lines = lines.ToArray() });
                lines = [];
            }
            currentId = id; stage = r.Str("stage"); revision = r.Int("revision");
            actor = r.Str("actor_username"); total = r.Dec("total"); confirmedAt = r.Dt("confirmed_at");
            lines.Add(new { denomination = r.Long("denomination"), quantity = r.Int("quantity"), subtotal = r.Dec("subtotal") });
        }
        if (currentId is not null)
            result.Add(new { id = currentId, stage, revision, actor, total, confirmedAt, lines = lines.ToArray() });
        return result;
    }

    private static async Task<List<object>> LoadEvents(NpgsqlConnection conn, Guid orderId)
    {
        var result = new List<object>();
        await using var r = await conn.Cmd("""
            SELECT id,action,actor_username,before_status,after_status,note,event_data::text AS event_data,occurred_at
            FROM cash_collection_events WHERE order_id=@id ORDER BY occurred_at,id
            """).With("@id", orderId).ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(new
            {
                id = r.Guid("id"), action = r.Str("action"), actor = r.Str("actor_username"),
                beforeStatus = r.IsDBNull(r.GetOrdinal("before_status")) ? null : r.Str("before_status"),
                afterStatus = r.IsDBNull(r.GetOrdinal("after_status")) ? null : r.Str("after_status"),
                note = r.Str("note"), data = r.IsDBNull(r.GetOrdinal("event_data"))
                    ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(r.Str("event_data")),
                occurredAt = r.Dt("occurred_at"),
            });
        return result;
    }

    private sealed record CustomerSnap(Guid Id, string Name, string Phone);
    private sealed record DriverSnap(Guid Id, string Username, string Name);
    private sealed record ComputedLine(long Denomination, int Quantity, decimal Subtotal);
    private sealed record CashComputation(List<ComputedLine> Lines, decimal Total, string? Error)
    {
        public Dictionary<string, int> AsDictionary() => Lines.ToDictionary(x => x.Denomination.ToString(), x => x.Quantity);
    }

    private sealed record OrderCore(Guid Id, string OrderNo, Guid CustomerId, string CustomerName, string CustomerPhone,
        Guid DriverEmployeeId, string DriverUsername, string DriverName, decimal ExpectedAmount, DateOnly ScheduledDate,
        DateTime HandoverDueAt, string Note, string Status, string CreatedBy, DateTime CreatedAt, string AcceptedBy,
        DateTime? AcceptedAt, string CollectedBy, DateTime? CollectedAt, decimal? CollectedAmount, string FailedBy,
        DateTime? FailedAt, string FailureReason, string ReceivedBy, DateTime? ReceivedAt, decimal? ReceivedAmount,
        Guid? PaymentId, string CancelledBy, DateTime? CancelledAt, string CancelReason)
    {
        public object ToDto(ClaimsPrincipal u, bool driverParticipant, bool accountingParticipant,
            Dictionary<string, int>? driverCash = null,
            Dictionary<string, int>? accountantCash = null)
        {
            var mine = driverParticipant && string.Equals(DriverUsername, u.Username(), StringComparison.OrdinalIgnoreCase);
            return new
            {
                Id, OrderNo, CustomerId, CustomerName, CustomerPhone, DriverEmployeeId, DriverUsername, DriverName,
                ExpectedAmount, ScheduledDate, HandoverDueAt, Note, Status, CreatedBy, CreatedAt, AcceptedBy, AcceptedAt,
                CollectedBy, CollectedAt, CollectedAmount, FailedBy, FailedAt, FailureReason, ReceivedBy, ReceivedAt,
                ReceivedAmount, PaymentId, CancelledBy, CancelledAt, CancelReason,
                driverCash = driverCash ?? [], accountantCash = accountantCash ?? [],
                overdue = Status is StatusPendingHandover or StatusVariance && DateTime.UtcNow > HandoverDueAt.ToUniversalTime(),
                expectedVariance = CollectedAmount is not null && CollectedAmount.Value != ExpectedAmount,
                cashVariance = CollectedAmount is not null && ReceivedAmount is not null &&
                               CollectedAmount.Value != ReceivedAmount.Value,
                mine, canAccept = mine && Status == StatusAssigned, canCollect = mine && Status == StatusAccepted,
                canFail = mine && Status is (StatusAssigned or StatusAccepted),
                // Tài xế bàn giao không được tự kiểm đếm tiền của mình; server chặn ở /receive nên
                // giao diện cũng phải giấu nút, đừng để đếm xong mới báo lỗi.
                canReceive = accountingParticipant && u.Can(Permissions.CollectionsReceive) &&
                             !SameUser(DriverUsername, u.Username()) &&
                             Status is (StatusPendingHandover or StatusVariance),
                canCancel = accountingParticipant && (u.Can(Permissions.CollectionsCreate) || u.Can(Permissions.CollectionsResolve)) &&
                            Status is StatusAssigned or StatusAccepted or StatusFailed,
                // Cùng lý do: /resolve loại tài xế, nên nút phải tắt từ đầu thay vì để nhập xong
                // lý do mới báo lỗi.
                canResolve = accountingParticipant && u.Can(Permissions.CollectionsResolve) &&
                             !SameUser(DriverUsername, u.Username()) &&
                             Status == StatusVariance && ReceivedAmount is not null,
            };
        }
    }

    public record CreateCollectionReq(Guid CustomerId, Guid DriverEmployeeId, decimal ExpectedAmount,
        DateOnly ScheduledDate, DateTime HandoverDueAt, string? Note);
    public record CashLineReq(long Denomination, int Quantity);
    public record CashCountReq(List<CashLineReq>? Lines, string? Reason = null);
    public record ReasonReq(string? Reason);
    public record ResolveVarianceReq(string? Action, string? Reason);
}
