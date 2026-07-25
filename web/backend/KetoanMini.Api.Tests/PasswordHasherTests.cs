using System.Security.Cryptography;
using KetoanMini.Api.Security;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Băm mật khẩu Argon2id + tương thích ngược PBKDF2. Thuần tính toán, KHÔNG cần CSDL nên luôn chạy được.
/// </summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_Then_Verify_RoundTrips_And_Uses_Argon2id()
    {
        var hash = PasswordHasher.Hash("m4t-kh4u!");

        Assert.StartsWith("ARGON2ID$", hash);
        Assert.True(PasswordHasher.Verify("m4t-kh4u!", hash));
        Assert.False(PasswordHasher.Verify("sai-mat-khau", hash));
    }

    [Fact]
    public void Hash_Is_Salted_So_Two_Hashes_Of_Same_Password_Differ()
    {
        Assert.NotEqual(PasswordHasher.Hash("trung-mat-khau"), PasswordHasher.Hash("trung-mat-khau"));
    }

    [Fact]
    public void Fresh_Argon2id_Hash_Does_Not_Need_Rehash()
    {
        Assert.False(PasswordHasher.NeedsRehash(PasswordHasher.Hash("abc123")));
    }

    [Fact]
    public void Legacy_Pbkdf2_Hash_Still_Verifies_But_Needs_Rehash()
    {
        var legacy = MakeLegacyPbkdf2("mat-khau-cu", iterations: 100_000);

        Assert.True(PasswordHasher.Verify("mat-khau-cu", legacy));
        Assert.False(PasswordHasher.Verify("khac", legacy));
        Assert.True(PasswordHasher.NeedsRehash(legacy)); // PBKDF2 → phải nâng lên Argon2id
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong-phai-dinh-dang-hop-le")]
    [InlineData("ARGON2ID$v=19$m=19456,t=2,p=1$khong-phai-base64$cung-vay")]
    public void Malformed_Hash_Fails_Verify_And_Wants_Rehash(string stored)
    {
        Assert.False(PasswordHasher.Verify("bat-ky", stored));
        Assert.True(PasswordHasher.NeedsRehash(stored));
    }

    /// <summary>Dựng đúng định dạng hash PBKDF2 cũ (PBKDF2$iters$salt$hash) mà app desktop từng lưu.</summary>
    private static string MakeLegacyPbkdf2(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
