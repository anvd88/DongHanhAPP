using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KetoanMini.Api.Data;
using KetoanMini.Api.Models;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Kiểm thử tích hợp đăng/tải bản cập nhật APK.
///  • APK thật 40–100MB nên phải vượt qua được trần body JSON (16MB) — trần riêng của endpoint tải APK.
///  • Web gọi "/api/releases/" (CÓ dấu / cuối) nên mọi chốt chặn theo path phải khớp cả hai dạng.
/// Dùng app_target riêng để không đụng các bản 'hr-apk' thật trong DB.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReleaseTests : IAsyncLifetime
{
    private const string Admin = "__test_release_admin__";
    private const string Target = "test-apk";
    private readonly ApiFactory _factory;

    public ReleaseTests(ApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd(
            @"INSERT INTO app_users
                 (id, username, full_name, email, role, password_hash, is_active, approval_status, approved_at, approved_by, created_at, is_deleted)
              VALUES (@id, @u, @u, '', 'Admin', @ph, TRUE, 'Approved', CURRENT_TIMESTAMP, 'test', CURRENT_TIMESTAMP, FALSE)
              ON CONFLICT (username) DO UPDATE SET is_active=TRUE, is_deleted=FALSE, role='Admin', approval_status='Approved'")
            .With("@id", Guid.NewGuid()).With("@u", Admin).With("@ph", PasswordHasher.Hash("test-pass"))
            .ExecuteNonQueryAsync();
        await CleanReleasesAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanReleasesAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await conn.Cmd("DELETE FROM app_users WHERE username=@u").With("@u", Admin).ExecuteNonQueryAsync();
    }

    private async Task CleanReleasesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        await using (var r = await conn.Cmd("SELECT id FROM app_releases WHERE app_target=@t")
            .With("@t", Target).ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) ReleaseStorage.TryDelete(r.GetInt64(0));
        }
        await conn.Cmd("DELETE FROM app_releases WHERE app_target=@t").With("@t", Target).ExecuteNonQueryAsync();
    }

    /// <summary>
    /// APK 17MB vượt trần body JSON (16MB) nhưng vẫn dưới trần APK (200MB) → phải đăng được.
    /// Trước đây chốt chặn 413 so path bằng "/api/releases" nên "/api/releases/" trượt về trần JSON.
    /// </summary>
    [Fact]
    public async Task Upload_ApkLargerThanJsonBodyLimit_IsAccepted()
    {
        var admin = await AdminClientAsync();
        var apk = FakeApk(17 * 1024 * 1024);

        var res = await admin.PostAsync("/api/releases/", ApkForm(apk, version: "9.0", versionCode: 900100));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    /// <summary>APK phải nằm trên ĐĨA và DB chỉ giữ metadata — tải về đúng từng byte, đúng SHA-256.</summary>
    [Fact]
    public async Task Upload_StoresApkOnDisk_NotInDatabase_AndDownloadRoundTrips()
    {
        var admin = await AdminClientAsync();
        var apk = FakeApk(3 * 1024 * 1024);
        var expectedSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(apk)).ToLowerInvariant();

        var created = await admin.PostAsync("/api/releases/", ApkForm(apk, version: "9.1", versionCode: 900101));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<ReleaseRow>();
        Assert.NotNull(dto);
        Assert.Equal(apk.LongLength, dto!.ApkSize);
        Assert.Equal(expectedSha, dto.ApkSha256);

        // Tệp nằm trên đĩa, cột bytea bỏ trống.
        Assert.True(File.Exists(ReleaseStorage.ApkPath(dto.Id)));
        Assert.Equal(apk.LongLength, new FileInfo(ReleaseStorage.ApkPath(dto.Id)).Length);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await using var r = await conn.Cmd(
                "SELECT has_apk, apk_data IS NULL AS blob_empty FROM app_releases WHERE id=@id")
                .With("@id", dto.Id).ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.True(r.GetBoolean(0));
            Assert.True(r.GetBoolean(1));
        }

        var downloaded = await admin.GetByteArrayAsync($"/api/releases/{dto.Id}/download");
        Assert.Equal(apk, downloaded);

        // Xóa bản phát hành phải dọn luôn tệp trên đĩa, không để rác chiếm chỗ.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/releases/{dto.Id}")).StatusCode);
        Assert.False(File.Exists(ReleaseStorage.ApkPath(dto.Id)));
    }

    /// <summary>Bản cũ còn nằm trong cột apk_data phải được chuyển ra đĩa lúc khởi động rồi tải được bình thường.</summary>
    [Fact]
    public async Task LegacyBlobInDatabase_MigratesToDisk()
    {
        var apk = FakeApk(64 * 1024);
        long id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            id = Convert.ToInt64(await conn.Cmd(
                @"INSERT INTO app_releases
                    (app_target, version, version_code, release_notes, is_mandatory, is_published,
                     apk_file_name, apk_size, apk_sha256, has_apk, apk_data)
                  VALUES (@t, '8.0', 800100, '', FALSE, TRUE, 'legacy.apk', @size, '', FALSE, @data)
                  RETURNING id")
                .With("@t", Target).With("@size", apk.LongLength).With("@data", apk)
                .ExecuteScalarAsync());

            var logger = scope.ServiceProvider.GetRequiredService<ILogger<ReleaseTests>>();
            await ReleaseStorage.MigrateDatabaseBlobsAsync(db, logger);
        }

        Assert.True(File.Exists(ReleaseStorage.ApkPath(id)));
        Assert.Equal(apk, await File.ReadAllBytesAsync(ReleaseStorage.ApkPath(id)));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Database>();
            await using var conn = await db.OpenAsync();
            await using var r = await conn.Cmd(
                "SELECT has_apk, apk_data IS NULL AS blob_empty FROM app_releases WHERE id=@id")
                .With("@id", id).ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.True(r.GetBoolean(0));   // đã bật cờ → mới được quảng bá cho app
            Assert.True(r.GetBoolean(1));   // bytea đã bỏ trống → DB không còn phình
        }

        var admin = await AdminClientAsync();
        Assert.Equal(apk, await admin.GetByteArrayAsync($"/api/releases/{id}/download"));
        ReleaseStorage.TryDelete(id);
    }

    private sealed record ReleaseRow(long Id, long ApkSize, string ApkSha256);

    private static byte[] FakeApk(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    private static MultipartFormDataContent ApkForm(byte[] apk, string version, int versionCode, bool published = true)
    {
        var file = new ByteArrayContent(apk);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.android.package-archive");
        return new MultipartFormDataContent
        {
            { file, "apk", $"ketoan-hr-{version}.apk" },
            { new StringContent(Target), "appTarget" },
            { new StringContent(version), "version" },
            { new StringContent(versionCode.ToString()), "versionCode" },
            { new StringContent("Ban thu nghiem"), "releaseNotes" },
            { new StringContent("false"), "isMandatory" },
            { new StringContent(published ? "true" : "false"), "isPublished" },
        };
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Database>();
        await using var conn = await db.OpenAsync();
        var id = (Guid)(await conn.Cmd("SELECT id FROM app_users WHERE username=@u").With("@u", Admin).ExecuteScalarAsync())!;
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokens.CreateToken(new UserDto(id, Admin, Admin, "", "Admin", true, "Approved", DateTime.UtcNow));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
