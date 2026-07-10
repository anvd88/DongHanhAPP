namespace KetoanMini.Api.Services;

/// <summary>
/// Bộ máy nhận diện khuôn mặt — TÁCH RIÊNG phần học máy khỏi nghiệp vụ chấm công.
/// Toàn bộ endpoint chấm công chỉ phụ thuộc interface này, nên khi đổi engine chỉ cần
/// viết 1 lớp mới và đổi đăng ký DI trong Program.cs — không sửa nghiệp vụ.
///
/// Mặc định đang chạy <see cref="AdaFaceR50Engine"/> (YuNet + AdaFace R50, nhận diện THẬT).
/// </summary>
public interface IFaceEngine
{
    /// <summary>Tên engine đang dùng (hiển thị ở /api/chamcong/trangthai).</summary>
    string Name { get; }

    /// <summary>Ngưỡng khớp khuyến nghị: similarity ≥ ngưỡng ⇒ coi là cùng một người.</summary>
    double MatchThreshold { get; }

    /// <summary>Kiểm tra ảnh là người thật (chống giơ ảnh/màn hình). Trả false nếu nghi giả mạo.</summary>
    bool CheckLiveness(byte[] imageBytes);

    /// <summary>Xác suất khuôn mặt là người thật (0..1) cho MỘT khung — để tổng hợp liveness cả loạt chụp.
    /// Trả 0 nếu không thấy mặt; trả 1 nếu engine không có model chống giả mạo.</summary>
    double LivenessProbability(byte[] imageBytes);

    /// <summary>Ngưỡng P(real) để coi là người thật (mặc định 0.5).</summary>
    double LivenessThreshold { get; }

    /// <summary>Phát hiện 1 khuôn mặt + trích vector đặc trưng. Trả null nếu KHÔNG thấy mặt.</summary>
    float[]? ExtractEmbedding(byte[] imageBytes);

    /// <summary>
    /// Trích + GỘP vector của NHIỀU khung (trung bình từng chiều rồi chuẩn hóa L2) → ổn định hơn 1 khung,
    /// giảm nhận nhầm/từ chối nhầm do nhiễu 1 khung. Bỏ qua khung không trích được; trả null nếu KHÔNG
    /// khung nào trích được. Vì mỗi vector đã chuẩn hóa L2, trung bình rồi chuẩn hóa lại vẫn hợp lệ cho cosine.
    /// </summary>
    float[]? ExtractFusedEmbedding(IReadOnlyList<byte[]> frames)
    {
        float[]? sum = null;
        var n = 0;
        foreach (var f in frames)
        {
            var e = ExtractEmbedding(f);
            if (e is null) continue;
            if (sum is null) sum = new float[e.Length];
            if (e.Length != sum.Length) continue;
            for (var i = 0; i < e.Length; i++) sum[i] += e[i];
            n++;
        }
        if (sum is null || n == 0) return null;

        double norm = 0;
        foreach (var v in sum) norm += (double)v * v;
        norm = Math.Sqrt(norm);
        if (norm <= 0) return sum;
        for (var i = 0; i < sum.Length; i++) sum[i] = (float)(sum[i] / norm);
        return sum;
    }

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

    /// <summary>
    /// Lấy màu TRUNG BÌNH vùng da khuôn mặt (mỗi kênh R,G,B trong 0..1) từ pixel GỐC — KHÔNG tăng
    /// sáng/CLAHE (vì làm méo màu) — phục vụ active-flash liveness: đối chiếu ánh phản xạ trên mặt với
    /// màu màn hình đang chiếu. Trả null nếu không thấy mặt. Mặc định null ⇒ engine không hỗ trợ
    /// (endpoint sẽ bỏ qua bước flash liveness cho engine đó).
    /// </summary>
    FaceSkinColor? SampleFaceSkinColor(byte[] imageBytes) => null;
}

/// <summary>Hướng mặt tương đối (tỉ lệ hình học từ 5 điểm landmark, không phải độ).</summary>
public readonly record struct FacePose(double Yaw, double Pitch);

/// <summary>Màu trung bình vùng da mặt (mỗi kênh 0..1) + tỉ lệ diện tích mặt/ảnh — cho active-flash liveness.</summary>
public readonly record struct FaceSkinColor(double R, double G, double B, double FaceRatio);

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

/// <summary>Chuyển vector đặc trưng ↔ byte[] để lưu cột bytea trong PostgreSQL.</summary>
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
