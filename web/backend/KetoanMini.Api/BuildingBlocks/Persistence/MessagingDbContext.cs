using Microsoft.EntityFrameworkCore;

namespace KetoanMini.Api.BuildingBlocks.Persistence;

/// <summary>
/// EF Core boundary for new messaging infrastructure. The forward-only SQL migration remains
/// authoritative, so this context is never used with EnsureCreated and cannot recreate legacy tables.
/// New modules can adopt their own context/schema incrementally without rewriting existing SQL.
/// </summary>
public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public DbSet<IntegrationOutboxRow> IntegrationOutbox => Set<IntegrationOutboxRow>();
    public DbSet<InboxMessageRow> InboxMessages => Set<InboxMessageRow>();
    public DbSet<RealtimeEventRow> RealtimeEvents => Set<RealtimeEventRow>();
    public DbSet<ApiIdempotencyRow> ApiIdempotency => Set<ApiIdempotencyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationOutboxRow>(entity =>
        {
            entity.ToTable("integration_outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(160);
            entity.Property(x => x.RoutingKey).HasColumnName("routing_key").HasMaxLength(200);
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(x => x.Headers).HasColumnName("headers").HasColumnType("jsonb");
            entity.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            entity.Property(x => x.PublishedAt).HasColumnName("published_at");
        });

        modelBuilder.Entity<InboxMessageRow>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(x => new { x.ConsumerName, x.MessageId });
            entity.Property(x => x.ConsumerName).HasColumnName("consumer_name").HasMaxLength(120);
            entity.Property(x => x.MessageId).HasColumnName("message_id");
            entity.Property(x => x.ReceivedAt).HasColumnName("received_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
        });

        modelBuilder.Entity<RealtimeEventRow>(entity =>
        {
            entity.ToTable("realtime_events");
            entity.HasKey(x => x.SequenceNo);
            entity.Property(x => x.SequenceNo).HasColumnName("sequence_no").ValueGeneratedOnAdd();
            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120);
            entity.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(64);
            entity.Property(x => x.AudienceType).HasColumnName("audience_type").HasMaxLength(20);
            entity.Property(x => x.AudienceKey).HasColumnName("audience_key").HasMaxLength(200);
            entity.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            entity.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        });

        modelBuilder.Entity<ApiIdempotencyRow>(entity =>
        {
            entity.ToTable("api_idempotency");
            entity.HasKey(x => new { x.Username, x.CommandType, x.IdempotencyKey });
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(128);
            entity.Property(x => x.CommandType).HasColumnName("command_type").HasMaxLength(160);
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            entity.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(x => x.ResponseStatus).HasColumnName("response_status");
            entity.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        });
    }
}

public sealed class IntegrationOutboxRow
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string RoutingKey { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string Headers { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class InboxMessageRow
{
    public string ConsumerName { get; set; } = "";
    public Guid MessageId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class RealtimeEventRow
{
    public long SequenceNo { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public string Scope { get; set; } = "";
    public string AudienceType { get; set; } = "all";
    public string? AudienceKey { get; set; }
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ApiIdempotencyRow
{
    public string Username { get; set; } = "";
    public string CommandType { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Status { get; set; } = "started";
    public int? ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
