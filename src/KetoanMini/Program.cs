using Wpf = System.Windows;

namespace KetoanMini;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (TryRunSqlLanConfigure(args))
        {
            return;
        }

        if (TryRunCommandLineExport(args))
        {
            return;
        }

        // Tạo một WPF Application duy nhất cho toàn bộ tiến trình.
        EnsureWpfApplication();

        // Nạp lựa chọn theme (Sáng/Tối) đã lưu trước khi dựng bất kỳ giao diện nào.
        ThemeState.Load();
        WpfTheme.ApplyCurrentTheme();

        // Dọn file cài đã tải ở lần cập nhật trước (sau khi bản mới đã được cài và chạy lên).
        UpdateInstaller.CleanupAfterUpdate();

        var store = CreateStore();
        if (store is null)
        {
            return;
        }

        if (!HandleVersionCheck(store))
        {
            return;
        }

        while (true)
        {
            store.CurrentUser = null;
            var login = new LoginWindow(store);
            if (login.ShowDialog() != true || login.AuthenticatedUser is null)
            {
                return;
            }

            store.CurrentUser = login.AuthenticatedUser;
            var mainWindow = new MainWindow(store, login.AuthenticatedUser);
            mainWindow.ShowDialog();
            if (!mainWindow.LogoutRequested)
            {
                return;
            }
        }
    }

    private static Wpf.Application EnsureWpfApplication()
    {
        return Wpf.Application.Current
            ?? new Wpf.Application
            {
                ShutdownMode = Wpf.ShutdownMode.OnExplicitShutdown
            };
    }

    /// <summary>
    /// Kiểm tra phiên bản khi mở app. Trả về false nếu phải thoát (đã chạy setup,
    /// hoặc bị chặn đăng nhập do bản quá cũ).
    /// </summary>
    private static bool HandleVersionCheck(AccountingStore store)
    {
        VersionCheckResult result;
        try
        {
            result = store.CheckVersion();
        }
        catch
        {
            // Không chặn khởi động app nếu việc kiểm tra phiên bản gặp lỗi.
            return true;
        }

        if (result.Latest is null)
        {
            return true;
        }

        // Bản quá cũ + admin đã bật chặn → bắt buộc cập nhật.
        if (result.MustBlock)
        {
            var dialog = new UpdateWindow(store, result.Latest, blocking: true);
            // DialogResult true nghia la da len lich chay setup; can thoat app de installer thay file.
            // Neu nguoi dung thoat ma chua cap nhat thi van chan dang nhap.
            dialog.ShowDialog();
            return false;
        }

        // Có bản mới (không bắt buộc) → popup; dù cập nhật hay để sau đều tiếp tục đăng nhập.
        if (result.UpdateAvailable)
        {
            var dialog = new UpdateWindow(store, result.Latest, blocking: false);
            if (dialog.ShowDialog() == true)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryRunCommandLineExport(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "--export-openxml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var outputPath = args[1];
        if (!outputPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.ChangeExtension(outputPath, ".xlsx");
        }

        var store = CreateStore(showSetupDialog: false);
        if (store is null)
        {
            return true;
        }

        var payload = store.BuildExportPayload();
        DirectExcelExporter.ExportCustomerWorkbookAsync(payload, outputPath).GetAwaiter().GetResult();
        return true;
    }

    private static bool TryRunSqlLanConfigure(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "--configure-sql-lan", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var instance = ArgumentValue(args, "--instance", "SQLEXPRESS01");
        var database = ArgumentValue(args, "--database", "KetoanMini");
        var login = ArgumentValue(args, "--login", "ketoan_app");
        var password = ArgumentValue(args, "--password", "");
        var portText = ArgumentValue(args, "--port", "1433");
        var configPath = ArgumentValue(args, "--config", DatabaseConnectionConfig.PrimaryConfigPath);
        var logPath = ArgumentValue(args, "--log", Path.Combine(AppContext.BaseDirectory, "configure_sql_lan.log"));

        try
        {
            if (!int.TryParse(portText, out var port))
            {
                port = 1433;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Thiếu --password cho login SQL.");
            }

            var result = SqlLanConfigurator.Configure(instance, database, login, password, port, configPath);
            File.WriteAllText(logPath, result, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            File.WriteAllText(logPath, "ERROR\r\n" + ex, Encoding.UTF8);
        }

        return true;
    }

    private static string ArgumentValue(string[] args, string name, string fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static AccountingStore? CreateStore(bool showSetupDialog = true)
    {
        var connectionString = DatabaseConnectionConfig.LoadConnectionString();
        try
        {
            return new AccountingStore(connectionString);
        }
        catch (Exception ex)
        {
            if (showSetupDialog)
            {
                var dialog = new DatabaseSetupWpfWindow(connectionString, ex.Message);
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        return new AccountingStore(dialog.SavedConnectionString);
                    }
                    catch (Exception retryEx)
                    {
                        Wpf.MessageBox.Show(
                            $"Đã lưu cấu hình nhưng vẫn chưa kết nối được SQL Server.\n\n{retryEx.Message}",
                            "Lỗi kết nối database",
                            Wpf.MessageBoxButton.OK,
                            Wpf.MessageBoxImage.Error);
                        return null;
                    }
                }
            }

            Wpf.MessageBox.Show(
                "Không kết nối được SQL Server.\n\n" +
                $"{ex.Message}\n\n" +
                "Các file cấu hình đang được app đọc:\n" +
                $"- Ưu tiên user: {DatabaseConnectionConfig.UserConfigPath}\n" +
                $"- Cạnh app: {DatabaseConnectionConfig.PrimaryConfigPath}",
                "Lỗi kết nối database",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
            return null;
        }
    }
}
