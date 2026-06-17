using Microsoft.Data.SqlClient;

namespace KetoanMini;

internal sealed class DatabaseSetupDialog : Form
{
    private readonly string _initialConnectionString;
    private readonly string _initialError;

    private TextBox _txtServer = new();
    private TextBox _txtDatabase = new();
    private TextBox _txtUser = new();
    private TextBox _txtPassword = new();
    private CheckBox _chkWindowsAuth = new();
    private Label _lblStatus = new();

    public string SavedConnectionString { get; private set; } = "";

    public DatabaseSetupDialog(string initialConnectionString, string initialError)
    {
        _initialConnectionString = initialConnectionString;
        _initialError = initialError;
        InitDialog();
        PopulateFromConnectionString(initialConnectionString);
    }

    private void InitDialog()
    {
        Text = "Cấu hình kết nối SQL Server";
        Size = new Size(640, 440);
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12), BackColor = AppTheme.Background };

        var title = new Label
        {
            Text = "Không kết nối được SQL Server",
            Dock = DockStyle.Top,
            Height = 30,
            Font = AppTheme.F14B,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent
        };

        var guide = new Label
        {
            Text = "Nhập thông tin máy chủ SQL trên mạng LAN. Cấu hình sẽ được lưu vào thư mục người dùng, không cần quyền admin.",
            Dock = DockStyle.Top,
            Height = 46,
            Font = AppTheme.F9,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 5,
            Height = 210,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (var i = 0; i < 5; i++)
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        _txtServer = Input("192.168.1.88,1433");
        _txtDatabase = Input("KetoanMini");
        _txtUser = Input("ketoan_app");
        _txtPassword = Input("");
        _txtPassword.UseSystemPasswordChar = true;
        _chkWindowsAuth = new CheckBox
        {
            Text = "Dùng Windows Authentication",
            Dock = DockStyle.Fill,
            Font = AppTheme.F9,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent
        };
        _chkWindowsAuth.CheckedChanged += (_, _) => ToggleSqlLoginFields();

        AddRow(table, 0, "Máy chủ / IP:", _txtServer);
        AddRow(table, 1, "Database:", _txtDatabase);
        AddRow(table, 2, "Tài khoản SQL:", _txtUser);
        AddRow(table, 3, "Mật khẩu:", _txtPassword);
        table.Controls.Add(new Label { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 4);
        table.Controls.Add(_chkWindowsAuth, 1, 4);

        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            Font = AppTheme.F8,
            ForeColor = AppTheme.Danger,
            BackColor = Color.Transparent,
            Text = $"Lỗi hiện tại: {_initialError}",
            AutoEllipsis = true
        };

        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
            BackColor = Color.Transparent
        };
        var btnSave = new RoundedButton
        {
            Text = "Kiểm tra && lưu",
            Width = 130,
            Height = 36,
            CornerRadius = 8,
            BackColor = AppTheme.Accent,
            ForeColor = Color.White,
            Font = AppTheme.F9B
        };
        var btnCancel = new RoundedButton
        {
            Text = "Thoát",
            Width = 90,
            Height = 36,
            CornerRadius = 8,
            BackColor = AppTheme.SurfaceAlt,
            ForeColor = AppTheme.TextPrimary,
            BorderColor = AppTheme.Border
        };
        btnSave.Click += (_, _) => TestAndSave();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnFlow.Controls.Add(btnSave);
        btnFlow.Controls.Add(btnCancel);

        main.Controls.Add(_lblStatus);
        main.Controls.Add(table);
        main.Controls.Add(guide);
        main.Controls.Add(title);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private static TextBox Input(string placeholder)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = AppTheme.F9,
            PlaceholderText = placeholder,
            Margin = new Padding(0, 4, 0, 4)
        };
    }

    private static void AddRow(TableLayoutPanel table, int row, string labelText, Control input)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = AppTheme.F9B,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 10, 0)
        };
        table.Controls.Add(label, 0, row);
        table.Controls.Add(input, 1, row);
    }

    private void PopulateFromConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            _txtServer.Text = builder.DataSource;
            _txtDatabase.Text = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "KetoanMini" : builder.InitialCatalog;
            _chkWindowsAuth.Checked = builder.IntegratedSecurity;
            _txtUser.Text = builder.UserID;
            _txtPassword.Text = builder.Password;
        }
        catch
        {
            _txtServer.Text = "";
            _txtDatabase.Text = "KetoanMini";
            _txtUser.Text = "ketoan_app";
            _txtPassword.Text = "";
        }

        ToggleSqlLoginFields();
    }

    private void ToggleSqlLoginFields()
    {
        var sqlLogin = !_chkWindowsAuth.Checked;
        _txtUser.Enabled = sqlLogin;
        _txtPassword.Enabled = sqlLogin;
    }

    private void TestAndSave()
    {
        var server = _txtServer.Text.Trim();
        var database = _txtDatabase.Text.Trim();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            SetStatus("Vui lòng nhập máy chủ và database.", AppTheme.Warning);
            return;
        }

        if (!_chkWindowsAuth.Checked && (string.IsNullOrWhiteSpace(_txtUser.Text) || string.IsNullOrWhiteSpace(_txtPassword.Text)))
        {
            SetStatus("Vui lòng nhập tài khoản và mật khẩu SQL.", AppTheme.Warning);
            return;
        }

        try
        {
            UseWaitCursor = true;
            Enabled = false;

            var connectionString = BuildConnectionString();
            _ = new AccountingStore(connectionString);
            DatabaseConnectionConfig.SaveUserConnectionString(connectionString);
            SavedConnectionString = connectionString;

            MessageBox.Show(
                $"Đã kết nối và lưu cấu hình thành công.\n\nFile cấu hình:\n{DatabaseConnectionConfig.UserConfigPath}",
                "Kết nối thành công",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, AppTheme.Danger);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    private string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _txtServer.Text.Trim(),
            InitialCatalog = _txtDatabase.Text.Trim(),
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ConnectTimeout = 10
        };

        if (_chkWindowsAuth.Checked)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = _txtUser.Text.Trim();
            builder.Password = _txtPassword.Text;
        }

        return builder.ConnectionString;
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }
}
