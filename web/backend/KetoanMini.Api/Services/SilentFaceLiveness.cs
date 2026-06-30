using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KetoanMini.Api.Services;

/// <summary>
/// Chống giả mạo khuôn mặt theo Silent-Face (minivision) — bản dựng đúng chuẩn gồm HAI model:
///   • 2.7_80x80_MiniFASNetV2      (crop tỉ lệ 2.7, đầu vào 80×80)
///   • 4_0_0_80x80_MiniFASNetV1SE  (crop tỉ lệ 4.0, đầu vào 80×80)
/// Mỗi model crop quanh bbox theo TỈ LỆ riêng (không phải crop vuông cố định), resize 80×80,
/// đưa BGR/255 vào ONNX → 3 lớp [giả, thật, giả] → softmax. Cộng softmax của 2 model rồi quyết định:
/// argmax == 1 ⇒ THẬT. Đây là phần khác biệt cốt lõi so với MiniFASNet đơn lẻ 2 lớp trước đây.
///
/// Tải model: xem Models/Face/SilentFace-AntiSpoof-README.md. Nếu thiếu file ⇒ <see cref="Available"/> = false.
/// </summary>
public sealed class SilentFaceLiveness : IDisposable
{
    private const int OutputClasses = 3;
    // Index lớp "sống/thật" trong đầu ra 3 lớp của 2 file ONNX (hpc203). Đã kiểm chứng thực tế = 2.
    // Nếu sau này đổi sang bộ ONNX khác (vd export theo quy ước minivision gốc) thì đổi về 1.
    private const int LiveClass = 2;

    private sealed record SpoofModel(InferenceSession Session, string InputName, double Scale, int Width, int Height);

    // Tên file chuẩn của Silent-Face: "{scale}_{H}x{W}_MiniFASNet...". Chấp nhận cả "2.7" lẫn "4_0_0".
    private static readonly Regex NamePattern =
        new(@"_(\d+)x(\d+)_MiniFASNet", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<SpoofModel> _models = new();

    public bool Available => _models.Count > 0;

    public SilentFaceLiveness(string modelDir)
    {
        if (!Directory.Exists(modelDir)) return;

        foreach (var path in Directory.EnumerateFiles(modelDir, "*.onnx"))
        {
            var fileName = Path.GetFileName(path);
            var m = NamePattern.Match(fileName);
            if (!m.Success) continue; // bỏ qua YuNet/AdaFace/MiniFASNet cũ — chỉ nhận file Silent-Face

            var h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var w = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var scale = ParseScale(fileName, fileName.Contains("SE", StringComparison.OrdinalIgnoreCase) ? 4.0 : 2.7);

            try
            {
                var session = new InferenceSession(path);
                var inputName = session.InputMetadata.Keys.FirstOrDefault();
                if (inputName is null) { session.Dispose(); continue; }
                _models.Add(new SpoofModel(session, inputName, scale, w, h));
            }
            catch
            {
                // File hỏng/không nạp được → bỏ qua, các model còn lại vẫn dùng được.
            }
        }
    }

    /// <summary>
    /// Xác suất khuôn mặt là người THẬT trong [0,1]. Quy ước: &gt; 0.5 ⇔ argmax == lớp "thật"
    /// (khớp đúng quyết định argmax của Silent-Face). Trả 1.0 nếu không có model nào.
    /// </summary>
    public double ProbabilityReal(Mat imageBgr, Rect faceRect)
    {
        if (_models.Count == 0) return 1.0;

        var accum = new double[OutputClasses];
        foreach (var model in _models)
        {
            using var crop = CropWithScale(imageBgr, faceRect, model.Scale, model.Width, model.Height);
            if (crop is null || crop.Empty()) continue;

            var tensor = ToTensorBgr(crop, model.Width, model.Height);
            var input = NamedOnnxValue.CreateFromTensor(model.InputName, tensor);
            using var results = model.Session.Run([input]);
            var logits = results.First().AsEnumerable<float>().Take(OutputClasses).ToArray();
            if (logits.Length < OutputClasses) continue;

            Softmax(logits);
            for (var i = 0; i < OutputClasses; i++) accum[i] += logits[i];
        }

        // Lớp "sống/thật" của 2 file ONNX này là index ĐÚNG = LiveClass (xác minh bằng thực đo:
        // mặt thật chính diện, ảnh nét → lớp 2 ~0.99 trên CẢ HAI model; tiền xử lý BGR/255 đã đúng
        // chuẩn minivision/hairymax). Quy ước gốc minivision là lớp 1 = thật, NHƯNG bản ONNX convert
        // sẵn (hpc203) lại xếp lớp thật ở index 2 — đọc nhầm index khiến mọi người thật bị coi là giả.
        // Trả P(lớp sống) đã chuẩn hóa trên tổng 3 lớp: > 0.5 ⇔ argmax == LiveClass.
        var live = accum[LiveClass];
        var total = accum[0] + accum[1] + accum[2];
        return total <= 1e-9 ? 1.0 : live / total;
    }

    private static double ParseScale(string fileName, double fallback)
    {
        // Lấy token đầu trước dấu "_" (vd "2.7" hoặc "4"). minivision đặt scale ở vị trí này.
        var firstToken = fileName.Split('_', 2)[0];
        return double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
            ? s
            : fallback;
    }

    /// <summary>
    /// Crop quanh bbox theo tỉ lệ <paramref name="scale"/> rồi resize về (outW,outH) — chuyển thể
    /// nguyên văn hàm _get_new_box/crop của Silent-Face: nếu khung vượt biên thì DỊCH vào trong
    /// (không pad), và tự giới hạn scale để không lớn hơn ảnh.
    /// </summary>
    private static Mat? CropWithScale(Mat img, Rect box, double scale, int outW, int outH)
    {
        int srcW = img.Width, srcH = img.Height;
        double x = box.X, y = box.Y, bw = box.Width, bh = box.Height;
        if (bw <= 0 || bh <= 0) return null;

        scale = Math.Min(Math.Min((srcH - 1) / bh, (srcW - 1) / bw), scale);
        double newW = bw * scale, newH = bh * scale;
        double cx = bw / 2.0 + x, cy = bh / 2.0 + y;

        double ltx = cx - newW / 2.0, lty = cy - newH / 2.0;
        double rbx = cx + newW / 2.0, rby = cy + newH / 2.0;

        if (ltx < 0) { rbx -= ltx; ltx = 0; }
        if (lty < 0) { rby -= lty; lty = 0; }
        if (rbx > srcW - 1) { ltx -= rbx - (srcW - 1); rbx = srcW - 1; }
        if (rby > srcH - 1) { lty -= rby - (srcH - 1); rby = srcH - 1; }

        int ix = Math.Clamp((int)ltx, 0, srcW - 1);
        int iy = Math.Clamp((int)lty, 0, srcH - 1);
        int iw = Math.Clamp((int)rbx - ix + 1, 1, srcW - ix);
        int ih = Math.Clamp((int)rby - iy + 1, 1, srcH - iy);

        using var roi = new Mat(img, new Rect(ix, iy, iw, ih));
        var dst = new Mat();
        Cv2.Resize(roi, dst, new Size(outW, outH));
        return dst;
    }

    // Silent-Face dùng ToTensor() trên ảnh cv2 (BGR) → giữ nguyên thứ tự kênh BGR, chỉ chia 255 (không trừ mean).
    private static DenseTensor<float> ToTensorBgr(Mat bgr, int w, int h)
    {
        var tensor = new DenseTensor<float>([1, 3, h, w]);
        var values = tensor.Buffer.Span;
        var plane = w * h;
        for (var yy = 0; yy < h; yy++)
        {
            for (var xx = 0; xx < w; xx++)
            {
                var p = bgr.At<Vec3b>(yy, xx);
                var off = yy * w + xx;
                values[off] = p.Item0 / 255f;             // B
                values[plane + off] = p.Item1 / 255f;     // G
                values[2 * plane + off] = p.Item2 / 255f; // R
            }
        }
        return tensor;
    }

    private static void Softmax(float[] v)
    {
        var max = v[0];
        for (var i = 1; i < v.Length; i++) if (v[i] > max) max = v[i];
        double sum = 0;
        for (var i = 0; i < v.Length; i++) { v[i] = (float)Math.Exp(v[i] - max); sum += v[i]; }
        if (sum <= 0) return;
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / sum);
    }

    public void Dispose()
    {
        foreach (var m in _models) m.Session.Dispose();
        _models.Clear();
    }
}
