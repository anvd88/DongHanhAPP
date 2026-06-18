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

        ApplicationConfiguration.Initialize();

        // Vỏ ứng dụng đang chuyển dần sang WPF: tạo một WPF Application duy nhất
        // cho tiến trình để các cửa sổ WPF (đăng nhập…) hoạt động. MainForm vẫn là
        // WinForms trong giai đoạn chuyển tiếp và chạy qua WinForms message loop.
        EnsureWpfApplication();

        // Nạp lựa chọn theme (Sáng/Tối) đã lưu trước khi dựng bất kỳ giao diện nào.
        ThemeState.Load();

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
            using var mainForm = new MainForm(store, login.AuthenticatedUser);
            Application.Run(mainForm);
            if (!mainForm.LogoutRequested)
            {
                return;
            }
        }
    }

    private static System.Windows.Application EnsureWpfApplication()
    {
        return System.Windows.Application.Current
            ?? new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
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
            // Đã cập nhật (DialogResult true) → cho tiếp tục tới đăng nhập;
            // nếu thoát mà chưa cập nhật → chặn đăng nhập (return false).
            return dialog.ShowDialog() == true;
        }

        // Có bản mới (không bắt buộc) → popup; dù cập nhật hay để sau đều tiếp tục đăng nhập.
        if (result.UpdateAvailable)
        {
            var dialog = new UpdateWindow(store, result.Latest, blocking: false);
            dialog.ShowDialog();
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
                using var dialog = new DatabaseSetupDialog(connectionString, ex.Message);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        return new AccountingStore(dialog.SavedConnectionString);
                    }
                    catch (Exception retryEx)
                    {
                        MessageBox.Show(
                            $"Đã lưu cấu hình nhưng vẫn chưa kết nối được SQL Server.\n\n{retryEx.Message}",
                            "Lỗi kết nối database",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return null;
                    }
                }
            }

            MessageBox.Show(
                "Không kết nối được SQL Server.\n\n" +
                $"{ex.Message}\n\n" +
                "Các file cấu hình đang được app đọc:\n" +
                $"- Ưu tiên user: {DatabaseConnectionConfig.UserConfigPath}\n" +
                $"- Cạnh app: {DatabaseConnectionConfig.PrimaryConfigPath}",
                "Lỗi kết nối database",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
    }
}
