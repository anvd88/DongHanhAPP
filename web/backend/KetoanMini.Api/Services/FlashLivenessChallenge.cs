using System.Security.Cryptography;
using KetoanMini.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace KetoanMini.Api.Services;

/// <summary>
/// ACTIVE-FLASH LIVENESS (chống giả mạo chủ động bằng ánh sáng màn hình).
///
/// Ý tưởng: server phát một CHUỖI MÀU NGẪU NHIÊN (theo phiên) → app hiển thị full màn hình từng màu
/// đúng thứ tự trong lúc quét, đồng thời gắn nhãn slot cho mỗi khung ảnh → server đo màu phản xạ trên
/// mặt theo từng slot và kiểm tra nó BIẾN THIÊN ĂN KHỚP với màu đã chiếu.
///
/// Chống được các đòn mà Silent-Face thụ động bỏ lọt:
///   • Phát lại video / deepfake stream: luồng dựng sẵn KHÔNG thể phản xạ đúng chuỗi màu ngẫu nhiên.
///   • Bơm video giả vào camera (camera injection): kẻ tấn công không biết trước chuỗi ⇒ tương quan ~0.
/// Vẫn giữ Silent-Face song song (ảnh in/màn hình giơ tay phản xạ được màu nhưng lộ ở kết cấu) ⇒ phòng
/// thủ nhiều lớp: yêu cầu QUA CẢ HAI.
///
/// Ngưỡng để nới tay (AWB của camera triệt bớt cast màu) — tinh chỉnh trên thiết bị thật nếu cần.
/// </summary>
public sealed class FlashLivenessChallenge
{
    // Ba màu cho phản hồi kênh rõ nhưng ĐÃ DỊU (bớt gắt/loá mắt). Bộ (Hex gửi client hiển thị, chroma
    // kỳ vọng r,g,b = HƯỚNG lệch màu để đối chiếu — không đổi khi dịu độ sáng hex).
    private static readonly (string Hex, double R, double G, double B)[] Palette =
    [
        ("#C83C2D", 1.00, 0.10, 0.06), // đỏ dịu
        ("#2FB048", 0.10, 1.00, 0.16), // lục dịu
        ("#3050D0", 0.10, 0.22, 1.00), // lam dịu
    ];

    public const int SlotCount = 6;   // số ô màu trong một chuỗi
    public const int SlotMs = 460;    // mỗi màu hiển thị bao lâu (ms) — chậm hơn chút cho đỡ nhấp nháy
    public const int SettleMs = 160;  // chờ màn hình + camera ổn định trước khi gắn khung vào slot (ms)

    // TTL ngắn: đủ để quét (~3s) + gửi preview + xác nhận. Single-use khi GHI công thật.
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(45);

    // Chặn cứng hay chỉ ghi log do CẤU HÌNH RUNTIME quyết định (bảng web_system_settings, admin bật/tắt
    // không cần build/restart) — truyền vào qua tham số enforce của Verify. Mặc định (chưa cấu hình) = chỉ
    // ghi log để HIỆU CHỈNH: verify vẫn chạy + log corr/reactivity nhưng KHÔNG chặn (Silent-Face vẫn gác).

    // Ngưỡng đã HẠ THẤP & fail-open: chỉ nghi ngờ khi mặt gần như KHÔNG phản ứng theo màn hình (chữ ký của
    // ảnh tĩnh / luồng bơm giả). Tinh chỉnh theo số log đo được trên máy thật.
    private const double MinReactivity = 0.0015; // tổng phương sai chroma tối thiểu (mặt phải đổi màu theo screen)
    private const double MinCorrelation = 0.15;  // tương quan TB 3 kênh giữa màu chiếu và màu phản xạ
    private const int MinSlotsWithFace = 3;      // dưới mức này ⇒ thiếu dữ liệu ⇒ FAIL-OPEN (không kết luận)
    private const int MinDistinctColors = 2;     // cần ít nhất 2 màu để đối chiếu có nghĩa

    private readonly IMemoryCache _cache;
    public FlashLivenessChallenge(IMemoryCache cache) => _cache = cache;

    // Vòng đệm SỐ ĐO gần nhất để hiển thị lên panel web (hiệu chỉnh ngưỡng mà không cần đọc console API).
    private const int MaxMetrics = 20;
    private readonly object _metricsGate = new();
    private readonly LinkedList<Metric> _metrics = new();

    /// <summary>Một lần đo flash-liveness (để admin so người thật ↔ giả rồi đặt ngưỡng).</summary>
    public readonly record struct Metric(DateTime AtUtc, string User, int SlotsWithFace,
        double Correlation, double Reactivity, string Reason, bool Suspect, bool Blocked);

    /// <summary>Số đo gần nhất (mới → cũ).</summary>
    public IReadOnlyList<Metric> RecentMetrics()
    {
        lock (_metricsGate) return _metrics.ToList();
    }

    private void AddMetric(string user, in Result r)
    {
        lock (_metricsGate)
        {
            _metrics.AddFirst(new Metric(DateTime.UtcNow, user ?? "", r.SlotsWithFace,
                r.Correlation, r.Reactivity, r.Reason, r.Suspect, r.Blocked));
            while (_metrics.Count > MaxMetrics) _metrics.RemoveLast();
        }
    }

    // Challenge đã phát: ràng buộc theo user + chuỗi chỉ số màu server đang mong đợi.
    private sealed record Issued(string User, int[] ColorIdx);

    private static string CacheKey(string id) => "flashlv:" + id;

    /// <summary>Sinh chuỗi màu ngẫu nhiên, lưu tạm (bind user, TTL) và trả về cho client hiển thị.</summary>
    public FlashChallengeResponse Issue(string user)
    {
        var idx = new int[SlotCount];
        var prev = -1;
        for (var i = 0; i < SlotCount; i++)
        {
            int c;
            do { c = RandomNumberGenerator.GetInt32(Palette.Length); } while (c == prev && Palette.Length > 1);
            idx[i] = c;
            prev = c;
        }
        EnsureAllColors(idx); // bảo đảm đủ 3 màu xuất hiện (tương quan mới có nghĩa)

        var id = Guid.NewGuid().ToString("N");
        _cache.Set(CacheKey(id), new Issued(user ?? "", idx), Ttl);

        var slots = new List<FlashSlot>(SlotCount);
        for (var i = 0; i < SlotCount; i++) slots.Add(new FlashSlot(i, Palette[idx[i]].Hex));
        return new FlashChallengeResponse(id, slots, SlotMs, SettleMs);
    }

    /// <summary>
    /// Kết quả xác minh. <see cref="Suspect"/> = số đo cho thấy KHÔNG phản ứng theo màn hình (nghi giả);
    /// <see cref="Blocked"/> = có chặn thật không (chỉ khi <see cref="EnforceBlock"/> bật). Kèm số đo để log.
    /// </summary>
    public readonly record struct Result(
        bool Suspect, bool Blocked, double Correlation, double Reactivity, int SlotsWithFace, string Reason);

    /// <summary>
    /// Đối chiếu màu phản xạ trên mặt (đã gắn slot) với chuỗi màu đã phát. Fail-open: chỉ NGHI (và chỉ chặn
    /// khi <see cref="EnforceBlock"/>) khi mặt gần như KHÔNG phản ứng theo màn hình VÀ không tương quan —
    /// chữ ký của ảnh tĩnh / luồng camera bơm giả. Thiếu dữ liệu (ít slot/màu) ⇒ không kết luận, cho qua.
    /// <paramref name="consume"/>=true ⇒ vô hiệu hóa challenge sau khi kết luận không nghi (chỉ khi GHI công
    /// thật) để chống phát lại chính challenge đó.
    /// </summary>
    public Result Verify(string user, string? challengeId,
        IReadOnlyList<(int Slot, FaceSkinColor Color)> samples, bool enforce, bool consume)
    {
        // Ghi mọi lần đo vào vòng đệm (kể cả các trường hợp cho qua) để panel hiển thị hiệu chỉnh.
        Result Log(Result r) { AddMetric(user, r); return r; }

        // Sai/hết hạn/nhầm user: không có cơ sở đối chiếu ⇒ không kết luận (Silent-Face vẫn gác).
        if (string.IsNullOrWhiteSpace(challengeId)) return Log(Pass("missing"));
        if (!_cache.TryGetValue(CacheKey(challengeId), out Issued? issued) || issued is null)
            return Log(Pass("expired"));
        if (!string.Equals(issued.User, user ?? "", StringComparison.OrdinalIgnoreCase))
            return Log(Pass("wronguser"));

        // Gom chroma (màu đã chuẩn hóa theo tổng sáng) trung bình cho từng slot.
        var perSlot = new Dictionary<int, (double r, double g, double b, int n)>();
        foreach (var (slot, col) in samples)
        {
            if (slot < 0 || slot >= issued.ColorIdx.Length) continue;
            var sum = col.R + col.G + col.B;
            if (sum <= 1e-6) continue;
            double r = col.R / sum, g = col.G / sum, b = col.B / sum;
            if (perSlot.TryGetValue(slot, out var cur))
                perSlot[slot] = (cur.r + r, cur.g + g, cur.b + b, cur.n + 1);
            else
                perSlot[slot] = (r, g, b, 1);
        }

        var slotsWithFace = perSlot.Count;
        if (slotsWithFace < MinSlotsWithFace) return Log(Pass("insufficient", slotsWithFace));

        // Dựng vector quan sát (phản xạ) và kỳ vọng (màu chiếu) theo các slot có dữ liệu.
        var obsR = new List<double>(); var obsG = new List<double>(); var obsB = new List<double>();
        var expR = new List<double>(); var expG = new List<double>(); var expB = new List<double>();
        var colorsSeen = new HashSet<int>();
        foreach (var (slot, acc) in perSlot)
        {
            obsR.Add(acc.r / acc.n); obsG.Add(acc.g / acc.n); obsB.Add(acc.b / acc.n);
            var ci = issued.ColorIdx[slot];
            colorsSeen.Add(ci);
            var (_, er, eg, eb) = Palette[ci];
            var es = er + eg + eb;
            expR.Add(er / es); expG.Add(eg / es); expB.Add(eb / es);
        }
        if (colorsSeen.Count < MinDistinctColors) return Log(Pass("fewcolors", slotsWithFace));

        // Phản ứng: mặt phải ĐỔI màu theo màn hình (variance chroma). Ảnh tĩnh/luồng bơm ≈ bất biến.
        var reactivity = Variance(obsR) + Variance(obsG) + Variance(obsB);

        // Tương quan Pearson từng kênh giữa màu chiếu (kỳ vọng) và màu phản xạ (quan sát), lấy trung bình.
        var corrs = new List<double>(3);
        AddCorr(corrs, expR, obsR);
        AddCorr(corrs, expG, obsG);
        AddCorr(corrs, expB, obsB);
        var corr = corrs.Count == 0 ? 0 : corrs.Average();

        // NGHI khi VỪA phẳng VỪA không tương quan (mặt không hề hưởng ứng màn hình). Người thật chỉ cần
        // MỘT trong hai tín hiệu là qua ⇒ giảm mạnh từ chối nhầm khi AWB camera triệt bớt cast.
        var suspect = reactivity < MinReactivity && corr < MinCorrelation;
        var blocked = suspect && enforce;
        if (!blocked && consume) _cache.Remove(CacheKey(challengeId));
        return Log(new(suspect, blocked, corr, reactivity, slotsWithFace, suspect ? "suspect" : "ok"));
    }

    // Cho qua (không nghi) khi không đủ cơ sở kết luận — an toàn về usability, Silent-Face vẫn gác.
    private static Result Pass(string reason, int slots = 0) => new(false, false, 0, 0, slots, reason);

    private static void AddCorr(List<double> into, IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var c = Pearson(x, y);
        if (!double.IsNaN(c)) into.Add(c);
    }

    // Bảo đảm cả 3 màu đều xuất hiện: thay một slot đang bị lặp bằng màu còn thiếu (SlotCount=6 > 3 màu
    // ⇒ luôn tồn tại slot lặp theo nguyên lý chuồng bồ câu).
    private static void EnsureAllColors(int[] idx)
    {
        for (var c = 0; c < Palette.Length; c++)
        {
            if (Array.IndexOf(idx, c) >= 0) continue;
            for (var i = 0; i < idx.Length; i++)
            {
                if (CountOf(idx, idx[i]) > 1) { idx[i] = c; break; }
            }
        }
    }

    private static int CountOf(int[] a, int val)
    {
        var c = 0;
        foreach (var x in a) if (x == val) c++;
        return c;
    }

    private static double Variance(IReadOnlyList<double> v)
    {
        var n = v.Count;
        if (n < 2) return 0;
        double m = 0;
        foreach (var x in v) m += x;
        m /= n;
        double s = 0;
        foreach (var x in v) { var d = x - m; s += d * d; }
        return s / n;
    }

    private static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = x.Count;
        if (n < 2 || y.Count != n) return double.NaN;
        double mx = 0, my = 0;
        for (var i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
        mx /= n; my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
        }
        if (sxx <= 1e-12 || syy <= 1e-12) return double.NaN;
        return sxy / Math.Sqrt(sxx * syy);
    }
}
