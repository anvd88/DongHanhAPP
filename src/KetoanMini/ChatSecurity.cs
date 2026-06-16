using System.Security.Cryptography;
using System.Text;

namespace KetoanMini;

public sealed record UserKeyPair(string PublicKeyPem, string PrivateKeyPem);

public sealed record E2eeChatPayload(
    string CipherText,
    string Nonce,
    string AuthTag,
    string EncryptedKeyForSender,
    string EncryptedKeyForReceiver);

public static class KeyStorageService
{
    private static readonly byte[] Entropy = "KetoanMini.ChatE2EE.v1"u8.ToArray();

    public static string? TryLoadPrivateKey(Guid userId)
    {
        var path = PrivateKeyPath(userId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(path));
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static void SavePrivateKey(Guid userId, string privateKeyPem)
    {
        Directory.CreateDirectory(KeyDirectory);
        var bytes = Encoding.UTF8.GetBytes(privateKeyPem);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllText(PrivateKeyPath(userId), Convert.ToBase64String(protectedBytes), Encoding.UTF8);
    }

    private static string PrivateKeyPath(Guid userId)
    {
        return Path.Combine(KeyDirectory, $"{userId:D}.key");
    }

    private static string KeyDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KetoanMini", "chat-keys");
}

public static class ChatCryptoService
{
    private const int AesKeySize = 32;
    private const int AesNonceSize = 12;
    private const int AesTagSize = 16;

    public static UserKeyPair GenerateUserKeyPair()
    {
        using var rsa = RSA.Create(3072);
        return new UserKeyPair(
            rsa.ExportSubjectPublicKeyInfoPem(),
            rsa.ExportPkcs8PrivateKeyPem());
    }

    public static E2eeChatPayload EncryptForUsers(string plainText, string senderPublicKeyPem, string receiverPublicKeyPem)
    {
        return EncryptBytes(Encoding.UTF8.GetBytes(plainText), senderPublicKeyPem, receiverPublicKeyPem);
    }

    public static string DecryptText(E2eeChatPayload payload, Guid currentUserId, Guid senderId, Guid receiverId, string privateKeyPem)
    {
        return Encoding.UTF8.GetString(DecryptBytes(payload, currentUserId, senderId, receiverId, privateKeyPem));
    }

    public static E2eeChatPayload EncryptBytes(byte[] plainBytes, string senderPublicKeyPem, string receiverPublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(senderPublicKeyPem) || string.IsNullOrWhiteSpace(receiverPublicKeyPem))
        {
            throw new InvalidOperationException("Người gửi hoặc người nhận chưa có khóa chat bảo mật.");
        }

        var aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        var nonce = RandomNumberGenerator.GetBytes(AesNonceSize);
        var tag = new byte[AesTagSize];
        var cipherBytes = new byte[plainBytes.Length];

        using (var aes = new AesGcm(aesKey, AesTagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        return new E2eeChatPayload(
            Convert.ToBase64String(cipherBytes),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            EncryptKeyForPublicKey(aesKey, senderPublicKeyPem),
            EncryptKeyForPublicKey(aesKey, receiverPublicKeyPem));
    }

    public static byte[] DecryptBytes(E2eeChatPayload payload, Guid currentUserId, Guid senderId, Guid receiverId, string privateKeyPem)
    {
        var encryptedKey = currentUserId == senderId
            ? payload.EncryptedKeyForSender
            : currentUserId == receiverId
                ? payload.EncryptedKeyForReceiver
                : throw new InvalidOperationException("Tài khoản hiện tại không thuộc cuộc trò chuyện này.");

        var aesKey = DecryptKeyWithPrivateKey(encryptedKey, privateKeyPem);
        var cipherBytes = Convert.FromBase64String(payload.CipherText);
        var nonce = Convert.FromBase64String(payload.Nonce);
        var tag = Convert.FromBase64String(payload.AuthTag);
        var plainBytes = new byte[cipherBytes.Length];

        using (var aes = new AesGcm(aesKey, AesTagSize))
        {
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }

        return plainBytes;
    }

    private static string EncryptKeyForPublicKey(byte[] aesKey, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return Convert.ToBase64String(rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256));
    }

    private static byte[] DecryptKeyWithPrivateKey(string encryptedKey, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        return rsa.Decrypt(Convert.FromBase64String(encryptedKey), RSAEncryptionPadding.OaepSHA256);
    }
}
