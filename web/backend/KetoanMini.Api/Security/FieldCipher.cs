using System.Security.Cryptography;
using KetoanMini.Api.Services;

namespace KetoanMini.Api.Security;

/// <summary>
/// Mã hóa dữ liệu nhạy cảm khi LƯU TRỮ (at-rest) bằng AES-256-GCM (bảo mật + toàn vẹn).
/// Định dạng blob: [magic "KME1" (4B)] [nonce (12B)] [tag (16B)] [ciphertext].
/// Khóa lấy từ <c>Security:FieldEncryptionKey</c> (base64 của 32 byte). Nếu thiếu/không hợp lệ
/// → chế độ "passthrough" (lưu KHÔNG mã hóa) kèm cảnh báo, để môi trường dev chưa cấu hình vẫn
/// chạy được. Nhờ magic prefix, dữ liệu cũ (chưa mã hóa) vẫn đọc được → di trú dần, không gãy.
/// </summary>
public sealed class FieldCipher
{
    private static readonly byte[] Magic = "KME1"u8.ToArray();
    private const int NonceLen = 12;
    private const int TagLen = 16;
    private const int HeaderLen = 4 + NonceLen + TagLen; // magic + nonce + tag

    private readonly byte[]? _key;

    public bool Enabled => _key is not null;

    public FieldCipher(IConfiguration config, ILogger<FieldCipher> logger)
    {
        var raw = config["Security:FieldEncryptionKey"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("Security:FieldEncryptionKey trong → du lieu nhay cam se luu KHONG ma hoa (chi nen o Development).");
            return;
        }
        byte[] key;
        try { key = Convert.FromBase64String(raw.Trim()); }
        catch
        {
            logger.LogWarning("Security:FieldEncryptionKey khong phai base64 hop le → bo qua ma hoa.");
            return;
        }
        if (key.Length != 32)
        {
            logger.LogWarning("Security:FieldEncryptionKey phai la 32 byte (AES-256) sau khi giai base64 → bo qua ma hoa.");
            return;
        }
        _key = key;
    }

    /// <summary>Blob đã ở dạng mã hóa (bắt đầu bằng magic prefix) hay chưa.</summary>
    public static bool IsEncrypted(byte[] data)
        => data.Length >= HeaderLen
           && data[0] == Magic[0] && data[1] == Magic[1] && data[2] == Magic[2] && data[3] == Magic[3];

    public byte[] Encrypt(byte[] plaintext)
    {
        if (_key is null) return plaintext; // passthrough khi chưa cấu hình khóa
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagLen];
        using var aes = new AesGcm(_key, TagLen);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        var outBuf = new byte[HeaderLen + cipher.Length];
        Buffer.BlockCopy(Magic, 0, outBuf, 0, 4);
        Buffer.BlockCopy(nonce, 0, outBuf, 4, NonceLen);
        Buffer.BlockCopy(tag, 0, outBuf, 4 + NonceLen, TagLen);
        Buffer.BlockCopy(cipher, 0, outBuf, HeaderLen, cipher.Length);
        return outBuf;
    }

    public byte[] Decrypt(byte[] data)
    {
        if (!IsEncrypted(data)) return data; // dữ liệu cũ chưa mã hóa → dùng nguyên trạng
        if (_key is null)
            throw new InvalidOperationException("Du lieu da ma hoa nhung thieu khoa Security:FieldEncryptionKey.");

        var nonce = new byte[NonceLen];
        var tag = new byte[TagLen];
        var cipherLen = data.Length - HeaderLen;
        var cipher = new byte[cipherLen];
        Buffer.BlockCopy(data, 4, nonce, 0, NonceLen);
        Buffer.BlockCopy(data, 4 + NonceLen, tag, 0, TagLen);
        Buffer.BlockCopy(data, HeaderLen, cipher, 0, cipherLen);

        var plain = new byte[cipherLen];
        using var aes = new AesGcm(_key, TagLen);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    // ----- Tiện ích cho vector đặc trưng khuôn mặt (dữ liệu sinh trắc nhạy cảm) -----
    public byte[] EncryptEmbedding(float[] embedding) => Encrypt(EmbeddingCodec.ToBytes(embedding));

    public float[] DecryptEmbedding(byte[] stored)
    {
        // Khi khóa đã được cấu hình, kho sinh trắc học phải hoàn toàn ở dạng AES-GCM. Không dùng
        // nhánh tương thích plaintext của Decrypt(): nếu một import/lỗi DB chèn blob thô sau startup,
        // request đầu tiên phải fail-closed thay vì âm thầm dùng dữ liệu chưa được xác thực.
        if (_key is not null && !IsEncrypted(stored))
            throw new InvalidOperationException(
                "Biometric embedding is not AES-GCM encrypted with the KME1 format.");

        return EmbeddingCodec.FromBytes(Decrypt(stored));
    }
}
