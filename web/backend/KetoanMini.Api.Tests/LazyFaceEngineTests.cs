using KetoanMini.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KetoanMini.Api.Tests;

/// <summary>
/// Bất biến của việc thả model khuôn mặt khi nhàn rỗi.
///
/// Hai thứ phải canh chặt nhất:
///  1. KHÔNG bao giờ được Dispose khi còn lượt inference đang chạy. OpenCV/ONNX là native nên
///     use-after-free làm SẬP cả tiến trình, không phải ném exception bắt được.
///  2. Đọc trạng thái chống giả mạo KHÔNG được kéo theo nạp model, nếu không app cứ hỏi trạng thái
///     là model bị ghim mãi trong RAM và toàn bộ mục đích tiết kiệm biến mất.
/// </summary>
public sealed class LazyFaceEngineTests
{
    /// <summary>Engine giả: đếm số lần dựng/huỷ, và báo được khi bị dùng sau khi đã huỷ.</summary>
    private sealed class FakeEngine : IFaceEngine, IDisposable
    {
        public static int Created;
        public static int Disposed;
        public static int UsedAfterDispose;

        private volatile bool _disposed;

        public FakeEngine() => Interlocked.Increment(ref Created);

        public string Name => "fake";
        public double MatchThreshold => 0.42;
        public double LivenessThreshold => 0.55;
        public AntiSpoofStatus AntiSpoof => new(AntiSpoofLevel.Full, "fake full");

        private void Touch()
        {
            if (_disposed) Interlocked.Increment(ref UsedAfterDispose);
        }

        public bool CheckLiveness(byte[] imageBytes) { Touch(); return true; }
        public double LivenessProbability(byte[] imageBytes) { Touch(); return 0.9; }
        public float[]? ExtractEmbedding(byte[] imageBytes)
        {
            Touch();
            // Ngủ một nhịp để bộ quét có cơ hội chen vào giữa lúc đang "inference".
            Thread.Sleep(2);
            Touch();
            return [1f, 0f, 0f];
        }
        public FaceFrameQuality? AssessFrame(byte[] imageBytes) { Touch(); return null; }
        public double Compare(float[] a, float[] b) { Touch(); return 1.0; }

        public void Dispose()
        {
            _disposed = true;
            Interlocked.Increment(ref Disposed);
        }

        public static void Reset() { Created = 0; Disposed = 0; UsedAfterDispose = 0; }
    }

    private static LazyFaceEngine Build() =>
        new(() => new FakeEngine(), NullLogger<LazyFaceEngine>.Instance);

    [Fact]
    public void Compare_NeverLoadsTheModel()
    {
        FakeEngine.Reset();
        using var engine = Build();

        // So khớp là cosine thuần. Nó được gọi trong vòng lặp trên TOÀN BỘ nhân viên đã đăng ký;
        // nếu nó nạp model thì việc so khớp sẽ ghim model và vô hiệu hoá cơ chế thả.
        var score = engine.Compare([1f, 0f], [1f, 0f]);

        Assert.Equal(1.0, score, 6);
        Assert.False(engine.IsLoaded);
        Assert.Equal(0, FakeEngine.Created);
    }

    [Fact]
    public void AntiSpoofStatus_LoadsOnceToLearnTheTruth_ThenNeverAgain()
    {
        FakeEngine.Reset();
        using var engine = Build();

        // Lần đầu BẮT BUỘC nạp: trả bừa None trong lúc chưa biết sẽ chặn oan mọi lượt chấm công
        // rơi vào vài giây đầu sau khi khởi động, như thể phát hiện giả mạo.
        Assert.Equal(AntiSpoofLevel.Full, engine.AntiSpoof.Level);
        Assert.Equal(1, FakeEngine.Created);

        // Sau khi đã biết thì hỏi bao nhiêu lần cũng KHÔNG nạp lại — kể cả khi model đã được thả.
        Assert.True(engine.UnloadIfIdle(TimeSpan.Zero));
        Assert.False(engine.IsLoaded);
        for (var i = 0; i < 100; i++)
        {
            _ = engine.AntiSpoof;
            _ = engine.Name;
            _ = engine.MatchThreshold;
        }
        Assert.Equal(1, FakeEngine.Created);
        Assert.False(engine.IsLoaded);
    }

    [Fact]
    public void MetadataSurvivesUnload_SoThresholdsStayTheEnginesRealValues()
    {
        FakeEngine.Reset();
        using var engine = Build();
        engine.Warmup();

        Assert.Equal(0.42, engine.MatchThreshold, 6);
        Assert.Equal(0.55, engine.LivenessThreshold, 6);

        Assert.True(engine.UnloadIfIdle(TimeSpan.Zero));
        // Đọc ngưỡng sau khi thả phải lấy từ bộ nhớ đệm, không được nạp lại 350 MB chỉ để biết 0.42.
        Assert.Equal(0.42, engine.MatchThreshold, 6);
        Assert.Equal(1, FakeEngine.Created);
    }

    [Fact]
    public void UnloadIsRefusedBeforeTheIdleWindowElapses()
    {
        FakeEngine.Reset();
        using var engine = Build();
        engine.ExtractEmbedding([1, 2, 3]);

        Assert.True(engine.IsLoaded);
        Assert.False(engine.UnloadIfIdle(TimeSpan.FromMinutes(10)));
        Assert.True(engine.IsLoaded);
        Assert.Equal(0, FakeEngine.Disposed);
    }

    [Fact]
    public void ReloadsAfterUnload_AndDisposesExactlyOncePerLoad()
    {
        FakeEngine.Reset();
        using var engine = Build();

        engine.ExtractEmbedding([1]);
        Assert.Equal(1, FakeEngine.Created);
        Assert.True(engine.UnloadIfIdle(TimeSpan.Zero));
        Assert.Equal(1, FakeEngine.Disposed);

        engine.ExtractEmbedding([1]);
        Assert.Equal(2, FakeEngine.Created);
        Assert.True(engine.IsLoaded);

        engine.Dispose();
        Assert.Equal(2, FakeEngine.Disposed);
        Assert.Equal(0, FakeEngine.UsedAfterDispose);
    }

    [Fact]
    public async Task SweeperNeverDisposesWhileInferenceIsRunning()
    {
        FakeEngine.Reset();
        using var engine = Build();

        // Một bên liên tục inference, một bên liên tục cố thu hồi với ngưỡng nhàn rỗi = 0
        // (tức "thu hồi ngay khi được phép"). Nếu thiếu đếm tham chiếu, engine sẽ bị huỷ giữa chừng.
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sweeper = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested) engine.UnloadIfIdle(TimeSpan.Zero);
        });

        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                engine.ExtractEmbedding([1, 2, 3]);
                engine.AssessFrame([1, 2, 3]);
                // Nhường một nhịp để bộ quét có cửa sổ thật mà chen vào giữa hai lượt.
                Thread.Sleep(1);
            }
        })).ToArray();

        await Task.WhenAll([sweeper, .. workers]);

        // Bất biến sống còn: không một lời gọi nào chạm vào engine đã bị huỷ.
        Assert.Equal(0, FakeEngine.UsedAfterDispose);

        // KHÔNG khẳng định "phải thu hồi được ít nhất N lần" trong lúc chạy đua — số đó phụ thuộc
        // lịch luồng nên sẽ flaky trên máy đang tải nặng (đã gặp thật khi chạy cả suite).
        // Thay bằng phép kiểm tất định sau khi mọi luồng đã dừng: nạp rồi thu hồi phải thành công
        // NGAY. Nó chứng minh bộ đếm lease đã về 0, tức không lease nào bị rò trong lúc tranh chấp.
        engine.ExtractEmbedding([1]);
        Assert.True(engine.IsLoaded);
        Assert.True(engine.UnloadIfIdle(TimeSpan.Zero), "con lease bi ro sau khi tranh chap");
        Assert.Equal(0, FakeEngine.UsedAfterDispose);
    }

    [Fact]
    public void FailureToLoadKeepsTheSystemFailClosed()
    {
        var attempts = 0;
        using var engine = new LazyFaceEngine(
            () => { attempts++; throw new FileNotFoundException("thieu adaface.onnx"); },
            NullLogger<LazyFaceEngine>.Instance);

        // Warmup nuốt lỗi để API vẫn khởi động được (giữ đúng hành vi cũ khi thiếu model).
        engine.Warmup();
        Assert.Equal(1, attempts);

        // Nhưng ranh giới bảo mật phải ĐÓNG: không có model thì mọi lượt quét bị chặn.
        Assert.Equal(AntiSpoofLevel.None, engine.AntiSpoof.Level);
        Assert.False(FaceAntiSpoofSecurity.IsOperational(engine));
        Assert.Equal(0.0, FaceAntiSpoofSecurity.ProbabilityReal(engine, [1, 2, 3]));
    }
}
