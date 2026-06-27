namespace KetoanMini.Api.Services;

/// <summary>
/// Bộ máy nhận diện khuôn mặt — TÁCH RIÊNG phần học máy khỏi nghiệp vụ chấm công.
/// Toàn bộ endpoint chấm công chỉ phụ thuộc interface này, nên khi đổi engine chỉ cần
/// viết 1 lớp mới và đổi đăng ký DI trong Program.cs — không sửa nghiệp vụ.
///
/// Mặc định đang chạy <see cref="OpenCvSFaceEngine"/> (OpenCV YuNet + SFace, nhận diện THẬT).
/// </summary>
public interface IFaceEngine
{
    /// <summary>Tên engine đang dùng (hiển thị ở /api/chamcong/trangthai).</summary>
    string Name { get; }

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

    /// <summary>
    /// Đánh giá chất lượng MỘT khung hình để chọn "ảnh tốt nhất" trong loạt chụp chấm công:
    /// đo độ nét, độ sáng, độ loá, kích cỡ &amp; hướng mặt rồi tổng hợp thành điểm 0..1.
    /// Trả null nếu ảnh hỏng; trả <see cref="FaceFrameQuality.FaceFound"/> = false nếu không thấy mặt.
    /// KHÔNG trích vector/chống giả mạo (nhẹ) — chỉ để xếp hạng khung, khâu nặng chạy 1 lần trên khung tốt nhất.
    /// </summary>
    FaceFrameQuality? AssessFrame(byte[] imageBytes);
}

/// <summary>Hướng mặt tương đối (tỉ lệ hình học từ 5 điểm landmark, không phải độ).</summary>
public readonly record struct FacePose(double Yaw, double Pitch);

/// <summary>
/// Chất lượng một khung hình khuôn mặt. Mọi chỉ số (trừ <see cref="Score"/>) là giá trị thô để
/// chẩn đoán/tinh chỉnh; <see cref="Score"/> là điểm tổng hợp 0..1 dùng để chọn khung tốt nhất.
/// </summary>
public readonly record struct FaceFrameQuality(
    bool FaceFound,
    double Score,
    double Sharpness,    // độ nét (phương sai Laplacian đã chuẩn hóa 0..1)
    double Brightness,   // độ sáng vùng mặt 0..1
    double GlareRatio,   // tỉ lệ điểm gần bão hòa (loá) trong vùng mặt 0..1
    double FaceRatio,    // diện tích mặt / diện tích ảnh
    FacePose Pose,
    double DetectScore); // độ tin cậy phát hiện 0..1

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
