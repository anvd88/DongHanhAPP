using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace KetoanMiniUpdater;

internal static class Program
{
    private const string AppExeName = "KetoanMini.exe";
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KetoanMini",
        "Updater",
        "updater.log");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            Log("Updater started.");

            var options = Options.Parse(args);
            if (options is null)
            {
                Show("Thieu tham so cap nhat.");
                return 2;
            }

            if (!File.Exists(options.PackagePath))
            {
                Show("Khong tim thay goi cap nhat:\n" + options.PackagePath);
                return 3;
            }

            if (!Directory.Exists(options.TargetDir))
            {
                Show("Khong tim thay thu muc cai dat:\n" + options.TargetDir);
                return 4;
            }

            WaitForProcessExit(options.WaitPid);
            ApplyUpdate(options);
            StartApp(options.AppPath);
            Log("Updater finished.");
            return 0;
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            Show("Cap nhat KetoanMini that bai.\n\n" + ex.Message + "\n\nChi tiet loi da duoc ghi tai:\n" + LogPath);
            return 1;
        }
    }

    private static void ApplyUpdate(Options options)
    {
        var workRoot = Path.Combine(Path.GetTempPath(), "KetoanMiniUpdate_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        var extractDir = Path.Combine(workRoot, "package");
        var backupDir = Path.Combine(workRoot, "backup");

        Directory.CreateDirectory(extractDir);
        Directory.CreateDirectory(backupDir);

        try
        {
            Log("Extracting package: " + options.PackagePath);
            ZipFile.ExtractToDirectory(options.PackagePath, extractDir, overwriteFiles: true);
            var payloadDir = ResolvePayloadDirectory(extractDir);
            ValidatePayloadVersion(payloadDir, options.ExpectedVersion);

            Log("Backing up current installation: " + options.TargetDir);
            CopyDirectory(options.TargetDir, backupDir, overwrite: true, skipPaths: [workRoot]);

            Log("Copying update payload: " + payloadDir);
            CopyDirectory(payloadDir, options.TargetDir, overwrite: true, skipPaths: [backupDir, workRoot]);

            if (!File.Exists(Path.Combine(options.TargetDir, AppExeName)))
            {
                throw new InvalidOperationException("Goi cap nhat khong tao ra " + AppExeName + ".");
            }
        }
        catch
        {
            Log("Applying update failed. Rolling back.");
            Rollback(backupDir, options.TargetDir);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private static string ResolvePayloadDirectory(string extractDir, int depth = 0)
    {
        if (File.Exists(Path.Combine(extractDir, AppExeName)))
        {
            return extractDir;
        }

        var dirs = Directory.GetDirectories(extractDir);
        if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], AppExeName)))
        {
            return dirs[0];
        }

        var nested = Directory.EnumerateFiles(extractDir, AppExeName, SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        if (!string.IsNullOrWhiteSpace(nested))
        {
            return nested;
        }

        var nestedPackage = Directory.EnumerateFiles(extractDir, "*.zip", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(extractDir, "*.kup", SearchOption.AllDirectories))
            .ToList();
        if (depth < 2 && nestedPackage.Count == 1)
        {
            var nestedDir = Path.Combine(Path.GetDirectoryName(nestedPackage[0])!, "_nested_update_payload");
            Directory.CreateDirectory(nestedDir);
            Log("Extracting nested update package: " + nestedPackage[0]);
            ZipFile.ExtractToDirectory(nestedPackage[0], nestedDir, overwriteFiles: true);
            return ResolvePayloadDirectory(nestedDir, depth + 1);
        }

        throw new InvalidOperationException(
            "Goi cap nhat khong co " + AppExeName + ". " +
            "Neu tai tu GitHub Actions, hay giai nen artifact mot lan roi chon file KetoanMiniUpdate-...zip ben trong.");
    }

    private static void ValidatePayloadVersion(string payloadDir, string? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            return;
        }

        var appPath = Path.Combine(payloadDir, AppExeName);
        var fileVersion = FileVersionInfo.GetVersionInfo(appPath).FileVersion
            ?? FileVersionInfo.GetVersionInfo(appPath).ProductVersion
            ?? "";
        var payloadVersion = NormalizeVersion(fileVersion);
        var expected = NormalizeVersion(expectedVersion);

        Log($"Payload version: {payloadVersion}; expected: {expected}");
        if (payloadVersion.Length == 0)
        {
            throw new InvalidOperationException("Khong doc duoc version cua " + AppExeName + " trong goi cap nhat.");
        }

        if (!string.Equals(payloadVersion, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Version trong goi cap nhat khong khop voi version phat hanh.\n" +
                "Goi update dang la: " + payloadVersion + "\n" +
                "Version tren app dang yeu cau: " + expected + "\n\n" +
                "Hay build lai workflow voi dung input version, hoac tang <Version> trong KetoanMini.csproj roi phat hanh lai.");
        }
    }

    private static string NormalizeVersion(string version)
    {
        version = (version ?? "").Trim();
        if (Version.TryParse(version, out var parsed))
        {
            return new Version(
                Math.Max(parsed.Major, 0),
                Math.Max(parsed.Minor, 0),
                Math.Max(parsed.Build, 0)).ToString(3);
        }

        return version;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, bool overwrite, IReadOnlyList<string> skipPaths)
    {
        Directory.CreateDirectory(targetDir);
        var sourceRoot = Path.GetFullPath(sourceDir);
        var targetRoot = Path.GetFullPath(targetDir);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(directory, skipPaths))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(file, skipPaths))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static bool ShouldSkip(string path, IReadOnlyList<string> skipPaths)
    {
        var full = Path.GetFullPath(path);
        return skipPaths.Any(skip =>
        {
            var skipFull = Path.GetFullPath(skip);
            return full.Equals(skipFull, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(skipFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void Rollback(string backupDir, string targetDir)
    {
        if (!Directory.Exists(backupDir))
        {
            return;
        }

        CopyDirectory(backupDir, targetDir, overwrite: true, skipPaths: []);
    }

    private static void WaitForProcessExit(int? pid)
    {
        if (pid is null or <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid.Value);
            Log("Waiting for app PID: " + pid.Value);
            process.WaitForExit(120_000);
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
    }

    private static void StartApp(string appPath)
    {
        if (!File.Exists(appPath))
        {
            appPath = Path.Combine(Path.GetDirectoryName(appPath) ?? "", AppExeName);
        }

        if (!File.Exists(appPath))
        {
            Log("App exe not found after update: " + appPath);
            return;
        }

        Log("Starting app: " + appPath);
        Process.Start(new ProcessStartInfo(appPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(appPath) ?? ""
        });
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log("Cannot delete temp directory: " + ex.Message);
        }
    }

    private static void Show(string message)
    {
        try
        {
            System.Windows.MessageBox.Show(message, "KetoanMini Updater", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch
        {
            Log(message);
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never block updates.
        }
    }

    private sealed record Options(string PackagePath, string TargetDir, string AppPath, string? ExpectedVersion, int? WaitPid)
    {
        public static Options? Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = args[i][2..];
                if (i + 1 >= args.Length)
                {
                    values[key] = "";
                    continue;
                }

                values[key] = args[++i];
            }

            if (!values.TryGetValue("package", out var package) || string.IsNullOrWhiteSpace(package))
            {
                return null;
            }

            if (!values.TryGetValue("target", out var target) || string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            values.TryGetValue("app", out var app);
            if (string.IsNullOrWhiteSpace(app))
            {
                app = Path.Combine(target, AppExeName);
            }

            int? waitPid = null;
            if (values.TryGetValue("wait-pid", out var pidText) && int.TryParse(pidText, out var pid))
            {
                waitPid = pid;
            }

            values.TryGetValue("expected-version", out var expectedVersion);

            return new Options(
                Path.GetFullPath(package),
                Path.GetFullPath(target),
                Path.GetFullPath(app),
                expectedVersion,
                waitPid);
        }
    }
}
