using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace KetoanMini.Api.Services;

/// <summary>
/// Face engine using YuNet for detection/alignment and AdaFace IR/R50 for recognition.
/// The AdaFace recognizer runs with ONNX Runtime. Liveness remains MiniFASNet through OpenCV DNN.
/// </summary>
public sealed class AdaFaceR50Engine : IFaceEngine, IDisposable
{
    private static readonly string[] YuNetOutputs =
    [
        "cls_8", "cls_16", "cls_32",
        "obj_8", "obj_16", "obj_32",
        "bbox_8", "bbox_16", "bbox_32",
        "kps_8", "kps_16", "kps_32",
    ];

    private static readonly int[] Strides = [8, 16, 32];
    private static readonly Point2f[] FaceReference =
    [
        new(38.2946f, 51.6963f),
        new(73.5318f, 51.5014f),
        new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f),
        new(70.7299f, 92.2041f),
    ];

    private const double LivenessCropFactor = 1.5;
    private const int LivenessSize = 128;
    private const double LivenessRealThreshold = 0.5;
    private const double DefaultMatchThreshold = 0.45;

    private readonly Net _detectorNet;
    private readonly InferenceSession _recognizerSession;
    private readonly string _recognizerInputName;
    private readonly SilentFaceLiveness? _silentFace;
    private readonly Net? _livenessNet;
    private readonly object _gate = new();

    private byte[]? _lastBytes;
    private FaceDetection? _lastDet;

    public AdaFaceR50Engine(IConfiguration config)
    {
        var modelDir = Path.Combine(AppContext.BaseDirectory, "Models", "Face");
        var detectorPath = Path.Combine(modelDir, "face_detection_yunet_2023mar.onnx");
        var recognizerPath = config["FaceRecognition:AdaFaceR50ModelPath"];
        if (string.IsNullOrWhiteSpace(recognizerPath))
            recognizerPath = Path.Combine(modelDir, "adaface.onnx");
        else if (!Path.IsPathRooted(recognizerPath))
            recognizerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, recognizerPath));

        if (!File.Exists(detectorPath))
            throw new FileNotFoundException("Missing YuNet face detection model.", detectorPath);
        if (!File.Exists(recognizerPath))
            throw new FileNotFoundException(
                "Missing AdaFace R50 ONNX model. Put it at Models/Face/adaface.onnx or set FaceRecognition:AdaFaceR50ModelPath.",
                recognizerPath);

        _detectorNet = CvDnn.ReadNetFromOnnx(detectorPath)
            ?? throw new InvalidOperationException("Cannot open YuNet model.");
        _detectorNet.SetPreferableBackend(Backend.OPENCV);
        _detectorNet.SetPreferableTarget(Target.CPU);

        // Tối ưu phiên ONNX Runtime cho R50 trên CPU: bật mọi phép gộp đồ thị và giới hạn số luồng
        // nội-toán (tránh tranh chấp luồng khi nhiều request chạy song song, nhanh & ổn định hơn).
        var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            InterOpNumThreads = 1,
        };
        _recognizerSession = new InferenceSession(recognizerPath, sessionOptions);
        _recognizerInputName = _recognizerSession.InputMetadata.Keys.FirstOrDefault()
            ?? throw new InvalidOperationException("AdaFace ONNX model has no input.");

        // Chống giả mạo ưu tiên Silent-Face (2 model MiniFASNetV2 + MiniFASNetV1SE, 80×80, 3 lớp).
        // Đặt 2 file .onnx vào Models/Face (xem SilentFace-AntiSpoof-README.md). Nếu chưa có,
        // lùi về model MiniFASNet đơn lẻ cũ (face_antispoof_minifasnet.onnx) để liveness vẫn chạy.
        try
        {
            var sf = new SilentFaceLiveness(modelDir);
            if (sf.Available) _silentFace = sf; else sf.Dispose();
        }
        catch { _silentFace = null; }

        if (_silentFace is null)
        {
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
    }

    public string Name => _silentFace is not null
        ? "AdaFace R50 (YuNet + 5-point alignment + ONNX Runtime + Silent-Face MiniFASNetV2/V1SE anti-spoof)"
        : _livenessNet is null
            ? "AdaFace R50 (YuNet + 5-point alignment + ONNX Runtime) - no anti-spoof model"
            : "AdaFace R50 (YuNet + 5-point alignment + ONNX Runtime + MiniFASNet anti-spoof)";

    public double MatchThreshold => DefaultMatchThreshold;

    public double LivenessThreshold => LivenessRealThreshold;

    public bool CheckLiveness(byte[] imageBytes) => LivenessProbability(imageBytes) >= LivenessRealThreshold;

    public double LivenessProbability(byte[] imageBytes)
    {
        using var image = Decode(imageBytes);
        if (image is null) return 0;

        lock (_gate)
        {
            var face = DetectCached(imageBytes, image);
            if (face is null) return 0;
            if (_silentFace is null && _livenessNet is null) return 1;
            return LivenessScore(image, face);
        }
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
            var tensor = ToAdaFaceTensor(aligned);
            var input = NamedOnnxValue.CreateFromTensor(_recognizerInputName, tensor);
            using var results = _recognizerSession.Run([input]);
            var feature = results.First().AsEnumerable<float>().ToArray();
            NormalizeInPlace(feature);
            return feature;
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
            var sharpNorm = Math.Clamp(sharpness / 250.0, 0, 1);
            var sizeNorm = Math.Clamp(faceRatio / 0.12, 0, 1);
            var brightScore = 1.0 - Math.Min(1.0, Math.Abs(brightness - 0.52) / 0.45);
            var frontal = 1.0 - Math.Min(1.0, Math.Abs(pose.Yaw) / 0.30);
            var glarePenalty = Math.Clamp(glare / 0.08, 0, 1);

            var score = (0.34 * sharpNorm + 0.22 * sizeNorm + 0.16 * brightScore
                         + 0.18 * frontal + 0.10 * face.Score) * (1.0 - 0.6 * glarePenalty);

            return new FaceFrameQuality(true, Math.Clamp(score, 0, 1),
                sharpNorm, brightness, glare, faceRatio, pose, face.Score);
        }
    }

    private double LivenessScore(Mat imageBgr, FaceDetection face)
    {
        // Ưu tiên Silent-Face: crop theo tỉ lệ riêng từng model rồi softmax 3 lớp, hợp nhất 2 model.
        if (_silentFace is not null)
        {
            try
            {
                var rect = ClampRect(face.Rect, imageBgr.Width, imageBgr.Height);
                if (rect.Width <= 1 || rect.Height <= 1) return 1.0;
                return _silentFace.ProbabilityReal(imageBgr, rect);
            }
            catch
            {
                return 1.0;
            }
        }

        // Lùi về model MiniFASNet đơn lẻ cũ (2 lớp, 128×128) nếu chưa cài Silent-Face.
        try
        {
            using var crop = CropForLiveness(imageBgr, face);
            if (crop is null || crop.Empty()) return 1.0;

            using var resized = new Mat();
            Cv2.Resize(crop, resized, new Size(LivenessSize, LivenessSize), 0, 0,
                crop.Width > LivenessSize ? InterpolationFlags.Area : InterpolationFlags.Lanczos4);

            using var blob = CvDnn.BlobFromImage(resized, 1.0 / 255.0,
                new Size(LivenessSize, LivenessSize), new Scalar(0, 0, 0), true, false);
            _livenessNet!.SetInput(blob);
            using var output = _livenessNet.Forward();

            using var flat = output.Reshape(1, 1);
            if (flat.Cols < 2) return 1.0;
            var diff = flat.At<float>(0, 0) - flat.At<float>(0, 1);
            return 1.0 / (1.0 + Math.Exp(-diff));
        }
        catch
        {
            return 1.0;
        }
    }

    private static DenseTensor<float> ToAdaFaceTensor(Mat alignedBgr)
    {
        // AdaFace gốc nạp ảnh theo thứ tự kênh BGR (cv2) rồi chuẩn hóa (x/127.5 − 1) — KHÔNG đảo sang RGB.
        // Vì vậy kênh 0 = B, kênh 1 = G, kênh 2 = R để khớp đúng phân phối lúc huấn luyện.
        // (Trước đây nạp nhầm theo RGB nên embedding tự nhất quán nhưng kém phân biệt; sửa lại buộc
        //  đăng ký lại toàn bộ mẫu cũ vì vector mới không còn so khớp được với vector RGB cũ.)
        var tensor = new DenseTensor<float>([1, 3, 112, 112]);
        var values = tensor.Buffer.Span;
        for (var y = 0; y < 112; y++)
        {
            for (var x = 0; x < 112; x++)
            {
                var pixel = alignedBgr.At<Vec3b>(y, x);
                var offset = y * 112 + x;
                values[offset] = pixel.Item0 / 127.5f - 1.0f;              // B
                values[112 * 112 + offset] = pixel.Item1 / 127.5f - 1.0f;  // G
                values[2 * 112 * 112 + offset] = pixel.Item2 / 127.5f - 1.0f; // R
            }
        }
        return tensor;
    }

    private static void NormalizeInPlace(float[] values)
    {
        double norm = 0;
        foreach (var v in values) norm += v * v;
        norm = Math.Sqrt(norm);
        if (norm <= 0) return;
        for (var i = 0; i < values.Length; i++)
            values[i] = (float)(values[i] / norm);
    }

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
        EnhanceForLowLight(image);
        return image;
    }

    private const double EnhanceDarkMean = 110;
    private const double EnhanceBrightMean = 200;
    private const double EnhanceGlareRatio = 0.045;

    private static void EnhanceForLowLight(Mat bgr)
    {
        using var lab = new Mat();
        Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
        var ch = Cv2.Split(lab);
        try
        {
            var l = ch[0];
            var mean = l.Mean().Val0;

            using var saturated = new Mat();
            Cv2.Threshold(l, saturated, 245, 255, ThresholdTypes.Binary);
            var glare = Cv2.CountNonZero(saturated) / (double)(l.Rows * l.Cols);

            var tooDark = mean < EnhanceDarkMean;
            var hasGlare = glare > EnhanceGlareRatio || mean > EnhanceBrightMean;
            if (!tooDark && !hasGlare) return;

            if (hasGlare) ApplyGammaInPlace(l, 1.5);
            if (tooDark) ApplyGammaInPlace(l, 0.7);

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

    private static void ApplyGammaInPlace(Mat gray8u, double gamma)
    {
        using var lut = new Mat(1, 256, MatType.CV_8UC1);
        var idx = lut.GetGenericIndexer<byte>();
        for (var i = 0; i < 256; i++)
            idx[0, i] = (byte)Math.Clamp(Math.Pow(i / 255.0, gamma) * 255.0, 0, 255);
        Cv2.LUT(gray8u, lut, gray8u);
    }

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
        Point2f[] src = [face.RightEye, face.LeftEye, face.Nose, face.RightMouth, face.LeftMouth];

        using var srcArr = InputArray.Create(src, MatType.CV_32FC2);
        using var dstArr = InputArray.Create(FaceReference, MatType.CV_32FC2);
        using var transform = Cv2.EstimateAffinePartial2D(srcArr, dstArr);

        var aligned = new Mat();
        if (transform is not null && !transform.Empty())
        {
            Cv2.WarpAffine(image, aligned, transform, new Size(112, 112), InterpolationFlags.Linear);
        }
        else
        {
            using var fallback = Cv2.GetAffineTransform(
                new[] { face.RightEye, face.LeftEye, face.Nose },
                new[] { FaceReference[0], FaceReference[1], FaceReference[2] });
            Cv2.WarpAffine(image, aligned, fallback, new Size(112, 112), InterpolationFlags.Linear);
        }
        return aligned;
    }

    public void Dispose()
    {
        _detectorNet.Dispose();
        _recognizerSession.Dispose();
        _silentFace?.Dispose();
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
