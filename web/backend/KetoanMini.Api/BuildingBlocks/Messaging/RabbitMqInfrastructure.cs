using System.Text;
using System.Text.Json;
using KetoanMini.Api.BuildingBlocks.Outbox;
using KetoanMini.Api.BuildingBlocks.Realtime;
using KetoanMini.Api.Data;
using KetoanMini.Api.Services;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace KetoanMini.Api.BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "amqp://guest:guest@localhost:5672/";
    public string Exchange { get; set; } = "ketoan.events";
    public ushort Prefetch { get; set; } = 16;
    public int PublishBatchSize { get; set; } = 50;
}

public sealed class MessagingReadiness
{
    public volatile bool PublisherConnected;
    public volatile bool ConsumersConnected;
}

internal static class RabbitTopology
{
    public const string DeadExchange = "ketoan.events.dlx";
    public static readonly string[] ConsumerQueues =
        ["notifications.q", "realtime-projection.q", "cache-invalidation.q"];
    public static readonly (string Suffix, int Milliseconds)[] RetryTiers =
        [("5s", 5_000), ("30s", 30_000), ("2m", 120_000)];

    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Topic, durable: true,
            autoDelete: false, arguments: null, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadExchange, ExchangeType.Topic, durable: true,
            autoDelete: false, arguments: null, cancellationToken: ct);

        foreach (var queue in ConsumerQueues)
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                cancellationToken: ct);
            var binding = queue == "notifications.q" ? "notifications.#" : "#";
            await channel.QueueBindAsync(queue, options.Exchange, binding, arguments: null, cancellationToken: ct);

            var deadQueue = queue + ".dlq";
            await channel.QueueDeclareAsync(deadQueue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                cancellationToken: ct);
            await channel.QueueBindAsync(deadQueue, DeadExchange, queue + ".dead", arguments: null,
                cancellationToken: ct);

            foreach (var (suffix, _) in RetryTiers)
            {
                var exchange = $"ketoan.retry.{queue}.{suffix}";
                var retryQueue = $"{queue}.retry.{suffix}";
                await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true,
                    autoDelete: false, arguments: null, cancellationToken: ct);
                await channel.QueueDeclareAsync(retryQueue, durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-queue-type"] = "quorum",
                    }, cancellationToken: ct);
                await channel.QueueBindAsync(retryQueue, exchange, "#", arguments: null, cancellationToken: ct);
            }
        }
    }

    public static ConnectionFactory Factory(RabbitMqOptions options) => new()
    {
        Uri = new Uri(options.ConnectionString),
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
        RequestedHeartbeat = TimeSpan.FromSeconds(30),
        ClientProvidedName = "ketoanmini-api",
        ConsumerDispatchConcurrency = 1,
    };

    public static CreateChannelOptions ConfirmingChannel => new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);
}

/// <summary>Claims PostgreSQL outbox rows, publishes persistent mandatory messages and marks only confirmed rows.</summary>
public sealed class RabbitOutboxPublisher(
    IntegrationOutbox outbox,
    MessagingReadiness readiness,
    Realtime.OutboxSignal outboxSignal,
    IOptions<RabbitMqOptions> configured,
    ILogger<RabbitOutboxPublisher> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = configured.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;
            IChannel? channel = null;
            try
            {
                connection = await RabbitTopology.Factory(_options).CreateConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(RabbitTopology.ConfirmingChannel, stoppingToken);
                await RabbitTopology.DeclareAsync(channel, _options, stoppingToken);
                readiness.PublisherConnected = true;

                while (!stoppingToken.IsCancellationRequested)
                {
                    var batch = await outbox.ClaimAsync(Math.Clamp(_options.PublishBatchSize, 1, 500), stoppingToken);
                    foreach (var message in batch)
                    {
                        try
                        {
                            var properties = new BasicProperties
                            {
                                Persistent = true,
                                ContentType = "application/json",
                                MessageId = message.Id.ToString("D"),
                                Type = message.EventType,
                                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                            };
                            await channel.BasicPublishAsync(_options.Exchange, message.RoutingKey, mandatory: true,
                                basicProperties: properties, body: Encoding.UTF8.GetBytes(message.Payload),
                                cancellationToken: stoppingToken);
                            // Confirm/mandatory return failures throw in client 7.x. Updating PostgreSQL can fail
                            // after a confirm; that deliberately yields a duplicate which Inbox absorbs.
                            await outbox.MarkPublishedAsync(message.Id, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            await outbox.RetryAsync(message.Id, message.Attempts, ex.Message, stoppingToken);
                            logger.LogWarning("Outbox event {EventId} publish attempt {Attempt} failed: {Message}",
                                message.Id, message.Attempts, ex.Message);
                        }
                    }
                    // Chuông pg_notify (xem PostgresWakeListener) cắt nhịp chờ ngay khi lệnh ghi commit.
                    if (batch.Count == 0)
                        await outboxSignal.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("RabbitMQ publisher unavailable; outbox remains durable: {Message}", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                readiness.PublisherConnected = false;
                if (channel is not null) await channel.DisposeAsync();
                if (connection is not null) await connection.DisposeAsync();
            }
        }
    }
}

/// <summary>Manual-ack, Inbox-protected consumers. Realtime projection uses a single dispatch lane.</summary>
public sealed class RabbitConsumersWorker(
    Database db,
    RealtimeEventStore realtime,
    RedisRealtimeCoordinator redis,
    PushService push,
    MessagingReadiness readiness,
    IOptions<RabbitMqOptions> configured,
    ILogger<RabbitConsumersWorker> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = configured.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? connection = null;
            var channels = new List<IChannel>();
            try
            {
                connection = await RabbitTopology.Factory(_options).CreateConnectionAsync(stoppingToken);
                foreach (var queue in RabbitTopology.ConsumerQueues)
                {
                    var channel = await connection.CreateChannelAsync(RabbitTopology.ConfirmingChannel, stoppingToken);
                    channels.Add(channel);
                    await RabbitTopology.DeclareAsync(channel, _options, stoppingToken);
                    await channel.BasicQosAsync(0, _options.Prefetch, global: false, stoppingToken);
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += (_, delivery) => HandleAsync(queue, channel, delivery, stoppingToken);
                    await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
                }
                readiness.ConsumersConnected = true;
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("RabbitMQ consumers unavailable: {Message}", ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                readiness.ConsumersConnected = false;
                foreach (var channel in channels) await channel.DisposeAsync();
                if (connection is not null) await connection.DisposeAsync();
            }
        }
    }

    private async Task HandleAsync(string queue, IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var body = delivery.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);
        var attempt = HeaderInt(delivery.BasicProperties.Headers, "x-retry-count");
        try
        {
            var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(json, IntegrationEventJson.Options)
                ?? throw new PermanentMessageException("Empty integration-event envelope.");
            await using var conn = await db.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            var inserted = await conn.Cmd("""
                INSERT INTO inbox_messages(consumer_name,message_id,correlation_id)
                VALUES (@consumer,@id,@correlation) ON CONFLICT DO NOTHING
                """, tx).With("@consumer", queue).With("@id", envelope.EventId)
                .With("@correlation", (object?)envelope.CorrelationId ?? DBNull.Value)
                .ExecuteNonQueryAsync(ct);
            long? cursor = null;
            if (inserted > 0)
            {
                if (queue == "realtime-projection.q" &&
                    envelope.EventType != "notifications.push.requested.v1")
                    cursor = await realtime.AppendAsync(conn, tx, envelope, ct);
                if (queue == "notifications.q" &&
                    envelope.EventType == "notifications.push.requested.v1")
                    await DispatchPushAsync(envelope, ct);
                if (queue == "cache-invalidation.q" &&
                    envelope.Data.ValueKind == JsonValueKind.Object &&
                    envelope.Data.TryGetProperty("scope", out var cacheScope))
                {
                    var scope = cacheScope.GetString() ?? "all";
                    if (!await redis.InvalidateCacheAsync(scope, envelope.EventId))
                        throw new InvalidOperationException("Redis cache invalidation is temporarily unavailable.");
                }
                await conn.Cmd("""
                    UPDATE inbox_messages SET completed_at=CURRENT_TIMESTAMP
                    WHERE consumer_name=@consumer AND message_id=@id
                    """, tx).With("@consumer", queue).With("@id", envelope.EventId).ExecuteNonQueryAsync(ct);
                // Đánh thức luồng SSE ở MỌI tiến trình qua PostgreSQL, không chỉ qua Redis: Redis là
                // bộ tăng tốc có thể tắt/hỏng, còn đường này đi cùng chính giao dịch vừa ghi sự kiện.
                if (cursor.HasValue)
                    await conn.Cmd("SELECT pg_notify(@channel,@payload)", tx)
                        .With("@channel", Realtime.PostgresWakeListener.RealtimeWakeChannel)
                        .With("@payload", cursor.Value.ToString()).ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            if (cursor.HasValue) await redis.PublishWakeAsync(cursor.Value);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            try
            {
                if (ex is PermanentMessageException || attempt >= RabbitTopology.RetryTiers.Length)
                    await DeadLetterAsync(queue, channel, delivery, json, attempt, ex, ct);
                else
                    await RetryAsync(queue, channel, delivery, body, attempt, ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            }
            catch (Exception retryFailure)
            {
                logger.LogError(retryFailure, "Could not route failed event from {Queue}; leaving it unacked.", queue);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
            }
        }
    }

    private async Task DispatchPushAsync(IntegrationEventEnvelope envelope, CancellationToken ct)
    {
        if (!push.Enabled) throw new InvalidOperationException("FCM is not configured.");
        if (envelope.Data.ValueKind != JsonValueKind.Object ||
            !envelope.Data.TryGetProperty("kind", out var kindValue) ||
            !envelope.Data.TryGetProperty("payload", out var payload))
            throw new PermanentMessageException("Invalid notifications.push.requested.v1 contract.");
        var job = payload.Deserialize<PushService.PushJob>(IntegrationEventJson.Options)
            ?? throw new PermanentMessageException("Invalid push payload.");
        var kind = kindValue.GetString();
        _ = kind switch
        {
            OutboxQueue.KindUserPush => await push.DispatchUserAsync(
                job.Username, job.Title, job.Body, job.NotifId, job.Target),
            OutboxQueue.KindAdminsPush => await push.DispatchAdminsAsync(
                job.Title, job.Body, job.NotifId, job.Target),
            OutboxQueue.KindAllPush => await push.DispatchAllAsync(
                job.Title, job.Body, job.NotifId, job.Target),
            _ => throw new PermanentMessageException($"Unknown push kind '{kind}'."),
        };
        ct.ThrowIfCancellationRequested();
    }

    private async Task RetryAsync(string queue, IChannel channel, BasicDeliverEventArgs delivery,
        byte[] body, int attempt, CancellationToken ct)
    {
        var tier = RabbitTopology.RetryTiers[Math.Clamp(attempt, 0, RabbitTopology.RetryTiers.Length - 1)];
        var properties = CopyProperties(delivery.BasicProperties, attempt + 1);
        await channel.BasicPublishAsync($"ketoan.retry.{queue}.{tier.Suffix}", delivery.RoutingKey,
            mandatory: true, basicProperties: properties, body: body, cancellationToken: ct);
    }

    private async Task DeadLetterAsync(string queue, IChannel channel, BasicDeliverEventArgs delivery,
        string envelope, int attempt, Exception error, CancellationToken ct)
    {
        var messageId = Guid.TryParse(delivery.BasicProperties.MessageId, out var parsed) ? parsed : Guid.NewGuid();
        var correlation = delivery.BasicProperties.CorrelationId;
        var lastError = error.Message.Length > 2000 ? error.Message[..2000] : error.Message;
        await using (var conn = await db.OpenAsync(ct))
        {
            await conn.Cmd("""
                INSERT INTO messaging_dead_letters
                    (message_id,source_queue,routing_key,attempts,last_error,correlation_id,envelope)
                VALUES (@id,@queue,@route,@attempts,@error,@correlation,@envelope::jsonb)
                """).With("@id", messageId).With("@queue", queue).With("@route", delivery.RoutingKey)
                .With("@attempts", attempt + 1).With("@error", lastError)
                .With("@correlation", (object?)correlation ?? DBNull.Value).With("@envelope", envelope)
                .ExecuteNonQueryAsync(ct);
        }
        var dead = JsonSerializer.Serialize(new
        {
            messageId,
            sourceQueue = queue,
            routingKey = delivery.RoutingKey,
            attempts = attempt + 1,
            lastError,
            correlationId = correlation,
            failedAt = DateTimeOffset.UtcNow,
            envelope = JsonDocument.Parse(envelope).RootElement,
        }, IntegrationEventJson.Options);
        await channel.BasicPublishAsync(RabbitTopology.DeadExchange, queue + ".dead", mandatory: true,
            basicProperties: CopyProperties(delivery.BasicProperties, attempt + 1),
            body: Encoding.UTF8.GetBytes(dead), cancellationToken: ct);
        logger.LogError("Event {MessageId} moved from {Queue} to DLQ after {Attempts} attempts: {Error}",
            messageId, queue, attempt + 1, lastError);
    }

    private static BasicProperties CopyProperties(IReadOnlyBasicProperties source, int attempt) => new()
    {
        Persistent = true,
        ContentType = source.ContentType ?? "application/json",
        MessageId = source.MessageId,
        CorrelationId = source.CorrelationId,
        Type = source.Type,
        Headers = new Dictionary<string, object?> { ["x-retry-count"] = attempt },
    };

    private static int HeaderInt(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var value) || value is null) return 0;
        return value switch { int i => i, long l => checked((int)l), byte[] b when int.TryParse(Encoding.UTF8.GetString(b), out var i) => i, _ => 0 };
    }

    private sealed class PermanentMessageException(string message) : Exception(message);
}

public sealed class MessagingObservabilityWorker(
    IntegrationOutbox outbox, ILogger<MessagingObservabilityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var value = await outbox.MetricsAsync(stoppingToken);
                logger.LogInformation(
                    "Messaging metrics pending={Pending} oldest_seconds={OldestAge:F0} max_attempts={Attempts} dead={Dead}",
                    value.Pending, value.OldestAgeSeconds, value.MaxAttempts, value.DeadLetters);
            }
            catch (Exception ex) { logger.LogWarning("Messaging metrics query failed: {Message}", ex.Message); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
