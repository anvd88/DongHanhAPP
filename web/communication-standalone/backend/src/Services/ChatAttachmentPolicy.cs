using System.Text.RegularExpressions;

namespace KetoanMini.Api.Services;

/// <summary>
/// Phân loại nội dung chat có blob. Tệp thường chỉ được giữ tạm; tin thoại, ảnh và video là nội dung
/// của tin nhắn nên phải còn tải lại được cho tới khi người gửi chủ động gỡ tin.
/// </summary>
internal static partial class ChatAttachmentPolicy
{
    internal const string FileKind = "file";
    internal const string VoiceKind = "voice";

    private static readonly HashSet<string> VoiceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aac", ".amr", ".m4a", ".mp3", ".ogg", ".opus", ".wav", ".webm",
    };

    private static readonly HashSet<string> InlineMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".bmp", ".gif", ".heic", ".heif", ".jpeg", ".jpg", ".png", ".svg", ".webp",
        ".3gp", ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".ogv", ".webm",
    };

    /// <summary>
    /// Chỉ client ghi âm mới được yêu cầu kind=voice. Client cũ không gửi kind tiếp tục là file;
    /// lifecycle của bản ghi cũ được nhận diện riêng bởi <see cref="IsPersistentVoice"/>.
    /// </summary>
    internal static bool TryResolveKind(string? requestedKind, string fileName, string? fileMime, out string kind)
    {
        var requested = (requestedKind ?? "").Trim();
        if (requested.Length == 0 || string.Equals(requested, FileKind, StringComparison.OrdinalIgnoreCase))
        {
            kind = FileKind;
            return true;
        }

        if (string.Equals(requested, VoiceKind, StringComparison.OrdinalIgnoreCase) &&
            IsSupportedVoiceMedia(fileName, fileMime))
        {
            kind = VoiceKind;
            return true;
        }

        kind = FileKind;
        return false;
    }

    internal static bool IsPersistentVoice(string? kind, string? fileName, string? fileMime) =>
        string.Equals(kind, VoiceKind, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(kind, FileKind, StringComparison.OrdinalIgnoreCase) && IsLegacyRecordedVoice(fileName, fileMime));

    internal static bool IsPersistentInlineMedia(string? kind, string? fileName, string? fileMime)
    {
        if (!string.Equals(kind, FileKind, StringComparison.OrdinalIgnoreCase)) return false;
        var mime = (fileMime ?? "").Trim();
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return true;
        if (IsLegacyRecordedVoice(fileName, fileMime)) return false;
        return InlineMediaExtensions.Contains(Path.GetExtension(fileName ?? ""));
    }

    internal static bool IsPersistentAttachment(string? kind, string? fileName, string? fileMime) =>
        IsPersistentVoice(kind, fileName, fileMime) || IsPersistentInlineMedia(kind, fileName, fileMime);

    internal static DateTime? BlobExpiresAt(
        string? kind,
        string? fileName,
        string? fileMime,
        DateTime utcNow,
        TimeSpan fileTtl) =>
        IsPersistentAttachment(kind, fileName, fileMime) ? null : utcNow.Add(fileTtl);

    internal static bool DeleteAfterRecipientDownload(string? kind, string? fileName, string? fileMime) =>
        !IsPersistentAttachment(kind, fileName, fileMime);

    internal static bool IsLegacyRecordedVoice(string? fileName, string? fileMime)
    {
        var leafName = Path.GetFileName(fileName ?? "");
        return LegacyRecorderName().IsMatch(leafName) && IsSupportedVoiceMedia(leafName, fileMime);
    }

    internal static bool IsSupportedVoiceMedia(string? fileName, string? fileMime)
    {
        var mime = (fileMime ?? "").Trim();
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return true;
        return VoiceExtensions.Contains(Path.GetExtension(fileName ?? ""));
    }

    [GeneratedRegex(@"^ghi-am-[0-9]+\.(ogg|m4a)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyRecorderName();
}
