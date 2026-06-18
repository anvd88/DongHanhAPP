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

        try
        {
            // Sau khi cập nhật xong, màn "Cập nhật thành công" sẽ tự gọi LogoutToLogin()
            // để quay lại trang đăng nhập (qua BeginInvoke, chạy sau khi dialog đóng).
            win.ShowDialog();
        }
        catch
        {
            // Bỏ qua lỗi hiển thị dialog.
        }
    }

    /// <summary>Đăng xuất và quay lại trang đăng nhập (vòng lặp trong Program.Main mở lại LoginWindow).</summary>
    public void LogoutToLogin()
    {
        LogoutRequested = true;
        _closeConfirmed = true; // bỏ qua hộp thoại xác nhận thoát trong OnFormClosing
        Close();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ĐỔI THEME SÁNG / TỐI (live) — dựng lại giao diện với bảng màu mới mà vẫn
    // giữ nguyên phiên đăng nhập, timer ca làm việc và trang đang xem.
    // ═════════════════════════════════════════════════════════════════════════
    private void ToggleThemeLive()
    {
        ThemeState.Toggle();
        ThemeState.Save();
        RebuildShell();
    }

    /// <summary>Dựng lại toàn bộ control tree từ bảng màu hiện tại (giữ session/timer).</summary>
    private void RebuildShell()
    {
        var key = _activeKey;

        // Khóa toàn bộ việc vẽ ở mức cửa sổ (WM_SETREDRAW) trong suốt quá trình
        // hủy/dựng lại cây control để không thấy các bước trung gian → hết nháy.
        // SuspendLayout chỉ hoãn tính layout, không chặn paint nên không đủ.
        var locked = IsHandleCreated;
        if (locked)
            SendMessage(Handle, WM_SETREDRAW, false, 0);

        SuspendLayout();
        try
        {
            // Hủy và xóa cây control cũ; các tham chiếu sẽ được dựng lại trong InitializeComponent.
            var old = Controls.Cast<Control>().ToArray();
            Controls.Clear();
            foreach (var c in old)
            {
                c.Dispose();
            }

            _pages.Clear();
            _navButtons.Clear();
            _userPanel = null;
            _workCard = null;

            BackColor = AppTheme.Background;
            InitializeComponent(); // dựng lại sidebar + header + content với màu mới

            Navigate(key);          // quay về đúng trang đang xem
            RefreshNotifCount();    // vẽ lại badge chuông
            RefreshOvertimeFlag();
            UpdateWorkShift();      // cập nhật lại thẻ ca làm việc
        }
        finally
        {
            ResumeLayout(false);

            if (locked)
            {
                // Bật lại vẽ và ép tô lại MỘT lần toàn bộ form + control con.
                SendMessage(Handle, WM_SETREDRAW, true, 0);
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
        }
    }

    // ── Win32: tô lại một lần sau khi dựng xong (chống nháy). WM_SETREDRAW và
    //    SendMessage đã khai báo ở MainForm.GiaCong.cs nên dùng lại ở đây. ───────
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_FRAME = 0x0400;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
}
