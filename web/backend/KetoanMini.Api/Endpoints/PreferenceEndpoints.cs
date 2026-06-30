using System.Security.Claims;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;

namespace KetoanMini.Api.Endpoints;

public static class PreferenceEndpoints
{
    private const string WaterReminderEnabledKey = "waterReminderEnabled";
    private const string EyeReminderEnabledKey = "eyeReminderEnabled";
    private const string KeepCreateVoucherOpenKey = "keepCreateVoucherOpen";
    private const string MessagePreviewEnabledKey = "messagePreviewEnabled";

    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/preferences").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal principal, Database db, CancellationToken ct) =>
        {
            var userId = principal.UserId();
            if (userId is null) return Results.Unauthorized();

            var values = await LoadPreferences(db, userId.Value, ct);
            return Results.Ok(ToDto(values));
        });

        g.MapPut("/", async (UserPreferencePatchRequest req, ClaimsPrincipal principal, Database db, CancellationToken ct) =>
        {
            var userId = principal.UserId();
            if (userId is null) return Results.Unauthorized();

            await EnsureTables(db, ct);
            await using var conn = await db.OpenAsync(ct);

            async Task SaveBool(string key, bool? value)
            {
                if (value is null) return;

                await conn.Cmd(@"
                    INSERT INTO web_user_preferences (user_id, preference_key, preference_value, updated_at)
                    VALUES (@userId, @key, @value, CURRENT_TIMESTAMP)
                    ON CONFLICT (user_id, preference_key) DO UPDATE SET
                        preference_value = EXCLUDED.preference_value,
                        updated_at = EXCLUDED.updated_at;")
                    .With("@userId", userId.Value)
                    .With("@key", key)
                    .With("@value", value.Value ? "true" : "false")
                    .ExecuteNonQueryAsync(ct);
            }

            await SaveBool(WaterReminderEnabledKey, req.WaterReminderEnabled);
            await SaveBool(EyeReminderEnabledKey, req.EyeReminderEnabled);
            await SaveBool(KeepCreateVoucherOpenKey, req.KeepCreateVoucherOpen);
            await SaveBool(MessagePreviewEnabledKey, req.MessagePreviewEnabled);

            var values = await LoadPreferences(db, userId.Value, ct);
            return Results.Ok(ToDto(values));
        });
    }

    public static async Task EnsureTables(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await conn.Cmd("""
            CREATE TABLE IF NOT EXISTS web_user_preferences (
                user_id uuid NOT NULL,
                preference_key varchar(120) NOT NULL,
                preference_value text NOT NULL DEFAULT '',
                updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT pk_web_user_preferences PRIMARY KEY (user_id, preference_key)
            );
            """)
            .ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, string>> LoadPreferences(Database db, Guid userId, CancellationToken ct)
    {
        await EnsureTables(db, ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = await db.OpenAsync(ct);
        await using var reader = await conn.Cmd(@"
            SELECT preference_key, preference_value
            FROM web_user_preferences
            WHERE user_id = @userId
              AND preference_key IN (@water, @eye, @keepCreate, @messagePreview);")
            .With("@userId", userId)
            .With("@water", WaterReminderEnabledKey)
            .With("@eye", EyeReminderEnabledKey)
            .With("@keepCreate", KeepCreateVoucherOpenKey)
            .With("@messagePreview", MessagePreviewEnabledKey)
            .ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            values[reader.Str("preference_key")] = reader.Str("preference_value");

        return values;
    }

    private static UserPreferencesDto ToDto(IReadOnlyDictionary<string, string> values)
        => new(
            ParseBool(values, WaterReminderEnabledKey, defaultValue: true),
            ParseBool(values, EyeReminderEnabledKey, defaultValue: true),
            ParseBool(values, KeepCreateVoucherOpenKey, defaultValue: false),
            ParseBool(values, MessagePreviewEnabledKey, defaultValue: true));

    private static bool ParseBool(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        => values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed) ? parsed : defaultValue;

    private static Guid? UserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
