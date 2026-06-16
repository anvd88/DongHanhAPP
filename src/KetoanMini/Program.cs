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

        var store = CreateStore();
        if (store is null)
        {
            return;
        }

        while (true)
        {
            store.CurrentUser = null;
            using var login = new LoginForm(store);
            if (login.ShowDialog() != DialogResult.OK || login.AuthenticatedUser is null)
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

        var store = CreateStore();
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

    private static AccountingStore? CreateStore()
    {
        try
        {
            return new AccountingStore(DatabaseConnectionConfig.LoadConnectionString());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không kết nối được SQL Server.\n\n{ex.Message}\n\nKiểm tra file cấu hình: {DatabaseConnectionConfig.PrimaryConfigPath}",
                "Lỗi kết nối database",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
    }
}
