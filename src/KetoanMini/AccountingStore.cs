using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace KetoanMini;

public sealed class AccountingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _connectionString;
    private readonly string _dataDirectory;
    private readonly string _legacyJsonPath;
    private readonly CustomerAliasBook _fileAliasBook;

    public AccountingData Data { get; private set; } = new();
    public CustomerAliasBook CustomerAliases { get; private set; } = CustomerAliasBook.Empty;
    public AppUser? CurrentUser { get; set; }

    public string DatabasePath => _connectionString;

    public AccountingStore(string connectionString)
    {
        _connectionString = NormalizeConnectionString(connectionString);
        _dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        _legacyJsonPath = Path.Combine(_dataDirectory, "ketoan_data.json");
        _fileAliasBook = CustomerAliasBook.LoadFromKnownLocations(Path.Combine(_dataDirectory, "ketoan_mini.db"));
        CustomerAliases = _fileAliasBook;

        Directory.CreateDirectory(_dataDirectory);
        Load();
    }

    public void Load()
    {
        EnsureDatabase();
        DeleteExpiredRegistrationCodes();

        if (IsDatabaseEmpty())
        {
            if (!TryImportLegacyJson())
            {
                Data = new AccountingData();
                SeedCustomersFromTemplate();
                SeedCustomersFromAliasBook();
                SeedCustomerAliasesFromFile();
            }

            Save();
            return;
        }

        Data = ReadAllFromDatabase();
        if (Data.CustomerAliases.Count == 0)
        {
            SeedCustomerAliasesFromFile();
        }
        RefreshAliasBook();
        NormalizeReferences();
        RefreshAliasBook();
        if (CustomerAliases.AliasCount > 0)
        {
            Save();
        }
    }

    public void Save()
    {
        EnsureDatabase();
        RefreshAliasBook();
        NormalizeReferences();
        RefreshAliasBook();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, "DELETE FROM document_lines;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM documents;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM payments;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM customer_aliases;");
        ExecuteNonQuery(connection, transaction, "DELETE FROM customers;");

        foreach (var customer in Data.Customers)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO customers
                    (id, name, tax_code, phone, address, note, is_active, created_at)
                VALUES
                    (@id, @name, @taxCode, @phone, @address, @note, @isActive, @createdAt);
                """;
            command.Parameters.AddWithValue("@id", customer.Id.ToString("D"));
            command.Parameters.AddWithValue("@name", customer.Name.Trim());
            command.Parameters.AddWithValue("@taxCode", customer.TaxCode.Trim());
            command.Parameters.AddWithValue("@phone", customer.Phone.Trim());
            command.Parameters.AddWithValue("@address", customer.Address.Trim());
            command.Parameters.AddWithValue("@note", customer.Note.Trim());
            command.Parameters.AddWithValue("@isActive", customer.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(customer.CreatedAt));
            command.ExecuteNonQuery();
        }

        foreach (var alias in Data.CustomerAliases)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO customer_aliases
                    (id, customer_id, customer_name, alias, created_at)
                VALUES
                    (@id, @customerId, @customerName, @alias, @createdAt);
                """;
            command.Parameters.AddWithValue("@id", alias.Id.ToString("D"));
            command.Parameters.AddWithValue("@customerId", alias.CustomerId.ToString("D"));
            command.Parameters.AddWithValue("@customerName", alias.CustomerName.Trim());
            command.Parameters.AddWithValue("@alias", alias.Alias.Trim());
            command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(alias.CreatedAt));
            command.ExecuteNonQuery();
        }

        foreach (var document in Data.Documents)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO documents
                        (id, voucher_no, doc_date, customer_id, customer_name, customer_input_name, content, note, created_at)
                    VALUES
                        (@id, @voucherNo, @docDate, @customerId, @customerName, @customerInputName, @content, @note, @createdAt);
                    """;
                command.Parameters.AddWithValue("@id", document.Id.ToString("D"));
                command.Parameters.AddWithValue("@voucherNo", document.VoucherNo.Trim());
                command.Parameters.AddWithValue("@docDate", ToDatabaseDate(document.Date));
                command.Parameters.AddWithValue("@customerId", document.CustomerId.ToString("D"));
                command.Parameters.AddWithValue("@customerName", document.CustomerName.Trim());
                command.Parameters.AddWithValue("@customerInputName", document.CustomerInputName.Trim());
                command.Parameters.AddWithValue("@content", document.Content.Trim());
                command.Parameters.AddWithValue("@note", document.Note.Trim());
                command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(document.CreatedAt));
                command.ExecuteNonQuery();
            }

            for (var lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
            {
                var line = document.Lines[lineIndex];
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO document_lines
                        (document_id, line_no, line_content, category, spec, quantity, unit_price, note)
                    VALUES
                        (@documentId, @lineNo, @lineContent, @category, @spec, @quantity, @unitPrice, @note);
                    """;
                command.Parameters.AddWithValue("@documentId", document.Id.ToString("D"));
                command.Parameters.AddWithValue("@lineNo", lineIndex + 1);
                command.Parameters.AddWithValue("@lineContent", line.LineContent.Trim());
                command.Parameters.AddWithValue("@category", line.Category.Trim());
                command.Parameters.AddWithValue("@spec", line.Spec.Trim());
                command.Parameters.AddWithValue("@quantity", ToDatabaseDecimal(line.Quantity));
                command.Parameters.AddWithValue("@unitPrice", ToDatabaseDecimal(line.UnitPrice));
                command.Parameters.AddWithValue("@note", line.Note.Trim());
                command.ExecuteNonQuery();
            }
        }

        foreach (var payment in Data.Payments)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO payments
                    (id, customer_id, customer_name, customer_input_name, pay_date, content, method, account, amount, note, created_at)
                VALUES
                    (@id, @customerId, @customerName, @customerInputName, @payDate, @content, @method, @account, @amount, @note, @createdAt);
                """;
            command.Parameters.AddWithValue("@id", payment.Id.ToString("D"));
            command.Parameters.AddWithValue("@customerId", payment.CustomerId.ToString("D"));
            command.Parameters.AddWithValue("@customerName", payment.CustomerName.Trim());
            command.Parameters.AddWithValue("@customerInputName", payment.CustomerInputName.Trim());
            command.Parameters.AddWithValue("@payDate", ToDatabaseDate(payment.Date));
            command.Parameters.AddWithValue("@content", payment.Content.Trim());
            command.Parameters.AddWithValue("@method", payment.Method.Trim());
            command.Parameters.AddWithValue("@account", payment.Account.Trim());
            command.Parameters.AddWithValue("@amount", ToDatabaseDecimal(payment.Amount));
            command.Parameters.AddWithValue("@note", payment.Note.Trim());
            command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(payment.CreatedAt));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<Customer> ActiveCustomers()
    {
        return Data.Customers
            .Where(customer => customer.IsActive)
            .OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public string ResolveCompanyName(string input)
    {
        return CustomerAliases.Resolve(input);
    }

    public IReadOnlyList<string> FindCompanySuggestions(string text, bool showAllWhenEmpty, int take = 20)
    {
        var normalized = NormalizeLookup(text);
        var aliasMatches = CustomerAliases.FindOfficialNames(text, showAllWhenEmpty, take);
        var databaseMatches = ActiveCustomers()
            .Where(customer =>
                string.IsNullOrWhiteSpace(normalized) ||
                NormalizeLookup(customer.Name).StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ||
                NormalizeLookup(customer.Name).Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(customer => customer.Name);

        return aliasMatches
            .Concat(databaseMatches)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => NormalizeLookup(name).StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Take(take)
            .ToList();
    }

    public AppUser? AuthenticateUser(string username, string password)
    {
        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id, username, full_name, role, is_active, approval_status, approved_at, approved_by, activation_code, public_key, is_deleted, deleted_at, password_hash, created_at
            FROM app_users
            WHERE username = @username
              AND is_deleted = 0;
            """;
        command.Parameters.AddWithValue("@username", username);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var storedHash = GetString(reader, "password_hash");
        if (!PasswordHasher.Verify(password, storedHash))
        {
            return null;
        }

        var user = ReadUser(reader);
        if (user.IsPendingApproval)
        {
            return user;
        }

        if (!user.IsActive)
        {
            throw new InvalidOperationException("Tài khoản này đã bị khóa. Liên hệ admin để mở lại.");
        }

        CurrentUser = user;
        EnsureChatKeyForUser(user);
        RecordAudit("Đăng nhập", "User", user.Username, "Đăng nhập ứng dụng.");
        return user;
    }

    public bool IsCurrentUserActive()
    {
        if (CurrentUser is null)
        {
            return false;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT is_active, approval_status
            FROM app_users
            WHERE id = @id
              AND is_deleted = 0;
            """;
        command.Parameters.AddWithValue("@id", CurrentUser.Id.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        return GetInt64(reader, "is_active") != 0 &&
            string.Equals(GetString(reader, "approval_status"), "Approved", StringComparison.OrdinalIgnoreCase);
    }

    public AppUser RegisterUser(string username, string fullName, string password, string activationCode = "")
    {
        activationCode = NormalizeActivationCode(activationCode);
        var hasActivationCode = !string.IsNullOrWhiteSpace(activationCode);
        if (hasActivationCode)
        {
            EnsureRegistrationCodeAvailable(activationCode);
        }

        var user = InsertUser(
            username,
            fullName,
            password,
            "User",
            isActive: hasActivationCode,
            approvalStatus: hasActivationCode ? "Approved" : "Pending",
            approvedBy: hasActivationCode ? "activation-code" : "",
            activationCode: activationCode);

        if (hasActivationCode)
        {
            MarkRegistrationCodeUsed(activationCode, user.Username);
            RecordAudit("Đăng ký bằng mã", "User", user.Username, "Tài khoản tự kích hoạt bằng mã đăng ký do admin tạo.");
        }
        else
        {
            RecordAudit("Đăng ký chờ duyệt", "User", user.Username, "Tài khoản mới đang chờ admin duyệt.");
        }

        return user;
    }

    public AppUser ActivatePendingUser(string username, string activationCode)
    {
        username = NormalizeUsername(username);
        activationCode = NormalizeActivationCode(activationCode);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Nhập tài khoản cần kích hoạt.");
        }

        if (string.IsNullOrWhiteSpace(activationCode))
        {
            throw new InvalidOperationException("Nhập mã kích hoạt do admin cấp.");
        }

        var user = FindUserByUsername(username) ?? throw new InvalidOperationException("Không tìm thấy tài khoản cần kích hoạt.");
        if (!user.IsPendingApproval)
        {
            if (user.IsActive)
            {
                CurrentUser = user;
                EnsureChatKeyForUser(user);
                return user;
            }

            throw new InvalidOperationException("Tài khoản này đã bị khóa. Liên hệ admin để mở lại.");
        }

        DeleteExpiredRegistrationCodes();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.Now;

        using (var codeCommand = connection.CreateCommand())
        {
            codeCommand.Transaction = transaction;
            codeCommand.CommandText = """
                UPDATE registration_codes
                SET used_at = @usedAt,
                    used_by = @usedBy
                WHERE code = @code
                  AND is_active = 1
                  AND used_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @usedAt);
                """;
            codeCommand.Parameters.AddWithValue("@code", activationCode);
            codeCommand.Parameters.AddWithValue("@usedAt", ToDatabaseDateTime(now));
            codeCommand.Parameters.AddWithValue("@usedBy", username);
            if (codeCommand.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException("Mã kích hoạt không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
            }
        }

        using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                UPDATE app_users
                SET is_active = 1,
                    approval_status = 'Approved',
                    approved_at = @approvedAt,
                    approved_by = 'activation-code',
                    activation_code = @activationCode
                WHERE id = @id
                  AND approval_status = 'Pending';
                """;
            userCommand.Parameters.AddWithValue("@id", user.Id.ToString("D"));
            userCommand.Parameters.AddWithValue("@approvedAt", ToDatabaseDateTime(now));
            userCommand.Parameters.AddWithValue("@activationCode", activationCode);
            if (userCommand.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException("Tài khoản này không còn ở trạng thái chờ kích hoạt.");
            }
        }

        transaction.Commit();

        var activated = FindUserByUsername(username) ?? user;
        CurrentUser = activated;
        EnsureChatKeyForUser(activated);
        RecordAudit("Kích hoạt tài khoản", "User", activated.Username, "Người dùng đăng nhập và kích hoạt bằng mã admin tạo.");
        RecordAudit("Đăng nhập", "User", activated.Username, "Đăng nhập ứng dụng.");
        return activated;
    }

    public void CreatePasswordResetRequest(string username)
    {
        username = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Nhập tên tài khoản cần lấy lại mật khẩu.");
        }

        var user = FindUserByUsername(username);
        if (user is null || !user.IsActive)
        {
            throw new InvalidOperationException("Không tìm thấy tài khoản đang hoạt động.");
        }

        if (user.IsAdmin)
        {
            throw new InvalidOperationException("Tài khoản admin không dùng chức năng quên mật khẩu. Hãy đổi mật khẩu admin trong Sửa hồ sơ sau khi đăng nhập.");
        }

        using var connection = OpenConnection();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT COUNT(*)
                FROM password_reset_requests
                WHERE username = @username
                  AND status = 'Pending';
                """;
            check.Parameters.AddWithValue("@username", username);
            if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                EnsurePasswordResetCodeForUser(user);
                RecordAudit("Yêu cầu quên mật khẩu", "User", username, "Người dùng gửi lại yêu cầu, yêu cầu cũ vẫn đang chờ admin xử lý.");
                return;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO password_reset_requests
                    (requested_at, username, full_name, status)
                VALUES
                    (@requestedAt, @username, @fullName, 'Pending');
                """;
            command.Parameters.AddWithValue("@requestedAt", ToDatabaseDateTime(DateTime.Now));
            command.Parameters.AddWithValue("@username", user.Username);
            command.Parameters.AddWithValue("@fullName", user.FullName);
            command.ExecuteNonQuery();
        }

        EnsurePasswordResetCodeForUser(user);
        RecordAudit("Yêu cầu quên mật khẩu", "User", user.Username, "Chờ admin đổi mật khẩu.");
    }

    public IReadOnlyList<PasswordResetRequest> GetPendingPasswordResetRequests()
    {
        EnsureCurrentAdmin();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, requested_at, username, full_name, status
            FROM password_reset_requests
            WHERE status = 'Pending'
              AND username NOT IN (
                  SELECT username
                  FROM app_users
                  WHERE role = 'Admin'
              )
            ORDER BY requested_at DESC, id DESC;
            """;
        using var reader = command.ExecuteReader();
        var requests = new List<PasswordResetRequest>();
        while (reader.Read())
        {
            requests.Add(ReadPasswordResetRequest(reader));
        }

        return requests;
    }

    public void ValidatePasswordResetCode(string username, string resetCode)
    {
        username = NormalizeUsername(username);
        resetCode = NormalizeActivationCode(resetCode);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Nhập tài khoản cần đổi mật khẩu.");
        }

        if (string.IsNullOrWhiteSpace(resetCode))
        {
            throw new InvalidOperationException("Nhập mã đổi mật khẩu do admin cấp.");
        }

        var user = FindUserByUsername(username) ?? throw new InvalidOperationException("Không tìm thấy tài khoản.");
        if (user.IsAdmin)
        {
            throw new InvalidOperationException("Tài khoản admin không dùng chức năng quên mật khẩu.");
        }

        if (!user.IsActive || user.IsPendingApproval)
        {
            throw new InvalidOperationException("Tài khoản chưa hoạt động. Liên hệ admin để mở lại.");
        }

        DeleteExpiredRegistrationCodes();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM registration_codes
            WHERE code = @code
              AND note = @note
              AND is_active = 1
              AND used_at IS NULL
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        command.Parameters.AddWithValue("@code", resetCode);
        command.Parameters.AddWithValue("@note", PasswordResetCodeNote(username));
        command.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            throw new InvalidOperationException("Mã đổi mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
        }
    }

    public void ResetPasswordWithCode(string username, string resetCode, string newPassword)
    {
        username = NormalizeUsername(username);
        resetCode = NormalizeActivationCode(resetCode);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Nhập tài khoản cần đổi mật khẩu.");
        }

        if (string.IsNullOrWhiteSpace(resetCode))
        {
            throw new InvalidOperationException("Nhập mã đổi mật khẩu do admin cấp.");
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            throw new InvalidOperationException("Nhập mật khẩu mới.");
        }

        var user = FindUserByUsername(username) ?? throw new InvalidOperationException("Không tìm thấy tài khoản.");
        if (user.IsAdmin)
        {
            throw new InvalidOperationException("Tài khoản admin không dùng chức năng quên mật khẩu.");
        }

        if (!user.IsActive || user.IsPendingApproval)
        {
            throw new InvalidOperationException("Tài khoản chưa hoạt động. Liên hệ admin để mở lại.");
        }

        DeleteExpiredRegistrationCodes();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.Now;

        using (var codeCommand = connection.CreateCommand())
        {
            codeCommand.Transaction = transaction;
            codeCommand.CommandText = """
                UPDATE registration_codes
                SET used_at = @usedAt,
                    used_by = @usedBy
                WHERE code = @code
                  AND note = @note
                  AND is_active = 1
                  AND used_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @usedAt);
                """;
            codeCommand.Parameters.AddWithValue("@code", resetCode);
            codeCommand.Parameters.AddWithValue("@note", PasswordResetCodeNote(username));
            codeCommand.Parameters.AddWithValue("@usedAt", ToDatabaseDateTime(now));
            codeCommand.Parameters.AddWithValue("@usedBy", username);
            if (codeCommand.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException("Mã đổi mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
            }
        }

        using (var userCommand = connection.CreateCommand())
        {
            userCommand.Transaction = transaction;
            userCommand.CommandText = """
                UPDATE app_users
                SET password_hash = @passwordHash
                WHERE username = @username;
                """;
            userCommand.Parameters.AddWithValue("@username", username);
            userCommand.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash(newPassword));
            userCommand.ExecuteNonQuery();
        }

        using (var resetCommand = connection.CreateCommand())
        {
            resetCommand.Transaction = transaction;
            resetCommand.CommandText = """
                UPDATE password_reset_requests
                SET status = 'Resolved',
                    resolved_at = @resolvedAt,
                    resolved_by = 'reset-code'
                WHERE username = @username
                  AND status = 'Pending';
                """;
            resetCommand.Parameters.AddWithValue("@username", username);
            resetCommand.Parameters.AddWithValue("@resolvedAt", ToDatabaseDateTime(now));
            resetCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        RecordAudit("Đổi mật khẩu bằng mã", "User", username, "Người dùng đặt lại mật khẩu bằng mã admin cấp.");
    }

    public WorkAccessRequest CreateOrGetWorkAccessRequest(DateTime requestedAt, string accessSlot, string reason)
    {
        EnsureCurrentUserActive();

        if (CurrentUser is null)
        {
            throw new InvalidOperationException("Chưa đăng nhập.");
        }

        if (CurrentUser.IsAdmin)
        {
            throw new InvalidOperationException("Admin không cần duyệt mở thao tác ngoài giờ.");
        }

        var workDate = DateOnly.FromDateTime(requestedAt);
        accessSlot = NormalizeAccessSlot(accessSlot);
        using var connection = OpenConnection();

        using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT TOP (1) id, work_date, requested_at, username, full_name, access_slot, reason, status, approved_at, approved_by, punch_at
                FROM work_access_requests
                WHERE username = @username
                  AND work_date = @workDate
                  AND access_slot = @accessSlot
                  AND status IN ('Pending', 'Approved')
                ORDER BY id DESC;
                """;
            check.Parameters.AddWithValue("@username", CurrentUser.Username);
            check.Parameters.AddWithValue("@workDate", ToDatabaseDate(workDate));
            check.Parameters.AddWithValue("@accessSlot", accessSlot);
            using var reader = check.ExecuteReader();
            if (reader.Read())
            {
                return ReadWorkAccessRequest(reader);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO work_access_requests
                    (work_date, requested_at, username, full_name, access_slot, reason, status)
                VALUES
                    (@workDate, @requestedAt, @username, @fullName, @accessSlot, @reason, 'Pending');
                """;
            command.Parameters.AddWithValue("@workDate", ToDatabaseDate(workDate));
            command.Parameters.AddWithValue("@requestedAt", ToDatabaseDateTime(requestedAt));
            command.Parameters.AddWithValue("@username", CurrentUser.Username);
            command.Parameters.AddWithValue("@fullName", CurrentUser.FullName);
            command.Parameters.AddWithValue("@accessSlot", accessSlot);
            command.Parameters.AddWithValue("@reason", reason);
            command.ExecuteNonQuery();
        }

        RecordAudit("Yêu cầu ngoài giờ", "WorkAccess", CurrentUser.Username, reason);
        return CreateOrGetWorkAccessRequest(requestedAt, accessSlot, reason);
    }

    public WorkAccessRequest CreateOrGetWorkAccessRequest(DateTime requestedAt, string reason)
    {
        return CreateOrGetWorkAccessRequest(requestedAt, "after_work", reason);
    }

    public bool HasApprovedWorkAccess(DateOnly workDate, string accessSlot)
    {
        if (CurrentUser?.IsAdmin == true)
        {
            return true;
        }

        if (CurrentUser is null)
        {
            return false;
        }

        accessSlot = NormalizeAccessSlot(accessSlot);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM work_access_requests
            WHERE username = @username
              AND work_date = @workDate
              AND access_slot = @accessSlot
              AND status = 'Approved';
            """;
        command.Parameters.AddWithValue("@username", CurrentUser.Username);
        command.Parameters.AddWithValue("@workDate", ToDatabaseDate(workDate));
        command.Parameters.AddWithValue("@accessSlot", accessSlot);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public bool HasApprovedWorkAccess(DateOnly workDate)
    {
        return HasApprovedWorkAccess(workDate, "after_work");
    }

    /// <summary>Returns the current user's approved overtime request for the date (or null), incl. ApprovedAt.</summary>
    public WorkAccessRequest? GetApprovedWorkAccess(DateOnly workDate, string accessSlot = "after_work")
    {
        if (CurrentUser is null)
        {
            return null;
        }

        accessSlot = NormalizeAccessSlot(accessSlot);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id, work_date, requested_at, username, full_name, access_slot, reason, status, approved_at, approved_by, punch_at
            FROM work_access_requests
            WHERE username = @username
              AND work_date = @workDate
              AND access_slot = @accessSlot
              AND status = 'Approved'
            ORDER BY approved_at DESC, id DESC;
            """;
        command.Parameters.AddWithValue("@username", CurrentUser.Username);
        command.Parameters.AddWithValue("@workDate", ToDatabaseDate(workDate));
        command.Parameters.AddWithValue("@accessSlot", accessSlot);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadWorkAccessRequest(reader) : null;
    }

    /// <summary>Returns the current user's latest overtime request for the date (any status), or null.</summary>
    public WorkAccessRequest? GetWorkAccessForToday(DateOnly workDate, string accessSlot = "after_work")
    {
        if (CurrentUser is null) return null;
        accessSlot = NormalizeAccessSlot(accessSlot);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id, work_date, requested_at, username, full_name, access_slot, reason, status, approved_at, approved_by, punch_at
            FROM work_access_requests
            WHERE username = @username
              AND work_date = @workDate
              AND access_slot = @accessSlot
              AND status IN ('Pending', 'Approved')
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("@username", CurrentUser.Username);
        command.Parameters.AddWithValue("@workDate", ToDatabaseDate(workDate));
        command.Parameters.AddWithValue("@accessSlot", accessSlot);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadWorkAccessRequest(reader) : null;
    }

    /// <summary>Records a "chấm công" punch on the user's pending/approved overtime request for the date.</summary>
    public void PunchWorkAccess(DateOnly workDate, string accessSlot = "after_work")
    {
        EnsureCurrentUserActive();
        if (CurrentUser is null) throw new InvalidOperationException("Chưa đăng nhập.");
        accessSlot = NormalizeAccessSlot(accessSlot);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE work_access_requests
            SET punch_at = @punchAt
            WHERE id = (
                SELECT TOP (1) id FROM work_access_requests
                WHERE username = @username AND work_date = @workDate AND access_slot = @accessSlot
                  AND status IN ('Pending', 'Approved') AND punch_at IS NULL
                ORDER BY id DESC
            );
            """;
        command.Parameters.AddWithValue("@punchAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@username", CurrentUser.Username);
        command.Parameters.AddWithValue("@workDate", ToDatabaseDate(workDate));
        command.Parameters.AddWithValue("@accessSlot", accessSlot);
        command.ExecuteNonQuery();
        RecordAudit("Chấm công tăng ca", "WorkAccess", CurrentUser.Username, "Người dùng chấm công bắt đầu tăng ca.");
    }

    /// <summary>Closes the user's active (approved) overtime when their work session ends, so the
    /// stopwatch stops at logout and a new login needs a fresh chấm công + admin approval.</summary>
    public void CompleteActiveOvertime(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE work_access_requests
                SET status = 'Completed'
                WHERE username = @username AND status = 'Approved' AND access_slot = 'after_work';
                """;
            command.Parameters.AddWithValue("@username", username);
            command.ExecuteNonQuery();
        }
        catch { /* best effort on shutdown */ }
    }

    // ── User sessions: single active login + online presence ────────────────

    /// <summary>Starts a session for the current user, ending any other active sessions (single login).</summary>
    public string StartSession(string machineName)
    {
        if (CurrentUser is null) throw new InvalidOperationException("Chưa đăng nhập.");
        var token = Guid.NewGuid().ToString("N");
        var now = ToDatabaseDateTime(DateTime.Now);
        using var connection = OpenConnection();

        using (var endOthers = connection.CreateCommand())
        {
            endOthers.CommandText = """
                UPDATE user_sessions
                SET is_active = 0, ended_at = @now, end_reason = N'Đăng nhập ở nơi khác'
                WHERE username = @username AND is_active = 1;
                """;
            endOthers.Parameters.AddWithValue("@now", now);
            endOthers.Parameters.AddWithValue("@username", CurrentUser.Username);
            endOthers.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO user_sessions (session_token, username, machine_name, started_at, last_seen, is_active)
                VALUES (@token, @username, @machine, @now, @now, 1);
                """;
            insert.Parameters.AddWithValue("@token", token);
            insert.Parameters.AddWithValue("@username", CurrentUser.Username);
            insert.Parameters.AddWithValue("@machine", machineName ?? "");
            insert.Parameters.AddWithValue("@now", now);
            insert.ExecuteNonQuery();
        }
        return token;
    }

    /// <summary>Heartbeat result; tells the client why (if at all) it must log out.</summary>
    public enum SessionStatus
    {
        /// <summary>Session is still valid — keep working.</summary>
        Alive,
        /// <summary>Session was ended by a login on another machine (single login).</summary>
        EndedElsewhere,
        /// <summary>The account was locked or deleted by an admin.</summary>
        AccountLocked
    }

    /// <summary>Heartbeat safety net: refreshes last_seen and reports whether this
    /// session must end. This is the slower fallback behind the instant LAN push in
    /// <see cref="SessionControlService"/> — it still catches a missed/blocked UDP signal.</summary>
    public SessionStatus CheckSession(string sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken)) return SessionStatus.EndedElsewhere;
        using var connection = OpenConnection();
        using (var update = connection.CreateCommand())
        {
            update.CommandText = """
                UPDATE user_sessions SET last_seen = @now
                WHERE session_token = @token AND is_active = 1;
                """;
            update.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
            update.Parameters.AddWithValue("@token", sessionToken);
            update.ExecuteNonQuery();
        }
        using (var check = connection.CreateCommand())
        {
            // Look at the LIVE account row only. A deleted-then-recreated username keeps
            // soft-deleted rows (is_deleted = 1) alongside the active one, so a plain JOIN
            // could read a stale deleted row and log a valid user out as "locked".
            check.CommandText = """
                SELECT s.is_active AS session_active,
                       (SELECT TOP 1 u.is_active
                        FROM app_users u
                        WHERE u.username = s.username AND u.is_deleted = 0) AS user_active
                FROM user_sessions s
                WHERE s.session_token = @token;
                """;
            check.Parameters.AddWithValue("@token", sessionToken);
            using var reader = check.ExecuteReader();
            if (!reader.Read()) return SessionStatus.EndedElsewhere;

            bool sessionActive = !reader.IsDBNull(0) && Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture) == 1;
            // user_active is NULL only when no live (non-deleted) account exists → it was deleted.
            bool accountLocked = reader.IsDBNull(1) || Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) != 1;

            if (accountLocked) return SessionStatus.AccountLocked;
            return sessionActive ? SessionStatus.Alive : SessionStatus.EndedElsewhere;
        }
    }

    public void EndSession(string sessionToken, string reason)
    {
        if (string.IsNullOrEmpty(sessionToken)) return;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE user_sessions
                SET is_active = 0, ended_at = @now, end_reason = @reason
                WHERE session_token = @token AND is_active = 1;
                """;
            command.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
            command.Parameters.AddWithValue("@reason", reason ?? "");
            command.Parameters.AddWithValue("@token", sessionToken);
            command.ExecuteNonQuery();
        }
        catch { /* best effort on shutdown */ }
    }

    /// <summary>Online status + minutes-online-today for every user (admin view).</summary>
    public IReadOnlyList<UserPresence> GetUserPresence()
    {
        var today = DateTime.Today;
        var now = DateTime.Now;
        var onlineThreshold = now.AddSeconds(-90);
        var byUser = new Dictionary<string, UserPresence>(StringComparer.OrdinalIgnoreCase);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT username, started_at, last_seen, ended_at, is_active
            FROM user_sessions
            WHERE last_seen >= @sinceMidnight OR is_active = 1;
            """;
        command.Parameters.AddWithValue("@sinceMidnight", ToDatabaseDateTime(today));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var username = GetString(reader, "username");
            var started = ParseDateTime(GetString(reader, "started_at"));
            var lastSeen = ParseDateTime(GetString(reader, "last_seen"));
            var endedRaw = GetString(reader, "ended_at");
            DateTime? ended = string.IsNullOrWhiteSpace(endedRaw) ? null : ParseDateTime(endedRaw);
            bool active = GetInt64(reader, "is_active") != 0;

            if (!byUser.TryGetValue(username, out var p))
            {
                p = new UserPresence { Username = username };
                byUser[username] = p;
            }

            if (active && lastSeen >= onlineThreshold) p.IsOnline = true;

            DateTime sessionEnd = ended ?? lastSeen;
            DateTime from = started > today ? started : today;
            DateTime to = sessionEnd < now ? sessionEnd : now;
            if (to > from) p.MinutesToday += (int)(to - from).TotalMinutes;
        }

        return byUser.Values.ToList();
    }

    /// <summary>Cheap change-signature of app_users; changes whenever a user is
    /// added (registration), approved, locked, edited or deleted. Used to push
    /// Nhân sự updates only when something actually changed (no periodic reset).</summary>
    public int GetUsersChangeToken()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(username, full_name, role, approval_status, is_active, is_deleted)), 0) FROM app_users;";
        var result = command.ExecuteScalar();
        return (result is null || result is DBNull) ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<WorkAccessRequest> GetPendingWorkAccessRequests()
    {
        EnsureCurrentAdmin();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, work_date, requested_at, username, full_name, access_slot, reason, status, approved_at, approved_by, punch_at
            FROM work_access_requests
            WHERE status = 'Pending'
            ORDER BY requested_at DESC, id DESC;
            """;
        using var reader = command.ExecuteReader();
        var requests = new List<WorkAccessRequest>();
        while (reader.Read())
        {
            requests.Add(ReadWorkAccessRequest(reader));
        }

        return requests;
    }

    public void ApproveWorkAccessRequests(IEnumerable<long> requestIds)
    {
        EnsureCurrentAdmin();
        var ids = requestIds.Distinct().Where(id => id > 0).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < ids.Count; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE work_access_requests
                SET status = 'Approved',
                    approved_at = @approvedAt,
                    approved_by = @approvedBy
                WHERE id = @id
                  AND status = 'Pending';
                """;
            command.Parameters.AddWithValue("@approvedAt", ToDatabaseDateTime(DateTime.Now));
            command.Parameters.AddWithValue("@approvedBy", CurrentUser?.Username ?? "admin");
            command.Parameters.AddWithValue("@id", ids[i]);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        RecordAudit("Duyệt ngoài giờ", "WorkAccess", string.Join(", ", ids), "Admin mở thao tác ngoài giờ cho nhân viên.");
    }

    public IReadOnlyList<AppUser> GetUsers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, username, full_name, role, is_active, approval_status, approved_at, approved_by, activation_code, public_key, is_deleted, deleted_at, password_hash, created_at
            FROM app_users
            WHERE is_deleted = 0
            ORDER BY role, username;
            """;
        using var reader = command.ExecuteReader();
        var users = new List<AppUser>();
        while (reader.Read())
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public void UpdateCurrentUserProfile(string fullName, string currentPassword, string newPassword)
    {
        EnsureCurrentUserActive();

        if (CurrentUser is null)
        {
            throw new InvalidOperationException("Bạn cần đăng nhập trước.");
        }

        fullName = fullName.Trim();
        newPassword = newPassword.Trim();

        using var connection = OpenConnection();
        var currentHash = "";
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT TOP (1) password_hash FROM app_users WHERE id = @id;";
            read.Parameters.AddWithValue("@id", CurrentUser.Id.ToString("D"));
            currentHash = Convert.ToString(read.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (string.IsNullOrWhiteSpace(currentPassword) || !PasswordHasher.Verify(currentPassword, currentHash))
            {
                throw new InvalidOperationException("Mật khẩu hiện tại không đúng.");
            }
        }

        using (var command = connection.CreateCommand())
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                command.CommandText = """
                    UPDATE app_users
                    SET full_name = @fullName
                    WHERE id = @id;
                    """;
            }
            else
            {
                command.CommandText = """
                    UPDATE app_users
                    SET full_name = @fullName,
                        password_hash = @passwordHash
                    WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash(newPassword));
            }

            command.Parameters.AddWithValue("@id", CurrentUser.Id.ToString("D"));
            command.Parameters.AddWithValue("@fullName", fullName);
            command.ExecuteNonQuery();
        }

        CurrentUser.FullName = fullName;
        RecordAudit("Sửa hồ sơ", "User", CurrentUser.Username, string.IsNullOrWhiteSpace(newPassword) ? "Đổi tên hiển thị." : "Đổi tên hiển thị và mật khẩu.");
    }

    public AppUser AdminCreateUser(string username, string fullName, string password)
    {
        EnsureCurrentAdmin();
        var user = InsertUser(username, fullName, password, "User", isActive: true);
        RecordAudit("Thêm tài khoản", "User", user.Username, "Admin tạo tài khoản User.");
        return user;
    }

    public void AdminApproveUser(Guid userId)
    {
        EnsureCurrentAdmin();
        var user = FindUserById(userId) ?? throw new InvalidOperationException("Không tìm thấy tài khoản cần duyệt.");
        if (user.IsAdmin)
        {
            throw new InvalidOperationException("Tài khoản admin không cần duyệt.");
        }

        if (!user.IsPendingApproval)
        {
            throw new InvalidOperationException("Chỉ duyệt tài khoản đang ở trạng thái chờ duyệt.");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE app_users
            SET is_active = 1,
                approval_status = 'Approved',
                approved_at = @approvedAt,
                approved_by = @approvedBy
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", userId.ToString("D"));
        command.Parameters.AddWithValue("@approvedAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@approvedBy", CurrentUser?.Username ?? "admin");
        command.ExecuteNonQuery();

        RecordAudit("Duyệt tài khoản", "User", user.Username, "Admin duyệt tài khoản đăng ký mới.");
    }

    public RegistrationCode AdminCreateRegistrationCode(string note = "")
    {
        EnsureCurrentAdmin();
        DeleteExpiredRegistrationCodes();
        var code = GenerateRegistrationCode();
        var createdAt = DateTime.Now;
        var expiresAt = createdAt.AddHours(1);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO registration_codes
                (code, note, is_active, created_at, expires_at, created_by)
            OUTPUT INSERTED.id
            VALUES
                (@code, @note, 1, @createdAt, @expiresAt, @createdBy);
            """;
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@note", note.Trim());
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(createdAt));
        command.Parameters.AddWithValue("@expiresAt", ToDatabaseDateTime(expiresAt));
        command.Parameters.AddWithValue("@createdBy", CurrentUser?.Username ?? "admin");
        var id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        RecordAudit("Tạo mã đăng ký", "RegistrationCode", code, note.Trim());

        return new RegistrationCode
        {
            Id = id,
            Code = code,
            Note = note.Trim(),
            IsActive = true,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            CreatedBy = CurrentUser?.Username ?? "admin"
        };
    }

    public RegistrationCode AdminCreatePasswordResetCode(Guid userId)
    {
        EnsureCurrentAdmin();
        DeleteExpiredRegistrationCodes();
        var user = FindUserById(userId) ?? throw new InvalidOperationException("Không tìm thấy tài khoản cần cấp mã.");
        if (user.IsAdmin)
        {
            throw new InvalidOperationException("Không cấp mã đổi mật khẩu cho tài khoản admin.");
        }

        if (!user.IsActive || user.IsPendingApproval)
        {
            throw new InvalidOperationException("Chỉ cấp mã đổi mật khẩu cho tài khoản đang hoạt động.");
        }

        var code = GenerateRegistrationCode();
        var createdAt = DateTime.Now;
        var expiresAt = createdAt.AddMinutes(15);
        var note = PasswordResetCodeNote(user.Username);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO registration_codes
                (code, note, is_active, created_at, expires_at, created_by)
            OUTPUT INSERTED.id
            VALUES
                (@code, @note, 1, @createdAt, @expiresAt, @createdBy);
            """;
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@note", note);
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(createdAt));
        command.Parameters.AddWithValue("@expiresAt", ToDatabaseDateTime(expiresAt));
        command.Parameters.AddWithValue("@createdBy", CurrentUser?.Username ?? "admin");
        var id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        RecordAudit("Tạo mã đổi mật khẩu", "RegistrationCode", code, $"Tài khoản: {user.Username}");

        return new RegistrationCode
        {
            Id = id,
            Code = code,
            Note = note,
            IsActive = true,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            CreatedBy = CurrentUser?.Username ?? "admin"
        };
    }

    public IReadOnlyList<RegistrationCode> GetRegistrationCodes(int take = 50)
    {
        EnsureCurrentAdmin();
        DeleteExpiredRegistrationCodes();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@take) id, code, note, is_active, created_at, expires_at, created_by, used_at, used_by
            FROM registration_codes
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 500));
        using var reader = command.ExecuteReader();
        var codes = new List<RegistrationCode>();
        while (reader.Read())
        {
            codes.Add(ReadRegistrationCode(reader));
        }

        return codes;
    }

    public void AdminDeleteRegistrationCode(long codeId)
    {
        EnsureCurrentAdmin();
        if (codeId <= 0)
        {
            throw new InvalidOperationException("Chọn mã cần xóa.");
        }

        using var connection = OpenConnection();
        var code = "";
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT TOP (1) code FROM registration_codes WHERE id = @id;";
            read.Parameters.AddWithValue("@id", codeId);
            code = Convert.ToString(read.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Không tìm thấy mã cần xóa.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM registration_codes WHERE id = @id;";
        command.Parameters.AddWithValue("@id", codeId);
        command.ExecuteNonQuery();
        RecordAudit("Xóa mã kích hoạt", "RegistrationCode", code, "Admin xóa mã trong màn hình quản lý mã.");
    }

    public AppUser AdminUpdateUser(Guid userId, string username, string fullName, string password, bool isActive)
    {
        EnsureCurrentAdmin();
        var existing = FindUserById(userId) ?? throw new InvalidOperationException("Không tìm thấy tài khoản cần sửa.");
        if (existing.IsAdmin)
        {
            throw new InvalidOperationException("Không sửa tài khoản admin tại màn hình quản lý tài khoản cấp thấp hơn.");
        }

        username = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Tên đăng nhập không được trống.");
        }

        EnsureUsernameAvailable(username, userId);
        var approvesPendingUser = existing.IsPendingApproval && isActive;
        var approvalStatus = isActive ? "Approved" : existing.IsPendingApproval ? "Pending" : "Approved";

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(password))
        {
            command.CommandText = """
                UPDATE app_users
                SET username = @username,
                    full_name = @fullName,
                    is_active = @isActive,
                    approval_status = @approvalStatus,
                    approved_at = CASE WHEN @approvesPendingUser = 1 THEN @approvedAt ELSE approved_at END,
                    approved_by = CASE WHEN @approvesPendingUser = 1 THEN @approvedBy ELSE approved_by END
                WHERE id = @id
                  AND is_deleted = 0;
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE app_users
                SET username = @username,
                    full_name = @fullName,
                    password_hash = @passwordHash,
                    is_active = @isActive,
                    approval_status = @approvalStatus,
                    approved_at = CASE WHEN @approvesPendingUser = 1 THEN @approvedAt ELSE approved_at END,
                    approved_by = CASE WHEN @approvesPendingUser = 1 THEN @approvedBy ELSE approved_by END
                WHERE id = @id
                  AND is_deleted = 0;
                """;
            command.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash(password));
        }

        command.Parameters.AddWithValue("@id", userId.ToString("D"));
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@fullName", fullName.Trim());
        command.Parameters.AddWithValue("@isActive", isActive ? 1 : 0);
        command.Parameters.AddWithValue("@approvalStatus", approvalStatus);
        command.Parameters.AddWithValue("@approvesPendingUser", approvesPendingUser ? 1 : 0);
        command.Parameters.AddWithValue("@approvedAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@approvedBy", CurrentUser?.Username ?? "admin");
        command.ExecuteNonQuery();

        var updated = FindUserById(userId) ?? existing;
        if (!string.IsNullOrWhiteSpace(password))
        {
            ResolvePasswordResetRequests(updated.Username);
        }

        RecordAudit("Sửa tài khoản", "User", updated.Username, $"Admin sửa thông tin tài khoản. Trạng thái: {(isActive ? "Hoạt động" : "Khóa")}{(string.IsNullOrWhiteSpace(password) ? "" : "; Đã đổi mật khẩu")}.");
        return updated;
    }

    public void AdminDeleteUser(Guid userId)
    {
        EnsureCurrentAdmin();
        var user = FindUserById(userId) ?? throw new InvalidOperationException("Không tìm thấy tài khoản cần xóa.");
        if (user.IsAdmin)
        {
            throw new InvalidOperationException("Không được xóa tài khoản admin.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        // Hard-delete EVERYTHING tied to this user so no data lingers in the DB
        // (no soft-deleted rows left behind). Chat rows are removed child-first to
        // satisfy the foreign keys: file_offers → messages → conversations.
        void RunUserDelete(string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", userId.ToString("D"));
            command.Parameters.AddWithValue("@username", user.Username);
            command.ExecuteNonQuery();
        }

        // The set of conversations this user takes part in (used to clear their
        // messages/offers even when only the username or only the id column is set).
        const string userConvos = """
            SELECT id FROM chat_conversations
            WHERE user_a_id = @id OR user_b_id = @id OR user_a = @username OR user_b = @username
            """;

        RunUserDelete($"""
            DELETE FROM chat_file_offers
            WHERE sender_id = @id OR receiver_id = @id
               OR sender_username = @username OR receiver_username = @username
               OR conversation_id IN ({userConvos})
               OR message_id IN (
                    SELECT id FROM chat_messages
                    WHERE sender_id = @id OR receiver_id = @id
                       OR sender_username = @username OR receiver_username = @username);
            """);

        RunUserDelete($"""
            DELETE FROM chat_messages
            WHERE sender_id = @id OR receiver_id = @id
               OR sender_username = @username OR receiver_username = @username
               OR conversation_id IN ({userConvos});
            """);

        RunUserDelete("""
            DELETE FROM chat_conversations
            WHERE user_a_id = @id OR user_b_id = @id OR user_a = @username OR user_b = @username;
            """);

        int deletedCodes;
        using (var deleteCodes = connection.CreateCommand())
        {
            deleteCodes.Transaction = transaction;
            deleteCodes.CommandText = """
                DELETE FROM registration_codes
                WHERE used_by = @username
                   OR (@activationCode <> '' AND code = @activationCode);
                """;
            deleteCodes.Parameters.AddWithValue("@username", user.Username);
            deleteCodes.Parameters.AddWithValue("@activationCode", user.ActivationCode);
            deletedCodes = deleteCodes.ExecuteNonQuery();
        }

        // Per-username records (overtime requests, reset requests, login sessions).
        foreach (var table in new[] { "work_access_requests", "password_reset_requests", "user_sessions" })
        {
            using var cleanup = connection.CreateCommand();
            cleanup.Transaction = transaction;
            cleanup.CommandText = $"DELETE FROM {table} WHERE username = @username;";
            cleanup.Parameters.AddWithValue("@username", user.Username);
            cleanup.ExecuteNonQuery();
        }

        // Finally remove the account row itself — a real DELETE, not a soft delete.
        // Also purges any leftover soft-deleted rows of the same username so the DB
        // never accumulates stale duplicates.
        using (var deleteUser = connection.CreateCommand())
        {
            deleteUser.Transaction = transaction;
            deleteUser.CommandText = "DELETE FROM app_users WHERE id = @id OR username = @username;";
            deleteUser.Parameters.AddWithValue("@id", userId.ToString("D"));
            deleteUser.Parameters.AddWithValue("@username", user.Username);
            deleteUser.ExecuteNonQuery();
        }

        transaction.Commit();

        RecordAudit("Xóa tài khoản", "User", user.Username, $"Admin xóa vĩnh viễn tài khoản User: đã xóa toàn bộ hội thoại/tin nhắn/file chat, dữ liệu tăng ca, yêu cầu đổi mật khẩu, phiên đăng nhập và {deletedCodes} mã kích hoạt liên quan.");
    }

    public IReadOnlyList<AuditLogEntry> GetAuditLogs(int take = 500)
    {
        EnsureCurrentAdmin();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@take) id, occurred_at, username, action, entity, entity_name, details
            FROM audit_logs
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 5000));
        using var reader = command.ExecuteReader();
        var logs = new List<AuditLogEntry>();
        while (reader.Read())
        {
            logs.Add(new AuditLogEntry
            {
                Id = GetInt64(reader, "id"),
                OccurredAt = ParseDateTime(GetString(reader, "occurred_at")),
                Username = GetString(reader, "username"),
                Action = GetString(reader, "action"),
                Entity = GetString(reader, "entity"),
                EntityName = GetString(reader, "entity_name"),
                Details = GetString(reader, "details")
            });
        }

        return logs;
    }

    public Customer AddOrUpdateCustomer(string name, string taxCode, string phone, string address, string note, string customerInputName = "")
    {
        EnsureCurrentUserActive();

        var typedName = customerInputName.Trim();
        if (string.IsNullOrWhiteSpace(typedName))
        {
            typedName = name.Trim();
        }

        name = ResolveCompanyName(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tên KH không được trống.");
        }

        var customer = Data.Customers.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));

        var created = customer is null;
        if (customer is null)
        {
            customer = new Customer { Name = name };
            Data.Customers.Add(customer);
        }

        customer.TaxCode = taxCode.Trim();
        customer.Phone = phone.Trim();
        customer.Address = address.Trim();
        customer.Note = note.Trim();
        customer.IsActive = true;
        RememberCustomerInputAlias(customer, typedName);
        Save();
        RecordAudit(created ? "Thêm KH" : "Sửa KH", "Customer", customer.Name, $"MST: {customer.TaxCode}; Điện thoại: {customer.Phone}");
        return customer;
    }

    public Customer UpdateCustomer(Guid customerId, string name, string taxCode, string phone, string address, string note, string customerInputName = "")
    {
        EnsureCurrentUserActive();

        var typedName = customerInputName.Trim();
        if (string.IsNullOrWhiteSpace(typedName))
        {
            typedName = name.Trim();
        }

        name = ResolveCompanyName(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tên KH không được trống.");
        }

        var customer = Data.Customers.FirstOrDefault(item => item.Id == customerId);
        if (customer is null)
        {
            throw new InvalidOperationException("Không tìm thấy khách hàng cần sửa.");
        }

        var duplicate = Data.Customers.FirstOrDefault(item =>
            item.Id != customerId &&
            string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (duplicate is not null)
        {
            throw new InvalidOperationException("Tên KH này đã tồn tại.");
        }

        var oldName = customer.Name;
        customer.Name = name;
        customer.TaxCode = taxCode.Trim();
        customer.Phone = phone.Trim();
        customer.Address = address.Trim();
        customer.Note = note.Trim();
        customer.IsActive = true;

        foreach (var document in Data.Documents.Where(item => item.CustomerId == customerId || string.Equals(item.CustomerName, oldName, StringComparison.CurrentCultureIgnoreCase)))
        {
            document.CustomerId = customerId;
            document.CustomerName = name;
        }

        foreach (var payment in Data.Payments.Where(item => item.CustomerId == customerId || string.Equals(item.CustomerName, oldName, StringComparison.CurrentCultureIgnoreCase)))
        {
            payment.CustomerId = customerId;
            payment.CustomerName = name;
        }

        foreach (var alias in Data.CustomerAliases.Where(item => item.CustomerId == customerId || string.Equals(item.CustomerName, oldName, StringComparison.CurrentCultureIgnoreCase)))
        {
            alias.CustomerId = customerId;
            alias.CustomerName = name;
        }

        RememberCustomerInputAlias(customer, typedName);
        Save();
        RecordAudit("Sửa KH", "Customer", customer.Name, $"Đổi từ: {oldName}; MST: {customer.TaxCode}; Điện thoại: {customer.Phone}");
        return customer;
    }

    public void DeleteCustomer(Guid customerId)
    {
        EnsureCurrentUserActive();

        var customer = Data.Customers.FirstOrDefault(item => item.Id == customerId);
        if (customer is null)
        {
            throw new InvalidOperationException("Không tìm thấy khách hàng cần xóa.");
        }

        customer.IsActive = false;
        Data.CustomerAliases.RemoveAll(alias => alias.CustomerId == customerId || string.Equals(alias.CustomerName, customer.Name, StringComparison.CurrentCultureIgnoreCase));
        Save();
        RecordAudit("Xóa KH", "Customer", customer.Name, "Ẩn khách hàng khỏi danh sách, giao dịch cũ vẫn giữ.");
    }

    public IReadOnlyList<CustomerAlias> AliasesForCustomer(string customerName)
    {
        var resolvedName = ResolveCompanyName(customerName);
        return Data.CustomerAliases
            .Where(alias => string.Equals(alias.CustomerName, resolvedName, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(alias => alias.Alias, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public CustomerAlias AddCustomerAlias(string customerName, string aliasText)
    {
        EnsureCurrentUserActive();

        var customer = EnsureCustomer(customerName);
        aliasText = aliasText.Trim();
        if (string.IsNullOrWhiteSpace(aliasText))
        {
            throw new InvalidOperationException("Bí danh không được trống.");
        }

        var resolvedAlias = ResolveCompanyName(aliasText);
        if (!string.Equals(resolvedAlias, aliasText, StringComparison.CurrentCultureIgnoreCase) &&
            !string.Equals(resolvedAlias, customer.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            throw new InvalidOperationException($"Bí danh này đang thuộc KH: {resolvedAlias}.");
        }

        if (string.Equals(resolvedAlias, customer.Name, StringComparison.CurrentCultureIgnoreCase) &&
            string.Equals(aliasText, customer.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            throw new InvalidOperationException("Bí danh đang trùng với Tên KH chuẩn.");
        }

        var existing = Data.CustomerAliases.FirstOrDefault(item =>
            string.Equals(item.CustomerName, customer.Name, StringComparison.CurrentCultureIgnoreCase) &&
            string.Equals(item.Alias, aliasText, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var alias = new CustomerAlias
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            Alias = aliasText
        };
        Data.CustomerAliases.Add(alias);
        Save();
        RecordAudit("Thêm bí danh", "CustomerAlias", customer.Name, $"Bí danh: {aliasText}");
        return alias;
    }

    public void DeleteCustomerAlias(Guid aliasId)
    {
        EnsureCurrentUserActive();

        var alias = Data.CustomerAliases.FirstOrDefault(item => item.Id == aliasId);
        if (alias is null)
        {
            throw new InvalidOperationException("Không tìm thấy bí danh cần xóa.");
        }

        Data.CustomerAliases.Remove(alias);
        Save();
        RecordAudit("Xóa bí danh", "CustomerAlias", alias.CustomerName, $"Bí danh: {alias.Alias}");
    }

    public void AddDocument(string voucherNo, DateOnly date, string customerName, string content, string note, List<DocumentLine> lines, string customerInputName = "")
    {
        EnsureCurrentUserActive();

        if (string.IsNullOrWhiteSpace(voucherNo))
        {
            throw new InvalidOperationException("Số phiếu không được trống.");
        }

        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Chứng từ cần ít nhất một dòng hàng.");
        }

        var typedName = CustomerInputNameForTrace(customerInputName, customerName);
        var customer = EnsureCustomer(customerName);
        RememberCustomerInputAlias(customer, typedName);
        var document = new Document
        {
            VoucherNo = voucherNo.Trim(),
            Date = date,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerInputName = TraceCustomerInput(typedName, customer.Name),
            Content = content.Trim(),
            Note = note.Trim(),
            Lines = lines
        };
        Data.Documents.Add(document);
        Save();
        RecordAudit("Nhập chứng từ", "Document", document.VoucherNo, $"KH: {document.CustomerName}; Nội dung: {document.Content}; Tổng: {ToDatabaseDecimal(document.Total)}");
    }

    public void AddPayment(string customerName, DateOnly date, string content, string method, string account, decimal amount, string note, string customerInputName = "")
    {
        EnsureCurrentUserActive();

        if (amount <= 0)
        {
            throw new InvalidOperationException("Số tiền phải lớn hơn 0.");
        }

        var typedName = CustomerInputNameForTrace(customerInputName, customerName);
        var customer = EnsureCustomer(customerName);
        RememberCustomerInputAlias(customer, typedName);
        var payment = new Payment
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerInputName = TraceCustomerInput(typedName, customer.Name),
            Date = date,
            Content = content.Trim(),
            Method = method.Trim(),
            Account = account.Trim(),
            Amount = Math.Abs(amount),
            Note = note.Trim()
        };
        Data.Payments.Add(payment);
        Save();
        RecordAudit("Nhập thanh toán", "Payment", payment.CustomerName, $"Nội dung: {payment.Content}; Số tiền: {ToDatabaseDecimal(payment.Amount)}");
    }

    public void UpdatePayment(Guid paymentId, string customerName, DateOnly date, string content, string method, string account, decimal amount, string note, string customerInputName = "")
    {
        EnsureCurrentUserActive();

        if (amount <= 0)
        {
            throw new InvalidOperationException("Số tiền phải lớn hơn 0.");
        }

        var payment = Data.Payments.FirstOrDefault(item => item.Id == paymentId);
        if (payment is null)
        {
            throw new InvalidOperationException("Không tìm thấy dòng thanh toán cần sửa.");
        }

        var typedName = CustomerInputNameForTrace(customerInputName, customerName);
        var customer = EnsureCustomer(customerName);
        RememberCustomerInputAlias(customer, typedName);
        payment.CustomerId = customer.Id;
        payment.CustomerName = customer.Name;
        payment.CustomerInputName = TraceCustomerInput(typedName, customer.Name);
        payment.Date = date;
        payment.Content = content.Trim();
        payment.Method = method.Trim();
        payment.Account = account.Trim();
        payment.Amount = Math.Abs(amount);
        payment.Note = note.Trim();
        Save();
        RecordAudit("Sửa thanh toán", "Payment", payment.CustomerName, $"Nội dung: {payment.Content}; Số tiền: {ToDatabaseDecimal(payment.Amount)}");
    }

    public void DeletePayment(Guid paymentId)
    {
        EnsureCurrentUserActive();

        var payment = Data.Payments.FirstOrDefault(item => item.Id == paymentId);
        if (payment is null)
        {
            throw new InvalidOperationException("Không tìm thấy dòng thanh toán cần xóa.");
        }

        Data.Payments.Remove(payment);
        Save();
        RecordAudit("Xóa thanh toán", "Payment", payment.CustomerName, $"Ngày: {payment.Date:yyyy-MM-dd}; Số tiền: {ToDatabaseDecimal(payment.Amount)}");
    }

    public ExportPayload BuildExportPayload()
    {
        EnsureCurrentUserActive();

        NormalizeReferences();
        return new ExportPayload
        {
            GeneratedAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
            Customers = ActiveCustomers()
                .Select(customer => new ExportCustomer
                {
                    Name = customer.Name,
                    TaxCode = customer.TaxCode,
                    Phone = customer.Phone,
                    Address = customer.Address,
                    Note = customer.Note
                })
                .ToList(),
            Documents = Data.Documents
                .OrderBy(document => document.Date)
                .ThenBy(document => document.CreatedAt)
                .Select(document => new ExportDocument
                {
                    VoucherNo = document.VoucherNo,
                    Date = document.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Customer = document.CustomerName,
                    CustomerInputName = document.CustomerInputName,
                    Content = document.Content,
                    Note = document.Note,
                    Lines = document.Lines.Select(line => new ExportDocumentLine
                    {
                        LineContent = line.LineContent,
                        Category = line.Category,
                        Spec = line.Spec,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice,
                        Note = line.Note
                    }).ToList()
                })
                .ToList(),
            Payments = Data.Payments
                .OrderBy(payment => payment.Date)
                .ThenBy(payment => payment.CreatedAt)
                .Select(payment => new ExportPayment
                {
                    Date = payment.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Customer = payment.CustomerName,
                    CustomerInputName = payment.CustomerInputName,
                    Content = payment.Content,
                    Method = payment.Method,
                    Account = payment.Account,
                    Amount = Math.Abs(payment.Amount),
                    Note = payment.Note
                })
                .ToList()
        };
    }

    public void SaveExportPayload(string path)
    {
        EnsureCurrentUserActive();

        var payload = BuildExportPayload();
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, options), Encoding.UTF8);
    }

    public IReadOnlyList<ChatMessage> GetChatMessages(string peerUsername, int take = 200)
    {
        if (CurrentUser is null)
        {
            return [];
        }

        peerUsername = NormalizeUsername(peerUsername);
        if (string.IsNullOrWhiteSpace(peerUsername))
        {
            return [];
        }

        var peer = FindUserByUsername(peerUsername);
        if (peer is null || peer.IsDeleted)
        {
            return [];
        }

        EnsureChatKeyForUser(CurrentUser);
        var conversation = new EmbeddedChatConversation
        {
            UserId = peer.Id,
            DeviceId = peer.Id.ToString("D"),
            Username = peer.Username,
            Name = peer.DisplayName,
            PublicKey = peer.PublicKey,
            AvatarText = peer.DisplayName
        };

        return new SecureChatRepository(_connectionString)
            .GetMessages(CurrentUser, conversation, take)
            .Select(message => new ChatMessage
            {
                Id = message.Id,
                SenderId = message.SenderUserId,
                ReceiverId = message.IsMine ? peer.Id : CurrentUser.Id,
                SenderUsername = message.IsMine ? CurrentUser.Username : peer.Username,
                ReceiverUsername = message.IsMine ? peer.Username : CurrentUser.Username,
                MessageType = message.Kind == EmbeddedChatMessageKind.File ? "File" : "Text",
                Text = message.Text,
                Status = message.DeliveryStatus.ToString(),
                CreatedAt = message.SentAt,
                FileName = message.Attachment?.FileName ?? "",
                FileSize = message.Attachment?.SizeBytes ?? 0
            })
            .ToList();
    }

    public Guid AddChatTextMessage(string receiverUsername, string plainText)
    {
        EnsureCurrentUserActive();
        if (CurrentUser is null)
        {
            throw new InvalidOperationException("Bạn cần đăng nhập trước khi gửi tin nhắn.");
        }

        receiverUsername = NormalizeUsername(receiverUsername);
        plainText = plainText.Trim();
        if (string.IsNullOrWhiteSpace(receiverUsername))
        {
            throw new InvalidOperationException("Chọn người nhận trước khi gửi tin nhắn.");
        }

        if (string.Equals(receiverUsername, CurrentUser.Username, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Không thể gửi tin nhắn cho chính tài khoản đang đăng nhập.");
        }

        if (string.IsNullOrWhiteSpace(plainText))
        {
            throw new InvalidOperationException("Nội dung tin nhắn không được trống.");
        }

        var messageId = Guid.NewGuid();
        var receiver = FindUserByUsername(receiverUsername) ?? throw new InvalidOperationException("Không tìm thấy người nhận đang hoạt động.");
        EnsureChatKeyForUser(CurrentUser);
        new SecureChatRepository(_connectionString).SaveTextMessage(CurrentUser, receiver.Id, receiver.Username, receiver.DisplayName, receiver.PublicKey, messageId, plainText, DateTime.Now);
        RecordAudit("Gửi tin nhắn LAN", "Chat", receiverUsername, "Đã gửi một tin nhắn được mã hóa.");
        return messageId;
    }

    public Guid AddChatFileOffer(string receiverUsername, string fileName, long fileSize, string senderIp, int senderPort, string transferToken)
    {
        EnsureCurrentUserActive();
        if (CurrentUser is null)
        {
            throw new InvalidOperationException("Bạn cần đăng nhập trước khi gửi file.");
        }

        receiverUsername = NormalizeUsername(receiverUsername);
        if (string.IsNullOrWhiteSpace(receiverUsername))
        {
            throw new InvalidOperationException("Chọn người nhận trước khi gửi file.");
        }

        if (senderPort <= 0 || string.IsNullOrWhiteSpace(senderIp) || string.IsNullOrWhiteSpace(transferToken))
        {
            throw new InvalidOperationException("Chưa có thông tin kết nối LAN để gửi file.");
        }

        var now = DateTime.Now;
        var conversationId = GetOrCreateChatConversationId(CurrentUser.Username, receiverUsername);
        var messageId = Guid.NewGuid();
        var offerId = Guid.NewGuid();
        var encryptedNotice = ChatCrypto.EncryptForPair(CurrentUser.Username, receiverUsername, "FILE");
        var encryptedFileName = ChatCrypto.EncryptForPair(CurrentUser.Username, receiverUsername, Path.GetFileName(fileName));
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var messageCommand = connection.CreateCommand())
        {
            messageCommand.Transaction = transaction;
            messageCommand.CommandText = """
                INSERT INTO chat_messages
                    (id, conversation_id, sender_username, receiver_username, message_type, cipher_text, nonce, created_at, status)
                VALUES
                    (@id, @conversationId, @sender, @receiver, N'FileOffer', @cipherText, @nonce, @createdAt, N'Pending');
                """;
            messageCommand.Parameters.AddWithValue("@id", messageId.ToString("D"));
            messageCommand.Parameters.AddWithValue("@conversationId", conversationId.ToString("D"));
            messageCommand.Parameters.AddWithValue("@sender", CurrentUser.Username);
            messageCommand.Parameters.AddWithValue("@receiver", receiverUsername);
            messageCommand.Parameters.AddWithValue("@cipherText", encryptedNotice.CipherText);
            messageCommand.Parameters.AddWithValue("@nonce", encryptedNotice.Nonce);
            messageCommand.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(now));
            messageCommand.ExecuteNonQuery();
        }

        using (var offerCommand = connection.CreateCommand())
        {
            offerCommand.Transaction = transaction;
            offerCommand.CommandText = """
                INSERT INTO chat_file_offers
                    (id, message_id, conversation_id, sender_username, receiver_username, encrypted_file_name, file_name_nonce, file_size, sender_ip, sender_port, transfer_token, status, created_at, expires_at)
                VALUES
                    (@id, @messageId, @conversationId, @sender, @receiver, @encryptedFileName, @fileNameNonce, @fileSize, @senderIp, @senderPort, @transferToken, N'Pending', @createdAt, @expiresAt);
                """;
            offerCommand.Parameters.AddWithValue("@id", offerId.ToString("D"));
            offerCommand.Parameters.AddWithValue("@messageId", messageId.ToString("D"));
            offerCommand.Parameters.AddWithValue("@conversationId", conversationId.ToString("D"));
            offerCommand.Parameters.AddWithValue("@sender", CurrentUser.Username);
            offerCommand.Parameters.AddWithValue("@receiver", receiverUsername);
            offerCommand.Parameters.AddWithValue("@encryptedFileName", encryptedFileName.CipherText);
            offerCommand.Parameters.AddWithValue("@fileNameNonce", encryptedFileName.Nonce);
            offerCommand.Parameters.AddWithValue("@fileSize", fileSize);
            offerCommand.Parameters.AddWithValue("@senderIp", senderIp.Trim());
            offerCommand.Parameters.AddWithValue("@senderPort", senderPort);
            offerCommand.Parameters.AddWithValue("@transferToken", transferToken.Trim());
            offerCommand.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(now));
            offerCommand.Parameters.AddWithValue("@expiresAt", ToDatabaseDateTime(now.AddMinutes(5)));
            offerCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        RecordAudit("Gửi lời mời file LAN", "Chat", receiverUsername, $"Kích thước: {fileSize} byte; hết hạn sau 5 phút.");
        return offerId;
    }

    public void AcceptChatFileOffer(Guid offerId)
    {
        UpdateChatFileOfferStatus(offerId, "Accepted", "accepted_at", requirePending: true);
    }

    public void CompleteChatFileOffer(Guid offerId)
    {
        UpdateChatFileOfferStatus(offerId, "Completed", "completed_at", requirePending: false);
    }

    public void FailChatFileOffer(Guid offerId)
    {
        UpdateChatFileOfferStatus(offerId, "Failed", null, requirePending: false);
    }

    public void ExpireOldChatFileOffers()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE chat_file_offers
            SET status = N'Expired'
            WHERE status = N'Pending'
              AND expires_at <= @now;

            UPDATE m
            SET status = f.status
            FROM chat_messages m
            INNER JOIN chat_file_offers f ON f.message_id = m.id
            WHERE f.status IN (N'Expired', N'Accepted', N'Completed', N'Failed');
            """;
        command.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
        command.ExecuteNonQuery();
    }

    public void RecordAudit(string action, string entity, string entityName, string details)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO audit_logs
                (occurred_at, user_id, username, action, entity, entity_name, details)
            VALUES
                (@occurredAt, @userId, @username, @action, @entity, @entityName, @details);
            """;
        command.Parameters.AddWithValue("@occurredAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@userId", CurrentUser is null ? DBNull.Value : CurrentUser.Id.ToString("D"));
        command.Parameters.AddWithValue("@username", CurrentUser?.Username ?? "system");
        command.Parameters.AddWithValue("@action", TextUtil.RepairMojibake(action.Trim()));
        command.Parameters.AddWithValue("@entity", TextUtil.RepairMojibake(entity.Trim()));
        command.Parameters.AddWithValue("@entityName", TextUtil.RepairMojibake(entityName.Trim()));
        command.Parameters.AddWithValue("@details", TextUtil.RepairMojibake(details.Trim()));
        command.ExecuteNonQuery();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CẬP NHẬT PHIÊN BẢN (releases + cấu hình chặn đăng nhập)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Kiểm tra phiên bản khi mở app. An toàn gọi trước khi đăng nhập.</summary>
    public VersionCheckResult CheckVersion()
    {
        var result = new VersionCheckResult { CurrentVersion = AppVersion.CurrentText };

        var published = ReadPublishedReleases();
        if (published.Count == 0)
        {
            return result;
        }

        var latest = published
            .OrderByDescending(release => AppVersion.Parse(release.Version))
            .First();
        result.Latest = latest;
        result.UpdateAvailable = AppVersion.IsValid(latest.Version) && AppVersion.CurrentIsOlderThan(latest.Version);

        if (IsUpdateEnforcementEnabled())
        {
            var mandatory = published
                .Where(release => release.IsMandatory && AppVersion.IsValid(release.Version))
                .Select(release => release.Version)
                .OrderByDescending(AppVersion.Parse)
                .FirstOrDefault();

            if (mandatory is not null && AppVersion.CurrentIsOlderThan(mandatory))
            {
                result.MustBlock = true;
            }
        }

        return result;
    }

    /// <summary>Bản phát hành mới nhất (theo số phiên bản) đang được công bố. Null nếu chưa có.</summary>
    public AppRelease? GetLatestPublishedRelease()
    {
        return ReadPublishedReleases()
            .OrderByDescending(release => AppVersion.Parse(release.Version))
            .FirstOrDefault();
    }

    /// <summary>Công tắc chặn đăng nhập khi bản quá cũ (do admin bật/tắt).</summary>
    public bool IsUpdateEnforcementEnabled()
    {
        return string.Equals(GetSetting("update.enforce_block"), "1", StringComparison.Ordinal);
    }

    /// <summary>Admin bật/tắt chế độ chặn đăng nhập với bản cũ.</summary>
    public void SetUpdateEnforcementEnabled(bool enabled)
    {
        EnsureCurrentAdmin();
        SetSetting("update.enforce_block", enabled ? "1" : "0");
        RecordAudit(
            enabled ? "Bật chặn đăng nhập bản cũ" : "Tắt chặn đăng nhập bản cũ",
            "AppUpdate",
            "update.enforce_block",
            enabled ? "Yêu cầu cập nhật bắt buộc" : "Không bắt buộc cập nhật");
    }

    /// <summary>Toàn bộ lịch sử phát hành (admin xem/quản lý).</summary>
    public IReadOnlyList<AppRelease> GetReleaseHistory(int take = 100)
    {
        EnsureCurrentAdmin();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@take)
                id, version, release_notes, setup_path, setup_file_name, file_size,
                CASE WHEN setup_file IS NULL THEN 0 ELSE 1 END AS has_file,
                is_mandatory, is_published, published_at, published_by
            FROM app_releases
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("@take", Math.Clamp(take, 1, 1000));
        using var reader = command.ExecuteReader();
        var releases = new List<AppRelease>();
        while (reader.Read())
        {
            releases.Add(ReadRelease(reader));
        }

        return releases;
    }

    /// <summary>Admin công bố một bản phát hành mới. Lưu UNC và/hoặc file nhúng DB.</summary>
    public AppRelease PublishRelease(string version, string releaseNotes, string setupPath, bool isMandatory, byte[]? setupFile, string setupFileName)
    {
        EnsureCurrentAdmin();

        version = (version ?? "").Trim();
        if (!AppVersion.IsValid(version))
        {
            throw new InvalidOperationException("Số phiên bản không hợp lệ (vd: 1.2.0).");
        }

        setupPath = (setupPath ?? "").Trim();
        setupFileName = (setupFileName ?? "").Trim();
        var hasEmbedded = setupFile is { Length: > 0 };
        if (string.IsNullOrWhiteSpace(setupPath) && !hasEmbedded)
        {
            throw new InvalidOperationException("Phải nhập đường dẫn LAN (UNC) hoặc chọn file setup để nhúng.");
        }

        if (hasEmbedded && string.IsNullOrWhiteSpace(setupFileName))
        {
            setupFileName = $"KetoanMiniSetup_{version}.exe";
        }

        long fileSize = hasEmbedded ? setupFile!.Length : 0;
        if (!hasEmbedded && !string.IsNullOrWhiteSpace(setupPath))
        {
            try
            {
                if (File.Exists(setupPath))
                {
                    fileSize = new FileInfo(setupPath).Length;
                    if (string.IsNullOrWhiteSpace(setupFileName))
                    {
                        setupFileName = Path.GetFileName(setupPath);
                    }
                }
            }
            catch
            {
                // Bỏ qua: không truy cập được UNC lúc công bố vẫn cho lưu (client sẽ thử lại khi tải).
            }
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_releases
                (version, release_notes, setup_path, setup_file_name, setup_file, file_size, is_mandatory, is_published, published_at, published_by, created_at)
            OUTPUT INSERTED.id
            VALUES
                (@version, @notes, @setupPath, @fileName, @file, @fileSize, @mandatory, 1, @publishedAt, @publishedBy, @createdAt);
            """;
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@notes", TextUtil.RepairMojibake((releaseNotes ?? "").Trim()));
        command.Parameters.AddWithValue("@setupPath", setupPath);
        command.Parameters.AddWithValue("@fileName", setupFileName);
        command.Parameters.AddWithValue("@file", (object?)setupFile ?? DBNull.Value);
        command.Parameters.AddWithValue("@fileSize", fileSize);
        command.Parameters.AddWithValue("@mandatory", isMandatory ? 1 : 0);
        command.Parameters.AddWithValue("@publishedAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@publishedBy", CurrentUser?.Username ?? "admin");
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(DateTime.Now));
        var newId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        RecordAudit("Phát hành phiên bản", "AppUpdate", version,
            $"Bắt buộc: {(isMandatory ? "Có" : "Không")}; Nguồn: {(hasEmbedded ? "File nhúng DB" : "UNC")} {setupPath}".Trim());

        return new AppRelease
        {
            Id = newId,
            Version = version,
            ReleaseNotes = (releaseNotes ?? "").Trim(),
            SetupPath = setupPath,
            SetupFileName = setupFileName,
            FileSize = fileSize,
            HasEmbeddedFile = hasEmbedded,
            IsMandatory = isMandatory,
            IsPublished = true,
            PublishedAt = DateTime.Now,
            PublishedBy = CurrentUser?.Username ?? "admin"
        };
    }

    /// <summary>Admin xóa một bản phát hành khỏi lịch sử.</summary>
    public void DeleteRelease(long releaseId)
    {
        EnsureCurrentAdmin();
        string version;
        using (var connection = OpenConnection())
        {
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT version FROM app_releases WHERE id = @id;";
                read.Parameters.AddWithValue("@id", releaseId);
                version = Convert.ToString(read.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
            }

            if (string.IsNullOrEmpty(version))
            {
                throw new InvalidOperationException("Không tìm thấy bản phát hành cần xóa.");
            }

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app_releases WHERE id = @id;";
            command.Parameters.AddWithValue("@id", releaseId);
            command.ExecuteNonQuery();
        }

        RecordAudit("Xóa phiên bản", "AppUpdate", version, "Xóa khỏi lịch sử cập nhật");
    }

    /// <summary>Đọc file setup nhúng trong DB (dùng khi không có UNC). An toàn gọi trước khi đăng nhập.</summary>
    public byte[]? GetReleaseSetupFile(long releaseId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT setup_file FROM app_releases WHERE id = @id;";
        command.Parameters.AddWithValue("@id", releaseId);
        var value = command.ExecuteScalar();
        return value is byte[] bytes && bytes.Length > 0 ? bytes : null;
    }

    private List<AppRelease> ReadPublishedReleases()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id, version, release_notes, setup_path, setup_file_name, file_size,
                CASE WHEN setup_file IS NULL THEN 0 ELSE 1 END AS has_file,
                is_mandatory, is_published, published_at, published_by
            FROM app_releases
            WHERE is_published = 1;
            """;
        using var reader = command.ExecuteReader();
        var releases = new List<AppRelease>();
        while (reader.Read())
        {
            releases.Add(ReadRelease(reader));
        }

        return releases;
    }

    private static AppRelease ReadRelease(SqlDataReader reader)
    {
        return new AppRelease
        {
            Id = GetInt64(reader, "id"),
            Version = GetString(reader, "version"),
            ReleaseNotes = GetString(reader, "release_notes"),
            SetupPath = GetString(reader, "setup_path"),
            SetupFileName = GetString(reader, "setup_file_name"),
            FileSize = GetInt64(reader, "file_size"),
            HasEmbeddedFile = GetInt64(reader, "has_file") != 0,
            IsMandatory = GetInt64(reader, "is_mandatory") != 0,
            IsPublished = GetInt64(reader, "is_published") != 0,
            PublishedAt = ParseDateTime(GetString(reader, "published_at")),
            PublishedBy = GetString(reader, "published_by")
        };
    }

    private string GetSetting(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key = @key;";
        command.Parameters.AddWithValue("@key", key);
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }

    private void SetSetting(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE app_settings
            SET setting_value = @value, updated_at = @updatedAt, updated_by = @updatedBy
            WHERE setting_key = @key;
            IF @@ROWCOUNT = 0
                INSERT INTO app_settings (setting_key, setting_value, updated_at, updated_by)
                VALUES (@key, @value, @updatedAt, @updatedBy);
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@updatedAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@updatedBy", CurrentUser?.Username ?? "system");
        command.ExecuteNonQuery();
    }

    private Guid GetOrCreateChatConversationId(string username1, string username2)
    {
        var pair = ChatPair(username1, username2);
        var existing = FindChatConversationId(pair.UserA, pair.UserB);
        if (existing is Guid id)
        {
            return id;
        }

        var conversationId = Guid.NewGuid();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_conversations
                (id, user_a, user_b, created_at)
            VALUES
                (@id, @userA, @userB, @createdAt);
            """;
        command.Parameters.AddWithValue("@id", conversationId.ToString("D"));
        command.Parameters.AddWithValue("@userA", pair.UserA);
        command.Parameters.AddWithValue("@userB", pair.UserB);
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(DateTime.Now));
        try
        {
            command.ExecuteNonQuery();
            return conversationId;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return FindChatConversationId(pair.UserA, pair.UserB) ?? conversationId;
        }
    }

    private Guid? FindChatConversationId(string username1, string username2)
    {
        var pair = ChatPair(username1, username2);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id
            FROM chat_conversations
            WHERE user_a = @userA
              AND user_b = @userB;
            """;
        command.Parameters.AddWithValue("@userA", pair.UserA);
        command.Parameters.AddWithValue("@userB", pair.UserB);
        var value = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private void UpdateChatFileOfferStatus(Guid offerId, string status, string? dateColumn, bool requirePending)
    {
        EnsureCurrentUserActive();
        if (CurrentUser is null)
        {
            throw new InvalidOperationException("Bạn cần đăng nhập trước.");
        }

        var columnSet = string.IsNullOrWhiteSpace(dateColumn) ? "" : $", {dateColumn} = @now";
        var pendingFilter = requirePending ? "AND status = N'Pending' AND expires_at > @now" : "";
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        Guid? messageId = null;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT TOP (1) message_id
                FROM chat_file_offers
                WHERE id = @id
                  AND (sender_username = @username OR receiver_username = @username);
                """;
            read.Parameters.AddWithValue("@id", offerId.ToString("D"));
            read.Parameters.AddWithValue("@username", CurrentUser.Username);
            var value = Convert.ToString(read.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (Guid.TryParse(value, out var parsed))
            {
                messageId = parsed;
            }
        }

        if (messageId is null)
        {
            throw new InvalidOperationException("Không tìm thấy lời mời nhận file.");
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE chat_file_offers
                SET status = @status{columnSet}
                WHERE id = @id
                  AND (sender_username = @username OR receiver_username = @username)
                  {pendingFilter};
                """;
            command.Parameters.AddWithValue("@id", offerId.ToString("D"));
            command.Parameters.AddWithValue("@username", CurrentUser.Username);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
            if (command.ExecuteNonQuery() == 0)
            {
                throw new InvalidOperationException("Lời mời nhận file đã hết hạn hoặc đã được xử lý.");
            }
        }

        using (var message = connection.CreateCommand())
        {
            message.Transaction = transaction;
            message.CommandText = """
                UPDATE chat_messages
                SET status = @status
                WHERE id = @messageId;
                """;
            message.Parameters.AddWithValue("@messageId", messageId.Value.ToString("D"));
            message.Parameters.AddWithValue("@status", status);
            message.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private ChatMessage ReadChatMessage(SqlDataReader reader)
    {
        var sender = GetString(reader, "sender_username");
        var receiver = GetString(reader, "receiver_username");
        var type = GetString(reader, "message_type");
        var text = "";
        if (string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                text = ChatCrypto.DecryptForPair(sender, receiver, GetString(reader, "cipher_text"), GetString(reader, "nonce"));
            }
            catch
            {
                text = "[Không giải mã được]";
            }
        }

        var offerIdText = GetString(reader, "offer_id");
        Guid? offerId = Guid.TryParse(offerIdText, out var parsedOfferId) ? parsedOfferId : null;
        var fileName = "";
        if (offerId is not null)
        {
            try
            {
                fileName = ChatCrypto.DecryptForPair(sender, receiver, GetString(reader, "encrypted_file_name"), GetString(reader, "file_name_nonce"));
            }
            catch
            {
                fileName = "[Không giải mã được tên file]";
            }
        }

        var expiresAt = GetString(reader, "expires_at");
        return new ChatMessage
        {
            Id = GetGuid(reader, "id"),
            ConversationId = GetGuid(reader, "conversation_id"),
            SenderUsername = sender,
            ReceiverUsername = receiver,
            MessageType = type,
            Text = text,
            Status = FirstNotBlank(GetString(reader, "status"), "Sent"),
            CreatedAt = ParseDateTime(GetString(reader, "created_at")),
            FileOfferId = offerId,
            FileName = fileName,
            FileSize = long.TryParse(GetString(reader, "file_size"), NumberStyles.Any, CultureInfo.InvariantCulture, out var fileSize) ? fileSize : 0,
            SenderAddress = GetString(reader, "sender_ip"),
            SenderPort = int.TryParse(GetString(reader, "sender_port"), NumberStyles.Any, CultureInfo.InvariantCulture, out var senderPort) ? senderPort : 0,
            TransferToken = GetString(reader, "transfer_token"),
            ExpiresAt = string.IsNullOrWhiteSpace(expiresAt) ? null : ParseDateTime(expiresAt)
        };
    }

    private static (string UserA, string UserB) ChatPair(string username1, string username2)
    {
        var a = NormalizeUsername(username1);
        var b = NormalizeUsername(username2);
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }

    private AppUser InsertUser(
        string username,
        string fullName,
        string password,
        string role,
        bool isActive,
        string approvalStatus = "Approved",
        string approvedBy = "",
        string activationCode = "")
    {
        username = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Tên đăng nhập không được trống.");
        }

        if (username.Contains(' ', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Tên đăng nhập không được chứa khoảng trắng.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Mật khẩu không được trống.");
        }

        EnsureUsernameAvailable(username, null);

        var user = new AppUser
        {
            Username = username,
            FullName = fullName.Trim(),
            Role = role,
            IsActive = isActive,
            ApprovalStatus = approvalStatus,
            ApprovedAt = string.Equals(approvalStatus, "Approved", StringComparison.OrdinalIgnoreCase) ? DateTime.Now : null,
            ApprovedBy = approvedBy,
            ActivationCode = activationCode
        };

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_users
                (id, username, full_name, role, password_hash, is_active, approval_status, approved_at, approved_by, activation_code, created_at)
            VALUES
                (@id, @username, @fullName, @role, @passwordHash, @isActive, @approvalStatus, @approvedAt, @approvedBy, @activationCode, @createdAt);
            """;
        command.Parameters.AddWithValue("@id", user.Id.ToString("D"));
        command.Parameters.AddWithValue("@username", user.Username);
        command.Parameters.AddWithValue("@fullName", user.FullName);
        command.Parameters.AddWithValue("@role", user.Role);
        command.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("@isActive", user.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@approvalStatus", user.ApprovalStatus);
        command.Parameters.AddWithValue("@approvedAt", user.ApprovedAt is null ? DBNull.Value : ToDatabaseDateTime(user.ApprovedAt.Value));
        command.Parameters.AddWithValue("@approvedBy", user.ApprovedBy);
        command.Parameters.AddWithValue("@activationCode", user.ActivationCode);
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(user.CreatedAt));
        command.ExecuteNonQuery();
        return user;
    }

    private void EnsureUsernameAvailable(string username, Guid? exceptUserId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id
            FROM app_users
            WHERE username = @username
              AND is_deleted = 0;
            """;
        command.Parameters.AddWithValue("@username", username);
        var existingId = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(existingId))
        {
            return;
        }

        if (exceptUserId is Guid id && string.Equals(existingId, id.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException("Tên đăng nhập này đã tồn tại.");
    }

    private AppUser? FindUserById(Guid userId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id, username, full_name, role, is_active, approval_status, approved_at, approved_by, activation_code, public_key, is_deleted, deleted_at, password_hash, created_at
            FROM app_users
            WHERE id = @id
              AND is_deleted = 0
            """;
        command.Parameters.AddWithValue("@id", userId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadUser(reader) : null;
    }

    private AppUser? FindUserByUsername(string username)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) id, username, full_name, role, is_active, approval_status, approved_at, approved_by, activation_code, public_key, is_deleted, deleted_at, password_hash, created_at
            FROM app_users
            WHERE username = @username
              AND is_deleted = 0;
            """;
        command.Parameters.AddWithValue("@username", username);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadUser(reader) : null;
    }

    private void ResolvePasswordResetRequests(string username)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE password_reset_requests
            SET status = 'Resolved',
                resolved_at = @resolvedAt,
                resolved_by = @resolvedBy
            WHERE username = @username
              AND status = 'Pending';
            """;
        command.Parameters.AddWithValue("@resolvedAt", ToDatabaseDateTime(DateTime.Now));
        command.Parameters.AddWithValue("@resolvedBy", CurrentUser?.Username ?? "admin");
        command.Parameters.AddWithValue("@username", username);
        command.ExecuteNonQuery();
    }

    private void EnsurePasswordResetCodeForUser(AppUser user)
    {
        DeleteExpiredRegistrationCodes();
        var username = NormalizeUsername(user.Username);
        var note = PasswordResetCodeNote(username);
        using var connection = OpenConnection();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = """
                SELECT COUNT(*)
                FROM registration_codes
                WHERE note = @note
                  AND is_active = 1
                  AND used_at IS NULL
                  AND (expires_at IS NULL OR expires_at > @now);
                """;
            check.Parameters.AddWithValue("@note", note);
            check.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
            if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        var code = GenerateRegistrationCode();
        var createdAt = DateTime.Now;
        var expiresAt = createdAt.AddMinutes(15);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO registration_codes
                (code, note, is_active, created_at, expires_at, created_by)
            VALUES
                (@code, @note, 1, @createdAt, @expiresAt, @createdBy);
            """;
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@note", note);
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(createdAt));
        command.Parameters.AddWithValue("@expiresAt", ToDatabaseDateTime(expiresAt));
        command.Parameters.AddWithValue("@createdBy", "password-reset");
        command.ExecuteNonQuery();
        RecordAudit("Tạo mã đổi mật khẩu", "RegistrationCode", code, $"Tài khoản: {username}");
    }

    private void DeleteExpiredRegistrationCodes()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE registration_codes
            SET expires_at = DATEADD(MINUTE, 15, created_at)
            WHERE note LIKE 'PASSWORD_RESET:%'
              AND (expires_at IS NULL OR expires_at > DATEADD(MINUTE, 15, created_at));

            DELETE FROM registration_codes
            WHERE used_at IS NULL
              AND expires_at IS NOT NULL
              AND expires_at <= @now;
            """;
        command.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
        command.ExecuteNonQuery();
    }

    private void EnsureRegistrationCodeAvailable(string activationCode)
    {
        DeleteExpiredRegistrationCodes();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM registration_codes
            WHERE code = @code
              AND is_active = 1
              AND used_at IS NULL
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        command.Parameters.AddWithValue("@code", activationCode);
        command.Parameters.AddWithValue("@now", ToDatabaseDateTime(DateTime.Now));
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            throw new InvalidOperationException("Mã kích hoạt không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
        }
    }

    private void MarkRegistrationCodeUsed(string activationCode, string username)
    {
        DeleteExpiredRegistrationCodes();
        var now = DateTime.Now;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE registration_codes
            SET used_at = @usedAt,
                used_by = @usedBy
            WHERE code = @code
              AND is_active = 1
              AND used_at IS NULL
              AND (expires_at IS NULL OR expires_at > @usedAt);
            """;
        command.Parameters.AddWithValue("@code", activationCode);
        command.Parameters.AddWithValue("@usedAt", ToDatabaseDateTime(now));
        command.Parameters.AddWithValue("@usedBy", username);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("Mã kích hoạt không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
        }
    }

    private void EnsureCurrentAdmin()
    {
        if (CurrentUser?.IsAdmin != true)
        {
            throw new InvalidOperationException("Chỉ tài khoản admin được dùng chức năng này.");
        }
    }

    private void EnsureCurrentUserActive()
    {
        if (CurrentUser is not null && !IsCurrentUserActive())
        {
            throw new InvalidOperationException("Tài khoản này đã bị admin khóa. Vui lòng đăng nhập lại bằng tài khoản được phép.");
        }
    }

    private void EnsureChatKeyForUser(AppUser user)
    {
        try
        {
            new SecureChatRepository(_connectionString).EnsureUserKeyPair(user);
        }
        catch
        {
            // Không chặn đăng nhập nếu máy chưa tạo được khóa chat; màn hình chat sẽ báo lỗi rõ hơn khi gửi.
        }
    }

    private static string CustomerInputNameForTrace(string customerInputName, string customerName)
    {
        return string.IsNullOrWhiteSpace(customerInputName)
            ? customerName.Trim()
            : customerInputName.Trim();
    }

    private static string TraceCustomerInput(string inputName, string officialName)
    {
        inputName = inputName.Trim();
        officialName = officialName.Trim();
        return string.IsNullOrWhiteSpace(inputName) ||
            string.Equals(inputName, officialName, StringComparison.CurrentCultureIgnoreCase)
            ? ""
            : inputName;
    }

    private void RememberCustomerInputAlias(Customer customer, string inputName)
    {
        inputName = inputName.Trim();
        if (string.IsNullOrWhiteSpace(inputName) ||
            string.Equals(inputName, customer.Name, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        var existingCustomer = Data.Customers.FirstOrDefault(item =>
            item.Id != customer.Id &&
            string.Equals(item.Name, inputName, StringComparison.CurrentCultureIgnoreCase));
        if (existingCustomer is not null)
        {
            return;
        }

        var existingAlias = Data.CustomerAliases.FirstOrDefault(alias =>
            string.Equals(alias.Alias, inputName, StringComparison.CurrentCultureIgnoreCase));
        if (existingAlias is not null)
        {
            return;
        }

        Data.CustomerAliases.Add(new CustomerAlias
        {
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            Alias = inputName
        });
    }

    private Customer EnsureCustomer(string customerName)
    {
        customerName = ResolveCompanyName(customerName);
        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new InvalidOperationException("Tên KH không được trống.");
        }

        var customer = Data.Customers.FirstOrDefault(item =>
            string.Equals(item.Name, customerName, StringComparison.CurrentCultureIgnoreCase));

        if (customer is not null)
        {
            return customer;
        }

        customer = new Customer { Name = customerName };
        Data.Customers.Add(customer);
        return customer;
    }

    private void NormalizeReferences()
    {
        var customersByName = new Dictionary<string, Customer>(StringComparer.CurrentCultureIgnoreCase);
        var remappedCustomerIds = new Dictionary<Guid, Guid>();
        var normalizedCustomers = new List<Customer>();

        foreach (var customer in Data.Customers.Where(customer => !string.IsNullOrWhiteSpace(customer.Name)))
        {
            customer.Id = customer.Id == Guid.Empty ? Guid.NewGuid() : customer.Id;
            customer.Name = ResolveCompanyName(customer.Name);

            if (customersByName.TryGetValue(customer.Name, out var existingCustomer))
            {
                remappedCustomerIds[customer.Id] = existingCustomer.Id;
                existingCustomer.TaxCode = FirstNotBlank(existingCustomer.TaxCode, customer.TaxCode);
                existingCustomer.Phone = FirstNotBlank(existingCustomer.Phone, customer.Phone);
                existingCustomer.Address = FirstNotBlank(existingCustomer.Address, customer.Address);
                existingCustomer.Note = FirstNotBlank(existingCustomer.Note, customer.Note);
                existingCustomer.IsActive |= customer.IsActive;
                continue;
            }

            customersByName[customer.Name] = customer;
            normalizedCustomers.Add(customer);
        }

        Data.Customers = normalizedCustomers;

        var normalizedAliases = new List<CustomerAlias>();
        var aliasLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in Data.CustomerAliases.Where(item => !string.IsNullOrWhiteSpace(item.Alias)))
        {
            if (remappedCustomerIds.TryGetValue(alias.CustomerId, out var mappedCustomerId))
            {
                alias.CustomerId = mappedCustomerId;
            }

            var customer = Data.Customers.FirstOrDefault(item => item.Id == alias.CustomerId);
            customer ??= ResolveOrCreateCustomer(alias.CustomerName, customersByName);
            alias.CustomerId = customer.Id;
            alias.CustomerName = customer.Name;
            alias.Alias = alias.Alias.Trim();
            alias.Id = alias.Id == Guid.Empty ? Guid.NewGuid() : alias.Id;

            var key = NormalizeLookup(alias.Alias);
            if (string.IsNullOrWhiteSpace(key) || aliasLookup.Contains(key) || string.Equals(alias.Alias, customer.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            aliasLookup.Add(key);
            normalizedAliases.Add(alias);
        }

        Data.CustomerAliases = normalizedAliases;

        foreach (var document in Data.Documents)
        {
            var originalName = document.CustomerName;
            document.CustomerName = ResolveCompanyName(document.CustomerName);
            if (string.IsNullOrWhiteSpace(document.CustomerInputName))
            {
                document.CustomerInputName = TraceCustomerInput(originalName, document.CustomerName);
            }

            if (remappedCustomerIds.TryGetValue(document.CustomerId, out var mappedCustomerId))
            {
                document.CustomerId = mappedCustomerId;
            }

            var customer = Data.Customers.FirstOrDefault(item => item.Id == document.CustomerId);
            customer ??= ResolveOrCreateCustomer(document.CustomerName, customersByName);
            document.CustomerId = customer.Id;
            document.CustomerName = customer.Name;
            document.CustomerInputName = TraceCustomerInput(document.CustomerInputName, customer.Name);
            RememberCustomerInputAlias(customer, document.CustomerInputName);
        }

        foreach (var payment in Data.Payments)
        {
            var originalName = payment.CustomerName;
            payment.CustomerName = ResolveCompanyName(payment.CustomerName);
            if (string.IsNullOrWhiteSpace(payment.CustomerInputName))
            {
                payment.CustomerInputName = TraceCustomerInput(originalName, payment.CustomerName);
            }

            if (remappedCustomerIds.TryGetValue(payment.CustomerId, out var mappedCustomerId))
            {
                payment.CustomerId = mappedCustomerId;
            }

            var customer = Data.Customers.FirstOrDefault(item => item.Id == payment.CustomerId);
            customer ??= ResolveOrCreateCustomer(payment.CustomerName, customersByName);
            payment.CustomerId = customer.Id;
            payment.CustomerName = customer.Name;
            payment.CustomerInputName = TraceCustomerInput(payment.CustomerInputName, customer.Name);
            RememberCustomerInputAlias(customer, payment.CustomerInputName);
        }

        return;

        Customer ResolveOrCreateCustomer(string name, Dictionary<string, Customer> customerLookup)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Khách hàng chưa xác định" : ResolveCompanyName(name);
            if (customerLookup.TryGetValue(name, out var customer))
            {
                return customer;
            }

            customer = new Customer { Name = name };
            customerLookup[name] = customer;
            Data.Customers.Add(customer);
            return customer;
        }
    }

    private void EnsureDatabase()
    {
        using var connection = OpenMasterConnection();
        ExecuteSqlServerScript(connection, LoadSqlServerSchema());
    }

    private bool IsDatabaseEmpty()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM customers) +
                (SELECT COUNT(*) FROM documents) +
                (SELECT COUNT(*) FROM payments);
            """;
        var value = command.ExecuteScalar();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture) == 0;
    }

    private AccountingData ReadAllFromDatabase()
    {
        var data = new AccountingData();
        var documentsById = new Dictionary<Guid, Document>();

        using var connection = OpenConnection();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, name, tax_code, phone, address, note, is_active, created_at
                FROM customers
                ORDER BY name;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                data.Customers.Add(new Customer
                {
                    Id = GetGuid(reader, "id"),
                    Name = GetString(reader, "name"),
                    TaxCode = GetString(reader, "tax_code"),
                    Phone = GetString(reader, "phone"),
                    Address = GetString(reader, "address"),
                    Note = GetString(reader, "note"),
                    IsActive = GetInt64(reader, "is_active") != 0,
                    CreatedAt = ParseDateTime(GetString(reader, "created_at"))
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, customer_id, customer_name, alias, created_at
                FROM customer_aliases
                ORDER BY customer_name, alias;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                data.CustomerAliases.Add(new CustomerAlias
                {
                    Id = GetGuid(reader, "id"),
                    CustomerId = GetGuid(reader, "customer_id"),
                    CustomerName = GetString(reader, "customer_name"),
                    Alias = GetString(reader, "alias"),
                    CreatedAt = ParseDateTime(GetString(reader, "created_at"))
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, voucher_no, doc_date, customer_id, customer_name, customer_input_name, content, note, created_at
                FROM documents
                ORDER BY doc_date, created_at, voucher_no;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var document = new Document
                {
                    Id = GetGuid(reader, "id"),
                    VoucherNo = GetString(reader, "voucher_no"),
                    Date = ParseDateOnly(GetString(reader, "doc_date")),
                    CustomerId = GetGuid(reader, "customer_id"),
                    CustomerName = GetString(reader, "customer_name"),
                    CustomerInputName = GetString(reader, "customer_input_name"),
                    Content = GetString(reader, "content"),
                    Note = GetString(reader, "note"),
                    CreatedAt = ParseDateTime(GetString(reader, "created_at"))
                };
                data.Documents.Add(document);
                documentsById[document.Id] = document;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT document_id, line_content, category, spec, quantity, unit_price, note
                FROM document_lines
                ORDER BY document_id, line_no, id;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var documentId = GetGuid(reader, "document_id");
                if (!documentsById.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.Lines.Add(new DocumentLine
                {
                    LineContent = GetString(reader, "line_content"),
                    Category = GetString(reader, "category"),
                    Spec = GetString(reader, "spec"),
                    Quantity = ParseDecimal(GetString(reader, "quantity")),
                    UnitPrice = ParseDecimal(GetString(reader, "unit_price")),
                    Note = GetString(reader, "note")
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, customer_id, customer_name, customer_input_name, pay_date, content, method, account, amount, note, created_at
                FROM payments
                ORDER BY pay_date, created_at;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                data.Payments.Add(new Payment
                {
                    Id = GetGuid(reader, "id"),
                    CustomerId = GetGuid(reader, "customer_id"),
                    CustomerName = GetString(reader, "customer_name"),
                    CustomerInputName = GetString(reader, "customer_input_name"),
                    Date = ParseDateOnly(GetString(reader, "pay_date")),
                    Content = GetString(reader, "content"),
                    Method = GetString(reader, "method"),
                    Account = GetString(reader, "account"),
                    Amount = ParseDecimal(GetString(reader, "amount")),
                    Note = GetString(reader, "note"),
                    CreatedAt = ParseDateTime(GetString(reader, "created_at"))
                });
            }
        }

        return data;
    }

    private bool TryImportLegacyJson()
    {
        if (!File.Exists(_legacyJsonPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_legacyJsonPath, Encoding.UTF8);
            Data = JsonSerializer.Deserialize<AccountingData>(json, JsonOptions) ?? new AccountingData();
            NormalizeReferences();
            return Data.Customers.Count > 0 || Data.Documents.Count > 0 || Data.Payments.Count > 0;
        }
        catch
        {
            Data = new AccountingData();
            return false;
        }
    }

    private void SeedCustomersFromTemplate()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "template", "filemau.xlsm");
        if (!File.Exists(templatePath))
        {
            return;
        }

        foreach (var name in ReadSheetNames(templatePath).Skip(1).SkipLast(9))
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                !Data.Customers.Any(customer => string.Equals(customer.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                Data.Customers.Add(new Customer { Name = name });
            }
        }
    }

    private void SeedCustomersFromAliasBook()
    {
        foreach (var name in CustomerAliases.OfficialNames)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                !Data.Customers.Any(customer => string.Equals(customer.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                Data.Customers.Add(new Customer { Name = name });
            }
        }
    }

    private void SeedCustomerAliasesFromFile()
    {
        foreach (var entry in _fileAliasBook.Entries)
        {
            var officialName = ResolveCompanyName(entry.OfficialName);
            if (string.IsNullOrWhiteSpace(officialName) || string.IsNullOrWhiteSpace(entry.Alias))
            {
                continue;
            }

            var customer = Data.Customers.FirstOrDefault(item => string.Equals(item.Name, officialName, StringComparison.CurrentCultureIgnoreCase));
            if (customer is null)
            {
                customer = new Customer { Name = officialName };
                Data.Customers.Add(customer);
            }

            if (Data.CustomerAliases.Any(alias => string.Equals(alias.Alias, entry.Alias, StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            Data.CustomerAliases.Add(new CustomerAlias
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                Alias = entry.Alias
            });
        }
    }

    private void RefreshAliasBook()
    {
        CustomerAliases = CustomerAliasBook.FromEntries(
            Data.Customers.Select(customer => customer.Name).Concat(_fileAliasBook.OfficialNames),
            Data.CustomerAliases.Select(alias => new CustomerAliasEntry(alias.CustomerName, alias.Alias)),
            _fileAliasBook.SourcePath);
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private SqlConnection OpenMasterConnection()
    {
        var builder = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master"
        };
        var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqlConnection connection, string tableName, string columnName, string definition)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT COUNT(*)
                FROM sys.columns
                WHERE object_id = OBJECT_ID(@tableName)
                  AND name = @columnName;
                """;
            command.Parameters.AddWithValue("@tableName", tableName);
            command.Parameters.AddWithValue("@columnName", columnName);
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"ALTER TABLE {tableName} ADD {columnName} {definition};";
            command.ExecuteNonQuery();
        }
    }

    private static void EnsureDefaultAdmin(SqlConnection connection)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM app_users WHERE username = 'admin';";
            if (Convert.ToInt64(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_users
                (id, username, full_name, role, password_hash, is_active, created_at)
            VALUES
                (@id, 'admin', 'Quản trị hệ thống', 'Admin', @passwordHash, 1, @createdAt);
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("@passwordHash", PasswordHasher.Hash("admin"));
        command.Parameters.AddWithValue("@createdAt", ToDatabaseDateTime(DateTime.Now));
        command.ExecuteNonQuery();
    }

    private static void ResolveAdminPasswordResetRequests(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE password_reset_requests
            SET status = 'Resolved',
                resolved_at = @resolvedAt,
                resolved_by = 'system'
            WHERE status = 'Pending'
              AND username IN (
                  SELECT username
                  FROM app_users
                  WHERE role = 'Admin'
              );
            """;
        command.Parameters.AddWithValue("@resolvedAt", ToDatabaseDateTime(DateTime.Now));
        command.ExecuteNonQuery();
    }

    private static AppUser ReadUser(SqlDataReader reader)
    {
        var approvedAt = GetString(reader, "approved_at");
        var deletedAt = GetString(reader, "deleted_at");
        return new AppUser
        {
            Id = GetGuid(reader, "id"),
            Username = GetString(reader, "username"),
            FullName = GetString(reader, "full_name"),
            Role = GetString(reader, "role"),
            IsActive = GetInt64(reader, "is_active") != 0,
            ApprovalStatus = FirstNotBlank(GetString(reader, "approval_status"), "Approved"),
            ApprovedAt = string.IsNullOrWhiteSpace(approvedAt) ? null : ParseDateTime(approvedAt),
            ApprovedBy = GetString(reader, "approved_by"),
            ActivationCode = GetString(reader, "activation_code"),
            PublicKey = GetString(reader, "public_key"),
            IsDeleted = GetInt64(reader, "is_deleted") != 0,
            DeletedAt = string.IsNullOrWhiteSpace(deletedAt) ? null : ParseDateTime(deletedAt),
            CreatedAt = ParseDateTime(GetString(reader, "created_at"))
        };
    }

    private static RegistrationCode ReadRegistrationCode(SqlDataReader reader)
    {
        var usedAt = GetString(reader, "used_at");
        var expiresAt = GetString(reader, "expires_at");
        return new RegistrationCode
        {
            Id = GetInt64(reader, "id"),
            Code = GetString(reader, "code"),
            Note = GetString(reader, "note"),
            IsActive = GetInt64(reader, "is_active") != 0,
            CreatedAt = ParseDateTime(GetString(reader, "created_at")),
            ExpiresAt = string.IsNullOrWhiteSpace(expiresAt) ? null : ParseDateTime(expiresAt),
            CreatedBy = GetString(reader, "created_by"),
            UsedAt = string.IsNullOrWhiteSpace(usedAt) ? null : ParseDateTime(usedAt),
            UsedBy = GetString(reader, "used_by")
        };
    }

    private static PasswordResetRequest ReadPasswordResetRequest(SqlDataReader reader)
    {
        return new PasswordResetRequest
        {
            Id = GetInt64(reader, "id"),
            RequestedAt = ParseDateTime(GetString(reader, "requested_at")),
            Username = GetString(reader, "username"),
            FullName = GetString(reader, "full_name"),
            Status = GetString(reader, "status")
        };
    }

    private static WorkAccessRequest ReadWorkAccessRequest(SqlDataReader reader)
    {
        var approvedAt = GetString(reader, "approved_at");
        var punchAt = GetString(reader, "punch_at");
        return new WorkAccessRequest
        {
            Id = GetInt64(reader, "id"),
            WorkDate = ParseDateOnly(GetString(reader, "work_date")),
            RequestedAt = ParseDateTime(GetString(reader, "requested_at")),
            Username = GetString(reader, "username"),
            FullName = GetString(reader, "full_name"),
            AccessSlot = GetString(reader, "access_slot"),
            Reason = GetString(reader, "reason"),
            Status = GetString(reader, "status"),
            ApprovedAt = string.IsNullOrWhiteSpace(approvedAt) ? null : ParseDateTime(approvedAt),
            ApprovedBy = GetString(reader, "approved_by"),
            PunchAt = string.IsNullOrWhiteSpace(punchAt) ? null : ParseDateTime(punchAt)
        };
    }

    private static IEnumerable<string> ReadSheetNames(string xlsmPath)
    {
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var archive = ZipFile.OpenRead(xlsmPath);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
        {
            yield break;
        }

        using var stream = workbookEntry.Open();
        var doc = XDocument.Load(stream);
        foreach (var sheet in doc.Descendants(spreadsheet + "sheet"))
        {
            var name = sheet.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }
        }
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(connectionString).InitialCatalog)
                ? "KetoanMini"
                : new SqlConnectionStringBuilder(connectionString).InitialCatalog,
            TrustServerCertificate = true
        };

        if (!builder.ContainsKey("Encrypt"))
        {
            builder.Encrypt = SqlConnectionEncryptOption.Optional;
        }

        if (builder.ConnectTimeout <= 0)
        {
            builder.ConnectTimeout = 15;
        }

        return builder.ConnectionString;
    }

    private static string LoadSqlServerSchema()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "database", "sqlserver_schema.sql"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "database", "sqlserver_schema.sql")
        };

        var path = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        if (path is null)
        {
            throw new FileNotFoundException("Khong tim thay file database/sqlserver_schema.sql de tao database SQL Server.");
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static void ExecuteSqlServerScript(SqlConnection connection, string script)
    {
        var batches = script
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Aggregate(new List<StringBuilder> { new() }, (list, line) =>
            {
                if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new StringBuilder());
                }
                else
                {
                    list[^1].AppendLine(line);
                }

                return list;
            });

        foreach (var batch in batches.Select(item => item.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.ExecuteNonQuery();
        }
    }

    private static string ToDatabaseDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string ToDatabaseDateTime(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string ToDatabaseDecimal(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FirstNotBlank(string primary, string fallback)
    {
        return string.IsNullOrWhiteSpace(primary) ? fallback.Trim() : primary.Trim();
    }

    private static string NormalizeUsername(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeActivationCode(string value)
    {
        return value.Trim().Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
    }

    private static string PasswordResetCodeNote(string username)
    {
        return $"PASSWORD_RESET:{NormalizeUsername(username)}";
    }

    private static string GenerateRegistrationCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToHexString(bytes);
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..12]}";
    }

    private static string NormalizeAccessSlot(string value)
    {
        value = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value) ? "outside_work" : value;
    }

    private static string NormalizeLookup(string value)
    {
        return string.Join(" ", TextUtil.RemoveDiacritics(value).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static DateOnly ParseDateOnly(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
            ? DateOnly.FromDateTime(dateTime)
            : DateOnly.FromDateTime(DateTime.Today);
    }

    private static DateTime ParseDateTime(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime)
            ? dateTime
            : DateTime.Now;
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static Guid GetGuid(SqlDataReader reader, string columnName)
    {
        return Guid.TryParse(GetString(reader, columnName), out var id) ? id : Guid.NewGuid();
    }

    private static string GetString(SqlDataReader reader, string columnName)
    {
        int ordinal;
        try
        {
            ordinal = reader.GetOrdinal(columnName);
        }
        catch (IndexOutOfRangeException)
        {
            return "";
        }

        if (reader.IsDBNull(ordinal))
        {
            return "";
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            bool boolean => boolean ? "1" : "0",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }

    private static long GetInt64(SqlDataReader reader, string columnName)
    {
        int ordinal;
        try
        {
            ordinal = reader.GetOrdinal(columnName);
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }

        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }
}

