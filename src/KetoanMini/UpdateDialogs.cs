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

    /// <summary>
    /// Cờ cài đặt im lặng cho trình cài Inno Setup 6 — cài ngầm, không hiện UI.
    ///   /VERYSILENT        : không hiện cửa sổ wizard lẫn thanh tiến trình.
    ///   /SUPPRESSMSGBOXES  : không hỏi hộp thoại xác nhận.
    ///   /NORESTART         : không tự khởi động lại máy.
    /// Dùng runner bên ngoài để đợi app thoát rồi mới chạy setup, tránh khóa file exe/dll đang cập nhật.
    /// </summary>
    private static readonly string[] InnoSilentArgs =
    [
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/CLOSEAPPLICATIONS",
        "/FORCECLOSEAPPLICATIONS",
        "/RESTARTAPPLICATIONS"
    ];

    /// <summary>
    /// Mở trình cài đặt đã tải về. Khi <paramref name="silent"/> = true, chạy ở
    /// chế độ im lặng (Inno Setup /VERYSILENT) — máy tự cài, không hiện giao diện.
    /// </summary>
    public static void RunInstaller(string setupPath, bool silent = false)
    {
        var psi = new ProcessStartInfo(setupPath) { UseShellExecute = true };
        if (silent)
        {
            psi.Arguments = string.Join(" ", InnoSilentArgs);
            psi.WindowStyle = ProcessWindowStyle.Hidden;
        }

        Process.Start(psi);
    }

    public static void RunInstallerAfterCurrentProcessExit(string setupPath, bool silent = false)
    {
        if (!File.Exists(setupPath))
        {
            throw new FileNotFoundException("Khong tim thay file setup da tai.", setupPath);
        }

        var args = silent ? InnoSilentArgs : [];
        var script = BuildDeferredInstallerScript(Environment.ProcessId, setupPath, args);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);

        if (Process.Start(psi) is null)
        {
            throw new InvalidOperationException("Khong khoi dong duoc tien trinh cai dat cap nhat.");
        }
    }

    private static string BuildDeferredInstallerScript(int parentPid, string setupPath, IReadOnlyList<string> args)
    {
        var psArgs = string.Join(", ", args.Select(PowerShellQuote));
        return
            "$ErrorActionPreference = 'Stop'\r\n" +
            "try {\r\n" +
            $"    Wait-Process -Id {parentPid} -Timeout 120 -ErrorAction SilentlyContinue\r\n" +
            "} catch { }\r\n" +
            $"$installerArgs = @({psArgs})\r\n" +
            $"$p = Start-Process -FilePath {PowerShellQuote(setupPath)} -ArgumentList $installerArgs -Wait -PassThru\r\n" +
            "if ($p -ne $null -and $p.ExitCode -ne $null) { exit $p.ExitCode }\r\n";
    }

    private static string PowerShellQuote(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

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
