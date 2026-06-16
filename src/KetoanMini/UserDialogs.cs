using System.Drawing.Drawing2D;

namespace KetoanMini;

// ═════════════════════════════════════════════════════════════════════════════
// ChangePasswordDialog — current user changes their own password
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class ChangePasswordDialog : Form
{
    private readonly AccountingStore _store;
    private readonly AppUser _user;

    private readonly TextBox _txtCurrent = new() { UseSystemPasswordChar = true, Font = AppTheme.F9 };
    private readonly TextBox _txtNew     = new() { UseSystemPasswordChar = true, Font = AppTheme.F9 };
    private readonly TextBox _txtConfirm = new() { UseSystemPasswordChar = true, Font = AppTheme.F9 };

    public ChangePasswordDialog(AccountingStore store, AppUser user)
    {
        _store = store;
        _user = user;

        Text = "Đổi mật khẩu";
        Size = new Size(430, 300);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12) };
        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 3; i++) tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

        void AddRow(int row, string label, Control ctrl)
        {
            var lbl = new Label { Text = label, Font = AppTheme.F9B, ForeColor = AppTheme.TextSecondary, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), BackColor = Color.Transparent };
            ctrl.Dock = DockStyle.Fill;
            tbl.Controls.Add(lbl, 0, row);
            tbl.Controls.Add(ctrl, 1, row);
        }

        AddRow(0, "Mật khẩu hiện tại:", _txtCurrent);
        AddRow(1, "Mật khẩu mới:", _txtNew);
        AddRow(2, "Nhập lại mật khẩu:", _txtConfirm);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var btnCancel = new RoundedButton { Text = "Hủy", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnSave = new RoundedButton { Text = "Cập nhật", Width = 100, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.Click += (s, e) => Save();
        btnFlow.Controls.Add(btnCancel);
        btnFlow.Controls.Add(btnSave);

        main.Controls.Add(tbl);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_txtNew.Text))
        {
            MessageBox.Show("Vui lòng nhập mật khẩu mới.", "Thiếu thông tin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_txtNew.Text != _txtConfirm.Text)
        {
            MessageBox.Show("Mật khẩu nhập lại không khớp.", "Không khớp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _store.UpdateCurrentUserProfile(_user.FullName, _txtCurrent.Text, _txtNew.Text);
            MessageBox.Show("Đã đổi mật khẩu thành công.", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// ProfileDialog — "Tùy chỉnh tài khoản": change display name + avatar image
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class ProfileDialog : Form
{
    private readonly AccountingStore _store;
    private readonly AppUser _user;

    private readonly TextBox _txtFullName = new() { Font = AppTheme.F10 };
    private readonly Panel _avatarPreview = new() { Size = new Size(96, 96), BackColor = Color.Transparent };

    private Image? _preview;
    private string? _pickedPath;   // non-null → a new image was chosen
    private bool _removed;         // true → avatar should be deleted

    public bool ProfileChanged { get; private set; }

    public ProfileDialog(AccountingStore store, AppUser user)
    {
        _store = store;
        _user = user;
        _preview = AvatarStore.Load(user.Username);

        Text = "Tùy chỉnh tài khoản";
        Size = new Size(460, 340);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12) };

        // Avatar preview (circular) + buttons
        _avatarPreview.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, _avatarPreview.Width - 1, _avatarPreview.Height - 1);
            if (_preview != null)
            {
                AvatarStore.DrawCircular(g, _preview, rect);
                using var ring = new Pen(AppTheme.Border, 1f);
                g.DrawEllipse(ring, rect);
            }
            else
            {
                using var bg = new SolidBrush(AppTheme.SidebarActive);
                g.FillEllipse(bg, rect);
                string initials = TextUtil.Initials(_user.DisplayName);
                TextRenderer.DrawText(g, initials, AppTheme.F18B, rect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        };

        var avatarWrap = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.Transparent };
        _avatarPreview.Location = new Point(0, 6);
        avatarWrap.Controls.Add(_avatarPreview);

        var btnChoose = new RoundedButton { Text = "Chọn ảnh...", Width = 120, Height = 32, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border, Location = new Point(112, 18) };
        var btnRemove = new RoundedButton { Text = "Xóa ảnh", Width = 100, Height = 32, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.Danger, BorderColor = AppTheme.Border, Location = new Point(112, 58) };
        btnChoose.Click += (s, e) => ChooseImage();
        btnRemove.Click += (s, e) =>
        {
            _preview?.Dispose();
            _preview = null;
            _pickedPath = null;
            _removed = true;
            _avatarPreview.Invalidate();
        };
        avatarWrap.Controls.Add(btnChoose);
        avatarWrap.Controls.Add(btnRemove);

        // Name field
        var nameWrap = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0) };
        var nameLbl = new Label { Text = "Tên hiển thị", Font = AppTheme.F9B, ForeColor = AppTheme.TextSecondary, Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
        _txtFullName.Text = _user.FullName;
        _txtFullName.Dock = DockStyle.Top;
        _txtFullName.Height = 30;
        nameWrap.Controls.Add(_txtFullName);
        nameWrap.Controls.Add(nameLbl);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var btnCancel = new RoundedButton { Text = "Hủy", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnSave = new RoundedButton { Text = "Lưu", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.Click += (s, e) => Save();
        btnFlow.Controls.Add(btnCancel);
        btnFlow.Controls.Add(btnSave);

        main.Controls.Add(nameWrap);
        main.Controls.Add(avatarWrap);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private void ChooseImage()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Chọn ảnh đại diện",
            Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            byte[] bytes = File.ReadAllBytes(ofd.FileName);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            _preview?.Dispose();
            _preview = new Bitmap(img);
            _pickedPath = ofd.FileName;
            _removed = false;
            _avatarPreview.Invalidate();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không đọc được ảnh: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Save()
    {
        try
        {
            string newName = _txtFullName.Text.Trim();
            if (!string.Equals(newName, _user.FullName, StringComparison.Ordinal))
            {
                _store.UpdateCurrentUserProfile(newName, "", "");
                ProfileChanged = true;
            }

            if (_pickedPath != null)
            {
                AvatarStore.Save(_user.Username, _pickedPath);
                ProfileChanged = true;
            }
            else if (_removed)
            {
                AvatarStore.Delete(_user.Username);
                ProfileChanged = true;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _preview?.Dispose();
        base.OnFormClosed(e);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// PasswordResetRequestsDialog — admin reviews pending "forgot password" requests
// and generates a one-time reset code for the chosen user
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class PasswordResetRequestsDialog : Form
{
    private readonly AccountingStore _store;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };

    public PasswordResetRequestsDialog(AccountingStore store)
    {
        _store = store;

        Text = "Yêu cầu đổi mật khẩu";
        Size = new Size(620, 420);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Text = "Chọn một yêu cầu rồi bấm \"Cấp mã đổi mật khẩu\". Mã có hiệu lực 15 phút, gửi cho người dùng để họ tự đổi mật khẩu ở màn hình đăng nhập.",
            ForeColor = AppTheme.TextSecondary,
            Padding = new Padding(16, 8, 16, 0),
            BackColor = Color.Transparent
        };

        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 8), BackColor = AppTheme.Background };
        AppTheme.StyleGrid(_grid);
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Username", HeaderText = "Tên đăng nhập", Width = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FullName", HeaderText = "Họ tên", Width = 200 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "RequestedAt", HeaderText = "Thời gian yêu cầu", Width = 170 });
        gridWrap.Controls.Add(_grid);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 9, 16, 9), BackColor = Color.Transparent };
        var btnClose = new RoundedButton { Text = "Đóng", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnGen = new RoundedButton { Text = "🔑 Cấp mã đổi mật khẩu", Width = 200, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnGen.Click += (s, e) => GenerateCode();
        btnFlow.Controls.Add(btnClose);
        btnFlow.Controls.Add(btnGen);

        Controls.Add(gridWrap);
        Controls.Add(btnFlow);
        Controls.Add(hint);

        Load += (s, e) => Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        try
        {
            foreach (var r in _store.GetPendingPasswordResetRequests())
                _grid.Rows.Add(r.Username, r.FullName, r.RequestedAt.ToString("dd/MM/yyyy HH:mm"));
            if (_grid.Rows.Count == 0)
            {
                _grid.Rows.Add("(Không có yêu cầu nào)", "", "");
                _grid.Rows[0].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GenerateCode()
    {
        if (_grid.SelectedRows.Count == 0) { MessageBox.Show("Hãy chọn một yêu cầu.", "Chưa chọn"); return; }
        string uname = _grid.SelectedRows[0].Cells["Username"].Value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(uname) || uname.StartsWith("(")) return;

        try
        {
            var user = _store.GetUsers().FirstOrDefault(u => u.Username == uname);
            if (user == null) { MessageBox.Show("Không tìm thấy tài khoản.", "Lỗi"); return; }
            var code = _store.AdminCreatePasswordResetCode(user.Id);
            CodeDisplayDialog.Show(this, uname, code);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// CodeDisplayDialog — shows a generated code in a copyable field
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class CodeDisplayDialog : Form
{
    private CodeDisplayDialog(string username, RegistrationCode code)
    {
        Text = "Mã đổi mật khẩu";
        Size = new Size(420, 250);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 14) };

        var lbl = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Text = $"Mã đổi mật khẩu cho tài khoản \"{username}\".\nGửi mã này cho người dùng (hết hạn lúc {code.ExpiresAt:HH:mm}).",
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent
        };

        var txtCode = new TextBox
        {
            Dock = DockStyle.Top,
            Font = AppTheme.F18B,
            Text = code.Code,
            ReadOnly = true,
            TextAlign = HorizontalAlignment.Center,
            Height = 44
        };

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        var btnOk = new RoundedButton { Text = "Xong", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnCopy = new RoundedButton { Text = "📋 Sao chép", Width = 120, Height = 34, CornerRadius = 8, BackColor = AppTheme.Accent, ForeColor = Color.White };
        btnOk.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        btnCopy.Click += (s, e) =>
        {
            try { Clipboard.SetText(code.Code); btnCopy.Text = "✓ Đã chép"; } catch { }
        };
        btnFlow.Controls.Add(btnOk);
        btnFlow.Controls.Add(btnCopy);

        main.Controls.Add(txtCode);
        main.Controls.Add(lbl);
        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    public static void Show(IWin32Window owner, string username, RegistrationCode code)
    {
        using var dlg = new CodeDisplayDialog(username, code);
        dlg.ShowDialog(owner);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// OvertimeRequestDialog — employee "Chấm công" (punch in) for overtime.
// The punch alone does NOT count; admin must approve. Once approved the shift-card
// stopwatch counts from the punch time (even if approved the next day).
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class OvertimeRequestDialog : Form
{
    private readonly AccountingStore _store;

    public OvertimeRequestDialog(AccountingStore store)
    {
        _store = store;

        Text = "Chấm công tăng ca";
        Size = new Size(470, 300);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var today = DateOnly.FromDateTime(DateTime.Now);
        WorkAccessRequest? req = null;
        try { req = _store.GetWorkAccessForToday(today); } catch { }

        var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12) };
        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 9, 16, 9), BackColor = Color.Transparent };
        var btnClose = new RoundedButton { Text = "Đóng", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnFlow.Controls.Add(btnClose);

        string info;
        if (req != null && req.IsApproved)
        {
            string baseInfo = req.PunchAt != null ? $"từ lúc chấm công {req.PunchAt:HH:mm}" : $"từ lúc duyệt {req.ApprovedAt:HH:mm}";
            info = $"✓ Tăng ca đã được duyệt.\n\nĐồng hồ trên thẻ \"Ca làm việc\" đang đếm {baseInfo}.";
        }
        else if (req != null && req.PunchAt != null)
        {
            info = $"Đã chấm công lúc {req.PunchAt:HH:mm} ({DateTime.Now:dd/MM/yyyy}).\n\nĐang chờ admin duyệt — khi được duyệt, giờ tăng ca tính TỪ LÚC CHẤM CÔNG (kể cả admin duyệt hôm sau).";
        }
        else
        {
            info = $"Hiện đang ngoài giờ làm việc ({DateTime.Now:HH:mm}).\n\nBấm \"Chấm công\" để bắt đầu ca tăng ca. Giờ tăng ca chỉ được tính sau khi admin duyệt (tính từ lúc bạn chấm công).";
            var btnPunch = new RoundedButton { Text = "🕐 Chấm công", Width = 150, Height = 34, CornerRadius = 8, BackColor = AppTheme.Success, ForeColor = Color.White };
            btnPunch.Click += (s, e) => Punch(today);
            btnFlow.Controls.Add(btnPunch);
        }

        main.Controls.Add(new Label { Dock = DockStyle.Fill, Text = info, ForeColor = AppTheme.TextPrimary, BackColor = Color.Transparent });

        Controls.Add(btnFlow);
        Controls.Add(main);
    }

    private void Punch(DateOnly today)
    {
        try
        {
            _store.CreateOrGetWorkAccessRequest(DateTime.Now, "Chấm công tăng ca");
            _store.PunchWorkAccess(today);
            MessageBox.Show(
                $"Đã chấm công lúc {DateTime.Now:HH:mm}.\nChờ admin duyệt để được tính tăng ca (tính từ thời điểm này).",
                "Đã chấm công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// WorkAccessRequestsDialog — admin reviews & approves pending overtime requests
// ═════════════════════════════════════════════════════════════════════════════
internal sealed class WorkAccessRequestsDialog : Form
{
    private readonly AccountingStore _store;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill };

    public WorkAccessRequestsDialog(AccountingStore store)
    {
        _store = store;

        Text = "Duyệt tăng ca (ngoài giờ)";
        Size = new Size(700, 430);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        MinimizeBox = false;

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Text = "Chọn yêu cầu rồi bấm \"Duyệt\" để cho phép nhân viên tăng ca ngoài giờ. Trạng thái card của họ sẽ chuyển sang \"Tăng ca\".",
            ForeColor = AppTheme.TextSecondary,
            Padding = new Padding(16, 8, 16, 0),
            BackColor = Color.Transparent
        };

        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 8), BackColor = AppTheme.Background };
        AppTheme.StyleGrid(_grid);
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Username", HeaderText = "Tên đăng nhập", Width = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FullName", HeaderText = "Họ tên", Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "WorkDate", HeaderText = "Ngày", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "Lý do", Width = 230 });
        gridWrap.Controls.Add(_grid);

        var btnFlow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 9, 16, 9), BackColor = Color.Transparent };
        var btnClose = new RoundedButton { Text = "Đóng", Width = 90, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        var btnApprove = new RoundedButton { Text = "✓ Duyệt", Width = 110, Height = 34, CornerRadius = 8, BackColor = AppTheme.Success, ForeColor = Color.White };
        var btnApproveAll = new RoundedButton { Text = "Duyệt tất cả", Width = 120, Height = 34, CornerRadius = 8, BackColor = AppTheme.SurfaceAlt, ForeColor = AppTheme.TextPrimary, BorderColor = AppTheme.Border };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnApprove.Click += (s, e) => ApproveSelected();
        btnApproveAll.Click += (s, e) => ApproveAll();
        btnFlow.Controls.Add(btnClose);
        btnFlow.Controls.Add(btnApprove);
        btnFlow.Controls.Add(btnApproveAll);

        Controls.Add(gridWrap);
        Controls.Add(btnFlow);
        Controls.Add(hint);

        Load += (s, e) => Reload();
    }

    private void Reload()
    {
        _grid.Rows.Clear();
        try
        {
            foreach (var r in _store.GetPendingWorkAccessRequests())
            {
                int row = _grid.Rows.Add(r.Username, r.FullName, r.WorkDate.ToString("dd/MM/yyyy"), r.Reason);
                _grid.Rows[row].Tag = r.Id;
            }
            if (_grid.Rows.Count == 0)
            {
                int row = _grid.Rows.Add("(Không có yêu cầu nào)", "", "", "");
                _grid.Rows[row].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApproveSelected()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not long id)
        {
            MessageBox.Show("Hãy chọn một yêu cầu.", "Chưa chọn");
            return;
        }
        try { _store.ApproveWorkAccessRequests(new[] { id }); Reload(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ApproveAll()
    {
        var ids = new List<long>();
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.Tag is long id) ids.Add(id);
        if (ids.Count == 0) return;
        try { _store.ApproveWorkAccessRequests(ids); Reload(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
