using KetoanMini.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class QrActionServiceTests
{
    [Fact]
    public void DecisionToken_IsBoundToUserSessionAction_AndRejectsTamperingOrExpiry()
    {
        var directory = Directory.CreateTempSubdirectory("qr-action-token-");
        try
        {
            var service = new QrActionTokenService(DataProtectionProvider.Create(directory));
            var created = service.Create("web_login", "ketoanmini-login:test", "alice", "sid-a",
                ["confirm", "reject"], TimeSpan.FromMinutes(5));

            Assert.True(service.TryRead(created.Value, "alice", "sid-a", "confirm", out var ticket));
            Assert.Equal("web_login", ticket.Handler);
            Assert.False(service.TryRead(created.Value, "ALICE", "sid-a", "confirm", out _));
            Assert.False(service.TryRead(created.Value, "bob", "sid-a", "confirm", out _));
            Assert.False(service.TryRead(created.Value, "alice", "sid-b", "confirm", out _));
            Assert.False(service.TryRead(created.Value, "alice", "sid-a", "other", out _));
            Assert.False(service.TryRead(created.Value[..^1] + (created.Value[^1] == 'A' ? "B" : "A"),
                "alice", "sid-a", "confirm", out _));

            var expired = service.Create("web_login", "ketoanmini-login:test", "alice", "sid-a",
                ["confirm"], TimeSpan.FromSeconds(-1));
            Assert.False(service.TryRead(expired.Value, "alice", "sid-a", "confirm", out _));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://example.com/help", true)]
    [InlineData("HTTPS://EXAMPLE.COM:443/help", true)]
    [InlineData("https://a.example.org/help", true)]
    [InlineData("https://example.org/help", false)]
    [InlineData("https://badexample.org/help", false)]
    [InlineData("https://example.org.evil.test/help", false)]
    [InlineData("http://example.com/help", false)]
    [InlineData("https://user@example.com/help", false)]
    [InlineData("https://127.0.0.1/help", false)]
    [InlineData("https://[::1]/help", false)]
    [InlineData("https://localhost/help", false)]
    [InlineData("file:///tmp/help", false)]
    [InlineData("javascript:alert(1)", false)]
    public void ConfiguredUrl_RequiresSafeHttpsAndHostBoundary(string url, bool expected)
    {
        var options = new StaticOptionsMonitor<QrScannerOptions>(new QrScannerOptions
        {
            // Kể cả cấu hình nhầm IP/localhost vào allowlist vẫn phải fail-closed.
            AllowedHttpsHosts = ["example.com", "*.example.org", "127.0.0.1", "::1", "[::1]", "localhost"]
        });
        var registry = new QrConfiguredActionRegistry(options);
        var action = new QrConfiguredAction { Url = url };
        Assert.Equal(expected, registry.TryGetAllowedHttpsUrl(action, out _));
    }

    [Fact]
    public void Registry_PrefersExactThenLongestPrefix()
    {
        var options = new StaticOptionsMonitor<QrScannerOptions>(new QrScannerOptions
        {
            Actions =
            [
                new() { Id = "short", Prefix = "company:" },
                new() { Id = "long", Prefix = "company:help:" },
                new() { Id = "exact", ExactValue = "company:help:home" }
            ]
        });
        var registry = new QrConfiguredActionRegistry(options);
        Assert.Equal("exact", registry.Match("company:help:home")?.Id);
        Assert.Equal("long", registry.Match("company:help:other")?.Id);
        Assert.Equal("short", registry.Match("company:news")?.Id);
    }

    [Fact]
    public void Registry_FailsClosedWhenListsAreNullFromConfiguration()
    {
        var options = new StaticOptionsMonitor<QrScannerOptions>(new QrScannerOptions
        {
            Actions = null!,
            AllowedHttpsHosts = null!
        });
        var registry = new QrConfiguredActionRegistry(options);

        Assert.Null(registry.Match("anything"));
        Assert.False(registry.TryGetAllowedHttpsUrl(
            new QrConfiguredAction { Url = "https://example.com/help" }, out _));
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
