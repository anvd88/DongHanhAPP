using KetoanMini.Api.Endpoints;
using KetoanMini.Api.Security;
using KetoanMini.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class ProductionSecurityValidatorTests
{
    private static readonly string ValidFieldKey = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Production_AcceptsFieldEncryptionKeyThatDecodesToExactly32Bytes()
    {
        var builder = CreateProductionBuilder(ValidFieldKey);

        ProductionSecurityValidator.Validate(builder);
    }

    [Theory]
    [MemberData(nameof(InvalidFieldKeys))]
    public void Production_RejectsMalformedOrWrongLengthFieldEncryptionKey(string fieldKey)
    {
        var builder = CreateProductionBuilder(fieldKey);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityValidator.Validate(builder));

        Assert.Contains("Security:FieldEncryptionKey", error.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> InvalidFieldKeys()
    {
        yield return [""];
        yield return ["not-base64-not-base64-not-base64-not-base64"];
        yield return [Convert.ToBase64String(new byte[31])];
        yield return [Convert.ToBase64String(new byte[33])];
        // A 32-character Base64 string decodes to 24 bytes. This used to pass the old length-only guard.
        yield return ["12345678901234567890123456789012"];
    }

    [Fact]
    public void BiometricIntegrity_RejectsWrongKeyCorruptionAndInvalidVector()
    {
        var correctCipher = CreateCipher(7);
        var wrongCipher = CreateCipher(89);
        var encrypted = correctCipher.EncryptEmbedding(
            Enumerable.Repeat(0.01f, 512).ToArray());

        ChamCongEndpoints.ValidateEncryptedEmbedding(
            correctCipher, encrypted, "active", "1");

        var wrongKeyError = Assert.Throws<InvalidOperationException>(() =>
            ChamCongEndpoints.ValidateEncryptedEmbedding(
                wrongCipher, encrypted, "active", "1"));
        Assert.Contains("Cannot authenticate biometric", wrongKeyError.Message,
            StringComparison.Ordinal);

        var corrupted = (byte[])encrypted.Clone();
        corrupted[^1] ^= 0x01;
        Assert.Throws<InvalidOperationException>(() =>
            ChamCongEndpoints.ValidateEncryptedEmbedding(
                correctCipher, corrupted, "staging", "request:1"));

        var invalidVector = correctCipher.EncryptEmbedding(new float[4]);
        var invalidVectorError = Assert.Throws<InvalidOperationException>(() =>
            ChamCongEndpoints.ValidateEncryptedEmbedding(
                correctCipher, invalidVector, "active", "2"));
        Assert.Contains("invalid embedding", invalidVectorError.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BiometricIntegrity_RejectsPlaintextAtRuntimeAndUnsafeLegacyMigration()
    {
        var cipher = CreateCipher(7);
        var validPlaintext = EmbeddingCodec.ToBytes(
            Enumerable.Repeat(0.01f, 512).ToArray());

        Assert.Throws<InvalidOperationException>(() =>
            cipher.DecryptEmbedding(validPlaintext));
        ChamCongEndpoints.ValidatePlaintextEmbeddingForMigration(validPlaintext, 1);

        var corruptedCiphertextWithoutMagic = new byte[2080];
        var lengthError = Assert.Throws<InvalidOperationException>(() =>
            ChamCongEndpoints.ValidatePlaintextEmbeddingForMigration(
                corruptedCiphertextWithoutMagic, 2));
        Assert.Contains("unexpected length", lengthError.Message, StringComparison.Ordinal);

        var nonFinite = Enumerable.Repeat(0.01f, 512).ToArray();
        nonFinite[12] = float.NaN;
        var finiteError = Assert.Throws<InvalidOperationException>(() =>
            ChamCongEndpoints.ValidatePlaintextEmbeddingForMigration(
                EmbeddingCodec.ToBytes(nonFinite), 3));
        Assert.Contains("invalid embedding", finiteError.Message, StringComparison.Ordinal);
    }

    private static WebApplicationBuilder CreateProductionBuilder(string fieldKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "jwt-production-key-2026-with-at-least-32-characters",
            ["Security:FieldEncryptionKey"] = fieldKey,
            ["ConnectionStrings:KetoanMini"] =
                "Host=localhost;Database=ketoanmini;Username=ketoanmini;Password=StrongDbSecret_2026",
            ["Bootstrap:AdminUsername"] = "root-operator",
            ["Bootstrap:AdminPassword"] = "StrongBootstrapSecret_2026!",
        });
        return builder;
    }

    private static FieldCipher CreateCipher(byte seed)
    {
        var rawKey = Enumerable.Range(0, 32)
            .Select(i => unchecked((byte)(seed + i)))
            .ToArray();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:FieldEncryptionKey"] = Convert.ToBase64String(rawKey),
            })
            .Build();

        return new FieldCipher(config, NullLogger<FieldCipher>.Instance);
    }
}
