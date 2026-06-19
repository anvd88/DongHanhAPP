using System.Security.Claims;
using KetoanMini.Api.Data;
using Microsoft.Data.SqlClient;

namespace KetoanMini.Api.Endpoints;

public static class ApiHelpers
{
    public static string Username(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? "";

    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.IsInRole("Admin");

    /// <summary>Ghi nhật ký hoạt động — giống RecordAudit của app desktop.</summary>
    public static async Task RecordAudit(this Database db, string username, string action, string entity, string entityName, string details)
    {
        try
        {
            await using var conn = await db.OpenAsync();
            await conn.Cmd(@"INSERT INTO dbo.audit_logs (occurred_at, username, action, entity, entity_name, details)
                             VALUES (SYSUTCDATETIME(), @u, @a, @e, @en, @d)")
                .With("@u", username).With("@a", action).With("@e", entity)
                .With("@en", entityName).With("@d", details)
                .ExecuteNonQueryAsync();
        }
        catch { /* không để lỗi audit chặn nghiệp vụ */ }
    }
}
