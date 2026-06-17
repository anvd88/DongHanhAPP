using System.Diagnostics;

namespace KetoanMini;

// ═════════════════════════════════════════════════════════════════════════════
// UpdateInstaller — tải file setup từ LAN (UNC) hoặc từ DB rồi chạy
// ═════════════════════════════════════════════════════════════════════════════
internal static class UpdateInstaller
{
    /// <summary>
    /// Tải file setup (ưu tiên UNC, fallback file nhúng DB) về thư mục tạm, báo tiến độ
    /// qua <paramref name="progress"/> (0..1). Trả về đường dẫn file đã lưu.
    /// </summary>
    public static async Task<string> DownloadAsync(AccountingStore store, AppRelease release, IProgress<double>? progress, CancellationToken ct = default)
    {
        var fileName = ResolveFileName(release);
        var targetDir = UpdatesFolder();
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
                await CopyWithProgressAsync(source, targetPath, progress, ct);
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
            progress?.Report(0);
            var bytes = await Task.Run(() => store.GetReleaseSetupFile(release.Id), ct)
                ?? throw new InvalidOperationException("Không đọc được file setup từ cơ sở dữ liệu.");
            await WriteWithProgressAsync(targetPath, bytes, progress, ct);
            return targetPath;
        }

        throw new InvalidOperationException("Bản phát hành này chưa có file setup để tải.");
    }

    /// <summary>Mở trình cài đặt đã tải về.</summary>
    public static void RunInstaller(string setupPath)
        => Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });

    private static async Task CopyWithProgressAsync(string source, string dest, IProgress<double>? progress, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

        var total = src.Length;
        long copied = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
            if (total > 0)
            {
                progress?.Report((double)copied / total);
            }
        }

        progress?.Report(1);
    }

    private static async Task WriteWithProgressAsync(string dest, byte[] bytes, IProgress<double>? progress, CancellationToken ct)
    {
        const int chunk = 1024 * 1024;
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, chunk, useAsync: true);
        for (var offset = 0; offset < bytes.Length; offset += chunk)
        {
            var count = Math.Min(chunk, bytes.Length - offset);
            await dst.WriteAsync(bytes.AsMemory(offset, count), ct);
            if (bytes.Length > 0)
            {
                progress?.Report((double)(offset + count) / bytes.Length);
            }
        }

        progress?.Report(1);
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

    /// <summary>Thư mục tạm riêng để tải file cài (sẽ được dọn sau khi cập nhật xong).</summary>
    private static string UpdatesFolder()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.GetTempPath();
        }

        return Path.Combine(baseDir, "KetoanMini", "Updates");
    }

    /// <summary>
    /// Dọn file cài đã tải sau khi cập nhật. Gọi lúc khởi động app: sau khi bản mới
    /// được cài và chạy lên, file setup cũ trong thư mục tạm sẽ bị xóa.
    /// </summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var dir = UpdatesFolder();
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // File có thể đang được dùng (trình cài chưa đóng) → bỏ qua, lần khởi động sau dọn tiếp.
                }
            }
        }
        catch
        {
            // Không để lỗi dọn dẹp chặn khởi động app.
        }
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

    private RoundedButton _btnUpdate = null!;
    private RoundedButton _btnLater = null!;
    private Panel _progressPanel = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;

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

        _btnUpdate = new RoundedButton { Text = "⬇  Cập nhật ngay", Width = 150, Height = 36, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White, Font = AppTheme.F9B };
        _btnLater = new RoundedButton
        {
            Text = _blocking ? "Thoát" : "Để sau",
            Width = 100,
            Height = 36,
            CornerRadius = 8,
            BackColor = AppTheme.SurfaceAlt,
            ForeColor = AppTheme.TextPrimary,
            BorderColor = AppTheme.Border
        };

        _btnUpdate.Click += (s, e) => DoUpdate();
        _btnLater.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        btnFlow.Controls.Add(_btnUpdate);
        btnFlow.Controls.Add(_btnLater);

        // Khu vực tiến trình tải (ẩn cho tới khi bấm Cập nhật ngay).
        _progressPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(24, 4, 24, 4),
            BackColor = Color.Transparent,
            Visible = false
        };
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Text = "",
            Font = AppTheme.F9,
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        _progressPanel.Controls.Add(_progressBar);
        _progressPanel.Controls.Add(_statusLabel);

        Controls.Add(main);
        Controls.Add(_progressPanel);
        Controls.Add(btnFlow);

        if (!_release.HasSetupSource)
        {
            _btnUpdate.Enabled = false;
            _btnUpdate.Text = "Chưa có file setup";
        }
    }

    private async void DoUpdate()
    {
        _btnUpdate.Enabled = false;
        _btnLater.Enabled = false;
        ControlBox = false; // không cho đóng giữa chừng khi đang tải
        _progressPanel.Visible = true;
        _progressBar.Value = 0;
        _statusLabel.Text = "Đang tải bản cập nhật... 0%";

        var progress = new Progress<double>(p =>
        {
            var pct = (int)Math.Round(Math.Clamp(p, 0, 1) * 100);
            _progressBar.Value = Math.Clamp(pct, 0, 100);
            _statusLabel.Text = $"Đang tải bản cập nhật... {pct}%";
        });

        try
        {
            var path = await UpdateInstaller.DownloadAsync(_store, _release, progress);
            _progressBar.Value = 100;
            _statusLabel.Text = "Đang mở trình cài đặt...";
            UpdateInstaller.RunInstaller(path);
            DialogResult = DialogResult.OK; // caller sẽ thoát app để cài đặt
            Close();
        }
        catch (Exception ex)
        {
            _progressPanel.Visible = false;
            _btnUpdate.Enabled = true;
            _btnLater.Enabled = true;
            ControlBox = !_blocking;
            MessageBox.Show(
                $"Không tải/chạy được file cập nhật.\n\n{ex.Message}",
                "Lỗi cập nhật",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
