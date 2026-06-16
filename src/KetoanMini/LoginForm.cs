using System.Drawing.Drawing2D;

namespace KetoanMini;

// ============================================================================
// LoginForm  — dark navy login / register screen
// ============================================================================
public sealed class LoginForm : Form
{
    // ─────────────────────────────────────────────────────────────────
    // COLORS
    // ─────────────────────────────────────────────────────────────────
    private static readonly Color NavyTop     = Color.FromArgb(15,  23,  42);   // #0F172A
    private static readonly Color NavyBottom  = Color.FromArgb(30,  58,  95);   // #1E3A5F
    private static readonly Color AccentBlue  = Color.FromArgb(37,  99,  235);  // #2563EB
    private static readonly Color CardBg      = Color.White;
    private static readonly Color TextDark    = Color.FromArgb(15,  23,  42);
    private static readonly Color TextMid     = Color.FromArgb(100, 116, 139);
    private static readonly Color TextLight   = Color.FromArgb(148, 163, 184);
    private static readonly Color ErrorRed    = Color.FromArgb(239, 68,  68);

    // ─────────────────────────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────────────────────────
    private readonly AccountingStore _store;
    private int _activeTab = 0;   // 0 = Đăng nhập, 1 = Đăng ký

    // ─────────────────────────────────────────────────────────────────
    // PUBLIC RESULT
    // ─────────────────────────────────────────────────────────────────
    public AppUser? AuthenticatedUser { get; private set; }

    // ─────────────────────────────────────────────────────────────────
    // CONTROLS — LOGIN TAB
    // ─────────────────────────────────────────────────────────────────
    private TextBox       _txtUsername     = null!;
    private TextBox       _txtPassword     = null!;
    private Panel         _pnlUserWrap     = null!;
    private Panel         _pnlPassWrap     = null!;
    private CheckBox      _chkShowPass     = null!;
    private LinkLabel     _lnkForgot       = null!;
    private RoundedButton _btnLogin        = null!;
    private LinkLabel     _lnkToRegister   = null!;
    private Label         _lblLoginError   = null!;
    private Panel         _pnlLoginContent = null!;

    // ─────────────────────────────────────────────────────────────────
    // CONTROLS — REGISTER TAB
    // ─────────────────────────────────────────────────────────────────
    private TextBox       _txtRegUser      = null!;
    private TextBox       _txtRegFullName  = null!;
    private TextBox       _txtRegPass      = null!;
    private TextBox       _txtRegConfirm   = null!;
    private TextBox       _txtRegCode      = null!;
    private Panel         _pnlRegConfWrap  = null!;
    private RoundedButton _btnRegister     = null!;
    private Label         _lblRegError     = null!;
    private Panel         _pnlRegContent   = null!;

    // ─────────────────────────────────────────────────────────────────
    // LAYOUT
    // ─────────────────────────────────────────────────────────────────
    private RoundedPanel _card      = null!;
    private Panel        _pnlHeader = null!;
    private LoginTabBar  _tabBar    = null!;
    private Panel        _pnlContent = null!;

    // ─────────────────────────────────────────────────────────────────
    // CONSTRUCTOR
    // ─────────────────────────────────────────────────────────────────
    public LoginForm(AccountingStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        InitializeComponent();
    }

    // ─────────────────────────────────────────────────────────────────
    // FORM PAINT — gradient background
    // ─────────────────────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var brush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(0, ClientSize.Height),
            NavyTop,
            NavyBottom);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        // Small footer credit at the bottom-right, drawn over the gradient so it stays crisp.
        const string footer = "Powered by Codex and Claude";
        using var footerFont  = new Font("Segoe UI", 8F);
        using var footerBrush = new SolidBrush(TextLight);
        var size = e.Graphics.MeasureString(footer, footerFont);
        e.Graphics.DrawString(
            footer, footerFont, footerBrush,
            ClientSize.Width  - size.Width  - 16f,
            ClientSize.Height - size.Height - 10f);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Suppress default background so OnPaint gradient is clean
        // (only works because DoubleBuffered=true)
    }

    // ─────────────────────────────────────────────────────────────────
    // BUILD UI
    // ─────────────────────────────────────────────────────────────────
    private void InitializeComponent()
    {
        // ── Form properties ─────────────────────────────────────────
        Text            = "Đăng nhập - Công ty TNHH Inox Cường Phát";
        Size            = new Size(520, 600);
        MinimumSize     = new Size(520, 600);
        MaximumSize     = new Size(520, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = NavyTop;
        DoubleBuffered  = true;
        Font            = new Font("Segoe UI", 9.5F);

        // ── Card ────────────────────────────────────────────────────
        _card = new RoundedPanel
        {
            Size         = new Size(460, 510),
            FillColor    = CardBg,
            CornerRadius = 16,
            ShadowDepth  = 4
        };
        CenterCard();
        Resize += (_, _) => CenterCard();

        // ── Header (navy band, 80px) ─────────────────────────────────
        _pnlHeader = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 80,
            BackColor = NavyTop
        };
        BuildHeader(_pnlHeader);

        // ── Tab bar ──────────────────────────────────────────────────
        _tabBar = new LoginTabBar(new[] { "Đăng nhập", "Đăng ký" })
        {
            Dock        = DockStyle.Top,
            Height      = 44,
            BackColor   = CardBg,
            ActiveIndex = 0
        };
        _tabBar.TabChanged += idx =>
        {
            _activeTab = idx;
            ShowActiveTab();
        };

        // ── Content area ─────────────────────────────────────────────
        _pnlContent = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = CardBg,
            Padding   = new Padding(32, 16, 32, 16)
        };

        BuildLoginPanel();
        BuildRegisterPanel();

        _pnlContent.Controls.Add(_pnlRegContent);
        _pnlContent.Controls.Add(_pnlLoginContent);

        _card.Controls.Add(_pnlContent);
        _card.Controls.Add(_tabBar);
        _card.Controls.Add(_pnlHeader);

        Controls.Add(_card);

        ShowActiveTab();

        KeyPreview = true;
        KeyDown   += OnFormKeyDown;
    }

    private void CenterCard()
    {
        _card.Location = new Point(
            (ClientSize.Width  - _card.Width)  / 2,
            (ClientSize.Height - _card.Height) / 2);
    }

    // ─────────────────────────────────────────────────────────────────
    // HEADER
    // ─────────────────────────────────────────────────────────────────
    private void BuildHeader(Panel parent)
    {
        var logo = new LoginLogoCircle
        {
            Size     = new Size(44, 44),
            Location = new Point(20, 18),
            Text     = "CP"
        };

        var lblCompany = new Label
        {
            Text      = "Công ty TNHH Inox Cường Phát",
            Font      = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
            Size      = new Size(370, 22),
            Location  = new Point(72, 16),
            BackColor = Color.Transparent
        };

        var lblSub = new Label
        {
            Text      = "Hệ thống quản lý kế toán",
            Font      = new Font("Segoe UI", 8.5F),
            ForeColor = TextLight,
            AutoSize  = false,
            Size      = new Size(370, 18),
            Location  = new Point(72, 40),
            BackColor = Color.Transparent
        };

        parent.Controls.AddRange(new Control[] { logo, lblCompany, lblSub });
    }

    // ─────────────────────────────────────────────────────────────────
    // LOGIN PANEL
    // ─────────────────────────────────────────────────────────────────
    private void BuildLoginPanel()
    {
        _pnlLoginContent = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = CardBg,
            Visible   = true
        };

        int y = 4;

        // Tài khoản
        _pnlLoginContent.Controls.Add(MakeFieldLabel("Tài khoản", y));
        y += 22;

        _txtUsername = MakeTb(tabIdx: 0);
        ApplyPlaceholder(_txtUsername, "Nhập tài khoản");
        _pnlUserWrap          = WrapInput(_txtUsername);
        _pnlUserWrap.Location = new Point(0, y);
        _pnlUserWrap.TabIndex = 0;   // Tab order: account → password (not the links)
        _pnlLoginContent.Controls.Add(_pnlUserWrap);
        y += _pnlUserWrap.Height + 12;

        // Mật khẩu
        _pnlLoginContent.Controls.Add(MakeFieldLabel("Mật khẩu", y));
        y += 22;

        _txtPassword             = MakeTb(tabIdx: 0);
        _txtPassword.PasswordChar = '●';
        ApplyPlaceholder(_txtPassword, "Nhập mật khẩu");
        _pnlPassWrap          = WrapInput(_txtPassword);
        _pnlPassWrap.Location = new Point(0, y);
        _pnlPassWrap.TabIndex = 1;
        _pnlLoginContent.Controls.Add(_pnlPassWrap);
        y += _pnlPassWrap.Height + 8;

        // Hiện mật khẩu
        _chkShowPass = new CheckBox
        {
            Text      = "Hiện mật khẩu",
            Font      = AppTheme.F9,
            ForeColor = TextMid,
            BackColor = CardBg,
            AutoSize  = true,
            Location  = new Point(0, y),
            TabIndex  = 2
        };
        _chkShowPass.CheckedChanged += (_, _) =>
            _txtPassword.PasswordChar = _chkShowPass.Checked ? '\0' : '●';

        // Quên mật khẩu
        _lnkForgot = new LinkLabel
        {
            Text      = "Quên mật khẩu?",
            Font      = AppTheme.F9,
            LinkColor = AccentBlue,
            AutoSize  = true,
            TabIndex  = 3
        };
        _lnkForgot.LinkClicked += (_, _) => ShowForgotPassword();

        _pnlLoginContent.Controls.Add(_chkShowPass);
        _pnlLoginContent.Controls.Add(_lnkForgot);

        // Right-align forgot link
        _pnlLoginContent.Layout += (_, _) =>
        {
            _lnkForgot.Location = new Point(
                _pnlLoginContent.ClientSize.Width - _lnkForgot.Width - 2,
                _chkShowPass.Top + (_chkShowPass.Height - _lnkForgot.Height) / 2);
        };

        y += Math.Max(_chkShowPass.Height, 20) + 14;

        // Error label
        _lblLoginError = MakeErrorLabel(y);
        _pnlLoginContent.Controls.Add(_lblLoginError);
        y += 20;

        // Đăng nhập button
        _btnLogin = new RoundedButton
        {
            Text         = "Đăng nhập",
            Font         = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor    = Color.White,
            BackColor    = AccentBlue,
            CornerRadius = 8,
            Size         = new Size(396, 42),
            Location     = new Point(0, y),
            TabIndex     = 4
        };
        _btnLogin.Click += (_, _) => Login();
        _pnlLoginContent.Controls.Add(_btnLogin);
        y += 52;

        // Link to register tab
        _lnkToRegister = new LinkLabel
        {
            Text      = "Chưa có tài khoản? Chuyển sang tab Đăng ký",
            Font      = AppTheme.F9,
            LinkColor = AccentBlue,
            AutoSize  = true,
            Location  = new Point(0, y),
            TabIndex  = 5
        };
        _lnkToRegister.LinkClicked += (_, _) =>
        {
            _tabBar.ActiveIndex = 1;
            _activeTab = 1;
            ShowActiveTab();
        };
        _pnlLoginContent.Controls.Add(_lnkToRegister);
    }

    // ─────────────────────────────────────────────────────────────────
    // REGISTER PANEL
    // ─────────────────────────────────────────────────────────────────
    private void BuildRegisterPanel()
    {
        _pnlRegContent = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = CardBg,
            Visible   = false,
            AutoScroll = true   // scroll vertically so the "Đăng ký" button is always reachable
        };

        int y   = 4;
        int tab = 10;

        // Tài khoản
        _pnlRegContent.Controls.Add(MakeFieldLabel("Tài khoản", y)); y += 22;
        _txtRegUser = MakeTb(tab++);
        ApplyPlaceholder(_txtRegUser, "Nhập tên đăng nhập");
        var wUser = WrapInput(_txtRegUser); wUser.Location = new Point(0, y);
        _pnlRegContent.Controls.Add(wUser); y += wUser.Height + 10;

        // Họ và tên
        _pnlRegContent.Controls.Add(MakeFieldLabel("Họ và tên", y)); y += 22;
        _txtRegFullName = MakeTb(tab++);
        ApplyPlaceholder(_txtRegFullName, "Nhập họ và tên đầy đủ");
        var wFull = WrapInput(_txtRegFullName); wFull.Location = new Point(0, y);
        _pnlRegContent.Controls.Add(wFull); y += wFull.Height + 10;

        // Mật khẩu
        _pnlRegContent.Controls.Add(MakeFieldLabel("Mật khẩu", y)); y += 22;
        _txtRegPass              = MakeTb(tab++);
        _txtRegPass.PasswordChar  = '●';
        ApplyPlaceholder(_txtRegPass, "Nhập mật khẩu");
        var wPass = WrapInput(_txtRegPass); wPass.Location = new Point(0, y);
        _pnlRegContent.Controls.Add(wPass); y += wPass.Height + 10;

        // Xác nhận mật khẩu
        _pnlRegContent.Controls.Add(MakeFieldLabel("Xác nhận mật khẩu", y)); y += 22;
        _txtRegConfirm              = MakeTb(tab++);
        _txtRegConfirm.PasswordChar  = '●';
        ApplyPlaceholder(_txtRegConfirm, "Nhập lại mật khẩu");
        _pnlRegConfWrap          = WrapInput(_txtRegConfirm);
        _pnlRegConfWrap.Location = new Point(0, y);
        _pnlRegContent.Controls.Add(_pnlRegConfWrap); y += _pnlRegConfWrap.Height + 10;

        // Mã kích hoạt (tùy chọn)
        _pnlRegContent.Controls.Add(MakeFieldLabel("Mã kích hoạt (không bắt buộc)", y)); y += 22;
        _txtRegCode = MakeTb(tab++);
        ApplyPlaceholder(_txtRegCode, "Nhập mã kích hoạt nếu có");
        var wCode = WrapInput(_txtRegCode); wCode.Location = new Point(0, y);
        _pnlRegContent.Controls.Add(wCode); y += wCode.Height + 8;

        // Error label
        _lblRegError = MakeErrorLabel(y);
        _pnlRegContent.Controls.Add(_lblRegError); y += 20;

        // Đăng ký button
        _btnRegister = new RoundedButton
        {
            Text         = "Đăng ký",
            Font         = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor    = Color.White,
            BackColor    = AccentBlue,
            CornerRadius = 8,
            Size         = new Size(396, 42),
            Location     = new Point(0, y),
            TabIndex     = tab
        };
        _btnRegister.Click += (_, _) => Register();
        _pnlRegContent.Controls.Add(_btnRegister);

        // Bottom spacer so the button isn't flush against the scroll edge
        _pnlRegContent.Controls.Add(new Panel { Location = new Point(0, y + 46), Size = new Size(1, 8), BackColor = CardBg });

        // Anchor inputs + button + fixed-width error label to Left|Right so that, when
        // the vertical scrollbar appears, they shrink instead of triggering a horizontal one.
        foreach (Control c in _pnlRegContent.Controls)
            if (c is LoginInputWrapPanel || c is RoundedButton || (c is Label lab && !lab.AutoSize))
                c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    // ─────────────────────────────────────────────────────────────────
    // SHOW ACTIVE TAB
    // ─────────────────────────────────────────────────────────────────
    private void ShowActiveTab()
    {
        _pnlLoginContent.Visible = (_activeTab == 0);
        _pnlRegContent.Visible   = (_activeTab == 1);

        // Adjust card height for register's taller content
        int newH = (_activeTab == 0) ? 510 : 550;
        if (_card.Height != newH)
        {
            _card.Height = newH;
            CenterCard();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // AUTHENTICATION LOGIC
    // ─────────────────────────────────────────────────────────────────
    private void Login()
    {
        HideError(_lblLoginError);
        ResetWrapHighlight(_pnlUserWrap);
        ResetWrapHighlight(_pnlPassWrap);

        var username = GetNonPlaceholder(_txtUsername, "Nhập tài khoản");
        var password = GetNonPlaceholder(_txtPassword, "Nhập mật khẩu");

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError(_lblLoginError, "Vui lòng nhập tài khoản.");
            MarkError(_pnlUserWrap);
            _txtUsername.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError(_lblLoginError, "Vui lòng nhập mật khẩu.");
            MarkError(_pnlPassWrap);
            _txtPassword.Focus();
            return;
        }

        try
        {
            var user = _store.AuthenticateUser(username, password);
            if (user is null)
            {
                ShowError(_lblLoginError, "Tài khoản hoặc mật khẩu không đúng.");
                MarkError(_pnlUserWrap);
                MarkError(_pnlPassWrap);
                return;
            }

            if (user.IsPendingApproval)
            {
                MessageBox.Show(
                    "Tài khoản của bạn đang chờ admin phê duyệt.\nVui lòng liên hệ quản trị viên.",
                    "Chờ phê duyệt",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            AuthenticatedUser = user;
            DialogResult      = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            ShowError(_lblLoginError, ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(_lblLoginError, $"Lỗi hệ thống: {ex.Message}");
        }
    }

    private void Register()
    {
        HideError(_lblRegError);

        var username = GetNonPlaceholder(_txtRegUser,     "Nhập tên đăng nhập");
        var fullName = GetNonPlaceholder(_txtRegFullName, "Nhập họ và tên đầy đủ");
        var password = GetNonPlaceholder(_txtRegPass,     "Nhập mật khẩu");
        var confirm  = GetNonPlaceholder(_txtRegConfirm,  "Nhập lại mật khẩu");
        var code     = GetNonPlaceholder(_txtRegCode,     "Nhập mã kích hoạt nếu có");

        if (string.IsNullOrWhiteSpace(username))
        { ShowError(_lblRegError, "Vui lòng nhập tên đăng nhập."); _txtRegUser.Focus(); return; }

        if (string.IsNullOrWhiteSpace(fullName))
        { ShowError(_lblRegError, "Vui lòng nhập họ và tên."); _txtRegFullName.Focus(); return; }

        if (string.IsNullOrWhiteSpace(password))
        { ShowError(_lblRegError, "Vui lòng nhập mật khẩu."); _txtRegPass.Focus(); return; }

        if (password.Length < 6)
        { ShowError(_lblRegError, "Mật khẩu phải có ít nhất 6 ký tự."); _txtRegPass.Focus(); return; }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError(_lblRegError, "Xác nhận mật khẩu không khớp.");
            MarkError(_pnlRegConfWrap);
            _txtRegConfirm.Focus();
            return;
        }

        try
        {
            var user = _store.RegisterUser(username, fullName, password, code);

            string msg = user.IsPendingApproval
                ? $"Đăng ký thành công!\n\nTài khoản \"{username}\" đang chờ admin phê duyệt.\nBạn sẽ đăng nhập được sau khi được duyệt."
                : $"Đăng ký thành công!\n\nTài khoản \"{username}\" đã được kích hoạt.\nBạn có thể đăng nhập ngay.";

            MessageBox.Show(msg, "Đăng ký thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

            ClearRegistrationForm();
            _tabBar.ActiveIndex = 0;
            _activeTab          = 0;
            ShowActiveTab();

            // Pre-fill username in login tab for convenience
            SetTextBoxValue(_txtUsername, username);
            _txtPassword.Focus();
        }
        catch (InvalidOperationException ex)
        {
            ShowError(_lblRegError, ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(_lblRegError, $"Lỗi hệ thống: {ex.Message}");
        }
    }

    private void ShowForgotPassword()
    {
        using var dlg = new ForgotPasswordDialog(_store);
        dlg.ShowDialog(this);
    }

    // ─────────────────────────────────────────────────────────────────
    // KEYBOARD
    // ─────────────────────────────────────────────────────────────────
    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            if (_activeTab == 0) Login();
            else                 Register();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // FACTORY HELPERS
    // ─────────────────────────────────────────────────────────────────
    private Label MakeFieldLabel(string text, int y) => new Label
    {
        Text      = text,
        Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = TextDark,
        AutoSize  = true,
        Location  = new Point(0, y),
        BackColor = CardBg
    };

    private static TextBox MakeTb(int tabIdx) => new TextBox
    {
        BorderStyle = BorderStyle.None,
        Font        = AppTheme.F10,
        ForeColor   = Color.FromArgb(15, 23, 42),
        TabIndex    = tabIdx
    };

    /// <summary>Wraps a TextBox in an InputWrapPanel that draws a styled border.</summary>
    private Panel WrapInput(TextBox tb)
    {
        const int wrapW = 396, wrapH = 38, padX = 10;
        var wrap = new LoginInputWrapPanel
        {
            Size      = new Size(wrapW, wrapH),
            BackColor = Color.White,
            Padding   = new Padding(padX, 0, padX, 0)
        };
        wrap.Controls.Add(tb);

        // Single-line TextBoxes keep their font height, so position + vertically
        // centre the box manually instead of docking (Dock=Fill pins it to the top).
        int th = tb.PreferredHeight;
        tb.SetBounds(padX, (wrapH - th) / 2, wrapW - padX * 2, th);
        tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        tb.Enter += (_, _) => { ((LoginInputWrapPanel)tb.Parent!).Focused = true;  tb.Parent!.Invalidate(); };
        tb.Leave += (_, _) =>
        {
            ((LoginInputWrapPanel)tb.Parent!).Focused    = false;
            ((LoginInputWrapPanel)tb.Parent!).ErrorState = false;
            tb.Parent!.Invalidate();
        };

        return wrap;
    }

    private static Label MakeErrorLabel(int y) => new Label
    {
        Text      = "",
        Font      = AppTheme.F9,
        ForeColor = Color.FromArgb(239, 68, 68),
        BackColor = Color.White,
        AutoSize  = false,
        Size      = new Size(396, 18),
        Location  = new Point(0, y),
        Visible   = false,
        TabStop   = false
    };

    private void ApplyPlaceholder(TextBox tb, string placeholder)
    {
        tb.Text      = placeholder;
        tb.ForeColor = TextLight;

        tb.Enter += (_, _) =>
        {
            if (tb.Text == placeholder)
            {
                tb.Text      = "";
                tb.ForeColor = TextDark;
            }
        };

        tb.Leave += (_, _) =>
        {
            if (string.IsNullOrEmpty(tb.Text))
            {
                tb.Text        = placeholder;
                tb.ForeColor   = TextLight;
                // Restore PasswordChar to 0 on placeholder so the hint text shows
                tb.PasswordChar = '\0';
            }
        };
    }

    private static string GetNonPlaceholder(TextBox tb, string placeholder)
    {
        var v = tb.Text;
        return (v == placeholder) ? "" : v.Trim();
    }

    private static void SetTextBoxValue(TextBox tb, string value)
    {
        tb.Text      = value;
        tb.ForeColor = Color.FromArgb(15, 23, 42);
    }

    private static void ShowError(Label lbl, string msg)
    {
        lbl.Text    = msg;
        lbl.Visible = true;
    }

    private static void HideError(Label lbl)
    {
        lbl.Text    = "";
        lbl.Visible = false;
    }

    private static void MarkError(Panel wrap)
    {
        if (wrap is LoginInputWrapPanel iwp)
        {
            iwp.ErrorState = true;
            iwp.Invalidate();
        }
    }

    private static void ResetWrapHighlight(Panel wrap)
    {
        if (wrap is LoginInputWrapPanel iwp)
        {
            iwp.ErrorState = false;
            iwp.Invalidate();
        }
    }

    private void ClearRegistrationForm()
    {
        foreach (var (tb, ph, pc) in new (TextBox, string, char)[]
        {
            (_txtRegUser,     "Nhập tên đăng nhập",         '\0'),
            (_txtRegFullName, "Nhập họ và tên đầy đủ",      '\0'),
            (_txtRegPass,     "Nhập mật khẩu",               '\0'),
            (_txtRegConfirm,  "Nhập lại mật khẩu",           '\0'),
            (_txtRegCode,     "Nhập mã kích hoạt nếu có",    '\0'),
        })
        {
            tb.PasswordChar = pc;
            tb.Text         = ph;
            tb.ForeColor    = TextLight;
        }
        HideError(_lblRegError);
    }

    // ─────────────────────────────────────────────────────────────────
    // DISPOSE
    // ─────────────────────────────────────────────────────────────────
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _btnLogin?.Dispose();
            _btnRegister?.Dispose();
        }
        base.Dispose(disposing);
    }
}

// ==========================================================================
// LOGIN-LOCAL HELPER CONTROLS
// These are private to the login screen and do NOT conflict with AppControls.cs.
// ==========================================================================

/// <summary>
/// Input wrapper panel that draws a rounded, styled border.
/// Named LoginInputWrapPanel to avoid naming conflicts.
/// </summary>
internal sealed class LoginInputWrapPanel : Panel
{
    private static readonly Color BorderNormal = Color.FromArgb(203, 213, 225);
    private static readonly Color BorderFocus  = Color.FromArgb(37,  99,  235);
    private static readonly Color BorderError  = Color.FromArgb(239, 68,  68);

    public new bool Focused { get; set; }
    public bool ErrorState { get; set; }

    public LoginInputWrapPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer
               | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var borderColor = ErrorState ? BorderError
                        : Focused    ? BorderFocus
                                     : BorderNormal;

        float thickness = (ErrorState || Focused) ? 1.5f : 1f;
        using var pen = new Pen(borderColor, thickness);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = BuildPath(rect, 6);
        g.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Vertically centre child controls
        foreach (Control c in Controls)
            c.Top = (Height - c.Height) / 2;
    }

    private static GraphicsPath BuildPath(Rectangle r, int cr)
    {
        var path = new GraphicsPath();
        int d = cr * 2;
        path.AddArc(r.X,         r.Y,          d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        path.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Custom-drawn two-tab bar with blue underline for active tab.
/// Named LoginTabBar to avoid conflicts.
/// </summary>
internal sealed class LoginTabBar : Panel
{
    private readonly string[] _tabs;
    private int _activeIndex;
    private int _hoverIndex = -1;

    private static readonly Color TabActive  = Color.FromArgb(37,  99,  235);
    private static readonly Color TabText    = Color.FromArgb(100, 116, 139);
    private static readonly Color TabTextAct = Color.FromArgb(37,  99,  235);
    private static readonly Color Divider    = Color.FromArgb(226, 232, 240);

    public event Action<int>? TabChanged;

    public int ActiveIndex
    {
        get => _activeIndex;
        set { _activeIndex = value; Invalidate(); }
    }

    public LoginTabBar(string[] tabs)
    {
        _tabs = tabs;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.AllPaintingInWmPaint, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.X);
        if (hit != _hoverIndex) { _hoverIndex = hit; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int hit = HitTest(e.X);
        if (hit >= 0 && hit != _activeIndex)
        {
            _activeIndex = hit;
            Invalidate();
            TabChanged?.Invoke(_activeIndex);
        }
    }

    private int HitTest(int x)
    {
        int tabW = Width / _tabs.Length;
        return Math.Clamp(x / tabW, 0, _tabs.Length - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Bottom divider line
        using (var pen = new Pen(Divider, 1))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        int tabW = Width / _tabs.Length;
        for (int i = 0; i < _tabs.Length; i++)
        {
            bool active = (i == _activeIndex);
            bool hover  = (i == _hoverIndex && !active);
            var  rect   = new Rectangle(i * tabW, 0, tabW, Height - 1);

            // Hover tint
            if (hover)
            {
                using var hb = new SolidBrush(Color.FromArgb(12, 37, 99, 235));
                g.FillRectangle(hb, rect);
            }

            // Tab label
            using var font = new Font("Segoe UI",
                                      active ? 9.5F : 9F,
                                      active ? FontStyle.Bold : FontStyle.Regular);
            using var sb   = new SolidBrush(active ? TabTextAct : TabText);
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(_tabs[i], font, sb, rect, sf);

            // Blue underline for active
            if (active)
            {
                using var pen = new Pen(TabActive, 2.5f);
                g.DrawLine(pen, rect.X + 14, Height - 2, rect.Right - 14, Height - 2);
            }
        }
    }
}

/// <summary>
/// Circular logo badge drawing initials.
/// Named LoginLogoCircle to avoid conflicts.
/// </summary>
internal sealed class LoginLogoCircle : Control
{
    private static readonly Color CircleBg   = Color.FromArgb(37,  99,  235);
    private static readonly Color CircleText = Color.White;

    public LoginLogoCircle()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.SupportsTransparentBackColor
               | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var sb = new SolidBrush(CircleBg))
            g.FillEllipse(sb, 0, 0, Width - 1, Height - 1);

        using var font = new Font("Segoe UI", 13F, FontStyle.Bold);
        using var tb   = new SolidBrush(CircleText);
        var sf = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(Text, font, tb, new RectangleF(0, 0, Width, Height), sf);
    }
}

// ==========================================================================
// FORGOT PASSWORD DIALOG
// ==========================================================================
internal sealed class ForgotPasswordDialog : Form
{
    private readonly AccountingStore _store;
    private TextBox _txtUsername = null!;
    private TextBox _txtCode     = null!;
    private TextBox _txtNewPass  = null!;
    private Label   _lblError    = null!;

    private static readonly Color NavyBg    = Color.FromArgb(15,  23,  42);
    private static readonly Color AccentBlue = Color.FromArgb(37,  99,  235);
    private static readonly Color ErrorRed   = Color.FromArgb(239, 68,  68);

    public ForgotPasswordDialog(AccountingStore store)
    {
        _store = store;
        Build();
    }

    private void Build()
    {
        Text            = "Quên mật khẩu";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        BackColor       = Color.White;
        Font            = new Font("Segoe UI", 9.5F);
        DoubleBuffered  = true;

        // ── Header band ──
        var header = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = NavyBg };
        header.Controls.Add(new Label
        {
            Text      = "Quên mật khẩu",
            Font      = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(24, 14),
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text      = "Lấy lại mật khẩu bằng mã do admin cấp",
            Font      = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize  = true,
            Location  = new Point(24, 41),
            BackColor = Color.Transparent
        });
        Controls.Add(header);

        // ── Content ── (manual layout: children are positioned at an explicit left
        // margin because Location-positioned controls do NOT respect Panel.Padding)
        var content = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.White
        };
        const int formW = 412, left = 28;
        const int innerW = formW - left * 2;   // equal left/right margins
        int y = 18;

        // Step 1 — request a reset code from the admin
        _txtUsername = AddInput(content, "Tài khoản", ref y, password: false, left, innerW);

        var btnRequest = new RoundedButton
        {
            Text         = "📨  Gửi yêu cầu cho admin",
            Font         = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor    = AccentBlue,
            BackColor    = Color.White,
            BorderColor  = AccentBlue,
            CornerRadius = 8,
            Size         = new Size(innerW, 38),
            Location     = new Point(left, y)
        };
        btnRequest.Click += DoRequest;
        content.Controls.Add(btnRequest);
        y += 38 + 16;

        // Divider + caption
        content.Controls.Add(new Panel { BackColor = Color.FromArgb(226, 232, 240), Size = new Size(innerW, 1), Location = new Point(left, y) });
        y += 12;
        content.Controls.Add(new Label
        {
            Text      = "Đã có mã từ admin? Nhập bên dưới để đặt lại:",
            Font      = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(100, 116, 139),
            AutoSize  = true,
            Location  = new Point(left, y),
            BackColor = Color.White
        });
        y += 26;

        // Step 2 — reset using the code
        _txtCode    = AddInput(content, "Mã đặt lại (admin cấp)", ref y, password: false, left, innerW);
        _txtNewPass = AddInput(content, "Mật khẩu mới", ref y, password: true, left, innerW);

        _lblError = new Label
        {
            Text      = "",
            Font      = new Font("Segoe UI", 8.5F),
            ForeColor = ErrorRed,
            BackColor = Color.White,
            AutoSize  = false,
            Size      = new Size(innerW, 18),
            Location  = new Point(left, y),
            Visible   = false
        };
        content.Controls.Add(_lblError); y += 22;

        var btnReset = new RoundedButton
        {
            Text         = "Đặt lại mật khẩu",
            Font         = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor    = Color.White,
            BackColor    = AccentBlue,
            CornerRadius = 8,
            Size         = new Size(innerW, 42),
            Location     = new Point(left, y)
        };
        btnReset.Click += DoReset;
        content.Controls.Add(btnReset);

        Controls.Add(content);
        content.BringToFront();

        // Size the window to fit the content exactly (balanced margins, no empty gap)
        ClientSize = new Size(formW, header.Height + y + btnReset.Height + 18);

        KeyPreview = true;
        KeyDown   += (_, e) => { if (e.KeyCode == Keys.Enter) { DoReset(null, EventArgs.Empty); e.Handled = true; } };
    }

    /// <summary>Adds a bold label + a rounded, vertically-centred input at the given left margin; advances y.</summary>
    private TextBox AddInput(Panel parent, string label, ref int y, bool password, int left, int width)
    {
        parent.Controls.Add(new Label
        {
            Text      = label,
            Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize  = true,
            Location  = new Point(left, y),
            BackColor = Color.White
        });
        y += 22;

        var tb = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10F), ForeColor = Color.FromArgb(15, 23, 42) };
        if (password) tb.PasswordChar = '●';

        var wrap = new LoginInputWrapPanel { Size = new Size(width, 38), Location = new Point(left, y), Padding = new Padding(10, 0, 10, 0) };
        wrap.Controls.Add(tb);
        int th = tb.PreferredHeight;
        tb.SetBounds(10, (38 - th) / 2, width - 20, th);
        tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        tb.Enter += (_, _) => { wrap.Focused = true;  wrap.Invalidate(); };
        tb.Leave += (_, _) => { wrap.Focused = false; wrap.ErrorState = false; wrap.Invalidate(); };

        parent.Controls.Add(wrap);
        y += 38 + 12;
        return tb;
    }

    private void DoRequest(object? sender, EventArgs e)
    {
        _lblError.Visible = false;
        var username = _txtUsername.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            _lblError.Text    = "Nhập tài khoản để gửi yêu cầu.";
            _lblError.Visible = true;
            return;
        }

        try
        {
            _store.CreatePasswordResetRequest(username);
            MessageBox.Show(
                "Đã gửi yêu cầu đặt lại mật khẩu.\n\nLiên hệ admin để nhận mã, sau đó nhập mã vào ô \"Mã đặt lại\" bên dưới.",
                "Đã gửi yêu cầu",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (InvalidOperationException ex)
        {
            _lblError.Text    = ex.Message;
            _lblError.Visible = true;
        }
        catch (Exception ex)
        {
            _lblError.Text    = $"Lỗi: {ex.Message}";
            _lblError.Visible = true;
        }
    }

    private void DoReset(object? sender, EventArgs e)
    {
        _lblError.Visible = false;

        var username = _txtUsername.Text.Trim();
        var code     = _txtCode.Text.Trim();
        var newPass  = _txtNewPass.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(newPass))
        {
            _lblError.Text    = "Vui lòng điền đầy đủ thông tin.";
            _lblError.Visible = true;
            return;
        }

        if (newPass.Length < 6)
        {
            _lblError.Text    = "Mật khẩu mới phải có ít nhất 6 ký tự.";
            _lblError.Visible = true;
            return;
        }

        try
        {
            _store.ResetPasswordWithCode(username, code, newPass);
            MessageBox.Show(
                "Đặt lại mật khẩu thành công!\nBạn có thể đăng nhập với mật khẩu mới.",
                "Thành công",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            _lblError.Text    = ex.Message;
            _lblError.Visible = true;
        }
        catch (Exception ex)
        {
            _lblError.Text    = $"Lỗi: {ex.Message}";
            _lblError.Visible = true;
        }
    }
}
