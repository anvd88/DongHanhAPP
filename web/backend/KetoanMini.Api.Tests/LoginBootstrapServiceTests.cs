using KetoanMini.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class LoginBootstrapServiceTests
{
    [Fact]
    public void VeBootstrap_RangBuocSidUserAgent_ChongSuaVaTuHetHan()
    {
        var directory = Directory.CreateTempSubdirectory("login-bootstrap-");
        try
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero));
            var service = new LoginBootstrapService(DataProtectionProvider.Create(directory), clock);
            var created = service.Create("sid-a", "Browser A", TimeSpan.FromMinutes(5));

            Assert.True(service.TryRead(created.Value, "sid-a", "Browser A"));
            Assert.False(service.TryRead(created.Value, "sid-b", "Browser A"));
            Assert.False(service.TryRead(created.Value, "sid-a", "Browser B"));
            // Đổi một byte ở giữa payload. Ký tự cuối Base64Url có thể chỉ chứa các bit đệm,
            // nên đổi riêng ký tự cuối đôi lúc vẫn giải mã thành đúng cùng một chuỗi byte.
            var tamperedChars = created.Value.ToCharArray();
            var tamperedIndex = tamperedChars.Length / 2;
            tamperedChars[tamperedIndex] = tamperedChars[tamperedIndex] == 'A' ? 'B' : 'A';
            Assert.False(service.TryRead(new string(tamperedChars), "sid-a", "Browser A"));
            Assert.False(service.TryRead(null, "sid-a", "Browser A"));
            Assert.False(service.TryRead(new string('x', 4_097), "sid-a", "Browser A"));

            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.False(service.TryRead(created.Value, "sid-a", "Browser A"));
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public void MoiLanKhoiTao_PhatMotVeKhacNhau()
    {
        var directory = Directory.CreateTempSubdirectory("login-bootstrap-");
        try
        {
            var service = new LoginBootstrapService(DataProtectionProvider.Create(directory));
            var first = service.Create("sid-a", "Browser A");
            var second = service.Create("sid-a", "Browser A");
            Assert.NotEqual(first.Value, second.Value);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
