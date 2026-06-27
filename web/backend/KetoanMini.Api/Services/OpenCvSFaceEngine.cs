using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace KetoanMini.Api.Services;

/// <summary>
/// Engine nhận diện khuôn mặt bằng OpenCV YuNet + SFace ONNX.
/// Model SFace trong opencv_zoo được cấp phép Apache 2.0.
/// </summary>
public sealed class OpenCvSFaceEngine : IFaceEngine, IDisposable
{
    private static readonly string[] YuNetOutputs =
    [
        "cls_8", "cls_16", "cls_32",
        "obj_8", "obj_16", "obj_32",
        "bbox_8", "bbox_16", "bbox_32",
        "kps_8", "kps_16", "kps_32",
    ];

    private static readonly int[] Strides = [8, 16, 32];
    private static readonly Point2f[] SFaceReference =
    [
        new(38.2946f, 51.6963f),
        new(73.5318f, 51.5014f),
        new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f),
        new(70.7299f, 92.2041f),
    ];

    // Tiền xử lý PAD (chống giả mạo) — khớp y hệt pipeline gốc facenox/face-antispoof-onnx:
    // crop vuông cạnh max(w,h)*1.5 quanh tâm bbox (đệm phản chiếu), resize 128, RGB, /255, NCHW.
    private const double LivenessCropFactor = 1.5;
    private const int LivenessSize = 128;
    // Ngưỡng P(real): ≥ ngưỡng ⇒ người thật. 0.5 = điểm vận hành gốc của model (argmax,
    // tương đương real_logit ≥ spoof_logit). Tăng (0.6–0.7) để siết chống giả mạo chặt hơn
    // nhưng dễ từ chối người thật hơn; giảm nếu camera/ánh sáng yếu.
    private const double LivenessRealThreshold = 0.5;

    private readonly Net _detectorNet;
    private readonly Net _recognizerNet;
    // Model chống giả mạo (MiniFASNetV2-SE, Apache-2.0). NULL nếu thiếu file → fallback chỉ
    // kiểm tra "có khuôn mặt" (không khóa chấm công), trạng thái hiển thị ở /api/chamcong/trangthai.
    private readonly Net? _livenessNet;
    private readonly object _gate = new();

    // Cache 1 ô để tránh detect 2 lần trên CÙNG khung hình trong 1 lượt /nhandien
    // (CheckLiveness rồi ExtractEmbedding nhận cùng mảng byte). Chỉ truy cập trong _gate.
    private byte[]? _lastBytes;
    private FaceDetection? _lastDet;

    public OpenCvSFaceEngine()
    {
        var modelDir = Path.Combine(AppContext.BaseDirectory, "Models", "Face");
        var detectorPath = Path.Combine(modelDir, "face_detection_yunet_2023mar.onnx");
        var recognizerPath = Path.Combine(modelDir, "face_recognition_sface_2021dec.onnx");

        if (!File.Exists(detectorPath))
            throw new FileNotFoundException("Thiếu model phát hiện khuôn mặt YuNet.", detectorPath);
        if (!File.Exists(recognizerPath))
            throw new FileNotFoundException("Thiếu model nhận diện khuôn mặt SFace.", recognizerPath);

        _detectorNet = CvDnn.ReadNetFromOnnx(detectorPath)
            ?? throw new InvalidOperationException("Không mở được model YuNet.");
        _recognizerNet = CvDnn.ReadNetFromOnnx(recognizerPath)
            ?? throw new InvalidOperationException("Không mở được model SFace.");
        _detectorNet.SetPreferableBackend(Backend.OPENCV);
        _detectorNet.SetPreferableTarget(Target.CPU);
        _recognizerNet.SetPreferableBackend(Backend.OPENCV);
        _recognizerNet.SetPreferableTarget(Target.CPU);

        // Model chống giả mạo là TÙY CHỌN: thiếu file thì vẫn chạy nhận diện (fallback liveness).
        var livenessPath = Path.Combine(modelDir, "face_antispoof_minifasnet.onnx");
        if (File.Exists(livenessPath))
        {
            try
            {
                var net = CvDnn.ReadNetFromOnnx(livenessPath);
                net?.SetPreferableBackend(Backend.OPENCV);
                net?.SetPreferableTarget(Target.CPU);
                _livenessNet = net;
            }
            catch { _livenessNet = null; }
        }
    }

    public string Name => _livenessNet is null
        ? "OpenCV SFace (YuNet + SFace) — chưa có chống giả mạo"
        : "OpenCV SFace (YuNet + SFace + chống giả mạo MiniFASNet)";

    // Ngưỡng cosine khuyến nghị của OpenCV SFace demo là khoảng 0.363.
    // Tăng lên nếu muốn giảm nhận nhầm, giảm xuống nếu camera/ánh sáng yếu.
    public double MatchThreshold => 0.363;

    public bool CheckLiveness(byte[] imageBytes)
    {
        using var image = Decode(imageBytes);
        if (image is null) return false;

        lock (_gate)
        {
            var face = DetectCached(imageBytes, image);
            if (face is null) return false;          // không thấy mặt → coi như không qua
            if (_livenessNet is null) return true;   // thiếu model PAD → chỉ xác nhận có mặt
            return LivenessScore(image, face) >= LivenessRealThreshold;
        }
    }

    /// <summary>
    /// Xác suất khuôn mặt là NGƯỜI THẬT (0..1) bằng MiniFASNet. Crop quanh khuôn mặt theo
    /// đúng tỉ lệ 1.5×, đệm phản chiếu, đưa vào model (RGB, [0,1], 128×128). Trả 1.0 (cho qua)
    /// nếu không crop/suy luận được — tránh khóa nhầm người thật do lỗi biên hiếm gặp.
    /// </summary>
    private double LivenessScore(Mat imageBgr, FaceDetection face)
    {
        try
        {
            using var crop = CropForLiveness(imageBgr, face);
            if (crop is null || crop.Empty()) return 1.0;

            using var resized = new Mat();
            Cv2.Resize(crop, resized, new Size(LivenessSize, LivenessSize), 0, 0,
                crop.Width > LivenessSize ? InterpolationFlags.Area : InterpolationFlags.Lanczos4);

            // Model ăn RGB nên swapRB=true (ảnh OpenCV là BGR); chuẩn hóa /255 sang [0,1].
            using var blob = CvDnn.BlobFromImage(resized, 1.0 / 255.0,
                new Size(LivenessSize, LivenessSize), new Scalar(0, 0, 0), true, false);
            _livenessNet!.SetInput(blob);
            using var output = _livenessNet.Forward();

            using var flat = output.Reshape(1, 1);
            if (flat.Cols < 2) return 1.0;
            // Output là 2 logit [real, spoof]; P(real) = sigmoid(real - spoof).
            var diff = flat.At<float>(0, 0) - flat.At<float>(0, 1);
            return 1.0 / (1.0 + Math.Exp(-diff));
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>Crop vuông quanh khuôn mặt (cạnh = max(w,h)×1.5), đệm phản chiếu — khớp pipeline gốc.</summary>
    private static Mat? CropForLiveness(Mat image, FaceDetection face)
    {
        int imgW = image.Width, imgH = image.Height;
        double w = face.Rect.Width, h = face.Rect.Height;
        if (w <= 0 || h <= 0) return null;

        var maxDim = Math.Max(w, h);
        var cx = face.Rect.X + w / 2.0;
        var cy = face.Rect.Y + h / 2.0;
        var cropSize = (int)(maxDim * LivenessCropFactor);
        if (cropSize <= 0) return null;
        var x = (int)(cx - maxDim * LivenessCropFactor / 2.0);
        var y = (int)(cy - maxDim * LivenessCropFactor / 2.0);

        int x1 = Math.Max(0, x), y1 = Math.Max(0, y);
        int x2 = Math.Min(imgW, x + cropSize), y2 = Math.Min(imgH, y + cropSize);
        if (x2 <= x1 || y2 <= y1) return null;

        int leftPad = Math.Max(0, -x), topPad = Math.Max(0, -y);
        int rightPad = Math.Max(0, x + cropSize - imgW), bottomPad = Math.Max(0, y + cropSize - imgH);

        using var roi = new Mat(image, new Rect(x1, y1, x2 - x1, y2 - y1));
        var padded = new Mat();
        Cv2.CopyMakeBorder(roi, padded, topPad, bottomPad, leftPad, rightPad, BorderTypes.Reflect101);
        return padded;
    }

    public float[]? ExtractEmbedding(byte[] imageBytes)
    {
        using var image = Decode(imageBytes);
        if (image is null) return null;

        lock (_gate)
        {
            var face = DetectCached(imageBytes, image);
            if (face is null) return null;

            using var aligned = AlignCrop(image, face);
            using var blob = CvDnn.BlobFromImage(aligned, 1.0, new Size(112, 112), new Scalar(0, 0, 0), true, false);
            _recognizerNet.SetInput(blob);
            using var feature = _recognizerNet.Forward();
            return ToFloatArray(feature);
        }
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
        if (na <= 0 || nb <= 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public FacePose? EstimatePose(byte[] imageBytes)
    {
        using var image = Decode(imageBytes);
        if (image is null) return null;

        lock (_gate)
        {
            var face = DetectCached(imageBytes, image);
            return face is null ? null : PoseFrom(face);
        }
    }

    /// <summary>Hướng mặt từ 5 landmark — null nếu hai mắt quá sát (suy biến, không tin được).</summary>
    private static FacePose? PoseFrom(FaceDetection face)
    {
        var eyeMidX = (face.LeftEye.X + face.RightEye.X) / 2.0;
        var eyeMidY = (face.LeftEye.Y + face.RightEye.Y) / 2.0;
        var eyeDist = Math.Abs(face.LeftEye.X - face.RightEye.X);
        if (eyeDist < 1) return null;

        var mouthMidY = (face.LeftMouth.Y + face.RightMouth.Y) / 2.0;
        var faceVert = mouthMidY - eyeMidY;
        var yaw = (face.Nose.X - eyeMidX) / eyeDist;
        var pitch = Math.Abs(faceVert) < 1 ? 0.5 : (face.Nose.Y - eyeMidY) / faceVert;
        return new FacePose(yaw, pitch);
    }

    public FaceFrameQuality? AssessFrame(byte[] imageBytes)
    {
        using var image = Decode(imageBytes);
        if (image is null) return null;

        lock (_gate)
        {
            var face = DetectCached(imageBytes, image);
            if (face is null)
                return new FaceFrameQuality(false, 0, 0, 0, 0, 0, default, 0);

            var rect = ClampRect(face.Rect, image.Width, image.Height);
            var faceRatio = rect.Width * rect.Height / (double)(image.Width * image.Height);

            double sharpness = 0, brightness = 0, glare = 0;
            if (rect.Width > 8 && rect.Height > 8)
            {
                using var roi = new Mat(image, rect);
                using var gray = new Mat();
                Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

                // Độ nét = phương sai Laplacian (ảnh nhòe ⇒ phương sai thấp).
                using var lap = new Mat();
                Cv2.Laplacian(gray, lap, MatType.CV_64F);
                Cv2.MeanStdDev(lap, out _, out var std);
                sharpness = std.Val0 * std.Val0;

                brightness = gray.Mean().Val0 / 255.0;

                using var glareMask = new Mat();
                Cv2.Threshold(gray, glareMask, 245, 255, ThresholdTypes.Binary);
                glare = Cv2.CountNonZero(glareMask) / (double)(gray.Rows * gray.Cols);
            }

            var pose = PoseFrom(face) ?? default;

            // Chuẩn hóa từng tiêu chí về 0..1 rồi tổng hợp có trọng số.
            var sharpNorm = Math.Clamp(sharpness / 250.0, 0, 1);              // ~250 = nét rõ
            var sizeNorm = Math.Clamp(faceRatio / 0.12, 0, 1);               // mặt ~12% ảnh là đủ to
            var brightScore = 1.0 - Math.Min(1.0, Math.Abs(brightness - 0.52) / 0.45);
            var frontal = 1.0 - Math.Min(1.0, Math.Abs(pose.Yaw) / 0.30);
            var glarePenalty = Math.Clamp(glare / 0.08, 0, 1);

            var score = (0.34 * sharpNorm + 0.22 * sizeNorm + 0.16 * brightScore
                         + 0.18 * frontal + 0.10 * face.Score) * (1.0 - 0.6 * glarePenalty);

            return new FaceFrameQuality(true, Math.Clamp(score, 0, 1),
                sharpNorm, brightness, glare, faceRatio, pose, face.Score);
        }
    }

    /// <summary>Cắt rect thực (số nguyên) của khuôn mặt nằm gọn trong khung ảnh.</summary>
    private static Rect ClampRect(Rect2d r, int imgW, int imgH)
    {
        var x = Math.Clamp((int)Math.Round(r.X), 0, Math.Max(0, imgW - 1));
        var y = Math.Clamp((int)Math.Round(r.Y), 0, Math.Max(0, imgH - 1));
        var w = Math.Clamp((int)Math.Round(r.Width), 0, imgW - x);
        var h = Math.Clamp((int)Math.Round(r.Height), 0, imgH - y);
        return new Rect(x, y, w, h);
    }

    private Mat? Decode(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0) return null;
        var image = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (image.Empty()) return null;
        EnhanceForLowLight(image);   // chuẩn hóa ánh sáng yếu / loá để mọi khâu sau bám mặt tốt hơn
        return image;
    }

    // Ngưỡng kích hoạt tăng cường ảnh (đo trên kênh L của LAB, 0..255). Chỉ can thiệp khi ảnh
    // thực sự tối/loá để ảnh đủ sáng giữ nguyên (bảo toàn hành vi cũ + điểm chống giả mạo).
    private const double EnhanceDarkMean = 110;     // sáng TB < ngưỡng ⇒ thiếu sáng
    private const double EnhanceBrightMean = 200;    // sáng TB > ngưỡng ⇒ quá sáng/ngược sáng
    private const double EnhanceGlareRatio = 0.045;  // tỉ lệ điểm gần bão hòa ⇒ bị loá

    /// <summary>
    /// Tăng cường ảnh BGR tại chỗ cho điều kiện ánh sáng kém: cân bằng tương phản cục bộ (CLAHE)
    /// trên kênh sáng L trong không gian LAB, kèm hiệu chỉnh gamma để kéo sáng vùng tối hoặc nén
    /// vùng loá. Chỉ chạy khi ảnh tối/quá sáng/loá — ảnh tốt được trả về nguyên trạng.
    /// </summary>
    private static void EnhanceForLowLight(Mat bgr)
    {
        using var lab = new Mat();
        Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
        var ch = Cv2.Split(lab);
        try
        {
            var l = ch[0]; // kênh sáng (8-bit)
            var mean = l.Mean().Val0;

            using var saturated = new Mat();
            Cv2.Threshold(l, saturated, 245, 255, ThresholdTypes.Binary);
            var glare = Cv2.CountNonZero(saturated) / (double)(l.Rows * l.Cols);

            var tooDark = mean < EnhanceDarkMean;
            var hasGlare = glare > EnhanceGlareRatio || mean > EnhanceBrightMean;
            if (!tooDark && !hasGlare) return; // đủ tốt → giữ nguyên

            if (hasGlare) ApplyGammaInPlace(l, 1.5);   // nén vùng sáng/loá
            if (tooDark) ApplyGammaInPlace(l, 0.7);     // kéo sáng vùng tối

            using var clahe = Cv2.CreateCLAHE(2.5, new Size(8, 8));
            clahe.Apply(l, l);

            using var merged = new Mat();
            Cv2.Merge(ch, merged);
            Cv2.CvtColor(merged, bgr, ColorConversionCodes.Lab2BGR);
        }
        finally
        {
            foreach (var c in ch) c.Dispose();
        }
    }

    /// <summary>Hiệu chỉnh gamma tại chỗ trên ảnh xám 8-bit bằng bảng tra (LUT).</summary>
    private static void ApplyGammaInPlace(Mat gray8u, double gamma)
    {
        using var lut = new Mat(1, 256, MatType.CV_8UC1);
        var idx = lut.GetGenericIndexer<byte>();
        for (var i = 0; i < 256; i++)
            idx[0, i] = (byte)Math.Clamp(Math.Pow(i / 255.0, gamma) * 255.0, 0, 255);
        Cv2.LUT(gray8u, lut, gray8u);
    }

    // Dùng lại kết quả detect nếu là CÙNG mảng byte vừa xử lý (so tham chiếu — an toàn, không
    // đụng độ hash, chỉ trúng trong nội bộ 1 request vì endpoint truyền cùng instance). Bỏ qua
    // được lần chạy YuNet thứ hai (forward + quét anchor) — phần tốn kém nhất của mỗi lượt chấm.
    private FaceDetection? DetectCached(byte[] imageBytes, Mat image)
    {
        if (ReferenceEquals(imageBytes, _lastBytes)) return _lastDet;
        var det = DetectLargest(image);
        _lastBytes = imageBytes;
        _lastDet = det;
        return det;
    }

    private FaceDetection? DetectLargest(Mat image)
    {
        var padW = ((image.Width - 1) / 32 + 1) * 32;
        var padH = ((image.Height - 1) / 32 + 1) * 32;

        using var padded = new Mat();
        Cv2.CopyMakeBorder(image, padded, 0, padH - image.Height, 0, padW - image.Width, BorderTypes.Constant, Scalar.All(0));
        using var blob = CvDnn.BlobFromImage(padded);
        _detectorNet.SetInput(blob);

        using var outputMats = new DisposableMats(YuNetOutputs.Length);
        _detectorNet.Forward(outputMats.Items, YuNetOutputs);

        FaceDetection? best = null;
        for (var scaleIndex = 0; scaleIndex < Strides.Length; scaleIndex++)
        {
            var stride = Strides[scaleIndex];
            var cols = padW / stride;
            var rows = padH / stride;

            using var cls = outputMats.Items[scaleIndex].Reshape(1, 1);
            using var obj = outputMats.Items[scaleIndex + 3].Reshape(1, 1);
            using var bbox = outputMats.Items[scaleIndex + 6].Reshape(1, 1);
            using var kps = outputMats.Items[scaleIndex + 9].Reshape(1, 1);

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var idx = r * cols + c;
                    var clsScore = Math.Clamp(cls.At<float>(0, idx), 0, 1);
                    var objScore = Math.Clamp(obj.At<float>(0, idx), 0, 1);
                    var score = Math.Sqrt(clsScore * objScore);
                    if (score < 0.82) continue;

                    var baseBox = idx * 4;
                    var cx = (c + bbox.At<float>(0, baseBox)) * stride;
                    var cy = (r + bbox.At<float>(0, baseBox + 1)) * stride;
                    var width = Math.Exp(bbox.At<float>(0, baseBox + 2)) * stride;
                    var height = Math.Exp(bbox.At<float>(0, baseBox + 3)) * stride;
                    var rect = new Rect2d(cx - width / 2.0, cy - height / 2.0, width, height);

                    var baseKps = idx * 10;
                    var face = new FaceDetection(
                        rect,
                        new Point2f((kps.At<float>(0, baseKps) + c) * stride, (kps.At<float>(0, baseKps + 1) + r) * stride),
                        new Point2f((kps.At<float>(0, baseKps + 2) + c) * stride, (kps.At<float>(0, baseKps + 3) + r) * stride),
                        new Point2f((kps.At<float>(0, baseKps + 4) + c) * stride, (kps.At<float>(0, baseKps + 5) + r) * stride),
                        new Point2f((kps.At<float>(0, baseKps + 6) + c) * stride, (kps.At<float>(0, baseKps + 7) + r) * stride),
                        new Point2f((kps.At<float>(0, baseKps + 8) + c) * stride, (kps.At<float>(0, baseKps + 9) + r) * stride),
                        score);

                    if (best is null || face.Rect.Width * face.Rect.Height > best.Rect.Width * best.Rect.Height)
                        best = face;
                }
            }
        }

        return best;
    }

    private static Mat AlignCrop(Mat image, FaceDetection face)
    {
        // Căn chỉnh bằng SIMILARITY transform (4-DOF: xoay + co giãn ĐỀU + tịnh tiến) từ CẢ 5
        // landmark — đúng như OpenCV FaceRecognizerSF::alignCrop. KHÔNG dùng affine 3 điểm
        // (Cv2.GetAffineTransform) vì affine 6-DOF có thể kéo méo/cắt xiên khuôn mặt; SFace
        // được huấn luyện trên ảnh align kiểu similarity nên crop méo làm tụt độ tương đồng.
        Point2f[] src = [face.RightEye, face.LeftEye, face.Nose, face.RightMouth, face.LeftMouth];

        using var srcArr = InputArray.Create(src, MatType.CV_32FC2);
        using var dstArr = InputArray.Create(SFaceReference, MatType.CV_32FC2);
        using var transform = Cv2.EstimateAffinePartial2D(srcArr, dstArr);

        var aligned = new Mat();
        if (transform is not null && !transform.Empty())
        {
            Cv2.WarpAffine(image, aligned, transform, new Size(112, 112), InterpolationFlags.Linear);
        }
        else
        {
            // Hiếm khi ước lượng thất bại (5 điểm suy biến) → quay về affine 3 điểm để vẫn chấm được.
            using var fallback = Cv2.GetAffineTransform(
                new[] { face.RightEye, face.LeftEye, face.Nose },
                new[] { SFaceReference[0], SFaceReference[1], SFaceReference[2] });
            Cv2.WarpAffine(image, aligned, fallback, new Size(112, 112), InterpolationFlags.Linear);
        }
        return aligned;
    }

    private static float[] ToFloatArray(Mat feature)
    {
        using var flat = feature.Reshape(1, 1);
        var values = new float[flat.Cols];
        for (var i = 0; i < flat.Cols; i++)
            values[i] = flat.At<float>(0, i);
        return values;
    }

    public void Dispose()
    {
        _detectorNet.Dispose();
        _recognizerNet.Dispose();
        _livenessNet?.Dispose();
    }

    private sealed record FaceDetection(
        Rect2d Rect,
        Point2f RightEye,
        Point2f LeftEye,
        Point2f Nose,
        Point2f RightMouth,
        Point2f LeftMouth,
        double Score);

    private sealed class DisposableMats : IDisposable
    {
        public DisposableMats(int count)
        {
            Items = Enumerable.Range(0, count).Select(_ => new Mat()).ToArray();
        }

        public Mat[] Items { get; }

        public void Dispose()
        {
            foreach (var item in Items) item.Dispose();
        }
    }
}
