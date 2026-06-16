using System.Drawing.Drawing2D;

namespace KetoanMini;

// ═════════════════════════════════════════════════════════════════════════════
// DocumentFormDialog — Create / Edit Document
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class DocumentFormDialog : Form
{
    private readonly AccountingStore _store;
    private readonly Document? _existing;
    public string SavedVoucherNo { get; private set; } = "";

    private TextBox _txtVoucherNo = new();
    private DateTimePicker _dtpDate = new();
    private ComboBox _cboCustomer = new();
    private TextBox _txtContent = new();
    private DataGridView _linesGrid = new();

    public DocumentFormDialog(AccountingStore store, Document? existing)
    {
        _store = store;
        _existing = existing;
        InitDialog();
        if (existing != null) PopulateForm(existing);
    }

    private void InitDialog()
    {
        Text = _existing == null ? "Tạo chứng từ mới" : "Chỉnh sửa chứng từ";
        Size = new Size(760, 560);
        MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var formTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 3,
            AutoSize = true,
            BackColor = Color.Transparent,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int i = 0; i < 3; i++) formTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        void AddFormRow(int col, int row, string label, Control ctrl)
        {
            var lbl = new Label { Text = label, Font = AppTheme.F9B, ForeColor = AppTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), BackColor = Color.Transparent };
            ctrl.Dock = DockStyle.Fill;
            formTable.Controls.Add(lbl, col, row);
            formTable.Controls.Add(ctrl, col + 1, row);
        }

        _txtVoucherNo = new TextBox { Font = AppTheme.F9 };
        _dtpDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Font = AppTheme.F9, Value = DateTime.Today };
        _cboCustomer = new ComboBox { Font = AppTheme.F9, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        _txtContent = new TextBox { Font = AppTheme.F9 };

        var customers = _store.ActiveCustomers();
        foreach (var c in customers) _cboCustomer.Items.Add(c.Name);
        _cboCustomer.TextChanged += (s, e) =>
        {
            var suggestions = _store.FindCompanySuggestions(_cboCustomer.Text, showAllWhenEmpty: false);
            _cboCustomer.Items.Clear();
            foreach (var sg in suggestions) _cboCustomer.Items.Add(sg);
        };

        AddFormRow(0, 0, "Số phiếu:", _txtVoucherNo);
        AddFormRow(2, 0, "Ngày:", _dtpDate);
        AddFormRow(0, 1, "Khách hàng:", _cboCustomer);
        AddFormRow(2, 1, "Nội dung:", _txtContent);

        // Lines grid
        var linesLbl = new Label { Text = "Danh sách hàng hóa / dịch vụ:", Font = AppTheme.F9B, ForeColor = AppTheme.TextPrimary, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.BottomLeft, BackColor = Color.Transparent };

        _linesGrid = new DataGridView { Dock = DockStyle.Fill, Height = 200 };
        AppTheme.StyleGrid(_linesGrid);
        _linesGrid.AllowUserToAddRows = true;
        _linesGrid.ReadOnly = false;
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LineContent", HeaderText = "Nội dung dòng", Width = 200 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Spec", HeaderText = "Quy cách", Width = 100 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Số lượng", Width = 90 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Price", HeaderText = "Đơn giá", Width = 120 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Thành tiền", Width = 120, ReadOnly = true });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Ghi chú", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _linesGrid.CellValueChanged += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = _linesGrid.Rows[e.RowIndex];
            if (decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out decimal qty) &&
                decimal.TryParse(row.Cells["Price"].Value?.ToString(), out decimal price))
            {
                row.Cells["Amount"].Value = TextUtil.FormatMoney(qty * price);
            }
        };

        // Buttons
        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var btnCancel = new RoundedButton { Text = "Hủy", Width = 80, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnSave = new RoundedButton { Text = "Lưu", Width = 80, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.Click += (s, e) => SaveDocument();
        btnFlow.Controls.Add(btnCancel);
        btnFlow.Controls.Add(btnSave);

        var gridWrap = new Panel { Dock = DockStyle.Fill };
        gridWrap.Controls.Add(_linesGrid);

        main.Controls.Add(gridWrap);
        main.Controls.Add(linesLbl);
        main.Controls.Add(formTable);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private void PopulateForm(Document doc)
    {
        _txtVoucherNo.Text = doc.VoucherNo;
        _dtpDate.Value = doc.Date.ToDateTime(TimeOnly.MinValue);
        _cboCustomer.Text = doc.CustomerName;
        _txtContent.Text = doc.Content;
        foreach (var line in doc.Lines)
        {
            int idx = _linesGrid.Rows.Add(line.LineContent, line.Spec, line.Quantity.ToString(), line.UnitPrice.ToString(), TextUtil.FormatMoney(line.Amount), line.Note);
        }
    }

    private void SaveDocument()
    {
        if (string.IsNullOrWhiteSpace(_txtVoucherNo.Text))
        {
            MessageBox.Show("Vui lòng nhập số phiếu.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var lines = new List<DocumentLine>();
        foreach (DataGridViewRow row in _linesGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string lc = row.Cells["LineContent"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(lc)) continue;
            decimal.TryParse(row.Cells["Qty"].Value?.ToString(), out decimal qty);
            decimal.TryParse(row.Cells["Price"].Value?.ToString(), out decimal price);
            lines.Add(new DocumentLine { LineContent = lc, Spec = row.Cells["Spec"].Value?.ToString() ?? "", Quantity = qty, UnitPrice = price, Note = row.Cells["Note"].Value?.ToString() ?? "" });
        }

        var dateOnly = DateOnly.FromDateTime(_dtpDate.Value);

        string voucherNo = _txtVoucherNo.Text.Trim();
        if (_existing != null)
        {
            _existing.VoucherNo = voucherNo;
            _existing.Date = dateOnly;
            _existing.CustomerName = _cboCustomer.Text.Trim();
            _existing.Content = _txtContent.Text.Trim();
            _existing.Lines = lines;
            _store.Save();
            SavedVoucherNo = voucherNo;
        }
        else
        {
            _store.AddDocument(
                voucherNo,
                dateOnly,
                _cboCustomer.Text.Trim(),
                _txtContent.Text.Trim(),
                "",
                lines);
            SavedVoucherNo = voucherNo;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}


// ═════════════════════════════════════════════════════════════════════════════
// GiaCongFormDialog — Create GiaCong Phieu
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class GiaCongFormDialog : Form
{
    private readonly GiaCongStore _giaCongStore;
    public string MaPhieu { get; private set; } = "";

    private TextBox _txtDoiTac = new();
    private TextBox _txtNhanVien = new();
    private ComboBox _cboLoaiPhieu = new();
    private DateTimePicker _dtpNgayLap = new();
    private DateTimePicker _dtpHanHT = new();
    private CheckBox _chkHanHT = new();
    private TextBox _txtGhiChu = new();
    private DataGridView _linesGrid = new();

    public GiaCongFormDialog(GiaCongStore store)
    {
        _giaCongStore = store;
        InitDialog();
    }

    private void InitDialog()
    {
        Text = "Tạo phiếu gia công mới";
        Size = new Size(820, 600);
        MinimumSize = new Size(700, 520);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        var formTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 4,
            AutoSize = true,
            BackColor = Color.Transparent,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        formTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int i = 0; i < 4; i++) formTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        void AddRow(int col, int row, string label, Control ctrl)
        {
            var lbl = new Label { Text = label, Font = AppTheme.F9B, ForeColor = AppTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), BackColor = Color.Transparent };
            ctrl.Dock = DockStyle.Fill;
            formTable.Controls.Add(lbl, col, row);
            formTable.Controls.Add(ctrl, col + 1, row);
        }

        _cboLoaiPhieu = new ComboBox { Font = AppTheme.F9, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboLoaiPhieu.Items.AddRange(new[] { "Xuất gia công", "Nhập gia công" });
        _cboLoaiPhieu.SelectedIndex = 0;

        _txtDoiTac = new TextBox { Font = AppTheme.F9 };
        _txtNhanVien = new TextBox { Font = AppTheme.F9 };
        _dtpNgayLap = new DateTimePicker { Format = DateTimePickerFormat.Short, Font = AppTheme.F9, Value = DateTime.Today };
        _dtpHanHT = new DateTimePicker { Format = DateTimePickerFormat.Short, Font = AppTheme.F9, Value = DateTime.Today.AddDays(30), Enabled = false };
        _chkHanHT = new CheckBox { Text = "Có hạn hoàn thành", Font = AppTheme.F9, AutoSize = true, BackColor = Color.Transparent };
        _chkHanHT.CheckedChanged += (s, e) => _dtpHanHT.Enabled = _chkHanHT.Checked;
        _txtGhiChu = new TextBox { Font = AppTheme.F9 };

        AddRow(0, 0, "Loại phiếu:", _cboLoaiPhieu);
        AddRow(2, 0, "Đối tác:", _txtDoiTac);
        AddRow(0, 1, "Nhân viên:", _txtNhanVien);
        AddRow(2, 1, "Ngày lập:", _dtpNgayLap);
        AddRow(2, 2, "Hạn HT:", _dtpHanHT);
        formTable.Controls.Add(_chkHanHT, 1, 2);
        AddRow(0, 3, "Ghi chú:", _txtGhiChu);

        // Lines grid
        var linesLbl = new Label { Text = "Danh sách hàng hóa:", Font = AppTheme.F9B, ForeColor = AppTheme.TextPrimary, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.BottomLeft, BackColor = Color.Transparent };

        _linesGrid = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(_linesGrid);
        _linesGrid.AllowUserToAddRows = true;
        _linesGrid.ReadOnly = false;

        var cboLoaiDong = new DataGridViewComboBoxColumn { Name = "LoaiDong", HeaderText = "Loại dòng", Width = 110 };
        foreach (var v in GiaCongLoaiDong.AllValues) cboLoaiDong.Items.Add(v);
        _linesGrid.Columns.Add(cboLoaiDong);
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MaHang", HeaderText = "Mã hàng", Width = 90 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TenHang", HeaderText = "Tên hàng", Width = 170 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DVT", HeaderText = "ĐVT", Width = 60 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SoLuong", HeaderText = "Số lượng", Width = 80 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DonGia", HeaderText = "Đơn giá GC", Width = 110 });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "GhiChu", HeaderText = "Ghi chú", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        // Buttons
        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var btnCancel = new RoundedButton { Text = "Hủy", Width = 80, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnSave = new RoundedButton { Text = "Lưu", Width = 80, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.Click += (s, e) => SavePhieu();
        btnFlow.Controls.Add(btnCancel);
        btnFlow.Controls.Add(btnSave);

        var gridWrap = new Panel { Dock = DockStyle.Fill };
        gridWrap.Controls.Add(_linesGrid);

        main.Controls.Add(gridWrap);
        main.Controls.Add(linesLbl);
        main.Controls.Add(formTable);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private void SavePhieu()
    {
        if (string.IsNullOrWhiteSpace(_txtDoiTac.Text))
        {
            MessageBox.Show("Vui lòng nhập tên đối tác.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var lines = new List<GiaCongHangHoa>();
        foreach (DataGridViewRow row in _linesGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string ten = row.Cells["TenHang"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(ten)) continue;
            decimal.TryParse(row.Cells["SoLuong"].Value?.ToString(), out decimal sl);
            decimal.TryParse(row.Cells["DonGia"].Value?.ToString(), out decimal dg);
            lines.Add(new GiaCongHangHoa
            {
                LoaiDong = row.Cells["LoaiDong"].Value?.ToString() ?? GiaCongLoaiDong.NguyenLieu,
                MaHang = row.Cells["MaHang"].Value?.ToString() ?? "",
                TenHang = ten,
                DonViTinh = row.Cells["DVT"].Value?.ToString() ?? "",
                SoLuong = sl,
                DonGiaGiaCong = dg,
                GhiChu = row.Cells["GhiChu"].Value?.ToString() ?? "",
                TrangThaiDong = GiaCongTrangThaiDong.Cho
            });
        }

        try
        {
            string ma = _giaCongStore.GenMaPhieu();
            DateOnly? han = _chkHanHT.Checked ? DateOnly.FromDateTime(_dtpHanHT.Value) : (DateOnly?)null;
            _giaCongStore.CreatePhieu(
                _cboLoaiPhieu.SelectedItem?.ToString() ?? "Xuất gia công",
                _txtDoiTac.Text.Trim(),
                _txtNhanVien.Text.Trim(),
                DateOnly.FromDateTime(_dtpNgayLap.Value),
                han,
                _txtGhiChu.Text.Trim(),
                lines);
            MaPhieu = ma;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi tạo phiếu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}


// ═════════════════════════════════════════════════════════════════════════════
// AddUserDialog — Admin create user
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class AddUserDialog : Form
{
    private readonly AccountingStore _store;
    private readonly AppUser _adminUser;

    private TextBox _txtUsername = new();
    private TextBox _txtFullName = new();
    private TextBox _txtPassword = new();
    private ComboBox _cboRole = new();

    public AddUserDialog(AccountingStore store, AppUser adminUser)
    {
        _store = store;
        _adminUser = adminUser;
        InitDialog();
    }

    private void InitDialog()
    {
        Text = "Thêm người dùng mới";
        Size = new Size(440, 320);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 16) };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = true,
            BackColor = Color.Transparent,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 4; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        void AddRow(int row, string label, Control ctrl)
        {
            var lbl = new Label { Text = label, Font = AppTheme.F9B, ForeColor = AppTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), BackColor = Color.Transparent };
            ctrl.Dock = DockStyle.Fill;
            tbl.Controls.Add(lbl, 0, row);
            tbl.Controls.Add(ctrl, 1, row);
        }

        _txtUsername = new TextBox { Font = AppTheme.F9 };
        _txtFullName = new TextBox { Font = AppTheme.F9 };
        _txtPassword = new TextBox { Font = AppTheme.F9, UseSystemPasswordChar = true };
        _cboRole = new ComboBox { Font = AppTheme.F9, DropDownStyle = ComboBoxStyle.DropDownList };
        _cboRole.Items.AddRange(new[] { "User", "Admin" });
        _cboRole.SelectedIndex = 0;

        AddRow(0, "Tên đăng nhập:", _txtUsername);
        AddRow(1, "Họ tên:", _txtFullName);
        AddRow(2, "Mật khẩu:", _txtPassword);
        // Role combobox shown but AdminCreateUser always creates "User"; role label only informational
        var roleLbl = new Label { Text = "Vai trò:", Font = AppTheme.F9B, ForeColor = AppTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), BackColor = Color.Transparent };
        _cboRole.Dock = DockStyle.Fill;
        tbl.Controls.Add(roleLbl, 0, 3);
        tbl.Controls.Add(_cboRole, 1, 3);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var btnCancel = new RoundedButton { Text = "Hủy", Width = 80, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnSave = new RoundedButton { Text = "Tạo", Width = 80, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.Click += (s, e) => CreateUser();
        btnFlow.Controls.Add(btnCancel);
        btnFlow.Controls.Add(btnSave);

        main.Controls.Add(tbl);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private void CreateUser()
    {
        if (string.IsNullOrWhiteSpace(_txtUsername.Text) || string.IsNullOrWhiteSpace(_txtPassword.Text))
        {
            MessageBox.Show("Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _store.AdminCreateUser(
                _txtUsername.Text.Trim(),
                _txtFullName.Text.Trim(),
                _txtPassword.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi tạo người dùng: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
