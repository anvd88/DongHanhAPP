using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

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
    private readonly GiaCongPhieu? _editingPhieu;
    private readonly string _currentUsername;
    public string MaPhieu { get; private set; } = "";

    private ComboBox _cboDoiTac = new();
    private TextBox _txtNhanVien = new();
    private ComboBox _cboLoaiPhieu = new();
    private DateTimePicker _dtpNgayLap = new();
    private DateTimePicker _dtpHanHT = new();
    private CheckBox _chkHanHT = new();
    private TextBox _txtGhiChu = new();
    private Label _ghiChuCounter = new();
    private DataGridView _linesGrid = new();

    public GiaCongFormDialog(GiaCongStore store, string currentUsername = "")
    {
        _giaCongStore = store;
        _currentUsername = currentUsername;
        InitDialog();
    }

    public GiaCongFormDialog(GiaCongStore store, GiaCongPhieu phieu, string currentUsername = "")
    {
        _giaCongStore = store;
        _editingPhieu = phieu;
        _currentUsername = currentUsername;
        InitDialog();
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WM_NCHITTEST || WindowState != FormWindowState.Normal || (int)m.Result != HTCLIENT)
            return;

        const int grip = 8;
        var p = PointToClient(Cursor.Position);
        var left = p.X <= grip;
        var right = p.X >= ClientSize.Width - grip;
        var top = p.Y <= grip;
        var bottom = p.Y >= ClientSize.Height - grip;

        if (left && top) m.Result = HTTOPLEFT;
        else if (right && top) m.Result = HTTOPRIGHT;
        else if (left && bottom) m.Result = HTBOTTOMLEFT;
        else if (right && bottom) m.Result = HTBOTTOMRIGHT;
        else if (left) m.Result = HTLEFT;
        else if (right) m.Result = HTRIGHT;
        else if (top) m.Result = HTTOP;
        else if (bottom) m.Result = HTBOTTOM;
    }

    private void InitDialog()
    {
        Text = _editingPhieu == null ? "Tạo phiếu gia công mới" : $"Sửa phiếu gia công - {_editingPhieu.MaPhieu}";
        Size = new Size(1180, 780);
        MinimumSize = new Size(960, 660);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F10;
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        Padding = new Padding(4);

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = AppTheme.Background,
            Padding = new Padding(20, 18, 20, 8)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

        shell.Controls.Add(BuildTitleBar(), 0, 0);
        shell.Controls.Add(BuildCreateCard(), 0, 1);

        var footer = new Label
        {
            Text = "Powered by Codex and Claude",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = AppTheme.F9,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent
        };
        shell.Controls.Add(footer, 0, 2);

        Controls.Add(shell);
    }

    private Control BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        bar.MouseDown += (_, e) => BeginDrag(e);

        var iconBox = new RoundedPanel
        {
            Width = 52,
            Height = 52,
            Left = 0,
            Top = 0,
            FillColor = Color.White,
            BorderColor = AppTheme.Border,
            CornerRadius = 8,
            ShadowDepth = 1
        };
        iconBox.Controls.Add(new Label
        {
            Text = "\uE8A5",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe MDL2 Assets", 20f),
            ForeColor = AppTheme.Accent,
            BackColor = Color.Transparent
        });
        iconBox.MouseDown += (_, e) => BeginDrag(e);
        bar.Controls.Add(iconBox);

        var title = new Label
        {
            Text = Text,
            AutoSize = false,
            Left = 70,
            Top = 8,
            Width = 620,
            Height = 42,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.F18B,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent
        };
        title.MouseDown += (_, e) => BeginDrag(e);
        bar.Controls.Add(title);

        var close = MakeWindowButton("×");
        var max = MakeWindowButton("□");
        var min = MakeWindowButton("−");
        close.Click += (_, _) => Close();
        max.Click += (_, _) =>
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            max.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
        };
        min.Click += (_, _) => WindowState = FormWindowState.Minimized;

        void PositionButtons()
        {
            close.Left = bar.Width - 42;
            max.Left = close.Left - 44;
            min.Left = max.Left - 44;
            close.Top = max.Top = min.Top = 10;
        }

        bar.Controls.Add(close);
        bar.Controls.Add(max);
        bar.Controls.Add(min);
        bar.Resize += (_, _) => PositionButtons();
        PositionButtons();
        return bar;
    }

    private Control BuildCreateCard()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.White,
            BorderColor = AppTheme.Border,
            CornerRadius = 8,
            ShadowDepth = 2,
            Padding = new Padding(24)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));

        layout.Controls.Add(BuildFormFields(), 0, 0);

        layout.Controls.Add(new Label
        {
            Text = "Danh sách hàng hóa",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = AppTheme.F12B,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.White
        }, 0, 1);

        layout.Controls.Add(BuildLinesArea(), 0, 2);
        layout.Controls.Add(BuildActionBar(), 0, 3);
        card.Controls.Add(layout);

        if (_editingPhieu != null)
            PopulateEditData(_editingPhieu);
        else
            AddLineRow();

        return card;
    }

    private Control BuildFormFields()
    {
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4,
            BackColor = Color.White
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));

        _cboLoaiPhieu = new ComboBox { Font = AppTheme.F10, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
        _cboLoaiPhieu.Items.AddRange(new[] { "Xuất gia công", "Nhập gia công" });
        _cboLoaiPhieu.SelectedIndex = 0;

        _cboDoiTac = new ComboBox
        {
            Font = AppTheme.F10,
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle = FlatStyle.Flat,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        LoadDoiTacSuggestions();

        _txtNhanVien = new TextBox
        {
            Font = AppTheme.F10,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Text = CurrentUsername(),
            BackColor = Color.White
        };

        _dtpNgayLap = new DateTimePicker { CustomFormat = "dd/MM/yyyy", Format = DateTimePickerFormat.Custom, Font = AppTheme.F10, Value = DateTime.Today };
        _dtpHanHT = new DateTimePicker { CustomFormat = "dd/MM/yyyy", Format = DateTimePickerFormat.Custom, Font = AppTheme.F10, Value = DateTime.Today.AddDays(30), Enabled = false };
        _chkHanHT = new CheckBox { Text = "Có hạn hoàn thành", Font = AppTheme.F10, AutoSize = true, BackColor = Color.White, ForeColor = AppTheme.TextPrimary };
        _chkHanHT.CheckedChanged += (_, _) => _dtpHanHT.Enabled = _chkHanHT.Checked;

        _txtGhiChu = new TextBox
        {
            Font = AppTheme.F10,
            BorderStyle = BorderStyle.None,
            Multiline = true,
            MaxLength = 255,
            PlaceholderText = "Nhập ghi chú (nếu có)",
            BackColor = Color.White
        };
        _ghiChuCounter = new Label { Text = "0/255", Dock = DockStyle.Bottom, Height = 20, TextAlign = ContentAlignment.MiddleRight, Font = AppTheme.F9, ForeColor = AppTheme.TextSecondary, BackColor = Color.White };
        _txtGhiChu.TextChanged += (_, _) => _ghiChuCounter.Text = $"{_txtGhiChu.TextLength}/255";

        AddField(form, 0, 0, "Loại phiếu *", FieldHost(_cboLoaiPhieu));
        AddField(form, 2, 0, "Đối tác", FieldHost(_cboDoiTac));
        AddField(form, 0, 1, "Nhân viên", FieldHost(_txtNhanVien));
        AddField(form, 2, 1, "Ngày lập *", FieldHost(_dtpNgayLap, new Padding(10, 7, 10, 6)));

        form.Controls.Add(new Label { Dock = DockStyle.Fill, BackColor = Color.White }, 0, 2);
        var chkHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0, 15, 0, 0), Margin = new Padding(0, 0, 20, 0) };
        chkHost.Controls.Add(_chkHanHT);
        form.Controls.Add(chkHost, 1, 2);
        AddField(form, 2, 2, "Hạn hoàn thành", FieldHost(_dtpHanHT, new Padding(10, 7, 10, 6)));
        AddField(form, 0, 3, "Ghi chú", NoteHost());

        return form;
    }

    private Control BuildLinesArea()
    {
        var box = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.White,
            BorderColor = AppTheme.Border,
            CornerRadius = 6,
            Padding = new Padding(0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

        _linesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToResizeRows = false,
            ReadOnly = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = AppTheme.Border,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 44
        };
        AppTheme.StyleGrid(_linesGrid);
        _linesGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _linesGrid.ColumnHeadersHeight = 44;
        _linesGrid.RowTemplate.Height = 58;
        _linesGrid.DefaultCellStyle.SelectionBackColor = Color.White;
        _linesGrid.DefaultCellStyle.SelectionForeColor = AppTheme.TextPrimary;
        _linesGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _linesGrid.CellContentClick += (_, e) => HandleLineGridClick(e);
        _linesGrid.CellEndEdit += (_, e) => FormatLineNumberCell(e);

        _linesGrid.Columns.Add(TextColumn("MaHang", "Mã hàng", 150, "Nhập mã hàng"));
        _linesGrid.Columns.Add(TextColumn("TenHang", "Tên hàng", 220, "Nhập tên hàng"));
        _linesGrid.Columns.Add(TextColumn("DVT", "ĐVT", 130, "Nhập ĐVT"));
        _linesGrid.Columns.Add(TextColumn("SoLuong", "Số lượng", 150, "0,00", right: true));
        _linesGrid.Columns.Add(TextColumn("DonGia", "Đơn giá GC", 160, "0,00", right: true));
        _linesGrid.Columns.Add(TextColumn("GhiChu", "Ghi chú", 220, "Nhập ghi chú", fill: true));
        _linesGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Delete",
            HeaderText = "",
            Text = "🗑",
            UseColumnTextForButtonValue = true,
            Width = 48,
            FlatStyle = FlatStyle.Flat
        });

        var addRowPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var addRow = new RoundedButton
        {
            Text = "⊕  Thêm dòng",
            Width = 150,
            Height = 34,
            CornerRadius = 8,
            BackColor = Color.White,
            ForeColor = AppTheme.Accent,
            BorderColor = Color.Transparent,
            Font = AppTheme.F10
        };
        addRow.Click += (_, _) => AddLineRow();
        addRowPanel.Controls.Add(addRow);
        addRowPanel.Resize += (_, _) =>
        {
            addRow.Left = Math.Max(0, (addRowPanel.Width - addRow.Width) / 2);
            addRow.Top = 7;
        };

        layout.Controls.Add(_linesGrid, 0, 0);
        layout.Controls.Add(addRowPanel, 0, 1);
        box.Controls.Add(layout);
        return box;
    }

    private Control BuildActionBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 20, 0, 0),
            BackColor = Color.White
        };
        var btnSave = new RoundedButton { Text = "▣  Lưu", Width = 110, Height = 44, CornerRadius = 6, BackColor = AppTheme.Accent, ForeColor = Color.White, Font = AppTheme.F10B };
        var btnCancel = new RoundedButton { Text = "Hủy", Width = 96, Height = 44, CornerRadius = 6, BackColor = Color.White, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border, Font = AppTheme.F10B, Margin = new Padding(0, 0, 18, 0) };
        btnSave.Click += (_, _) => SavePhieu();
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bar.Controls.Add(btnSave);
        bar.Controls.Add(btnCancel);
        return bar;
    }

    private static void AddField(TableLayoutPanel form, int col, int row, string labelText, Control input)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.F10,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.White,
            Padding = new Padding(4, 0, 0, 0)
        };
        form.Controls.Add(label, col, row);
        form.Controls.Add(input, col + 1, row);
    }

    private static RoundedPanel FieldHost(Control input, Padding? padding = null)
    {
        input.Dock = DockStyle.Fill;
        input.Margin = Padding.Empty;

        var host = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.White,
            BorderColor = AppTheme.Border,
            CornerRadius = 6,
            Padding = padding ?? new Padding(14, 12, 14, 8),
            Margin = new Padding(0, 6, 24, 6)
        };
        host.Controls.Add(input);
        return host;
    }

    private RoundedPanel NoteHost()
    {
        var host = FieldHost(_txtGhiChu, new Padding(14, 10, 14, 4));
        host.Controls.Add(_ghiChuCounter);
        return host;
    }

    private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width, string nullValue, bool right = false, bool fill = false)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                NullValue = nullValue,
                Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            }
        };
    }

    private static RoundedButton MakeWindowButton(string text)
    {
        return new RoundedButton
        {
            Text = text,
            Width = 34,
            Height = 34,
            CornerRadius = 6,
            BackColor = AppTheme.Background,
            BorderColor = Color.Transparent,
            ForeColor = AppTheme.TextPrimary,
            Font = AppTheme.F12B
        };
    }

    private void BeginDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private void LoadDoiTacSuggestions()
    {
        try
        {
            var names = _giaCongStore.GetAllPhieu()
                .Select(p => p.DoiTac.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Cast<object>()
                .ToArray();
            _cboDoiTac.Items.AddRange(names);
        }
        catch
        {
            // Suggestions are only a convenience; creating a phiếu must still work if they fail.
        }
    }

    private void AddLineRow(GiaCongHangHoa? line = null)
    {
        var index = _linesGrid.Rows.Add(
            line?.MaHang ?? "",
            line?.TenHang ?? "",
            line?.DonViTinh ?? "",
            (line?.SoLuong ?? 0m).ToString("N2"),
            (line?.DonGiaGiaCong ?? 0m).ToString("N2"),
            line?.GhiChu ?? "",
            "🗑");

        _linesGrid.Rows[index].Height = 58;
    }

    private void HandleLineGridClick(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _linesGrid.Columns[e.ColumnIndex].Name != "Delete")
            return;

        if (_linesGrid.Rows.Count <= 1)
        {
            foreach (DataGridViewCell cell in _linesGrid.Rows[e.RowIndex].Cells)
                if (cell.OwningColumn.Name != "Delete")
                    cell.Value = cell.OwningColumn.Name is "SoLuong" or "DonGia" ? 0m.ToString("N2") : "";
            return;
        }

        _linesGrid.Rows.RemoveAt(e.RowIndex);
    }

    private void FormatLineNumberCell(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        var columnName = _linesGrid.Columns[e.ColumnIndex].Name;
        if (columnName is not ("SoLuong" or "DonGia"))
            return;

        var cell = _linesGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        cell.Value = ParseDecimal(cell.Value).ToString("N2");
    }

    private string CurrentUsername()
        => string.IsNullOrWhiteSpace(_currentUsername) ? Environment.UserName : _currentUsername.Trim();

    private void SavePhieu()
    {
        if (string.IsNullOrWhiteSpace(_cboDoiTac.Text))
        {
            MessageBox.Show("Vui lòng nhập tên đối tác.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var lines = CollectLines();
        var loaiPhieu = _cboLoaiPhieu.SelectedItem?.ToString() ?? "Xuất gia công";
        var han = _chkHanHT.Checked ? DateOnly.FromDateTime(_dtpHanHT.Value) : (DateOnly?)null;

        try
        {
            if (_editingPhieu == null)
            {
                var created = _giaCongStore.CreatePhieu(
                    loaiPhieu,
                    _cboDoiTac.Text.Trim(),
                    _txtNhanVien.Text.Trim(),
                    DateOnly.FromDateTime(_dtpNgayLap.Value),
                    han,
                    _txtGhiChu.Text.Trim(),
                    lines);
                MaPhieu = created.MaPhieu;
            }
            else
            {
                _giaCongStore.UpdatePhieu(
                    _editingPhieu.Id,
                    loaiPhieu,
                    _cboDoiTac.Text.Trim(),
                    _txtNhanVien.Text.Trim(),
                    DateOnly.FromDateTime(_dtpNgayLap.Value),
                    han,
                    _txtGhiChu.Text.Trim(),
                    lines);
                MaPhieu = _editingPhieu.MaPhieu;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            var action = _editingPhieu == null ? "tạo" : "sửa";
            MessageBox.Show($"Lỗi {action} phiếu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private List<GiaCongHangHoa> CollectLines()
    {
        var lines = new List<GiaCongHangHoa>();
        var loaiDong = ResolveLoaiDong();
        foreach (DataGridViewRow row in _linesGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string ten = row.Cells["TenHang"].Value?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(ten)) continue;
            lines.Add(new GiaCongHangHoa
            {
                LoaiDong = loaiDong,
                MaHang = row.Cells["MaHang"].Value?.ToString() ?? "",
                TenHang = ten,
                DonViTinh = row.Cells["DVT"].Value?.ToString() ?? "",
                SoLuong = ParseDecimal(row.Cells["SoLuong"].Value),
                DonGiaGiaCong = ParseDecimal(row.Cells["DonGia"].Value),
                GhiChu = row.Cells["GhiChu"].Value?.ToString() ?? "",
                TrangThaiDong = GiaCongTrangThaiDong.Cho
            });
        }

        return lines;
    }

    private string ResolveLoaiDong()
    {
        var loaiPhieu = _cboLoaiPhieu.SelectedItem?.ToString() ?? _cboLoaiPhieu.Text;
        return TextUtil.RemoveDiacritics(loaiPhieu).Contains("nhap", StringComparison.OrdinalIgnoreCase)
            ? GiaCongLoaiDong.ThanhPham
            : GiaCongLoaiDong.NguyenLieu;
    }

    private static decimal ParseDecimal(object? value)
    {
        var text = value?.ToString()?.Trim() ?? "";
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var current))
            return current;
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
            return invariant;

        text = text.Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var compact) ? compact : 0m;
    }

    private void PopulateEditData(GiaCongPhieu phieu)
    {
        _cboLoaiPhieu.SelectedItem = _cboLoaiPhieu.Items
            .Cast<object>()
            .FirstOrDefault(item => string.Equals(item.ToString(), phieu.LoaiPhieu, StringComparison.OrdinalIgnoreCase))
            ?? _cboLoaiPhieu.Items[0];
        _cboDoiTac.Text = phieu.DoiTac;
        _txtNhanVien.Text = phieu.NhanVienPhuTrach;
        _dtpNgayLap.Value = phieu.NgayLap.ToDateTime(TimeOnly.MinValue);
        _chkHanHT.Checked = phieu.HanHoanThanh.HasValue;
        if (phieu.HanHoanThanh.HasValue)
            _dtpHanHT.Value = phieu.HanHoanThanh.Value.ToDateTime(TimeOnly.MinValue);
        _dtpHanHT.Enabled = _chkHanHT.Checked;
        _txtGhiChu.Text = phieu.GhiChu;

        foreach (var line in phieu.HangHoaList)
            AddLineRow(line);

        if (_linesGrid.Rows.Count == 0)
            AddLineRow();
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
