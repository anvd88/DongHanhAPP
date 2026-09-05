using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace KetoanMini.Api.BuildingBlocks.Realtime;

public sealed class RedisOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "localhost:6379,abortConnect=false";
    public int PresenceTtlSeconds { get; set; } = 90;
}

/// <summary>Redis is only an accelerator for wake-up/presence/soft locks, never durable state.</summary>
public sealed class RedisRealtimeCoordinator(
    IOptions<RedisOptions> configured,
    RealtimeWakeHub wake,
    BusinessEventWriter businessEvents,
    ILogger<RedisRealtimeCoordinator> logger) : BackgroundService, IAsyncDisposable
{
    private const string WakeChannel = "ketoan:realtime:wake";
    private readonly RedisOptions _options = configured.Value;
    private ConnectionMultiplexer? _connection;

    public bool Available => _connection?.IsConnected == true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection ??= await ConnectionMultiplexer.ConnectAsync(_options.ConnectionString);
                await _connection.GetSubscriber().SubscribeAsync(
                    RedisChannel.Literal(WakeChannel), (_, value) =>
                    {
                        if (long.TryParse(value, out var cursor)) wake.Publish(cursor);
                    });
                while (!stoppingToken.IsCancellationRequested && _connection.IsConnected)
                {
                    await ReapOfflineTransitionsAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("Redis realtime unavailable; SSE polling remains active: {Message}", ex.Message);
                if (_connection is not null) await _connection.DisposeAsync();
                _connection = null;
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async Task PublishWakeAsync(long cursor)
    {
        wake.Publish(cursor);
        try
        {
            if (_connection?.IsConnected == true)
                await _connection.GetSubscriber().PublishAsync(RedisChannel.Literal(WakeChannel), cursor);
        }
        catch (Exception ex) { logger.LogDebug("Redis wake publish failed: {Message}", ex.Message); }
    }

    public async Task TouchPresenceAsync(string username, string sessionId, string connectionId)
    {
        try
        {
            if (_connection?.IsConnected != true) return;
            var db = _connection.GetDatabase();
            var ttl = TimeSpan.FromSeconds(Math.Clamp(_options.PresenceTtlSeconds, 30, 300));
            var key = $"presence:{username}:{sessionId}:{connectionId}";
            var summary = $"presence:user:{username.ToLowerInvariant()}";
            var wasOnline = await db.KeyExistsAsync(summary);
            await db.StringSetAsync(key, "1", ttl);
            await db.StringSetAsync(summary, "1", ttl);
            await db.SortedSetAddAsync("presence:expirations", username.ToLowerInvariant(),
                DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds());
            if (!wasOnline) await businessEvents.InvalidatedAsync("presence", username);
        }
        catch (Exception ex) { logger.LogDebug("Redis presence refresh failed: {Message}", ex.Message); }
    }

    public async Task<bool?> IsUserOnlineAsync(string username)
    {
        try
        {
            if (_connection?.IsConnected != true) return null;
            return await _connection.GetDatabase().KeyExistsAsync($"presence:user:{username.ToLowerInvariant()}");
        }
        catch { return null; }
    }

    private async Task ReapOfflineTransitionsAsync(CancellationToken ct)
    {
        if (_connection?.IsConnected != true) return;
        var db = _connection.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expired = await db.SortedSetRangeByScoreAsync("presence:expirations", stop: now, take: 100);
        foreach (var value in expired)
        {
            ct.ThrowIfCancellationRequested();
            var username = value.ToString();
            if (await db.KeyExistsAsync($"presence:user:{username}")) continue;
            if (await db.SortedSetRemoveAsync("presence:expirations", value))
                await businessEvents.InvalidatedAsync("presence", username, ct);
        }
    }

    public async Task<bool> TryAcquireSoftLockAsync(string key, string owner, TimeSpan ttl)
    {
        try
        {
            return _connection?.IsConnected == true &&
                await _connection.GetDatabase().StringSetAsync(key, owner, ttl, When.NotExists);
        }
        catch { return false; }
    }

    public async Task<bool> RenewSoftLockAsync(string key, string owner, TimeSpan ttl)
    {
        const string script = "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('pexpire',KEYS[1],ARGV[2]) else return 0 end";
        try
        {
            if (_connection?.IsConnected != true) return false;
            var milliseconds = Math.Max(1L, (long)ttl.TotalMilliseconds);
            var result = await _connection.GetDatabase().ScriptEvaluateAsync(
                script, [key], [owner, milliseconds]);
            return (long)result > 0;
        }
        catch { return false; }
    }

    public async Task<bool> ReleaseSoftLockAsync(string key, string owner)
    {
        const string script = "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
        try
        {
            if (_connection?.IsConnected != true) return false;
            var result = await _connection.GetDatabase().ScriptEvaluateAsync(script, [key], [owner]);
            return (long)result > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Idempotent generation invalidation. Consumers can safely repeat this SET after a crash;
    /// cache-aside entries from the prior generation expire naturally and are never trusted again.
    /// </summary>
    public async Task<bool> InvalidateCacheAsync(string scope, Guid eventId)
    {
        try
        {
            if (!_options.Enabled) return true;
            if (_connection?.IsConnected != true) return false;
            await _connection.GetDatabase().StringSetAsync(
                $"cache:generation:{NormalizeCachePart(scope)}", eventId.ToString("N"));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Redis cache invalidation failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<T> GetCacheAsideAsync<T>(string scope, string key, TimeSpan ttl,
        Func<Task<T>> loadFromPostgres)
    {
        if (_connection?.IsConnected != true) return await loadFromPostgres();
        try
        {
            var database = _connection.GetDatabase();
            var safeScope = NormalizeCachePart(scope);
            var generation = await database.StringGetAsync($"cache:generation:{safeScope}");
            var cacheKey = $"cache:{safeScope}:{(generation.HasValue ? generation.ToString() : "0")}:{NormalizeCachePart(key)}";
            var existing = await database.StringGetAsync(cacheKey);
            if (existing.HasValue && JsonSerializer.Deserialize<T>(existing!) is { } cached) return cached;
            var loaded = await loadFromPostgres();
            await database.StringSetAsync(cacheKey, JsonSerializer.Serialize(loaded), ttl);
            return loaded;
        }
        catch
        {
            // Redis is an accelerator: cache parse/network failures fall back to the source of truth.
            return await loadFromPostgres();
        }
    }

    private static string NormalizeCachePart(string value)
        => new(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
