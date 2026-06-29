using KetoanMini.Api.Data;
using KetoanMini.Api.Models;

namespace KetoanMini.Api.Endpoints;

public static class ReleaseEndpoints
{
    public static void MapReleases(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/releases").RequireAuthorization(p => p.RequireRole("Admin"));

        g.MapGet("/", async (Database db) =>
        {
            await using var conn = await db.OpenAsync();
            var list = new List<ReleaseDto>();
            await using var r = await conn.Cmd(
                @"SELECT id, version, release_notes, is_mandatory, is_published, published_at, published_by
                  FROM app_releases ORDER BY id DESC LIMIT 50").ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new ReleaseDto(r.Long("id"), r.Str("version"), r.Str("release_notes"),
                    r.Bool("is_mandatory"), r.Bool("is_published"), r.Dt("published_at"), r.Str("published_by")));
            return Results.Ok(list);
        });
    }
}
