using System.Diagnostics;

namespace KetoanMini;

// ═════════════════════════════════════════════════════════════════════════════
// UpdateInstaller — tải file setup từ LAN (UNC) hoặc từ DB rồi chạy
// ═════════════════════════════════════════════════════════════════════════════
internal static class UpdateInstaller
{
    /// <summary>
    /// Tải file setup của bản phát hành (ưu tiên UNC, fallback file nhúng DB) về máy
    /// và khởi chạy. Trả về true nếu đã chạy setup (lúc đó nên thoát app để cập nhật).
    /// </summary>
    public static bool TryDownloadAndRun(AccountingStore store, AppRelease release, out string error)
    {
        error = "";
        try
        {
            var savedPath = Download(store, release);
            Process.Start(new ProcessStartInfo(savedPath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Chỉ tải file setup về máy (không chạy). Trả về đường dẫn file đã lưu.</summary>
    public static string Download(AccountingStore store, AppRelease release)
    {
        var fileName = ResolveFileName(release);
        var targetDir = DownloadsFolder();
        Directory.CreateDirectory(targetDir);
        var targetPath = UniquePath(Path.Combine(targetDir, fileName));

        // 1) Ưu tiên đường dẫn LAN (UNC / thư mục chia sẻ).
        if (!string.IsNullOrWhiteSpace(release.SetupPath))
        {
            var source = release.SetupPath.Trim();
            if (Directory.Exists(source))
            {
                source = Path.Combine(source, fileName);
            }

            if (File.Exists(source))
            {
                File.Copy(source, targetPath, overwrite: true);
                return targetPath;
            }

            // Nếu có UNC nhưng không truy cập được mà cũng không có file nhúng → báo lỗi rõ ràng.
            if (!release.HasEmbeddedFile)
            {
                throw new FileNotFoundException(
                    $"Không tìm thấy file setup tại đường dẫn LAN:\n{release.SetupPath}\n\n" +
                    "Kiểm tra lại quyền truy cập thư mục chia sẻ trên mạng.");
            }
        }

        // 2) Fallback: file nhúng trong DB.
        if (release.HasEmbeddedFile)
        {
            var bytes = store.GetReleaseSetupFile(release.Id)
                ?? throw new InvalidOperationException("Không đọc được file setup từ cơ sở dữ liệu.");
            File.WriteAllBytes(targetPath, bytes);
            return targetPath;
        }

        throw new InvalidOperationException("Bản phát hành này chưa có file setup để tải.");
    }

    private static string ResolveFileName(AppRelease release)
    {
        var name = release.SetupFileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"KetoanMiniSetup_{release.Version}.exe";
        }

        // Loại bỏ ký tự không hợp lệ trong tên file.
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private static string DownloadsFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            var downloads = Path.Combine(profile, "Downloads");
            if (Directory.Exists(downloads))
            {
                return downloads;
            }
        }

        return Path.Combine(Path.GetTempPath(), "KetoanMiniUpdate");
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// UpdateDialog — popup thông báo cập nhật khi mở app
//   blocking = false: "Cập nhật ngay" + "Để sau"
//   blocking = true : "Cập nhật ngay" + "Thoát" (chặn đăng nhập, bắt buộc cập nhật)
// DialogResult.OK     => đã chạy setup, app nên thoát
// DialogResult.Cancel => người dùng hoãn (Để sau) hoặc thoát (Thoát)
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class UpdateDialog : Form
{
    private readonly AccountingStore _store;
    private readonly AppRelease _release;
    private readonly bool _blocking;

    public UpdateDialog(AccountingStore store, AppRelease release, bool blocking)
    {
        _store = store;
        _release = release;
        _blocking = blocking;

        Text = blocking ? "Bắt buộc cập nhật" : "Có bản cập nhật mới";
        Size = new Size(480, 380);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        // Chặn đóng bằng nút X khi bắt buộc cập nhật để buộc người dùng lựa chọn rõ ràng.
        ControlBox = !blocking;

        BuildUi();
    }

    private void BuildUi()
    {
        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12), BackColor = Color.Transparent };

        var title = new Label
        {
            Text = _blocking
                ? "⚠  Cần cập nhật để tiếp tục"
                : "🎉  Đã có phiên bản mới",
            Font = AppTheme.F14B,
            ForeColor = _blocking ? AppTheme.Danger : AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 40,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        var versionInfo = new Label
        {
            Text = $"Phiên bản hiện tại: {AppVersion.CurrentText}    →    Phiên bản mới: {_release.Version}",
            Font = AppTheme.F9B,
            ForeColor = AppTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 28,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        var blockNote = new Label
        {
            Text = _blocking
                ? "Quản trị viên yêu cầu cập nhật bản này để có thể đăng nhập. Vui lòng cập nhật ngay."
                : "Bạn nên cập nhật để sử dụng các tính năng mới nhất.",
            Font = AppTheme.F9,
            ForeColor = _blocking ? AppTheme.Danger : AppTheme.TextMuted,
            Dock = DockStyle.Top,
            Height = 40,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            BackColor = Color.Transparent
        };

        var notesTitle = new Label
        {
            Text = "Nội dung cập nhật:",
            Font = AppTheme.F9B,
            ForeColor = AppTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 24,
            BackColor = Color.Transparent
        };

        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_release.ReleaseNotes) ? "(Không có ghi chú)" : _release.ReleaseNotes,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextPrimary,
            Font = AppTheme.F9
        };

        main.Controls.Add(notes);
        main.Controls.Add(notesTitle);
        main.Controls.Add(blockNote);
        main.Controls.Add(versionInfo);
        main.Controls.Add(title);

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(24, 12, 24, 12),
            BackColor = Color.Transparent
        };

        var btnUpdate = new RoundedButton { Text = "⬇  Cập nhật ngay", Width = 150, Height = 36, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White, Font = AppTheme.F9B };
        var btnLater = new RoundedButton
        {
            Text = _blocking ? "Thoát" : "Để sau",
            Width = 100,
            Height = 36,
            CornerRadius = 8,
            BackColor = AppTheme.SurfaceAlt,
            ForeColor = AppTheme.TextPrimary,
            BorderColor = AppTheme.Border
        };

        btnUpdate.Click += (s, e) => DoUpdate();
        btnLater.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        btnFlow.Controls.Add(btnUpdate);
        btnFlow.Controls.Add(btnLater);

        Controls.Add(main);
        Controls.Add(btnFlow);

        if (!_release.HasSetupSource)
        {
            btnUpdate.Enabled = false;
            btnUpdate.Text = "Chưa có file setup";
        }
    }

    private void DoUpdate()
    {
        UseWaitCursor = true;
        Enabled = false;
        try
        {
            if (UpdateInstaller.TryDownloadAndRun(_store, _release, out var error))
            {
                DialogResult = DialogResult.OK; // caller sẽ thoát app
                Close();
                return;
            }

            MessageBox.Show(
                $"Không tải/chạy được file cập nhật.\n\n{error}",
                "Lỗi cập nhật",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            Enabled = true;
        }
    }
}
