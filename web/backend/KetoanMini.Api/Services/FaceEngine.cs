namespace KetoanMini.Api.Services;

/// <summary>
/// Bộ máy nhận diện khuôn mặt — TÁCH RIÊNG phần học máy khỏi nghiệp vụ chấm công.
/// Toàn bộ endpoint chấm công chỉ phụ thuộc interface này, nên khi đổi engine chỉ cần
/// viết 1 lớp mới và đổi đăng ký DI trong Program.cs — không sửa nghiệp vụ.
///
/// Mặc định đang chạy <see cref="OpenCvSFaceEngine"/> (OpenCV YuNet + SFace, nhận diện THẬT).
/// Vẫn giữ <see cref="PlaceholderFaceEngine"/> để chạy thử khi máy thiếu model/thư viện.
/// </summary>
public interface IFaceEngine
{
    /// <summary>Tên engine đang dùng (hiển thị ở /api/chamcong/trangthai để biết đã cắm thật chưa).</summary>
    string Name { get; }

    /// <summary>Đã là engine nhận diện THẬT chưa (false = đang chạy bản giả lập).</summary>
    bool IsReal { get; }

    /// <summary>Ngưỡng khớp khuyến nghị: similarity ≥ ngưỡng ⇒ coi là cùng một người.</summary>
    double MatchThreshold { get; }

    /// <summary>Kiểm tra ảnh là người thật (chống giơ ảnh/màn hình). Trả false nếu nghi giả mạo.</summary>
    bool CheckLiveness(byte[] imageBytes);

    /// <summary>Phát hiện 1 khuôn mặt + trích vector đặc trưng. Trả null nếu KHÔNG thấy mặt.</summary>
    float[]? ExtractEmbedding(byte[] imageBytes);

    /// <summary>Độ tương đồng cosine giữa 2 vector (0..1, càng cao càng giống).</summary>
    double Compare(float[] a, float[] b);

    /// <summary>
    /// Ước lượng hướng mặt từ landmark (dùng khi đăng ký để kiểm tra tư thế). Trả null nếu
    /// không thấy mặt. Yaw &gt; 0 = người dùng quay sang TRÁI, &lt; 0 = quay sang PHẢI;
    /// Pitch nhỏ hơn = ngước lên, lớn hơn = cúi xuống. Là TỈ LỆ tương đối theo hình học, không phải độ.
    /// </summary>
    FacePose? EstimatePose(byte[] imageBytes);
}

/// <summary>Hướng mặt tương đối (tỉ lệ hình học từ 5 điểm landmark, không phải độ).</summary>
public readonly record struct FacePose(double Yaw, double Pitch);

/// <summary>
/// ⚠️ BẢN GIẢ LẬP — KHÔNG nhận diện thật. Vector được tạo bằng cách băm nội dung ảnh,
/// nên chỉ "khớp" khi hai ảnh gần như giống hệt nhau về byte (đủ để kiểm thử luồng
/// đăng ký → chấm công → ghi nhật ký). Người thật chụp 2 lần KHÁC nhau sẽ KHÔNG khớp.
/// THAY bằng OpenCvSFaceEngine (hoặc FaceONNX/InsightFace) để nhận diện thật.
/// </summary>
public sealed class PlaceholderFaceEngine : IFaceEngine
{
    public string Name => "Placeholder (giả lập)";
    public bool IsReal => false;

    // Bản giả dùng ngưỡng cao vì chỉ khớp ảnh gần trùng byte; engine thật thường ~0.6.
    public double MatchThreshold => 0.92;

    public bool CheckLiveness(byte[] imageBytes) => imageBytes is { Length: > 0 };

    public float[]? ExtractEmbedding(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length < 32) return null; // coi như không thấy mặt

        const int dim = 128;
        var v = new float[dim];
        for (var i = 0; i < imageBytes.Length; i++)
            v[i % dim] += imageBytes[i];

        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        if (norm == 0) return null;
        for (var i = 0; i < dim; i++) v[i] = (float)(v[i] / norm);
        return v;
    }

    public double Compare(float[] a, float[] b)
    {
        if (a is null || b is null || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public FacePose? EstimatePose(byte[] imageBytes) =>
        imageBytes is { Length: > 0 } ? new FacePose(0, 0) : null;
}

/// <summary>Chuyển vector đặc trưng ↔ byte[] để lưu cột VARBINARY trong SQL Server.</summary>
public static class EmbeddingCodec
{
    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] b)
    {
        var v = new float[b.Length / sizeof(float)];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }
}
