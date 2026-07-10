namespace KetoanMini.Api.Services;

/// <summary>
/// Vòng đệm SỐ ĐO chống giả mạo Silent-Face gần nhất (điểm P(real) từng khung của một lượt quét) để hiển
/// thị lên panel web — hiệu chỉnh ngưỡng/cách tổng hợp có SỐ LIỆU thay vì đoán. Chỉ trong RAM, mất khi
/// restart. Singleton.
/// </summary>
public sealed class LivenessMetricsLog
{
    private const int MaxItems = 20;
    private readonly object _gate = new();
    private readonly LinkedList<LivenessMetric> _items = new();

    /// <summary>
    /// Một lượt quét: điểm Silent-Face cao nhất/trung bình/nhì + số khung + ngưỡng + qua/chặn; kèm
    /// <paramref name="MotionSpan"/> = biên độ góc quay đầu (yaw max − min) của lượt đó (chống ảnh tĩnh:
    /// ảnh không quay đầu ⇒ span ≈ 0). MotionSpan &lt; 0 nghĩa là lượt này không chạy kiểm tra chuyển động.
    /// </summary>
    public readonly record struct LivenessMetric(
        DateTime AtUtc, string User, double Best, double Mean, double Second, int Frames, double Threshold,
        bool Passed, double MotionSpan);

    public void Record(string user, IReadOnlyList<double> scores, double threshold, bool passed, double motionSpan = -1)
    {
        double best = 0, mean = 0, second = 0;
        if (scores.Count > 0)
        {
            var sorted = scores.OrderByDescending(x => x).ToList();
            best = sorted[0];
            second = sorted.Count > 1 ? sorted[1] : sorted[0];
            mean = scores.Average();
        }
        lock (_gate)
        {
            _items.AddFirst(new LivenessMetric(DateTime.UtcNow, user ?? "", best, mean, second, scores.Count,
                threshold, passed, motionSpan));
            while (_items.Count > MaxItems) _items.RemoveLast();
        }
    }

    /// <summary>Các lượt gần nhất (mới → cũ).</summary>
    public IReadOnlyList<LivenessMetric> Recent()
    {
        lock (_gate) return _items.ToList();
    }
}
