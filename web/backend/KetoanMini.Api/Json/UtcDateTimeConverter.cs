using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KetoanMini.Api.Json;

/// <summary>
/// Toàn bộ mốc thời gian trong hệ thống được lưu dạng UTC (SYSUTCDATETIME / DateTime.UtcNow).
/// Khi đọc từ SQL, DateTime có Kind = Unspecified nên System.Text.Json KHÔNG gắn 'Z' →
/// trình duyệt hiểu nhầm là giờ địa phương và hiển thị sai (lệch đúng số giờ múi giờ).
///
/// Converter này luôn xuất ISO-8601 có 'Z' (đánh dấu UTC) để client tự đổi sang giờ địa
/// phương. Áp dụng cho cả DateTime và DateTime? (System.Text.Json tự dùng cho Nullable).
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        // Chuẩn hóa về UTC để xử lý nhất quán phía server.
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified: giá trị lấy từ DB vốn là UTC → chỉ cần đánh dấu Kind.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
