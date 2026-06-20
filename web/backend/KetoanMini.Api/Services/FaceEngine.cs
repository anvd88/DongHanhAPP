using SkiaSharp;
using ViewFaceCore;          // extension SKBitmap.ToFaceImage()
using ViewFaceCore.Core;     // FaceDetector / FaceLandmarker / FaceRecognizer / FaceAntiSpoofing
using ViewFaceCore.Model;    // FaceInfo / FaceMarkPoint / AntiSpoofingStatus …

namespace KetoanMini.Api.Services;

/// <summary>
/// Bộ máy nhận diện khuôn mặt — TÁCH RIÊNG phần học máy khỏi nghiệp vụ chấm công.
/// Toàn bộ endpoint chấm công chỉ phụ thuộc interface này, nên khi đổi engine chỉ cần
/// viết 1 lớp mới và đổi đăng ký DI trong Program.cs — không sửa nghiệp vụ.
///
/// Mặc định đang chạy <see cref="ViewFaceCoreEngine"/> (SeetaFace6, nhận diện THẬT).
/// Vẫn giữ <see cref="PlaceholderFaceEngine"/> để chạy thử khi máy thiếu thư viện gốc.
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
}

/// <summary>
/// ⚠️ BẢN GIẢ LẬP — KHÔNG nhận diện thật. Vector được tạo bằng cách băm nội dung ảnh,
/// nên chỉ "khớp" khi hai ảnh gần như giống hệt nhau về byte (đủ để kiểm thử luồng
/// đăng ký → chấm công → ghi nhật ký). Người thật chụp 2 lần KHÁC nhau sẽ KHÔNG khớp.
/// THAY bằng ViewFaceCore (hoặc FaceONNX/InsightFace) để nhận diện thật.
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

/// <summary>
/// Engine nhận diện THẬT bằng SeetaFace6 (qua thư viện ViewFaceCore).
///
/// Yêu cầu môi trường: Windows x64 + Microsoft Visual C++ 2015–2022 Redistributable (x64).
/// Các file model (.csta) và thư viện gốc được NuGet tự chép vào thư mục output khi build.
///
/// ⚠️ Các đối tượng SeetaFace KHÔNG an toàn đa luồng, trong khi engine là singleton dùng
/// chung cho mọi request → mọi lời gọi gốc được tuần tự hóa bằng <c>_gate</c>. Tải chấm công
/// thấp (vài lượt/phút) nên việc khóa không gây nghẽn.
/// </summary>
public sealed class ViewFaceCoreEngine : IFaceEngine, IDisposable
{
    private readonly FaceDetector _detector = new();
    private readonly FaceLandmarker _marker = new();
    private readonly FaceRecognizer _recognizer = new();
    private readonly FaceAntiSpoofing _antiSpoofing = new();
    private readonly object _gate = new();

    public string Name => "ViewFaceCore (SeetaFace6)";
    public bool IsReal => true;

    // Ngưỡng khuyến nghị của SeetaFace cho model nhận dạng thường (~0.62).
    public double MatchThreshold => 0.62;

    public bool CheckLiveness(byte[] imageBytes)
    {
        try
        {
            lock (_gate)
            {
                using var bitmap = SKBitmap.Decode(imageBytes);
                if (bitmap is null) return false;                 // ảnh hỏng/không giải mã được
                using var img = bitmap.ToFaceImage();

                var faces = _detector.Detect(img);
                if (faces.Length == 0) return false;              // không có mặt -> không phải người thật

                var face = Largest(faces);
                var marks = _marker.Mark(img, face);
                var r = _antiSpoofing.AntiSpoofing(img, face, marks);

                // Chỉ CHẶN khi model chắc chắn là giả mạo. Ảnh 1 khung dễ ra Fuzzy/Detecting,
                // nếu chặn các trạng thái này thì gần như không ai chấm công được.
                return r.Status != AntiSpoofingStatus.Spoof;
            }
        }
        catch
        {
            // Model chống giả mạo trục trặc -> không chặn (vẫn còn bước so khớp khuôn mặt phía sau).
            return true;
        }
    }

    public float[]? ExtractEmbedding(byte[] imageBytes)
    {
        try
        {
            lock (_gate)
            {
                using var bitmap = SKBitmap.Decode(imageBytes);
                if (bitmap is null) return null;
                using var img = bitmap.ToFaceImage();

                var faces = _detector.Detect(img);
                if (faces.Length == 0) return null;               // không thấy mặt

                var face = Largest(faces);                         // chọn mặt to nhất (gần camera nhất)
                var marks = _marker.Mark(img, face);
                return _recognizer.Extract(img, marks);           // vector đặc trưng thật
            }
        }
        catch
        {
            return null;
        }
    }

    public double Compare(float[] a, float[] b)
    {
        // Chiều vector khác nhau = dữ liệu cũ của engine khác -> bỏ qua, không khớp.
        if (a is null || b is null || a.Length != b.Length) return 0;
        try { lock (_gate) return _recognizer.Compare(a, b); }    // độ tương đồng cosine
        catch { return 0; }
    }

    /// <summary>Chọn khuôn mặt có diện tích lớn nhất trong khung hình.</summary>
    private static FaceInfo Largest(FaceInfo[] faces)
    {
        var best = faces[0];
        var bestArea = (long)best.Location.Width * best.Location.Height;
        for (var i = 1; i < faces.Length; i++)
        {
            var area = (long)faces[i].Location.Width * faces[i].Location.Height;
            if (area > bestArea) { best = faces[i]; bestArea = area; }
        }
        return best;
    }

    public void Dispose()
    {
        _detector.Dispose();
        _marker.Dispose();
        _recognizer.Dispose();
        _antiSpoofing.Dispose();
    }
}
