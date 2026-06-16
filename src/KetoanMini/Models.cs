using System.Text.Json.Serialization;

namespace KetoanMini;

public sealed class AccountingData
{
    public List<Customer> Customers { get; set; } = [];
    public List<CustomerAlias> CustomerAliases { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public string ApprovalStatus { get; set; } = "Approved";
    public DateTime? ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = "";
    public string ActivationCode { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Username : FullName;

    [JsonIgnore]
    public bool IsPendingApproval => string.Equals(ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase);
}

public sealed class RegistrationCode
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ExpiresAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UsedAt { get; set; }
    public string UsedBy { get; set; } = "";

    [JsonIgnore]
    public bool IsExpired => ExpiresAt is not null && ExpiresAt.Value <= DateTime.Now;

    [JsonIgnore]
    public bool IsAvailable => IsActive && UsedAt is null && !IsExpired;
}

public sealed class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class PasswordResetRequest
{
    public long Id { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.Now;
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Status { get; set; } = "Pending";
}

public sealed class WorkAccessRequest
{
    public long Id { get; set; }
    public DateOnly WorkDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateTime RequestedAt { get; set; } = DateTime.Now;
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string AccessSlot { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime? ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTime? PunchAt { get; set; }

    [JsonIgnore]
    public bool IsApproved => string.Equals(Status, "Approved", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Online presence + minutes-online-today for a user (Nhân sự view).</summary>
public sealed class UserPresence
{
    public string Username { get; set; } = "";
    public bool IsOnline { get; set; }
    public int MinutesToday { get; set; }
}

public sealed class LanChatPeer
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string Address { get; set; } = "";
    public int FilePort { get; set; }
    public int ChatPort { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool IsOnline => LastSeen >= DateTime.Now.AddSeconds(-10);

    [JsonIgnore]
    public string NameText => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
}

public sealed class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string SenderUsername { get; set; } = "";
    public string ReceiverUsername { get; set; } = "";
    public string MessageType { get; set; } = "Text";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "Sent";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public Guid? FileOfferId { get; set; }
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string SenderAddress { get; set; } = "";
    public int SenderPort { get; set; }
    public string TransferToken { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }

    [JsonIgnore]
    public bool IsFileOffer => string.Equals(MessageType, "FileOffer", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsPendingFileOffer => IsFileOffer
        && string.Equals(Status, "Pending", StringComparison.OrdinalIgnoreCase)
        && ExpiresAt is not null
        && ExpiresAt.Value > DateTime.Now;
}

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string TaxCode { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class CustomerAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string Alias { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string VoucherNo { get; set; } = "";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerInputName { get; set; } = "";
    public string Content { get; set; } = "";
    public string Note { get; set; } = "";
    public List<DocumentLine> Lines { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public decimal Total => Lines.Sum(line => line.Quantity * line.UnitPrice);
}

public sealed class DocumentLine
{
    public string LineContent { get; set; } = "";
    public string Category { get; set; } = "";
    public string Spec { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Note { get; set; } = "";

    [JsonIgnore]
    public decimal Amount => Quantity * UnitPrice;
}

public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerInputName { get; set; } = "";
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Content { get; set; } = "";
    public string Method { get; set; } = "";
    public string Account { get; set; } = "";
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public decimal SignedAmount => IsExpenseContent(Content) ? -Math.Abs(Amount) : Math.Abs(Amount);

    public static bool IsExpenseContent(string content)
    {
        var value = TextUtil.RemoveDiacritics(content).Trim().ToLowerInvariant();
        return value is "chi tra" or "tra tien";
    }
}

public sealed class ExportPayload
{
    [JsonPropertyName("generated_at")]
    public string GeneratedAt { get; set; } = DateTime.Now.ToString("s");

    [JsonPropertyName("customers")]
    public List<ExportCustomer> Customers { get; set; } = [];

    [JsonPropertyName("documents")]
    public List<ExportDocument> Documents { get; set; } = [];

    [JsonPropertyName("payments")]
    public List<ExportPayment> Payments { get; set; } = [];
}

public sealed class ExportCustomer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tax_code")]
    public string TaxCode { get; set; } = "";

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = "";

    [JsonPropertyName("address")]
    public string Address { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}

public sealed class ExportDocument
{
    [JsonPropertyName("voucher_no")]
    public string VoucherNo { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("customer")]
    public string Customer { get; set; } = "";

    [JsonPropertyName("customer_input_name")]
    public string CustomerInputName { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("lines")]
    public List<ExportDocumentLine> Lines { get; set; } = [];
}

public sealed class ExportDocumentLine
{
    [JsonPropertyName("line_content")]
    public string LineContent { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("spec")]
    public string Spec { get; set; } = "";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}

public sealed class ExportPayment
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("customer")]
    public string Customer { get; set; } = "";

    [JsonPropertyName("customer_input_name")]
    public string CustomerInputName { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("account")]
    public string Account { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";
}
