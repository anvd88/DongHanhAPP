using System.Windows.Interop;

namespace KetoanMini;

public sealed partial class MainForm
{
    // ═════════════════════════════════════════════════════════════════════════
    // KIỂM TRA / CẬP NHẬT PHIÊN BẢN KHI ĐANG DÙNG APP
    //   • Lúc mở app: kiểm tra ngầm, nếu có bản mới thì hiện badge ở chuông.
    //   • Menu người dùng: "Kiểm tra cập nhật" → mở cửa sổ thông báo cập nhật.
    //   • Chuông thông báo: hiện "Có bản cập nhật mới x.y.z" cho mọi người dùng.
    //   • Bấm cập nhật → màn "Đang cập nhật" tải + cài im lặng (Inno Setup),
    //     xong thì thoát app để trình cài thay thế file.
    // ═════════════════════════════════════════════════════════════════════════

    // Bản phát hành mới đang chờ cập nhật (null = đang dùng bản mới nhất).
    private AppRelease? _pendingUpdate;

    /// <summary>Kiểm tra phiên bản ở nền lúc mở app — không chặn giao diện.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginCheckForUpdates();
    }

    private void BeginCheckForUpdates()
    {
        Task.Run(() =>
        {
            AppRelease? latest = null;
            try
            {
                var result = _store.CheckVersion();
                latest = result.UpdateAvailable ? result.Latest : null;
            }
            catch
            {
                // Không để lỗi kiểm tra phiên bản ảnh hưởng tới phiên làm việc.
            }

            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _pendingUpdate = latest;
                        RefreshNotifCount();
                    }));
                }
            }
            catch
            {
                // Form có thể đã đóng trong lúc kiểm tra — bỏ qua.
            }
        });
    }

    /// <summary>Người dùng bấm "Kiểm tra cập nhật" trong menu.</summary>
    private void CheckForUpdatesInteractive()
    {
        AppRelease? latest;
        try
        {
            var result = _store.CheckVersion();
            latest = result.UpdateAvailable ? result.Latest : null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Không kiểm tra được phiên bản.\n\n" + ex.Message,
                "Kiểm tra cập nhật",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _pendingUpdate = latest;
        RefreshNotifCount();

        if (latest is null)
        {
            MessageBox.Show(
                $"Bạn đang dùng phiên bản mới nhất ({AppVersion.CurrentText}).",
                "Kiểm tra cập nhật",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        OpenUpdateDialog(latest);
    }

    /// <summary>Mở cửa sổ thông báo cập nhật (WPF) làm modal trên MainForm.</summary>
    private void OpenUpdateDialog(AppRelease release)
    {
        var win = new UpdateWindow(_store, release, blocking: false);
        _ = new WindowInteropHelper(win) { Owner = Handle };

        bool? result;
        try
        {
            result = win.ShowDialog();
        }
        catch
        {
            result = false;
        }

        if (result == true)
        {
            // Đã khởi chạy trình cài im lặng → thoát app để Inno Setup thay file.
            ExitForUpdate();
        }
    }

    /// <summary>Đóng app (không hỏi xác nhận, không quay lại đăng nhập) để trình cài chạy.</summary>
    private void ExitForUpdate()
    {
        _closeConfirmed = true; // bỏ qua hộp thoại xác nhận thoát trong OnFormClosing
        Close();
    }
}
