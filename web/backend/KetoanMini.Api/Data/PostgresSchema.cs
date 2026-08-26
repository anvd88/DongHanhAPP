using KetoanMini.Api.Security;
using Npgsql;

namespace KetoanMini.Api.Data;

public static class PostgresSchema
{
    public static async Task EnsureAsync(Database db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        await db.EnsureDatabaseExistsAsync(ct);
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd(SchemaSql).ExecuteNonQueryAsync(ct);
        await RecordBaselineMigrationAsync(conn, ct);
        await SeedAdminAsync(conn, config, logger, ct);
    }

    private static async Task RecordBaselineMigrationAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version varchar(64) PRIMARY KEY,
                description text NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            INSERT INTO schema_migrations(version, description)
            VALUES ('001_baseline', 'Versioned baseline for the consolidated KetoanMini schema')
            ON CONFLICT (version) DO NOTHING;
            """).ExecuteNonQueryAsync(ct);
    }

    private static async Task SeedAdminAsync(NpgsqlConnection conn, IConfiguration config, ILogger logger, CancellationToken ct)
    {
        var hasAdmin = await conn.Cmd(
            "SELECT 1 FROM app_users WHERE role = 'Admin' AND is_deleted = FALSE LIMIT 1")
            .ExecuteScalarAsync(ct);
        if (hasAdmin is not null and not DBNull)
            return;

        var username = (config["Bootstrap:AdminUsername"] ?? "").Trim();
        var password = config["Bootstrap:AdminPassword"] ?? "";
        var fullName = (config["Bootstrap:AdminFullName"] ?? "Administrator").Trim();
        var email = (config["Bootstrap:AdminEmail"] ?? "").Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Khong seed admin PostgreSQL vi Bootstrap:AdminUsername/AdminPassword dang rong.");
            return;
        }

        await conn.Cmd("""
            INSERT INTO app_users
                (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
            VALUES
                (@id, @username, @fullName, @email, 'Admin', @passwordHash, TRUE, 'Approved', CURRENT_TIMESTAMP, 'system', CURRENT_TIMESTAMP, FALSE)
            ON CONFLICT (username) DO UPDATE SET
                role = 'Admin',
                is_active = TRUE,
                is_deleted = FALSE,
                approval_status = 'Approved',
                approved_at = COALESCE(app_users.approved_at, CURRENT_TIMESTAMP);
            """)
            .With("@id", Guid.NewGuid())
            .With("@username", username)
            .With("@fullName", string.IsNullOrWhiteSpace(fullName) ? username : fullName)
            .With("@email", email)
            .With("@passwordHash", PasswordHasher.Hash(password))
            .ExecuteNonQueryAsync(ct);

        logger.LogInformation("Da seed tai khoan admin mac dinh cho PostgreSQL: {Username}", username);
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS app_users (
            id uuid NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL UNIQUE,
            full_name varchar(256) NOT NULL DEFAULT '',
            email varchar(256) NOT NULL DEFAULT '',
            role varchar(32) NOT NULL DEFAULT 'Employee',
            password_hash text NOT NULL,
            is_active boolean NOT NULL DEFAULT TRUE,
            approval_status varchar(32) NOT NULL DEFAULT 'Approved',
            approved_at timestamptz NULL,
            approved_by varchar(128) NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            is_deleted boolean NOT NULL DEFAULT FALSE
        );

        ALTER TABLE app_users ADD COLUMN IF NOT EXISTS email varchar(256) NOT NULL DEFAULT '';
        ALTER TABLE app_users ADD COLUMN IF NOT EXISTS is_deleted boolean NOT NULL DEFAULT FALSE;
        -- Phiên bản phân quyền: tăng mỗi lần vai trò/quyền của tài khoản thay đổi. Client so sánh để
        -- biết phải nạp lại hồ sơ truy cập; server dùng để ghi audit "quyền cũ → quyền mới".
        ALTER TABLE app_users ADD COLUMN IF NOT EXISTS authorization_version integer NOT NULL DEFAULT 1;
        UPDATE app_users SET role = 'Employee' WHERE lower(role) = 'user';

        -- Vai trò THỨ HAI cấp thêm cho một tài khoản (ngoài cột role chính). Nhờ đó một người có thể
        -- vừa giữ vai trò chính (Kế toán/Nhân viên…) vừa được cấp thêm "Thủ kho" để giao việc & nghiệm thu.
        CREATE TABLE IF NOT EXISTS user_roles (
            username varchar(128) NOT NULL,
            role varchar(32) NOT NULL,
            granted_by varchar(128) NOT NULL DEFAULT '',
            granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (username, role)
        );
        CREATE INDEX IF NOT EXISTS ix_user_roles_username ON user_roles (username);
        -- Vai trò cấp TẠM THỜI (vd trưởng phòng đi vắng, ủy quyền duyệt đơn 2 tuần). NULL = vĩnh viễn.
        -- Hết hạn thì AccessProfileService tự bỏ qua — không cần ai nhớ đi thu hồi.
        ALTER TABLE user_roles ADD COLUMN IF NOT EXISTS expires_at timestamptz NULL;

        -- LỊCH SỬ CẤP/THU HỒI QUYỀN. Tách khỏi audit_logs chung để tra soát phân quyền không bị lẫn
        -- vào hàng vạn dòng nghiệp vụ, và để giữ được ảnh chụp "trước → sau" của từng lần thay đổi.
        CREATE TABLE IF NOT EXISTS user_role_history (
            id bigserial PRIMARY KEY,
            occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            username varchar(128) NOT NULL,
            changed_by varchar(128) NOT NULL DEFAULT '',
            action varchar(64) NOT NULL,
            roles_before text NOT NULL DEFAULT '',
            roles_after text NOT NULL DEFAULT '',
            reason text NOT NULL DEFAULT '',
            client_ip varchar(64) NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS ix_user_role_history_user ON user_role_history (username, occurred_at DESC);

        CREATE TABLE IF NOT EXISTS customers (
            id uuid NOT NULL PRIMARY KEY,
            name varchar(256) NOT NULL,
            tax_code varchar(64) NOT NULL DEFAULT '',
            phone varchar(64) NOT NULL DEFAULT '',
            address text NOT NULL DEFAULT '',
            is_active boolean NOT NULL DEFAULT TRUE,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        -- Số dư công nợ tại thời điểm bắt đầu theo dõi trên hệ thống.
        -- Số dương: khách còn nợ; số âm: khách đã trả trước.
        CREATE TABLE IF NOT EXISTS customer_opening_balances (
            customer_id uuid NOT NULL PRIMARY KEY REFERENCES customers(id) ON DELETE CASCADE,
            amount numeric(18,2) NOT NULL DEFAULT 0,
            as_of_date date NOT NULL,
            note text NOT NULL DEFAULT '',
            updated_by varchar(128) NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS ix_customer_opening_balances_date
            ON customer_opening_balances (as_of_date);

        CREATE TABLE IF NOT EXISTS documents (
            id uuid NOT NULL PRIMARY KEY,
            voucher_no varchar(64) NOT NULL,
            doc_date date NOT NULL,
            customer_id uuid NULL REFERENCES customers(id) ON DELETE SET NULL,
            customer_name varchar(256) NOT NULL DEFAULT '',
            customer_input_name varchar(256) NOT NULL DEFAULT '',
            document_type varchar(16) NOT NULL DEFAULT 'document',
            content text NOT NULL DEFAULT '',
            note text NOT NULL DEFAULT '',
            issued_at timestamptz NULL,
            cancelled_at timestamptz NULL,
            cancelled_by varchar(128) NOT NULL DEFAULT '',
            cancel_reason text NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS issued_at timestamptz NULL;
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS cancelled_at timestamptz NULL;
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS cancelled_by varchar(128) NOT NULL DEFAULT '';
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS cancel_reason text NOT NULL DEFAULT '';
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'documents'
                  AND column_name = 'document_type'
            ) THEN
                ALTER TABLE documents ADD COLUMN document_type varchar(16) NOT NULL DEFAULT 'document';
                UPDATE documents
                SET document_type = CASE
                    WHEN UPPER(voucher_no) LIKE 'PT%' OR LOWER(content) LIKE '%phiếu thu%' OR LOWER(content) LIKE '%thu tiền%' THEN 'receipt'
                    WHEN UPPER(voucher_no) LIKE 'PC%' OR LOWER(content) LIKE '%phiếu chi%' OR LOWER(content) LIKE '%chi tiền%' THEN 'payment'
                    ELSE 'document'
                END;
            END IF;
        END $$;
        -- Bất biến phát hành phiếu xuất kho:
        --   phiếu nháp không có số; phiếu đã phát hành bắt buộc có số.
        -- Dọn trạng thái do phiên bản cũ từng lưu số trước khi gửi lệnh in.
        UPDATE documents
        SET voucher_no = ''
        WHERE document_type = 'document' AND issued_at IS NULL AND voucher_no <> '';
        UPDATE documents
        SET issued_at = NULL
        WHERE document_type = 'document' AND issued_at IS NOT NULL AND BTRIM(voucher_no) = '';
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'ck_documents_warehouse_issue_number'
                  AND conrelid = 'documents'::regclass
            ) THEN
                ALTER TABLE documents
                ADD CONSTRAINT ck_documents_warehouse_issue_number
                CHECK (
                    document_type <> 'document'
                    OR (issued_at IS NULL AND voucher_no = '')
                    OR (issued_at IS NOT NULL AND BTRIM(voucher_no) <> '')
                );
            END IF;
        END $$;
        -- Số phiếu xuất kho là bất biến sau khi phát hành. Ràng buộc này bảo vệ cả các cập nhật
        -- ngoài giao diện/API để số trong DB không thể lệch với số đã in trên phiếu thực tế.
        CREATE OR REPLACE FUNCTION prevent_issued_warehouse_voucher_no_change()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            IF OLD.document_type = 'document'
               AND OLD.issued_at IS NOT NULL
               AND NEW.voucher_no IS DISTINCT FROM OLD.voucher_no THEN
                RAISE EXCEPTION 'Không thể thay đổi số phiếu xuất kho đã phát hành.'
                    USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
        END;
        $$;
        DROP TRIGGER IF EXISTS trg_documents_issued_voucher_no_immutable ON documents;
        CREATE TRIGGER trg_documents_issued_voucher_no_immutable
        BEFORE UPDATE OF voucher_no ON documents
        FOR EACH ROW
        EXECUTE FUNCTION prevent_issued_warehouse_voucher_no_change();

        -- ── Giao hàng cho phiếu xuất kho ────────────────────────────────────────────────────
        -- Phiếu xuất kho ĐÃ IN phải đi tới khách bằng một trong hai đường: lái xe chở đi, hoặc
        -- khách tự lấy tại kho. Ghi rõ đường nào để đối soát được khi thiếu phiếu.
        --   delivery_mode = ''       chưa gán (phiếu vừa in, chưa quyết định)
        --                 = 'driver' đã gán cho lái xe
        --                 = 'pickup' khách lấy tại kho
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_mode varchar(16) NOT NULL DEFAULT '';
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_driver_username varchar(128) NOT NULL DEFAULT '';
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_driver_name varchar(200) NOT NULL DEFAULT '';
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_assigned_at timestamptz NULL;
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_assigned_by varchar(128) NOT NULL DEFAULT '';
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_note text NOT NULL DEFAULT '';
        -- Việc giao hàng sinh tự động cho lái xe. Giữ khoá ở đây để màn "Việc được giao" nối
        -- ngược lại phiếu mà không cần bảng trung gian.
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_task_id uuid NULL;
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint
                WHERE conname = 'ck_documents_delivery_mode'
                  AND conrelid = 'documents'::regclass
            ) THEN
                ALTER TABLE documents
                ADD CONSTRAINT ck_documents_delivery_mode
                CHECK (
                    delivery_mode IN ('', 'driver', 'pickup')
                    -- Gán cho lái xe thì bắt buộc phải biết là lái xe nào.
                    AND (delivery_mode <> 'driver' OR BTRIM(delivery_driver_username) <> '')
                    -- Khách tự lấy thì không được đứng tên lái xe nào.
                    AND (delivery_mode <> 'pickup' OR BTRIM(delivery_driver_username) = '')
                );
            END IF;
        END $$;
        -- Truy vấn nóng: "lái xe X đang cầm những phiếu nào" (đối soát cuối ngày).
        CREATE INDEX IF NOT EXISTS ix_documents_delivery_driver
            ON documents (delivery_driver_username, doc_date DESC)
            WHERE delivery_mode = 'driver';

        -- ── Đối soát phiếu khi lái xe giao xong, nộp tờ phiếu về cho kế toán ────────────────
        -- Số cân/số lượng khách nhận thực tế hiếm khi trùng đúng số đã xuất, và đơn giá đôi khi bị
        -- viết sai lúc xuất kho. Kế toán sửa lại theo tờ phiếu có chữ ký khách, rồi xác nhận
        -- "phiếu đã về kho" để đóng việc của lái xe.
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_returned_at timestamptz NULL;
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_returned_by varchar(128) NOT NULL DEFAULT '';
        ALTER TABLE documents ADD COLUMN IF NOT EXISTS delivery_return_note text NOT NULL DEFAULT '';
        CREATE INDEX IF NOT EXISTS ix_documents_delivery_pending_return
            ON documents (doc_date DESC)
            WHERE delivery_mode = 'driver' AND delivery_returned_at IS NULL;

        -- Chứng từ kế toán đã nhập chỉ được chuyển sang trạng thái hủy, tuyệt đối không xóa vật lý.
        CREATE OR REPLACE FUNCTION prevent_document_physical_delete()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            RAISE EXCEPTION 'Không thể xóa vật lý phiếu kế toán; hãy chuyển phiếu sang trạng thái hủy.'
                USING ERRCODE = '23514';
        END;
        $$;
        DROP TRIGGER IF EXISTS trg_documents_no_physical_delete ON documents;
        CREATE TRIGGER trg_documents_no_physical_delete
        BEFORE DELETE ON documents
        FOR EACH ROW
        EXECUTE FUNCTION prevent_document_physical_delete();

        CREATE TABLE IF NOT EXISTS document_lines (
            id bigserial NOT NULL PRIMARY KEY,
            document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            line_no integer NOT NULL DEFAULT 0,
            line_content text NOT NULL DEFAULT '',
            category varchar(128) NOT NULL DEFAULT '',
            spec text NOT NULL DEFAULT '',
            quantity numeric(18,2) NOT NULL DEFAULT 0,
            unit_price numeric(18,2) NOT NULL DEFAULT 0,
            note text NOT NULL DEFAULT ''
        );

        -- Ảnh chụp các dòng phiếu tại thời điểm PHÁT HÀNH = "hàng xuất đi" trên tờ giấy đã in.
        -- Bảng riêng chứ không phải cột thêm vào document_lines, vì mỗi lần lưu phiếu là xoá sạch
        -- rồi chèn lại document_lines — snapshot nằm chung sẽ bị cuốn theo và mất mốc so sánh.
        CREATE TABLE IF NOT EXISTS document_issued_lines (
            document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            line_no integer NOT NULL,
            line_content text NOT NULL DEFAULT '',
            spec text NOT NULL DEFAULT '',
            quantity numeric(18,2) NOT NULL DEFAULT 0,
            unit_price numeric(18,2) NOT NULL DEFAULT 0,
            note text NOT NULL DEFAULT '',
            captured_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (document_id, line_no)
        );
        -- Phiếu đã phát hành từ trước khi có tính năng này chưa có ảnh chụp. Lấy hiện trạng làm mốc
        -- để chúng hiện "chênh lệch 0" thay vì lệch toàn bộ so với một mốc rỗng.
        INSERT INTO document_issued_lines (document_id, line_no, line_content, spec, quantity, unit_price, note)
        SELECT DISTINCT ON (l.document_id, l.line_no)
               l.document_id, l.line_no, l.line_content, l.spec, l.quantity, l.unit_price, l.note
        FROM document_lines l
        JOIN documents d ON d.id = l.document_id
        WHERE d.document_type = 'document' AND d.issued_at IS NOT NULL
        ORDER BY l.document_id, l.line_no, l.id
        ON CONFLICT (document_id, line_no) DO NOTHING;

        -- Lịch sử chỉnh sửa hàng thực nhận. Mỗi lần kế toán bấm lưu, mỗi dòng THỰC SỰ đổi đẻ ra
        -- một bản ghi cũ→mới; không đổi thì không ghi, để sổ không loãng.
        CREATE TABLE IF NOT EXISTS document_line_edits (
            id bigserial PRIMARY KEY,
            document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            line_no integer NOT NULL DEFAULT 0,
            line_content text NOT NULL DEFAULT '',
            old_quantity numeric(18,2) NOT NULL DEFAULT 0,
            new_quantity numeric(18,2) NOT NULL DEFAULT 0,
            old_unit_price numeric(18,2) NOT NULL DEFAULT 0,
            new_unit_price numeric(18,2) NOT NULL DEFAULT 0,
            reason text NOT NULL DEFAULT '',
            actor_username varchar(128) NOT NULL DEFAULT '',
            actor_name varchar(200) NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS ix_document_line_edits_doc ON document_line_edits (document_id, id);

        -- ── Hàng khách trả về ───────────────────────────────────────────────────────────────
        -- Phiếu trả hàng là một documents có document_type='return', dòng hàng nằm trong
        -- document_lines như mọi phiếu khác. Nhờ vậy tổng tiền, công nợ, sổ giao dịch dùng chung
        -- một đường tính, không phải dựng bảng tiền song song.
        --
        -- Hai cột dưới đây trả lời câu hỏi cốt lõi: món hàng trả về NẰM Ở ĐƠN NÀO. Hệ thống không
        -- có bảng giá — cùng một mặt hàng mỗi đơn một giá — nên phải trỏ đúng dòng của đơn nguồn
        -- thì mới trừ công nợ đúng số tiền đã bán.
        ALTER TABLE document_lines ADD COLUMN IF NOT EXISTS source_document_id uuid NULL
            REFERENCES documents(id) ON DELETE RESTRICT;
        ALTER TABLE document_lines ADD COLUMN IF NOT EXISTS source_line_no integer NULL;
        CREATE INDEX IF NOT EXISTS ix_document_lines_source
            ON document_lines (source_document_id, source_line_no)
            WHERE source_document_id IS NOT NULL;

        CREATE SEQUENCE IF NOT EXISTS goods_return_seq START 1;

        -- ── Danh mục hàng hoá ───────────────────────────────────────────────────────────────
        -- Trước đây chủng loại/quy cách là CHỮ TỰ DO trên từng dòng phiếu, nên "Thép tấm 10mm",
        -- "thep tam 10 ly" và "Thép tấm 10 mm" là ba mặt hàng khác nhau với máy: không thống kê
        -- được theo mặt hàng, và tra hàng khách trả về phải dò chữ nên gõ lệch là không ra.
        --
        -- Danh mục này KHÔNG ép: ô nhập trên phiếu vẫn cho gõ tự do, chọn từ danh mục chỉ là đường
        -- nhanh. Dòng nào chọn từ danh mục thì đóng dấu product_id để thống kê đi theo mã chứ không
        -- theo chính tả.
        CREATE TABLE IF NOT EXISTS products (
            id uuid NOT NULL PRIMARY KEY,
            code varchar(32) NOT NULL DEFAULT '',
            name varchar(256) NOT NULL DEFAULT '',
            spec varchar(256) NOT NULL DEFAULT '',
            unit varchar(24) NOT NULL DEFAULT 'kg',
            note text NOT NULL DEFAULT '',
            is_active boolean NOT NULL DEFAULT TRUE,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        -- Một mặt hàng = một cặp (tên, quy cách). Khoá theo chữ thường để không đẻ bản trùng chỉ
        -- khác hoa/thường — đúng thứ danh mục sinh ra để ngăn.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_products_name_spec
            ON products (lower(name), lower(spec));
        CREATE UNIQUE INDEX IF NOT EXISTS ux_products_code
            ON products (lower(code)) WHERE code <> '';
        CREATE SEQUENCE IF NOT EXISTS product_code_seq START 1;

        -- ON DELETE SET NULL: xoá một mặt hàng khỏi danh mục KHÔNG được phép làm hỏng phiếu cũ.
        ALTER TABLE document_lines ADD COLUMN IF NOT EXISTS product_id uuid NULL
            REFERENCES products(id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS ix_document_lines_product
            ON document_lines (product_id) WHERE product_id IS NOT NULL;

        -- ── Mua hàng: nhà cung cấp + phiếu nhập mua ─────────────────────────────────────────
        -- Đây là vế NHẬP mà hệ thống chưa từng có (trước nay chỉ ghi bán ra + tiền + công nợ phải
        -- thu). Không có nó thì không thể tính tồn kho lẫn giá vốn — "tồn = nhập − xuất".
        CREATE TABLE IF NOT EXISTS suppliers (
            id uuid NOT NULL PRIMARY KEY,
            name varchar(256) NOT NULL,
            tax_code varchar(64) NOT NULL DEFAULT '',
            phone varchar(64) NOT NULL DEFAULT '',
            address text NOT NULL DEFAULT '',
            note text NOT NULL DEFAULT '',
            is_active boolean NOT NULL DEFAULT TRUE,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_suppliers_name ON suppliers (lower(name));

        -- Phiếu nhập mua đứng RIÊNG, không dùng chung bảng documents như phiếu bán/thu/chi/trả hàng.
        -- Lý do: documents đã gánh quá nhiều thứ của vòng đời phiếu BÁN (số phiếu in ra bất biến,
        -- giao hàng, đối soát, hàng trả về, công nợ phải THU). Nhét chiều mua vào đó là mỗi truy vấn
        -- tiền lại phải nhớ loại trừ thêm một document_type nữa — đúng cái bẫy đã dính với 'return'.
        CREATE TABLE IF NOT EXISTS purchases (
            id uuid NOT NULL PRIMARY KEY,
            voucher_no varchar(64) NOT NULL DEFAULT '',
            doc_date date NOT NULL,
            supplier_id uuid NULL REFERENCES suppliers(id) ON DELETE SET NULL,
            supplier_name varchar(256) NOT NULL DEFAULT '',
            -- Số hoá đơn/phiếu giấy của nhà cung cấp — thứ để đối chiếu khi họ đòi tiền.
            supplier_invoice_no varchar(64) NOT NULL DEFAULT '',
            note text NOT NULL DEFAULT '',
            paid_amount numeric(18,2) NOT NULL DEFAULT 0,
            cancelled_at timestamptz NULL,
            cancelled_by varchar(128) NOT NULL DEFAULT '',
            cancel_reason text NOT NULL DEFAULT '',
            created_by varchar(128) NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS ix_purchases_date ON purchases (doc_date DESC, voucher_no DESC);
        CREATE INDEX IF NOT EXISTS ix_purchases_supplier ON purchases (supplier_id, doc_date DESC);
        CREATE SEQUENCE IF NOT EXISTS purchase_voucher_seq START 1;

        CREATE TABLE IF NOT EXISTS purchase_lines (
            id bigserial NOT NULL PRIMARY KEY,
            purchase_id uuid NOT NULL REFERENCES purchases(id) ON DELETE CASCADE,
            line_no integer NOT NULL DEFAULT 0,
            product_id uuid NULL REFERENCES products(id) ON DELETE SET NULL,
            line_content text NOT NULL DEFAULT '',
            spec text NOT NULL DEFAULT '',
            quantity numeric(18,2) NOT NULL DEFAULT 0,
            unit_price numeric(18,2) NOT NULL DEFAULT 0,
            note text NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS ix_purchase_lines_purchase ON purchase_lines (purchase_id, line_no);
        CREATE INDEX IF NOT EXISTS ix_purchase_lines_product
            ON purchase_lines (product_id) WHERE product_id IS NOT NULL;


        CREATE TABLE IF NOT EXISTS payments (
            id uuid NOT NULL PRIMARY KEY,
            customer_id uuid NULL REFERENCES customers(id) ON DELETE SET NULL,
            customer_name varchar(256) NOT NULL DEFAULT '',
            customer_input_name varchar(256) NOT NULL DEFAULT '',
            amount numeric(18,2) NOT NULL DEFAULT 0,
            pay_date date NOT NULL DEFAULT CURRENT_DATE,
            note text NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS customer_aliases (
            id bigserial NOT NULL PRIMARY KEY,
            customer_id uuid NULL REFERENCES customers(id) ON DELETE CASCADE,
            customer_name varchar(256) NOT NULL DEFAULT '',
            alias varchar(256) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS audit_logs (
            id bigserial NOT NULL PRIMARY KEY,
            user_id uuid NULL,
            occurred_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            username varchar(128) NOT NULL DEFAULT '',
            action varchar(256) NOT NULL DEFAULT '',
            entity varchar(128) NOT NULL DEFAULT '',
            entity_name varchar(256) NOT NULL DEFAULT '',
            details text NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS user_sessions (
            session_token varchar(128) NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL DEFAULT '',
            machine_name varchar(128) NOT NULL DEFAULT '',
            started_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_seen timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ended_at timestamptz NULL,
            is_active boolean NOT NULL DEFAULT TRUE,
            end_reason text NOT NULL DEFAULT '',
            client_kind varchar(20) NOT NULL DEFAULT 'Desktop'
        );

        ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS client_kind varchar(20) NOT NULL DEFAULT 'Desktop';
        -- Quản lý thiết bị đăng nhập: mô tả trình duyệt/thiết bị + cờ thu hồi từ xa.
        ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS user_agent text NOT NULL DEFAULT '';
        ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS revoked boolean NOT NULL DEFAULT FALSE;
        ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS revoked_at timestamptz NULL;
        ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS revoked_by varchar(128) NOT NULL DEFAULT '';

        CREATE TABLE IF NOT EXISTS app_releases (
            id bigserial NOT NULL PRIMARY KEY,
            app_target varchar(32) NOT NULL DEFAULT 'hr-apk',
            version varchar(64) NOT NULL DEFAULT '',
            version_code integer NOT NULL DEFAULT 1,
            release_notes text NOT NULL DEFAULT '',
            is_mandatory boolean NOT NULL DEFAULT FALSE,
            is_published boolean NOT NULL DEFAULT FALSE,
            apk_file_name varchar(256) NOT NULL DEFAULT '',
            apk_mime_type varchar(128) NOT NULL DEFAULT 'application/vnd.android.package-archive',
            apk_size bigint NOT NULL DEFAULT 0,
            apk_sha256 varchar(64) NOT NULL DEFAULT '',
            -- APK nằm trên ĐĨA (ReleaseStorage), DB chỉ giữ metadata + cờ này. Cột apk_data là di sản
            -- của cách lưu cũ: ReleaseStorage.MigrateDatabaseBlobsAsync chuyển ra đĩa rồi bỏ trống.
            has_apk boolean NOT NULL DEFAULT FALSE,
            apk_data bytea NULL,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            published_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            published_by varchar(128) NOT NULL DEFAULT ''
        );

        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS app_target varchar(32) NOT NULL DEFAULT 'hr-apk';
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS version_code integer NOT NULL DEFAULT 1;
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS apk_file_name varchar(256) NOT NULL DEFAULT '';
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS apk_mime_type varchar(128) NOT NULL DEFAULT 'application/vnd.android.package-archive';
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS apk_size bigint NOT NULL DEFAULT 0;
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS apk_sha256 varchar(64) NOT NULL DEFAULT '';
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS apk_data bytea NULL;
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS has_apk boolean NOT NULL DEFAULT FALSE;
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;
        ALTER TABLE app_releases ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;
        CREATE INDEX IF NOT EXISTS ix_app_releases_latest
            ON app_releases (app_target, is_published, version_code DESC, published_at DESC, id DESC);

        CREATE TABLE IF NOT EXISTS work_access_requests (
            id bigserial NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL DEFAULT '',
            approved_by varchar(128) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS password_reset_requests (
            id bigserial NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL DEFAULT '',
            resolved_by varchar(128) NOT NULL DEFAULT ''
        );

        -- Mã khôi phục mật khẩu do admin cấp (thay cho reset bằng khuôn mặt đã tắt). Lưu HASH, một lần dùng.
        CREATE TABLE IF NOT EXISTS password_recovery_codes (
            id bigserial NOT NULL PRIMARY KEY,
            username varchar(128) NOT NULL,
            code_hash text NOT NULL,
            created_by varchar(128) NOT NULL DEFAULT '',
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at timestamptz NULL,
            used_at timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_recovery_username ON password_recovery_codes(username);

        -- Mã bảo mật 6 số của ứng dụng di động. Lưu Ở MÁY CHỦ (không còn bản sao nào trên điện thoại):
        -- mất/bị lấy cắp máy cũng không có gì để dò offline, và bộ đếm sai + khoá thử lại là của máy
        -- chủ nên xoá dữ liệu app hay cài lại app KHÔNG reset được số lần thử. Chỉ lưu hash Argon2id.
        CREATE TABLE IF NOT EXISTS app_pin_codes (
            username varchar(128) NOT NULL PRIMARY KEY,
            pin_hash text NOT NULL,
            failed_attempts integer NOT NULL DEFAULT 0,
            locked_until timestamptz NULL,
            created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_verified_at timestamptz NULL
        );

        CREATE TABLE IF NOT EXISTS registration_codes (
            id bigserial NOT NULL PRIMARY KEY,
            created_by varchar(128) NOT NULL DEFAULT '',
            used_by varchar(128) NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS app_settings (
            setting_key varchar(128) NOT NULL PRIMARY KEY,
            setting_value text NOT NULL DEFAULT '',
            updated_by varchar(128) NOT NULL DEFAULT '',
            updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS web_verified_users (
            username varchar(128) NOT NULL PRIMARY KEY,
            granted_by varchar(128) NOT NULL DEFAULT '',
            granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS web_diamond_members (
            username varchar(128) NOT NULL PRIMARY KEY,
            granted_by varchar(128) NOT NULL DEFAULT '',
            granted_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS ix_app_users_username_live ON app_users (username) WHERE is_deleted = FALSE;
        CREATE INDEX IF NOT EXISTS ix_customers_name_active ON customers (name) WHERE is_active = TRUE;
        CREATE INDEX IF NOT EXISTS ix_documents_customer ON documents (customer_id, doc_date DESC);
        CREATE INDEX IF NOT EXISTS ix_documents_date ON documents (doc_date DESC, voucher_no DESC);
        CREATE INDEX IF NOT EXISTS ix_documents_type_date ON documents (document_type, doc_date DESC, voucher_no DESC);
        CREATE INDEX IF NOT EXISTS ix_document_lines_document ON document_lines (document_id, line_no);
        CREATE INDEX IF NOT EXISTS ix_payments_customer ON payments (customer_id, pay_date DESC);
        CREATE INDEX IF NOT EXISTS ix_audit_logs_occurred ON audit_logs (occurred_at DESC);
        CREATE INDEX IF NOT EXISTS ix_user_sessions_presence ON user_sessions (username, is_active, last_seen DESC);
        """;
}
