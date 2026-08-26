using Microsoft.Extensions.Options;

namespace KetoanMini.Api.Services;

/// <summary>
/// Bọc <see cref="AdaFaceR50Engine"/> để model chỉ nằm trong RAM khi thực sự dùng.
///
/// VÌ SAO: đo trên chính máy chủ này, engine chiếm ~348 MB private (adaface.onnx nặng 166 MB cộng
/// arena của ONNX Runtime) — khoảng 79% bộ nhớ tiến trình API. Nhưng phần lớn thời gian trong ngày
/// không ai chấm công. Nạp lại chỉ tốn ~0,8 giây nên giữ thường trú là lãng phí.
///
/// Đo thực tế 3 vòng nạp/giải phóng: 375 MB → 34,5 MB → 384 MB → 37,1 MB → 377 MB → 36,8 MB.
/// Mức nền đứng yên, không phình dần, nên vòng lặp này không gây phân mảnh.
///
/// AN TOÀN LUỒNG — đây là chỗ dễ chết nhất:
/// OpenCV/ONNX là native. Dispose trong khi một luồng khác đang inference là use-after-free, sập cả
/// tiến trình chứ không phải ném exception. Vì vậy mọi lối vào đều phải đi qua <see cref="Lease"/>
/// đếm tham chiếu; bộ quét nhàn rỗi chỉ được thu hồi khi số lượt đang chạy về 0.
///
/// FAIL-CLOSED: khi chưa từng nạp được model, <see cref="AntiSpoof"/> giữ mức
/// <see cref="AntiSpoofLevel.None"/> nên <see cref="FaceAntiSpoofSecurity"/> chặn mọi lượt quét.
/// Không bao giờ được đoán bừa là "Full" chỉ vì chưa nạp.
/// </summary>
public sealed class LazyFaceEngine : IFaceEngine, IDisposable
{
    private readonly Func<IFaceEngine> _factory;
    private readonly ILogger<LazyFaceEngine> _logger;
    private readonly object _gate = new();

    private IFaceEngine? _engine;
    private int _activeLeases;
    private DateTime _lastUsedUtc = DateTime.UtcNow;
    private bool _disposed;

    // Metadata chụp lại từ lần nạp thành công đầu tiên. Trước đó phải là giá trị ĐÓNG:
    // AntiSpoofLevel.None ⇒ mọi lượt chấm công bị từ chối, thay vì âm thầm cho qua.
    private string _name = "AdaFace R50 (chua nap)";
    private double _matchThreshold;
    private double _livenessThreshold;
    private AntiSpoofStatus _antiSpoof = new(AntiSpoofLevel.None, "Chưa nạp model nhận diện.");
    private bool _metadataKnown;

    public LazyFaceEngine(Func<IFaceEngine> factory, ILogger<LazyFaceEngine> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>Model đang nằm trong RAM hay không (để hiện ở trang trạng thái/giám sát).</summary>
    public bool IsLoaded { get { lock (_gate) return _engine is not null; } }

    public string Name { get { lock (_gate) return _name; } }

    public double MatchThreshold
    {
        get
        {
            lock (_gate)
            {
                if (_metadataKnown) return _matchThreshold;
            }
            // Ngưỡng khớp phải là số thật của engine, không được đoán: đoán thấp là nhận nhầm người.
            using var lease = Acquire();
            lock (_gate) return _matchThreshold;
        }
    }

    public double LivenessThreshold
    {
        get
        {
            lock (_gate)
            {
                if (_metadataKnown) return _livenessThreshold;
            }
            using var lease = Acquire();
            lock (_gate) return _livenessThreshold;
        }
    }

    /// <summary>
    /// Mức chống giả mạo THẬT của engine.
    ///
    /// Sau khi đã biết (Warmup lúc khởi động, hoặc lần nạp đầu tiên) thì phục vụ từ bộ nhớ đệm và
    /// KHÔNG nạp lại: app hỏi trạng thái rất thường xuyên, nạp theo là ghim model vĩnh viễn.
    ///
    /// Nhưng lần đầu tiên thì BẮT BUỘC phải nạp để biết. Nếu trả bừa mức None trong lúc chưa biết,
    /// mọi lượt chấm công rơi vào khoảng vài giây đầu sau khi khởi động sẽ bị chặn oan như thể phát
    /// hiện giả mạo. Nạp thất bại thì ở lại None — đóng đúng chỗ cần đóng.
    /// </summary>
    public AntiSpoofStatus AntiSpoof
    {
        get
        {
            lock (_gate)
            {
                if (_metadataKnown) return _antiSpoof;
            }
            try
            {
                using var lease = Acquire();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Khong nap duoc face engine de doc muc chong gia mao; giu fail-closed.");
            }
            lock (_gate) return _antiSpoof;
        }
    }

    /// <summary>
    /// Nạp thử một lần lúc khởi động để biết mức chống giả mạo thật, rồi thả cho bộ quét thu hồi.
    /// Nuốt lỗi có chủ đích: thiếu file model thì API vẫn phải chạy (giữ đúng hành vi cũ), chỉ riêng
    /// chấm công bị chặn vì AntiSpoof ở lại mức None.
    /// </summary>
    public void Warmup()
    {
        try
        {
            using var lease = Acquire();
            _logger.LogInformation("Face engine da nap de lay metadata: {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Khong nap duoc face engine luc khoi dong; cham cong se bi tu choi (fail-closed).");
        }
    }

    /// <summary>
    /// Thu hồi model nếu đã quá <paramref name="idleFor"/> không ai dùng và KHÔNG còn lượt nào chạy.
    /// Trả về true nếu vừa giải phóng.
    /// </summary>
    public bool UnloadIfIdle(TimeSpan idleFor)
    {
        IFaceEngine? victim;
        lock (_gate)
        {
            if (_engine is null || _activeLeases > 0) return false;
            if (DateTime.UtcNow - _lastUsedUtc < idleFor) return false;
            victim = _engine;
            _engine = null;
        }

        // Dispose NGOÀI lock: giải phóng arena native mất vài chục ms, không nên chặn request tới.
        (victim as IDisposable)?.Dispose();
        _logger.LogInformation("Da giai phong face engine sau {Minutes:N1} phut khong dung.", idleFor.TotalMinutes);
        return true;
    }

    private Lease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_engine is null)
            {
                // Nạp TRONG lock có chủ đích: nhiều request đến cùng lúc phải cùng chờ một lần nạp,
                // thay vì mỗi request tự dựng một engine 350 MB.
                var created = _factory();
                _engine = created;
                if (!_metadataKnown)
                {
                    _name = created.Name;
                    _matchThreshold = created.MatchThreshold;
                    _livenessThreshold = created.LivenessThreshold;
                    _antiSpoof = created.AntiSpoof;
                    _metadataKnown = true;
                }
            }
            _activeLeases++;
            return new Lease(this, _engine);
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            _activeLeases--;
            _lastUsedUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Giữ engine sống trong suốt một thao tác. Bộ quét nhàn rỗi không thể thu hồi khi còn lease.</summary>
    private readonly struct Lease : IDisposable
    {
        private readonly LazyFaceEngine _owner;
        public readonly IFaceEngine Engine;

        public Lease(LazyFaceEngine owner, IFaceEngine engine)
        {
            _owner = owner;
            Engine = engine;
        }

        public void Dispose() => _owner.Release();
    }

    public bool CheckLiveness(byte[] imageBytes)
    {
        using var lease = Acquire();
        return lease.Engine.CheckLiveness(imageBytes);
    }

    public double LivenessProbability(byte[] imageBytes)
    {
        using var lease = Acquire();
        return lease.Engine.LivenessProbability(imageBytes);
    }

    public float[]? ExtractEmbedding(byte[] imageBytes)
    {
        using var lease = Acquire();
        return lease.Engine.ExtractEmbedding(imageBytes);
    }

    /// <summary>
    /// Giữ MỘT lease cho cả loạt khung thay vì lấy/trả từng khung. Loạt chấm công có nhiều khung;
    /// mỗi lần lấy lease là một lần vào lock, và tệ hơn là tạo khe hở để bộ quét chen vào giữa loạt.
    /// </summary>
    public float[]? ExtractFusedEmbedding(IReadOnlyList<byte[]> frames)
    {
        using var lease = Acquire();
        return lease.Engine.ExtractFusedEmbedding(frames);
    }

    public FaceFrameQuality? AssessFrame(byte[] imageBytes)
    {
        using var lease = Acquire();
        return lease.Engine.AssessFrame(imageBytes);
    }

    /// <summary>
    /// Cosine thuần, KHÔNG chạm tới model. Quan trọng: hàm này được gọi trong vòng lặp trên toàn bộ
    /// nhân viên đã đăng ký; nếu nó kéo theo nạp model thì việc so khớp sẽ ghim model trong RAM và
    /// vô hiệu hoá toàn bộ mục đích của lớp này. Công thức sao chép nguyên từ AdaFaceR50Engine.Compare.
    /// </summary>
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

    public void Dispose()
    {
        IFaceEngine? victim;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            victim = _engine;
            _engine = null;
        }
        (victim as IDisposable)?.Dispose();
    }
}

/// <summary>Cấu hình giải phóng model khi nhàn rỗi.</summary>
public sealed class FaceEngineIdleOptions
{
    public const string Section = "FaceRecognition";

    /// <summary>
    /// Số phút không dùng thì thả model. Đặt 0 (hoặc số âm) để TẮT hẳn việc giải phóng — model giữ
    /// thường trú như trước. Đây là van an toàn để quay lại hành vi cũ mà không phải build lại.
    /// </summary>
    public double IdleUnloadMinutes { get; set; } = 10;
}

/// <summary>
/// Quét định kỳ và thả model khi nhàn rỗi.
///
/// Cũng chịu trách nhiệm nạp thử lúc khởi động: mức chống giả mạo phải được biết TRƯỚC khi có lượt
/// chấm công đầu tiên, nếu không trang trạng thái sẽ báo "None" và mọi lượt quét bị chặn oan.
/// </summary>
public sealed class FaceEngineIdleUnloader(
    LazyFaceEngine engine,
    IOptions<FaceEngineIdleOptions> options,
    ILogger<FaceEngineIdleUnloader> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleMinutes = options.Value.IdleUnloadMinutes;
        if (idleMinutes <= 0)
        {
            logger.LogInformation("Giai phong face engine khi nhan roi: DA TAT (IdleUnloadMinutes={Value}).", idleMinutes);
            engine.Warmup();
            return;
        }

        var idleFor = TimeSpan.FromMinutes(idleMinutes);
        // Quét dày hơn khoảng nhàn rỗi để độ trễ thu hồi không vượt quá ~1/4 ngưỡng.
        var sweepEvery = TimeSpan.FromSeconds(Math.Clamp(idleFor.TotalSeconds / 4, 30, 300));

        // Nạp thử ngoài luồng khởi động: 0,8 giây không nên làm chậm việc mở cổng HTTP.
        await Task.Run(engine.Warmup, stoppingToken).ConfigureAwait(false);
        logger.LogInformation(
            "Giai phong face engine sau {Idle:N0} phut khong dung (quet moi {Sweep:N0} giay).",
            idleFor.TotalMinutes, sweepEvery.TotalSeconds);

        using var timer = new PeriodicTimer(sweepEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    engine.UnloadIfIdle(idleFor);
                }
                catch (Exception ex)
                {
                    // Thu hồi thất bại không được làm chết vòng quét; lần sau thử lại.
                    logger.LogWarning(ex, "Khong giai phong duoc face engine lan nay.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dừng bình thường khi tắt máy chủ.
        }
    }
}
