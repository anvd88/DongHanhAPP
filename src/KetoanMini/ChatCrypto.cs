using System.Security.Cryptography;
using System.Text;

namespace KetoanMini;

internal readonly record struct ChatEncryptedPayload(string CipherText, string Nonce);

internal static class ChatCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private const string Purpose = "KetoanMini.LanChat.v1";

    public static ChatEncryptedPayload EncryptForPair(string username1, string username2, string plainText)
    {
        var key = DerivePairKey(username1, username2);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var combined = new byte[cipherBytes.Length + tag.Length];
        Buffer.BlockCopy(cipherBytes, 0, combined, 0, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, combined, cipherBytes.Length, tag.Length);

        return new ChatEncryptedPayload(Convert.ToBase64String(combined), Convert.ToBase64String(nonce));
    }

    public static string DecryptForPair(string username1, string username2, string cipherText, string nonceText)
    {
        if (string.IsNullOrWhiteSpace(cipherText) || string.IsNullOrWhiteSpace(nonceText))
        {
            return "";
        }

        var key = DerivePairKey(username1, username2);
        var combined = Convert.FromBase64String(cipherText);
        var nonce = Convert.FromBase64String(nonceText);
        if (combined.Length < TagSize)
        {
            return "";
        }

        var cipherBytes = combined[..^TagSize];
        var tag = combined[^TagSize..];
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DerivePairKey(string username1, string username2)
    {
        var names = new[] { Normalize(username1), Normalize(username2) }
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var pair = $"{names[0]}|{names[1]}";
        using var kdf = new Rfc2898DeriveBytes(
            $"{Purpose}|{pair}",
            Encoding.UTF8.GetBytes(Purpose),
            Iterations,
            HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeySize);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
