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

    /// <summary>
    /// Mức chống giả mạo ĐANG chạy thật. CỐ Ý không có giá trị mặc định: engine nào cũng phải tự khai,
    /// vì mặc định "đầy đủ" là một lời nói dối im lặng đúng ở chỗ nguy hiểm nhất.
    /// </summary>
    AntiSpoofStatus AntiSpoof { get; }

    /// <summary>Kiểm tra ảnh là người thật (chống giơ ảnh/màn hình). Trả false nếu nghi giả mạo.</summary>
    bool CheckLiveness(byte[] imageBytes);

    /// <summary>Xác suất khuôn mặt là người thật (0..1) cho MỘT khung — để tổng hợp liveness cả loạt chụp.
    /// Fail-closed: trả 0 nếu không thấy mặt, thiếu model hoặc inference thất bại.</summary>
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
    /// Đánh giá chất lượng MỘT khung hình để chọn "ảnh tốt nhất" trong loạt chụp chấm công:
    /// đo độ nét, độ sáng, độ loá, kích cỡ &amp; hướng mặt rồi tổng hợp thành điểm 0..1.
    /// Trả null nếu ảnh hỏng; trả <see cref="FaceFrameQuality.FaceFound"/> = false nếu không thấy mặt.
    /// KHÔNG trích vector/chống giả mạo (nhẹ) — chỉ để xếp hạng khung, khâu nặng chạy 1 lần trên khung tốt nhất.
    /// </summary>
    FaceFrameQuality? AssessFrame(byte[] imageBytes);
}

/// <summary>
/// Mức chống giả mạo (ảnh/màn hình) thực sự đang chạy.
///
/// Vì sao phải phơi ra thay vì để trong log: khi model chống giả mạo không nạp được, mọi lượt quét phải
/// bị từ chối (fail-closed), đồng thời trạng thái phải đi tới tận màn hình quản trị để vận hành khắc phục.
/// </summary>
public enum AntiSpoofLevel
{
    /// <summary>
    /// KHÔNG có model nào — mọi lượt quét bị từ chối. Đây là mức phải báo động đỏ tận panel quản trị.
    /// </summary>
    None = 0,
    /// <summary>Chỉ còn model MiniFASNet đơn lẻ cũ (yếu hơn Silent-Face hai model).</summary>
    Basic = 1,
    /// <summary>Đủ Silent-Face (MiniFASNetV2 + MiniFASNetV1SE) như thiết kế.</summary>
    Full = 2,
}

/// <summary>Mức chống giả mạo + mô tả ngắn để hiện lên panel quản trị.</summary>
public readonly record struct AntiSpoofStatus(AntiSpoofLevel Level, string Detail);

/// <summary>Shared fail-closed boundary used by every API that relies on face liveness.</summary>
public static class FaceAntiSpoofSecurity
{
    public static bool IsOperational(IFaceEngine engine) => engine.AntiSpoof.Level != AntiSpoofLevel.None;

    public static double ProbabilityReal(IFaceEngine engine, byte[] imageBytes)
    {
        if (!IsOperational(engine)) return 0.0;
        try
        {
            var score = engine.LivenessProbability(imageBytes);
            return double.IsFinite(score) ? Math.Clamp(score, 0.0, 1.0) : 0.0;
        }
        catch
        {
            // The security boundary must never turn an inference failure into a live face. Concrete
            // engines log their detailed exception; this catch also protects future implementations.
            return 0.0;
        }
    }
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
    double DetectScore,  // độ tin cậy phát hiện 0..1
    // Độ MỞ MẮT ước lượng 0..1 (min hai mắt) — để chặn "nhắm mắt/lim dim" phía SERVER. Đây là heuristic
    // hình học (không phải model), 1.0 = không đánh giá được ⇒ fail-open (không chặn). Xem AdaFaceR50Engine.EyeOpenScore.
    double EyeOpen = 1.0,
    // Điểm cười 0..1 do SERVER tính từ độ mở rộng hai khóe miệng so với khoảng cách hai mắt.
    // Đây là tín hiệu hình học từ landmark YuNet, không nhận giá trị do ứng dụng điện thoại gửi lên.
    double Smile = 0.0);

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
