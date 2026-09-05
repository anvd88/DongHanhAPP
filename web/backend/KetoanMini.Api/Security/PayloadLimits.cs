using KetoanMini.Api.Endpoints;

namespace KetoanMini.Api.Security;

public static class PayloadLimits
{
    public const long MaxJsonBodyBytes = 16L * 1024 * 1024;
    public const long MaxQrActionBodyBytes = 32L * 1024;
    public const long MaxApkBytes = 200L * 1024 * 1024;
    public const int MaxImageBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Trần SỐ ẢNH cho một lượt quét (chấm công, đăng nhập/đặt lại mật khẩu bằng khuôn mặt). Client gom cả
    /// loạt khung rồi server tự chọn khung tốt nhất, nên trần này PHẢI phủ được loạt khung thật của client:
    /// đặt thấp hơn thì MỌI lượt quét bị 400 trước khi engine kịp chạy (đã xảy ra: trần 12 trong khi web gửi
    /// 16 khung, app gửi 14). Sửa trần khung ở client thì phải soát lại trần này — xem KetoanMini.Api.Tests
    /// PayloadLimitsTests, nơi chốt bất biến "trần server ≥ loạt khung client".
    /// Tải nặng đã bị chặn bằng BYTE (<see cref="MaxImageBytes"/> mỗi ảnh + <see cref="MaxJsonBodyBytes"/> cả
    /// body); trần đếm này chỉ để giới hạn số lần chạy engine mỗi request.
    /// </summary>
    public const int MaxImagesPerRequest = 16;

    /// <summary>
    /// Đăng ký khuôn mặt gửi NHIỀU GÓC trong MỘT request (3 góc × tối đa 10 khung) nên tổng ảnh cao hơn hẳn
    /// một lượt quét thường. Endpoint có xác thực và mỗi tài khoản chỉ đăng ký được một lần ⇒ nới an toàn.
    /// </summary>
    public const int MaxImagesPerEnrollRequest = 36;

    /// <summary>
    /// Trần Content-Length cho một request. Vài endpoint nhận payload lớn hơn nhiều trần JSON và tự
    /// áp giới hạn riêng trong handler; chốt chặn 413 phải nới đúng bằng trần của handler, nếu không
    /// request hợp lệ bị trả 413 trước khi handler kịp chạy.
    /// </summary>
    public static long MaxRequestBytesFor(string? method, PathString path)
    {
        if (!HttpMethods.IsPost(method ?? "")) return MaxJsonBodyBytes;

        // Web gọi "/api/releases/" (có "/" cuối) nên phải bỏ "/" cuối trước khi so khớp.
        var value = path.Value?.TrimEnd('/');
        if (value is null) return MaxJsonBodyBytes;

        if (string.Equals(value, "/api/releases", StringComparison.OrdinalIgnoreCase))
            return MaxApkBytes;

        if (path.StartsWithSegments("/api/qr"))
            return MaxQrActionBodyBytes;

        return MaxJsonBodyBytes;
    }

    public static bool ExceedsEncodedImageLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var comma = value.IndexOf(',');
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            value = value[(comma + 1)..];
        return value.Length > ((MaxImageBytes + 2) / 3) * 4;
    }

    public static bool TryDecodeImage(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;

        var comma = value.IndexOf(',');
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            value = value[(comma + 1)..];

        // Base64 expands data by 4/3. Reject before allocating a large byte array.
        if (ExceedsEncodedImageLimit(value)) return false;
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes is { Length: > 0 and <= MaxImageBytes };
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
