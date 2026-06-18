using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfShapes = System.Windows.Shapes;
using WpfThreading = System.Windows.Threading;

namespace KetoanMini;

// ============================================================================
// UpdateProgressWindow — màn hình "Đang cập nhật" (WPF/XAML).
//
//   • Cung tròn xanh xoay liên tục quanh icon mũi tên (trạng thái đang cập nhật).
//   • Thanh tiến trình gradient 0→100, phần trăm số lớn, dòng trạng thái.
//   • Khung 3 bước: Kiểm tra phiên bản / Tải bản cập nhật / Cài đặt.
//
// Hai chế độ:
//   1) Constructor rỗng  -> chế độ DEMO (1.2.0 → 1.2.1, 68%) để xem giao diện.
//   2) Constructor (store, release, blocking) -> chế độ THẬT: tự tải gói cập
//      nhật rồi chạy trình cài đặt Inno Setup ở chế độ IM LẶNG (/VERYSILENT),
//      máy tự cài, người dùng chỉ chờ. Khi xong DialogResult = true để caller
//      thoát app cho trình cài thay thế file.
// ============================================================================
public partial class UpdateProgressWindow : Wpf.Window
{
    private enum StepState { Pending, Active, Done }

    private readonly AccountingStore? _store;
    private readonly AppRelease? _release;
    private readonly bool _blocking;
    private readonly bool _demoMode;

    private readonly CancellationTokenSource _cts = new();
    private bool _installing;          // đã khởi chạy trình cài → không cho hủy nữa
    private WpfThreading.DispatcherTimer? _demoTimer;

    // ── Chế độ DEMO: xem giao diện, không tải/cài thật ──────────────────────
    public UpdateProgressWindow()
    {
        _demoMode = true;
        InitializeComponent();
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        CurrentVersion = "1.2.0";
        NewVersion = "1.2.1";
        ProgressPercent = 68;
        StatusText = "Đang tải gói cập nhật...";

        SetStep(StepState.Done, StepState.Active, StepState.Pending);
        Loaded += (_, _) => StartDemoProgress();
    }

    // ── Chế độ THẬT: tải + cài im lặng ──────────────────────────────────────
    public UpdateProgressWindow(AccountingStore store, AppRelease release, bool blocking)
    {
        _store = store;
        _release = release;
        _blocking = blocking;

        InitializeComponent();
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        TxtHeader.Text = blocking ? "Bắt buộc cập nhật" : "Cập nhật phiên bản";
        CurrentVersion = AppVersion.CurrentText;
        NewVersion = release.Version;
        ProgressPercent = 0;
        StatusText = "Đang chuẩn bị...";

        // Bước 1 (kiểm tra phiên bản) coi như đã xong; bắt đầu ở bước 2.
        SetStep(StepState.Done, StepState.Active, StepState.Pending);

        // Không cho bấm Thoát trong lúc cập nhật (khớp giao diện); chỉ còn nút X
        // ở góc để hủy khi vẫn đang tải.
        BtnExit.IsEnabled = false;

        Loaded += async (_, _) => await RunUpdateAsync();
    }

    // ── Thuộc tính điều khiển giao diện ─────────────────────────────────────
    public string CurrentVersion
    {
        get => RunCurrent.Text;
        set => RunCurrent.Text = value;
    }

    public string NewVersion
    {
        get => RunNew.Text;
        set => RunNew.Text = value;
    }

    /// <summary>Phần trăm tiến trình 0..100 (cập nhật cả thanh và chữ phần trăm).</summary>
    public int ProgressPercent
    {
        get => (int)ProgressBarMain.Value;
        set
        {
            var v = Math.Clamp(value, 0, 100);
            ProgressBarMain.Value = v;
            TxtPercent.Text = $"{v}%";
        }
    }

    public string StatusText
    {
        get => TxtStatus.Text;
        set => TxtStatus.Text = value;
    }

    // ── Demo: chạy thanh tiến trình lặp lại cho sinh động ───────────────────
    private void StartDemoProgress()
    {
        _demoTimer = new WpfThreading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _demoTimer.Tick += (_, _) =>
        {
            var next = ProgressPercent + 1;
            if (next > 100)
            {
                next = 0;
            }

            ProgressPercent = next;
            StatusText = next < 90 ? "Đang tải gói cập nhật..." : "Đang cài đặt bản cập nhật...";
            SetStep(StepState.Done, next < 90 ? StepState.Active : StepState.Done, next < 90 ? StepState.Pending : StepState.Active);
        };
        _demoTimer.Start();
    }

    // ── Chạy cập nhật thật: tải (0→90%) rồi cài im lặng (90→100%) ────────────
    private async Task RunUpdateAsync()
    {
        if (_store is null || _release is null)
        {
            return;
        }

        SetStep(StepState.Done, StepState.Active, StepState.Pending);
        StatusText = "Đang tải gói cập nhật...";

        // Tiến trình tải chiếm 0..90%, chừa 90..100% cho bước cài đặt.
        var progress = new Progress<double>(p =>
            ProgressPercent = (int)Math.Round(Math.Clamp(p, 0, 1) * 90));

        try
        {
            var path = await UpdateInstaller.DownloadAsync(_store, _release, progress, _cts.Token);

            // Bước 3: cài đặt im lặng (Inno Setup /VERYSILENT) — không hiện UI.
            _installing = true;
            BtnClose.IsEnabled = false;
            SetStep(StepState.Done, StepState.Done, StepState.Active);
            var isZipPackage = UpdateInstaller.IsZipUpdatePackage(path);
            StatusText = isZipPackage ? "Đang chuẩn bị cập nhật nhanh..." : "Đang cài đặt bản cập nhật...";
            await RampProgressAsync(ProgressPercent, 100, _cts.Token);

            if (isZipPackage)
            {
                UpdateInstaller.RunZipUpdaterAfterCurrentProcessExit(path);
            }
            else
            {
                UpdateInstaller.RunInstallerAfterCurrentProcessExit(path, silent: true);
            }

            SetStep(StepState.Done, StepState.Done, StepState.Done);
            ProgressPercent = 100;
            StatusText = "Đã sẵn sàng cài đặt. Ứng dụng sẽ đóng để cập nhật...";
            await Task.Delay(1200, _cts.Token);

            DialogResult = true; // báo UpdateWindow: đã lên lịch chạy setup -> thoát app
            Close();
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            StatusText = "Cập nhật thất bại.";
            Wpf.MessageBox.Show(
                $"Không tải/cài được bản cập nhật.\n\n{ex.Message}",
                "Lỗi cập nhật",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    /// <summary>Tăng phần trăm mượt từ <paramref name="from"/> đến <paramref name="to"/> để bước cài có hiệu ứng.</summary>
    private async Task RampProgressAsync(int from, int to, CancellationToken ct)
    {
        for (var v = from; v <= to; v++)
        {
            ct.ThrowIfCancellationRequested();
            ProgressPercent = v;
            await Task.Delay(18, ct);
        }
    }

    // ── Cập nhật trạng thái 3 bước ──────────────────────────────────────────
    private void SetStep(StepState s1, StepState s2, StepState s3)
    {
        ApplyStep(Step1Circle, Step1Num, Step1Check, Step1Label, s1);
        ApplyStep(Step2Circle, Step2Num, Step2Check, Step2Label, s2);
        ApplyStep(Step3Circle, Step3Num, Step3Check, Step3Label, s3);
    }

    private void ApplyStep(WpfShapes.Ellipse circle, WpfControls.TextBlock num,
                           WpfControls.Viewbox check, WpfControls.TextBlock label, StepState state)
    {
        var blue = (WpfMedia.Brush)FindResource("PrimaryBrush");
        switch (state)
        {
            case StepState.Done:
                circle.Fill = blue;
                num.Visibility = Wpf.Visibility.Collapsed;
                check.Visibility = Wpf.Visibility.Visible;
                label.Foreground = WpfTheme.Brush("#071338");
                break;
            case StepState.Active:
                circle.Fill = blue;
                num.Visibility = Wpf.Visibility.Visible;
                num.Foreground = WpfMedia.Brushes.White;
                check.Visibility = Wpf.Visibility.Collapsed;
                label.Foreground = WpfTheme.Brush("#1757EA");
                break;
            default: // Pending
                circle.Fill = WpfTheme.Brush("#E4E9F2");
                num.Visibility = Wpf.Visibility.Visible;
                num.Foreground = WpfTheme.Brush("#9AA3B2");
                check.Visibility = Wpf.Visibility.Collapsed;
                label.Foreground = WpfTheme.Brush("#9AA3B2");
                break;
        }
    }

    // ── Sự kiện ──────────────────────────────────────────────────────────────
    private void Window_PreviewKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            TryCancel();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, WpfInput.MouseButtonEventArgs e)
    {
        if (e.ButtonState == WpfInput.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, Wpf.RoutedEventArgs e) => TryCancel();

    private void BtnExit_Click(object sender, Wpf.RoutedEventArgs e) => TryCancel();

    private void TryCancel()
    {
        if (_demoMode)
        {
            _demoTimer?.Stop();
            DialogResult = false;
            Close();
            return;
        }

        // Đang cài đặt thì không cho hủy (tránh hỏng bản cài).
        if (_installing)
        {
            return;
        }

        _cts.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _demoTimer?.Stop();
        _cts.Dispose();
        base.OnClosed(e);
    }
}
