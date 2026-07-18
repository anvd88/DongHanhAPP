using System.Net;
using KetoanMini.Api.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace KetoanMini.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class InfrastructureTests
{
    private readonly ApiFactory _factory;
    public InfrastructureTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task OpenApiContract_IsPublished()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"openapi\"", json);
        Assert.Contains("/api/auth/login", json);
    }

    [Fact]
    public async Task BaselineMigration_IsRecordedAndIdempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await PostgresSchema.EnsureAsync(db, new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await PostgresSchema.EnsureAsync(db, new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        await using var connection = await db.OpenAsync();
        var count = Convert.ToInt32(await connection.Cmd(
            "SELECT COUNT(*) FROM schema_migrations WHERE version='001_baseline'").ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public void EveryNonPublicApiEndpoint_DeclaresAuthorization()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.RoutePattern.RawText is not "/api/info" and not "/api/health")
            .ToList();

        var missing = endpoints.Where(e =>
                e.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.RoutePattern.RawText)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        Assert.True(missing.Length == 0,
            "API endpoints must explicitly require authentication or explicitly opt into AllowAnonymous: " +
            string.Join(", ", missing));
    }
}
