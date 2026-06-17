using System.Reflection;

namespace KetoanMini;

/// <summary>
/// Trợ giúp lấy và so sánh phiên bản ứng dụng. Phiên bản hiện tại lấy từ
/// AssemblyVersion (khai báo &lt;Version&gt; trong KetoanMini.csproj).
/// </summary>
public static class AppVersion
{
    /// <summary>Phiên bản đang chạy của ứng dụng (vd: 1.1.0).</summary>
    public static Version Current { get; } = ResolveCurrent();

    /// <summary>Chuỗi phiên bản hiển thị, dạng "x.y.z".</summary>
    public static string CurrentText => Current.ToString(3);

    private static Version ResolveCurrent()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? new Version(1, 0, 0) : NormalizeParts(version);
    }

    /// <summary>
    /// Phân tích một chuỗi phiên bản (vd "1.2", "1.2.3", "1.2.3.4") thành Version.
    /// Trả về 0.0.0 nếu không hợp lệ.
    /// </summary>
    public static Version Parse(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
        {
            return new Version(0, 0, 0);
        }

        // Version.TryParse cần ít nhất "major.minor"; bổ sung phần thiếu.
        if (!text.Contains('.'))
        {
            text += ".0";
        }

        return Version.TryParse(text, out var parsed)
            ? NormalizeParts(parsed)
            : new Version(0, 0, 0);
    }

    /// <summary>So sánh hai chuỗi phiên bản: &lt;0 nếu a cũ hơn b, 0 nếu bằng, &gt;0 nếu a mới hơn.</summary>
    public static int Compare(string a, string b) => Parse(a).CompareTo(Parse(b));

    /// <summary>True nếu phiên bản hiện tại cũ hơn <paramref name="otherVersion"/>.</summary>
    public static bool CurrentIsOlderThan(string otherVersion) => Current.CompareTo(Parse(otherVersion)) < 0;

    /// <summary>True nếu chuỗi <paramref name="text"/> là số phiên bản hợp lệ và lớn hơn 0.0.0.</summary>
    public static bool IsValid(string? text) => Parse(text) > new Version(0, 0, 0);

    private static Version NormalizeParts(Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0));
    }
}
