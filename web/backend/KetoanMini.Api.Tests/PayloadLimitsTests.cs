using System.Globalization;
using System.Text.RegularExpressions;
using KetoanMini.Api.Security;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Chốt bất biến "trần ảnh của server ≥ loạt khung client gửi" cho các luồng quét khuôn mặt.
///
/// Client (web + app) gom cả LOẠT khung rồi để server chọn khung tốt nhất. Nếu trần đếm ảnh của server nhỏ
/// hơn loạt khung client gửi thì MỌI lượt quét bị 400 ("Tối đa N ảnh mỗi yêu cầu") ngay trước khi engine kịp
/// chạy — người dùng chỉ thấy "Chưa nhận diện được". Đã xảy ra thật: trần server là 12 trong khi web gửi 16
/// khung, app gửi 14, còn tự đăng ký khuôn mặt gửi tới 3 góc × 10 khung.
///
/// Test đọc THẲNG hằng số trong mã client (Kotlin/TS) vì đây là ràng buộc xuyên ngôn ngữ — không compiler nào
/// bắt hộ. Regex không khớp ⇒ hằng số đã đổi tên/dời chỗ ⇒ test đỏ để buộc soát lại cặp trần này.
/// </summary>
public sealed class PayloadLimitsTests
{
    private static readonly string[] AttendanceCameraPath =
        ["Ketoanapk", "android", "app", "src", "main", "kotlin", "com", "ketoanapk", "hr", "ui", "AttendanceCamera.kt"];

    private static readonly string[] FaceEnrollPath =
        ["Ketoanapk", "android", "app", "src", "main", "kotlin", "com", "ketoanapk", "hr", "ui", "FaceEnrollScreen.kt"];

    // Bản viết lại frontend (2026-08-31) gộp trạm chấm công vào một tệp màn hình; hằng số chụp
    // liên tiếp đổi tên từ MAX_AIM_FRAMES sang BURST_FRAMES. Test này trỏ vào tệp cũ nên đã im
    // tiếng từ đó — chốt lại theo tệp mới.
    private static readonly string[] CheckInScannerPath =
        ["frontend", "src", "pages", "cham-cong.tsx"];

    [Fact]
    public void AndroidAttendanceBurst_FitsServerLimit()
    {
        var src = ReadClientSource(AttendanceCameraPath);

        // Quét giữ khung tĩnh: mọi khung mang slot -1 ⇒ trần đúng bằng MAX_FRAMES.
        var maxFrames = ReadConstant(src, @"const val MAX_FRAMES\s*=\s*(\d+)", "MAX_FRAMES (AttendanceCamera.kt)");

        Assert.True(
            maxFrames <= PayloadLimits.MaxImagesPerRequest,
            $"App chấm công gửi tối đa {maxFrames} khung nhưng server chỉ nhận {PayloadLimits.MaxImagesPerRequest} "
            + "⇒ mọi lượt chấm công sẽ bị 400. Nới PayloadLimits.MaxImagesPerRequest hoặc hạ MAX_FRAMES.");
    }

    [Fact]
    public void AndroidMotionBurst_FitsServerLimit()
    {
        var src = ReadClientSource(AttendanceCameraPath);

        // Liveness quay đầu: 3 bước (motionStep 0/1/2) gắn slot 0/1/2, mỗi slot giữ tối đa PER_SLOT_CAP khung.
        const int motionSlots = 3;
        var perSlot = ReadConstant(src, @"const val PER_SLOT_CAP\s*=\s*(\d+)", "PER_SLOT_CAP (AttendanceCamera.kt)");
        var burst = perSlot * motionSlots;

        Assert.True(
            burst <= PayloadLimits.MaxImagesPerRequest,
            $"Chấm công kiểu quay đầu gửi tối đa {burst} khung ({motionSlots} slot × {perSlot}) nhưng server chỉ "
            + $"nhận {PayloadLimits.MaxImagesPerRequest} ⇒ mọi lượt quay đầu sẽ bị 400.");
    }

    [Fact]
    public void AndroidSelfEnrollBurst_FitsServerEnrollLimit()
    {
        var src = ReadClientSource(FaceEnrollPath);

        // Tự đăng ký gửi MỘT request gồm nhiều góc, mỗi góc một loạt khung ⇒ server cộng TỔNG các góc.
        var perPose = ReadConstant(src, @"const val E_MAX_FRAMES\s*=\s*(\d+)", "E_MAX_FRAMES (FaceEnrollScreen.kt)");
        var poses = Regex.Matches(src, @"EnrollPoseSpec\(""").Count;
        Assert.True(poses > 0, "Không đọc được số góc trong ENROLL_POSES — soát lại trần ảnh đăng ký của server.");

        var burst = perPose * poses;
        Assert.True(
            burst <= PayloadLimits.MaxImagesPerEnrollRequest,
            $"Tự đăng ký khuôn mặt gửi tối đa {burst} ảnh ({poses} góc × {perPose}) nhưng server chỉ nhận "
            + $"{PayloadLimits.MaxImagesPerEnrollRequest} ⇒ không ai đăng ký được khuôn mặt.");
    }

    [Fact]
    public void WebCheckInBurst_FitsServerLimit()
    {
        var src = ReadClientSource(CheckInScannerPath);
        var maxFrames = ReadConstant(src, @"const BURST_FRAMES\s*=\s*(\d+)", "BURST_FRAMES (cham-cong.tsx)");

        Assert.True(
            maxFrames <= PayloadLimits.MaxImagesPerRequest,
            $"Web chấm công gửi tối đa {maxFrames} khung nhưng server chỉ nhận {PayloadLimits.MaxImagesPerRequest} "
            + "⇒ mọi lượt chấm công trên web/kiosk sẽ bị 400.");
    }

    // --- Đọc mã client ---------------------------------------------------------------------------------

    private static string ReadClientSource(string[] parts)
    {
        var path = Path.Combine([RepoRoot, .. parts]);
        Assert.True(File.Exists(path), $"Không thấy tệp mã client: {path}");
        return File.ReadAllText(path);
    }

    private static int ReadConstant(string source, string pattern, string what)
    {
        var m = Regex.Match(source, pattern);
        Assert.True(m.Success, $"Không đọc được hằng số {what} — có thể đã đổi tên. Soát lại trần ảnh của server.");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static readonly string RepoRoot = FindRepoRoot();

    // Thư mục gốc = nơi chứa cả mã app (Ketoanapk/) lẫn mã web (frontend/), tìm bằng cách đi ngược từ thư mục build.
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Ketoanapk"))
                && Directory.Exists(Path.Combine(dir.FullName, "frontend")))
                return dir.FullName;
        }
        throw new InvalidOperationException(
            $"Không tìm thấy thư mục gốc chứa Ketoanapk/ và frontend/ (đi ngược từ {AppContext.BaseDirectory}).");
    }
}
