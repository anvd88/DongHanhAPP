using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CashFundMessagingTests(ApiFactory factory)
{
    [Fact]
    public async Task CreateAndReverse_AreIdempotentTransactionalAndVersionProtected()
    {
        var username = "cash-msg-" + Guid.NewGuid().ToString("N")[..10];
        var userId = Guid.NewGuid();
        var db = factory.Services.GetRequiredService<Database>();
        await using (var seed = await db.OpenAsync())
            await seed.Cmd("""
                INSERT INTO app_users(id,username,full_name,role,password_hash,is_active,approval_status,is_deleted)
                VALUES (@id,@username,'Cash messaging test','Cashier',@password,TRUE,'Approved',FALSE)
                """).With("@id", userId).With("@username", username)
                .With("@password", PasswordHasher.Hash("test-pass")).ExecuteNonQueryAsync();

        var tokens = factory.Services.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(userId, username, "Cash messaging test", "",
            AppRoles.Cashier, true, "Approved", DateTime.UtcNow));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createKey = Guid.NewGuid().ToString("N");
        var request = new { direction = "in", amount = 123456m, reason = "integration test", counterparty = "test" };
        client.DefaultRequestHeaders.Add("Idempotency-Key", createKey);
        var first = await client.PostAsJsonAsync("/api/cash-fund/entries", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = firstBody.GetProperty("id").GetGuid();
        var entryNo = firstBody.GetProperty("entryNo").GetString()!;
        Assert.Equal(1, firstBody.GetProperty("version").GetInt64());
        Assert.Equal("\"1\"", first.Headers.ETag?.ToString());

        var replay = await client.PostAsJsonAsync("/api/cash-fund/entries", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(id, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        await using (var check = await db.OpenAsync())
        {
            Assert.Equal(1, Convert.ToInt32(await check.Cmd(
                "SELECT COUNT(*) FROM cash_fund_manual_entries WHERE id=@id").With("@id", id).ExecuteScalarAsync()));
            Assert.Equal(1, Convert.ToInt32(await check.Cmd(
                "SELECT COUNT(*) FROM audit_logs WHERE entity='CashFund' AND entity_name=@entry")
                .With("@entry", entryNo).ExecuteScalarAsync()));
            Assert.Equal(1, Convert.ToInt32(await check.Cmd("""
                SELECT COUNT(*) FROM integration_outbox
                WHERE event_type='accounting.cash-fund.entry-created.v1' AND aggregate_id=@id
                """).With("@id", id.ToString()).ExecuteScalarAsync()));
        }

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        var reversed = await client.PostAsJsonAsync($"/api/cash-fund/entries/{id}/reverse",
            new { reason = "reverse test" });
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        Assert.Equal(2, (await reversed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64());

        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var stale = await client.PostAsJsonAsync($"/api/cash-fund/entries/{id}/reverse",
            new { reason = "stale retry" });
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        await using var cleanup = await db.OpenAsync();
        await cleanup.Cmd("DELETE FROM api_idempotency WHERE username=@username")
            .With("@username", username).ExecuteNonQueryAsync();
        await cleanup.Cmd("DELETE FROM integration_outbox WHERE aggregate_id=@id")
            .With("@id", id.ToString()).ExecuteNonQueryAsync();
        await cleanup.Cmd("DELETE FROM audit_logs WHERE entity='CashFund' AND entity_name=@entry")
            .With("@entry", entryNo).ExecuteNonQueryAsync();
        await cleanup.Cmd("DELETE FROM cash_fund_manual_entries WHERE id=@id")
            .With("@id", id).ExecuteNonQueryAsync();
        await cleanup.Cmd("DELETE FROM app_users WHERE id=@id").With("@id", userId).ExecuteNonQueryAsync();
    }
}
