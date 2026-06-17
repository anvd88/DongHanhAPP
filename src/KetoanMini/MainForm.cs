using System.Drawing.Drawing2D;

namespace KetoanMini;

public sealed partial class MainForm : Form
{
    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly AccountingStore _store;
    private readonly GiaCongStore _giaCongStore;
    private readonly AppUser _currentUser;
    private readonly Dictionary<string, Control> _pages = new();
    private string _activeKey = "dashboard";
    private readonly List<SidebarNavButton> _navButtons = new();
    private Panel _contentPanel = new();
    public bool LogoutRequested { get; private set; }

    // Header user cluster state
    private Panel? _userPanel;    // header notif/avatar cluster (for repaint)
    private Image? _avatarImage;  // cached avatar for the current user
    private int _notifCount;      // pending approvals + reset requests (admin only)

    // Work-shift card state (real-time)
    private WorkShiftCard? _workCard;
    private System.Windows.Forms.Timer? _shiftTimer;
    private DateTime? _otApprovedAt; // cached: overtime stopwatch start (punch ?? approval), null = none
    private int  _shiftSeconds;     // tick counter (drives the overtime stopwatch)
    private static readonly TimeSpan WorkStart = new(8, 0, 0);
    private static readonly TimeSpan WorkEnd   = new(17, 0, 0);

    // Session state (single active login + presence)
    private string _sessionToken = "";
    private SessionControlService? _sessionControl; // instant LAN push (login-elsewhere / admin lock)
    private bool _forcedLogout;
    private bool _closeConfirmed;
    private Action? _nhanSuReload;       // full rebuild — only on app_users change events
    private Action? _nhanSuPresence;     // in-place online/minutes cell update (no row reset)
    private int _usersToken = int.MinValue; // last seen app_users change-signature

    // KeToan page state
    private DataGridView? _docGrid;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainForm(AccountingStore store, AppUser user)
    {
        _store = store;
        _currentUser = user;
        _giaCongStore = new GiaCongStore(store.DatabasePath);
        _giaCongStore.EnsureGiaCongTables();
        _avatarImage = AvatarStore.Load(user.Username);
        try { _sessionToken = _store.StartSession(Environment.MachineName); } catch { _sessionToken = ""; }

        // Event-driven session control: listen for LAN push signals and, the moment
        // we log in, tell any other live session of this user to log out immediately
        // (single login) instead of making it wait for the next DB heartbeat.
        _sessionControl = new SessionControlService();
        _sessionControl.ForceLogout += OnRemoteForceLogout;
        _sessionControl.Start(_currentUser.Username, _sessionToken);
        if (!string.IsNullOrEmpty(_sessionToken))
            _sessionControl.BroadcastLoginTakeover(_currentUser.Username, _sessionToken);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        InitializeComponent();
        RefreshNotifCount();
        Navigate("dashboard");

        // Real-time work-shift status: tick every second (overtime stopwatch),
        // re-check overtime approval from the DB every 30s.
        RefreshOvertimeFlag();
        UpdateWorkShift();
        _shiftTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _shiftTimer.Tick += (_, _) =>
        {
            _shiftSeconds++;
            if (_shiftSeconds % 15 == 0 && !CheckSessionAlive()) return; // forced logout handled inside
            if (_shiftSeconds % 30 == 0) RefreshOvertimeFlag();
            if (_currentUser.IsAdmin)
            {
                // Event-driven: reload the user list ONLY when app_users actually changes
                // (new registration / approval / lock / delete) — not on a fixed interval.
                if (_shiftSeconds % 4 == 0) CheckUsersChanged();
                // Presence (online + minutes) ticks in-place so the grid never "resets".
                if (_shiftSeconds % 20 == 0 && _activeKey == "nhansu") _nhanSuPresence?.Invoke();
            }
            UpdateWorkShift();
        };
        _shiftTimer.Start();
    }

    // ── Shell layout ──────────────────────────────────────────────────────────
    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "Công ty TNHH Inox Cường Phát";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1200, 700);
        BackColor = AppTheme.Background;
        Font = AppTheme.F9;
        StartPosition = FormStartPosition.WindowsDefaultBounds;

        // Root: 1 row × 2 cols
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var sidebar = BuildSidebar();
        sidebar.Dock = DockStyle.Fill;
        root.Controls.Add(sidebar, 0, 0);

        // MainArea: 2 rows
        var mainArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        mainArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        mainArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var header = BuildHeader();
        header.Dock = DockStyle.Fill;
        mainArea.Controls.Add(header, 0, 0);

        _contentPanel = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = Padding.Empty
        };
        mainArea.Controls.Add(_contentPanel, 0, 1);

        root.Controls.Add(mainArea, 1, 0);
        Controls.Add(root);

        ResumeLayout(false);
    }

    // ── NAVIGATE ──────────────────────────────────────────────────────────────
    private void Navigate(string key)
    {
        // Nhân sự + Cập nhật are admin-only; deflect non-admins back to the dashboard.
        if ((key == "nhansu" || key == "capnhat") && !_currentUser.IsAdmin)
            key = "dashboard";

        _activeKey = key;
        foreach (var btn in _navButtons)
            btn.IsActive = btn.NavKey == key;

        // Freeze the content area while building + swapping pages so the transition
        // is painted once (no flicker on first open of a page).
        SuspendDraw(_contentPanel);
        try
        {
            if (!_pages.TryGetValue(key, out var page))
            {
                page = key switch
                {
                    "dashboard" => BuildDashboardPage(),
                    "ketoan"    => BuildKeToanPage(),
                    "giacong"   => BuildGiaCongPage(),
                    "banhang"   => BuildBanHangPage(),
                    "nhansu"    => BuildNhanSuProPage(),
                    "baocao"    => BuildBaoCaoPage(),
                    "kho"       => BuildPlaceholderPage("Hàng tồn kho", "Quản lý hàng hóa trong kho"),
                    "muahang"   => BuildPlaceholderPage("Mua hàng", "Quản lý đơn mua hàng"),
                    "taisan"    => BuildPlaceholderPage("Tài sản cố định", "Quản lý tài sản"),
                    "danhmuc"   => BuildPlaceholderPage("Danh mục", "Danh mục hệ thống"),
                    "caidat"    => BuildPlaceholderPage("Cài đặt", "Cài đặt ứng dụng"),
                    "saoluu"    => BuildSaoLuuPage(),
                    "capnhat"   => BuildCapNhatPage(),
                    _           => BuildPlaceholderPage(key, "")
                };
                page.Dock = DockStyle.Fill;
                _pages[key] = page;
                _contentPanel.Controls.Add(page);
            }

            foreach (Control ctrl in _contentPanel.Controls)
            {
                ctrl.Visible = false;
            }

            page.Visible = true;
            page.BringToFront();
        }
        finally
        {
            ResumeDraw(_contentPanel);
            _contentPanel.Update();
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SIDEBAR
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildSidebar()
    {
        var sidebar = new Panel
        {
            BackColor = AppTheme.SidebarBg,
            Width = 215
        };
        sidebar.SuspendLayout();

        // Logo top
        var logoPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = AppTheme.SidebarBg,
            Padding = new Padding(12, 12, 12, 8)
        };
        logoPanel.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Blue circle badge
            var badgeRect = new Rectangle(12, 18, 36, 36);
            using var badgePath = RoundedPanel.RoundedRect(badgeRect, 18);
            using var badgeBrush = new SolidBrush(AppTheme.SidebarActive);
            g.FillPath(badgeBrush, badgePath);

            TextRenderer.DrawText(g, "CP", AppTheme.F9B, badgeRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);

            var nameRect = new Rectangle(54, 14, 148, 32);
            TextRenderer.DrawText(g, "Công ty TNHH\nInox Cường Phát", AppTheme.F9B, nameRect, Color.White,
                TextFormatFlags.Top | TextFormatFlags.WordBreak);

            var subRect = new Rectangle(54, 50, 148, 18);
            TextRenderer.DrawText(g, "Hệ thống quản lý kế toán", AppTheme.F8, subRect, AppTheme.SidebarText,
                TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        };

        // Bottom version bar
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BackColor = AppTheme.SidebarBg
        };
        bottomPanel.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Green dot
            using var dotBrush = new SolidBrush(AppTheme.Success);
            g.FillEllipse(dotBrush, 14, 14, 10, 10);
            var txtRect = new Rectangle(30, 0, 170, 40);
            TextRenderer.DrawText(g, $"Phiên bản {AppVersion.CurrentText}", AppTheme.F8, txtRect, AppTheme.SidebarText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        };

        // Nav area
        var navPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.SidebarBg,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 4)
        };

        var navItems = new List<Control>();

        void AddNavBtn(string key, string icon, string title)
        {
            var btn = new SidebarNavButton
            {
                NavKey = key,
                Icon = icon,
                Title = title,
                Dock = DockStyle.Top,
                Height = 40,
                Width = 215
            };
            btn.Click += (s, e) => Navigate(key);
            _navButtons.Add(btn);
            navItems.Add(btn);
        }

        void AddSection(string text)
        {
            var lbl = new Label
            {
                Text = text,
                Font = AppTheme.FSect,
                ForeColor = AppTheme.SidebarSection,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(16, 0, 0, 4),
                BackColor = AppTheme.SidebarBg
            };
            navItems.Add(lbl);
        }

        AddNavBtn("dashboard", "⊞", "Tổng quan");
        AddSection("NGHIỆP VỤ");
        AddNavBtn("ketoan",   "₫",  "Kế toán");
        AddNavBtn("kho",      "▦",  "Hàng tồn kho");
        AddNavBtn("banhang",  "⊕",  "Bán hàng");
        AddNavBtn("muahang",  "⊙",  "Mua hàng");
        AddNavBtn("giacong",  "⚙",  "Gia công");
        AddNavBtn("taisan",   "⬡",  "Tài sản cố định");
        AddSection("QUẢN LÝ");
        if (_currentUser.IsAdmin)
            AddNavBtn("nhansu",   "👤", "Nhân sự");
        AddNavBtn("baocao",   "📊", "Báo cáo");
        AddNavBtn("danhmuc",  "☰",  "Danh mục");
        AddSection("HỆ THỐNG");
        AddNavBtn("caidat",   "⚙",  "Cài đặt");
        AddNavBtn("saoluu",   "💾", "Sao lưu");
        if (_currentUser.IsAdmin)
            AddNavBtn("capnhat",  "⬆", "Cập nhật");

        // Add in reverse order because Dock=Top stacks bottom-up
        for (int i = navItems.Count - 1; i >= 0; i--)
            navPanel.Controls.Add(navItems[i]);

        sidebar.Controls.Add(navPanel);
        sidebar.Controls.Add(bottomPanel);
        sidebar.Controls.Add(logoPanel);
        sidebar.ResumeLayout(false);

        return sidebar;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HEADER
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildHeader()
    {
        var header = new Panel
        {
            BackColor = AppTheme.HeaderBg,
            Padding = new Padding(16, 0, 16, 0)
        };
        header.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            BackColor = Color.Transparent
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Col 0: Company name + subtitle
        var companyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 10) };
        var companyName = new Label
        {
            Text = "Công ty TNHH Inox Cường Phát",
            Font = AppTheme.F10B,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 22,
            AutoSize = false,
            TextAlign = ContentAlignment.BottomLeft
        };
        var companySub = new Label
        {
            Text = "Hệ thống kế toán doanh nghiệp",
            Font = AppTheme.F8,
            ForeColor = AppTheme.TextMuted,
            Dock = DockStyle.Top,
            Height = 18,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft
        };
        companyPanel.Controls.Add(companySub);
        companyPanel.Controls.Add(companyName);
        tbl.Controls.Add(companyPanel, 0, 0);

        // Col 1: SearchBox
        var searchWrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(8, 13, 8, 13) };
        var search = new SearchBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Nhập để tìm kiếm...",
            HintText = "Ctrl + K"
        };
        searchWrap.Controls.Add(search);
        tbl.Controls.Add(searchWrap, 1, 0);

        // Col 2: Work schedule card (navy card, real-time status, hover-animated border)
        var workWrap = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(8, 5, 8, 5) };
        var workCard = new WorkShiftCard
        {
            Dock = DockStyle.Fill,
            CornerRadius = 8
        };
        workCard.Click += (_, _) => OnWorkCardClick();
        _workCard = workCard;
        workWrap.Controls.Add(workCard);
        tbl.Controls.Add(workWrap, 2, 0);

        // Col 3: Period + notif + avatar (interactive)
        var userPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(4, 10, 4, 10), Cursor = Cursors.Hand };
        _userPanel = userPanel;

        // Shared geometry (used by both Paint and hit-testing)
        Rectangle NotifRect()  => new Rectangle(54, (userPanel.Height - 28) / 2, 28, 28);
        Rectangle AvatarRect() => new Rectangle(86, (userPanel.Height - 30) / 2, 30, 30);
        Rectangle UserHit()    => new Rectangle(86, 0, Math.Max(0, userPanel.Width - 86), userPanel.Height);

        userPanel.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Period label
            string period = DateTime.Now.ToString("MM/yyyy");
            TextRenderer.DrawText(g, period, AppTheme.F8, new Rectangle(0, 0, 50, userPanel.Height), AppTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            // Notif button
            var notifRect = NotifRect();
            using (var notifPath = RoundedPanel.RoundedRect(notifRect, 8))
            using (var notifBrush = new SolidBrush(AppTheme.SurfaceAlt))
            using (var notifPen = new Pen(AppTheme.Border, 1f))
            {
                g.FillPath(notifBrush, notifPath);
                g.DrawPath(notifPen, notifPath);
            }
            TextRenderer.DrawText(g, "🔔", AppTheme.F9, notifRect, AppTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);

            // Notif badge (count of pending items, admin only)
            if (_notifCount > 0)
            {
                string txt = _notifCount > 9 ? "9+" : _notifCount.ToString();
                var badge = new Rectangle(notifRect.Right - 14, notifRect.Top - 2, 16, 16);
                using (var bb = new SolidBrush(AppTheme.Danger))
                    g.FillEllipse(bb, badge);
                TextRenderer.DrawText(g, txt, AppTheme.FSect, badge, Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }

            // Avatar circle — image if available, otherwise initials
            var avatarRect = AvatarRect();
            if (_avatarImage != null)
            {
                AvatarStore.DrawCircular(g, _avatarImage, avatarRect);
            }
            else
            {
                using var avatarBrush = new SolidBrush(AppTheme.SidebarActive);
                g.FillEllipse(avatarBrush, avatarRect);
                string initials = TextUtil.Initials(_currentUser.DisplayName);
                TextRenderer.DrawText(g, initials, AppTheme.F8B, avatarRect, Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            }

            // Name label
            var nameRect = new Rectangle(120, 0, userPanel.Width - 120, userPanel.Height);
            if (nameRect.Width > 0)
            {
                string shortName = _currentUser.DisplayName.Split(' ').LastOrDefault() ?? _currentUser.DisplayName;
                TextRenderer.DrawText(g, shortName, AppTheme.F8B, nameRect, AppTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        };

        userPanel.MouseClick += (s, e) =>
        {
            if (NotifRect().Contains(e.Location)) { ShowNotificationsMenu(userPanel); return; }
            if (UserHit().Contains(e.Location))   { ShowUserMenu(userPanel); return; }
        };

        tbl.Controls.Add(userPanel, 3, 0);

        header.Controls.Add(tbl);
        return header;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HEADER ACTIONS — user menu, notifications, logout, profile
    // ═════════════════════════════════════════════════════════════════════════
    private void ShowUserMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { Font = AppTheme.F9 };
        menu.Items.Add(new ToolStripMenuItem($"{_currentUser.DisplayName}  ({_currentUser.Role})") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⚙  Tùy chỉnh tài khoản", null, (_, __) => OpenProfileDialog());
        menu.Items.Add("🔑  Đổi mật khẩu", null, (_, __) => OpenChangePasswordDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⎋  Đăng xuất", null, (_, __) => PerformLogout());
        menu.Show(anchor, new Point(anchor.Width - menu.Width + 4, anchor.Height - 6));
    }

    private void ShowNotificationsMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { Font = AppTheme.F9 };

        if (!_currentUser.IsAdmin)
        {
            menu.Items.Add(new ToolStripMenuItem("Không có thông báo mới") { Enabled = false });
            menu.Show(anchor, new Point(anchor.Width - menu.Width + 4, anchor.Height - 6));
            return;
        }

        try
        {
            var pendingUsers = _store.GetUsers().Where(u => u.IsPendingApproval && !u.IsAdmin).ToList();
            var resetReqs = _store.GetPendingPasswordResetRequests();
            var otReqs = _store.GetPendingWorkAccessRequests();

            menu.Items.Add(new ToolStripMenuItem("THÔNG BÁO") { Enabled = false, Font = AppTheme.FSect });

            if (pendingUsers.Count == 0 && resetReqs.Count == 0 && otReqs.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("Không có thông báo mới") { Enabled = false });
            }
            else
            {
                foreach (var u in pendingUsers)
                {
                    var item = new ToolStripMenuItem($"✓  Duyệt tài khoản: {u.Username}");
                    var uid = u.Id;
                    var uname = u.Username;
                    item.Click += (_, __) =>
                    {
                        try
                        {
                            _store.AdminApproveUser(uid);
                            MessageBox.Show($"Đã duyệt tài khoản \"{uname}\".", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            InvalidatePage("nhansu");
                            RefreshNotifCount();
                        }
                        catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi"); }
                    };
                    menu.Items.Add(item);
                }

                if (resetReqs.Count > 0)
                {
                    if (pendingUsers.Count > 0) menu.Items.Add(new ToolStripSeparator());
                    var open = new ToolStripMenuItem($"🔑  Yêu cầu đổi mật khẩu ({resetReqs.Count})");
                    open.Click += (_, __) => OpenResetRequestsDialog();
                    menu.Items.Add(open);
                }

                if (otReqs.Count > 0)
                {
                    if (pendingUsers.Count > 0 || resetReqs.Count > 0) menu.Items.Add(new ToolStripSeparator());
                    var openOt = new ToolStripMenuItem($"🕐  Yêu cầu tăng ca ({otReqs.Count})");
                    openOt.Click += (_, __) => OpenWorkAccessRequestsDialog();
                    menu.Items.Add(openOt);
                }
            }
        }
        catch (Exception ex)
        {
            menu.Items.Add(new ToolStripMenuItem("Lỗi tải thông báo: " + ex.Message) { Enabled = false });
        }

        menu.Show(anchor, new Point(anchor.Width - menu.Width + 4, anchor.Height - 6));
    }

    private void OpenProfileDialog()
    {
        using var dlg = new ProfileDialog(_store, _currentUser);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ProfileChanged)
        {
            _avatarImage?.Dispose();
            _avatarImage = AvatarStore.Load(_currentUser.Username);
            _userPanel?.Invalidate();
            InvalidatePage("nhansu");
        }
    }

    private void OpenChangePasswordDialog()
    {
        using var dlg = new ChangePasswordDialog(_store, _currentUser);
        dlg.ShowDialog(this);
    }

    private void OpenResetRequestsDialog()
    {
        using var dlg = new PasswordResetRequestsDialog(_store);
        dlg.ShowDialog(this);
        RefreshNotifCount();
    }

    private void PerformLogout()
    {
        if (MessageBox.Show("Đăng xuất khỏi tài khoản hiện tại?", "Đăng xuất",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        LogoutRequested = true;
        Close();
    }

    /// <summary>Recomputes the header notification badge (admin: pending approvals + reset requests).</summary>
    private void RefreshNotifCount()
    {
        int count = 0;
        if (_currentUser.IsAdmin)
        {
            try
            {
                count += _store.GetUsers().Count(u => u.IsPendingApproval && !u.IsAdmin);
                count += _store.GetPendingPasswordResetRequests().Count;
                count += _store.GetPendingWorkAccessRequests().Count;
            }
            catch { count = 0; }
        }
        _notifCount = count;
        _userPanel?.Invalidate();
    }

    /// <summary>Drops a cached page so it rebuilds with fresh data on next navigation.</summary>
    private void InvalidatePage(string key)
    {
        if (_pages.TryGetValue(key, out var page))
        {
            _contentPanel.Controls.Remove(page);
            page.Dispose();
            _pages.Remove(key);
            if (_activeKey == key) Navigate(key);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // WORK SHIFT (real-time)
    // ═════════════════════════════════════════════════════════════════════════
    private static readonly Color ShiftWorking = Color.FromArgb(52,  211, 153); // green
    private static readonly Color ShiftOff     = Color.FromArgb(248, 113, 113); // red
    private static readonly Color ShiftOvertime = Color.FromArgb(251, 191, 36); // amber
    private static readonly Color ShiftNeutral = Color.FromArgb(148, 163, 184); // gray

    /// <summary>Re-checks (and caches) the approval time of the current user's overtime for today.</summary>
    private void RefreshOvertimeFlag()
    {
        _otApprovedAt = null;
        if (_currentUser.IsAdmin) return;
        try
        {
            var req = _store.GetApprovedWorkAccess(DateOnly.FromDateTime(DateTime.Now));
            // Count from the punch ("chấm công") time if present, else from approval time.
            if (req != null) _otApprovedAt = req.PunchAt ?? req.ApprovedAt ?? req.RequestedAt;
        }
        catch { _otApprovedAt = null; }
    }

    /// <summary>Recomputes the work-shift status from the current time + cached overtime approval.</summary>
    private void UpdateWorkShift()
    {
        if (_workCard is null) return;

        var now = DateTime.Now;
        var t = now.TimeOfDay;
        bool isWorkTime = t >= WorkStart && t < WorkEnd;
        string time = $"{WorkStart:hh\\:mm} - {WorkEnd:hh\\:mm}";

        string status;
        Color color;

        if (isWorkTime)
        {
            status = "Đang làm việc";
            color  = ShiftWorking;
        }
        else if (_currentUser.IsAdmin)
        {
            status = "Ngoài giờ";          // admin unrestricted, no overtime concept
            color  = ShiftNeutral;
        }
        else if (_otApprovedAt is DateTime since)
        {
            status = "Tăng ca";
            color  = ShiftOvertime;
            time   = FormatOvertime(now, since);  // stopwatch counts from approval time
        }
        else
        {
            status = "Hết giờ";
            color  = ShiftOff;
        }

        _workCard.LabelText   = "Ca làm việc";
        _workCard.StatusText  = status;
        _workCard.StatusColor = color;
        _workCard.TimeText    = time;
    }

    /// <summary>Formats elapsed overtime (since the approval time) as a HH:MM:SS stopwatch.</summary>
    private static string FormatOvertime(DateTime now, DateTime since)
    {
        var elapsed = now - since;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void OnWorkCardClick()
    {
        // Admin → review/approve overtime requests; employee → register overtime.
        if (_currentUser.IsAdmin)
        {
            OpenWorkAccessRequestsDialog();
            return;
        }

        var now = DateTime.Now.TimeOfDay;
        bool isWorkTime = now >= WorkStart && now < WorkEnd;

        if (isWorkTime)
        {
            MessageBox.Show(
                $"Bạn đang trong ca làm việc ({WorkStart:hh\\:mm} - {WorkEnd:hh\\:mm}).",
                "Ca làm việc", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool overtimeApproved = false;
        try { overtimeApproved = _store.HasApprovedWorkAccess(DateOnly.FromDateTime(DateTime.Now)); }
        catch { }

        if (overtimeApproved)
        {
            MessageBox.Show(
                "Bạn đã được duyệt tăng ca cho hôm nay. Trạng thái: Tăng ca.",
                "Tăng ca", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Off-hours, not yet approved → offer to register overtime
        using var dlg = new OvertimeRequestDialog(_store);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            UpdateWorkShift();
    }

    private void OpenWorkAccessRequestsDialog()
    {
        using var dlg = new WorkAccessRequestsDialog(_store);
        dlg.ShowDialog(this);
        RefreshNotifCount();
        UpdateWorkShift();
    }

    /// <summary>Detects app_users changes (e.g. a new registration) and pushes them into the UI.
    /// Reloads only when the change-signature differs — never a blind periodic reset.</summary>
    private void CheckUsersChanged()
    {
        try
        {
            int token = _store.GetUsersChangeToken();
            if (_usersToken == int.MinValue) { _usersToken = token; return; } // baseline
            if (token == _usersToken) return;

            _usersToken = token;
            RefreshNotifCount();                               // bell badge reflects new pending items
            if (_activeKey == "nhansu") _nhanSuReload?.Invoke(); // push the new account into the list
        }
        catch { /* transient DB error — try again next tick */ }
    }

    /// <summary>Heartbeat fallback: if the session was ended elsewhere or the account was
    /// locked, force this client to log out. This is the safety net behind the instant
    /// LAN push (<see cref="OnRemoteForceLogout"/>) — it catches a missed UDP signal.</summary>
    private bool CheckSessionAlive()
    {
        if (string.IsNullOrEmpty(_sessionToken)) return true;
        AccountingStore.SessionStatus status;
        try { status = _store.CheckSession(_sessionToken); }
        catch { return true; } // transient DB error — don't kick the user out
        if (status == AccountingStore.SessionStatus.Alive) return true;

        var reason = status == AccountingStore.SessionStatus.AccountLocked
            ? "Tài khoản của bạn đã bị khoá."
            : "Tài khoản của bạn vừa đăng nhập ở một máy khác.\nPhiên làm việc tại đây đã kết thúc.";
        ForceLogoutNow(reason);
        return false;
    }

    /// <summary>Handles an instant LAN push signal (login elsewhere / admin lock). Raised on a
    /// background thread, so marshal the logout onto the UI thread.</summary>
    private void OnRemoteForceLogout(object? sender, SessionControlEventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => ForceLogoutNow(e.Reason))); }
        catch { /* form is already tearing down */ }
    }

    /// <summary>Ends this session immediately, shows why, and closes back to the login screen.</summary>
    private void ForceLogoutNow(string reason)
    {
        if (_forcedLogout) return; // already logging out — don't show the message twice
        _forcedLogout = true;
        _shiftTimer?.Stop();
        MessageBox.Show(this, reason, "Đăng xuất", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        LogoutRequested = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_forcedLogout && !LogoutRequested && !_closeConfirmed)
        {
            var result = MessageBox.Show(
                "Bạn có chắc chắn muốn thoát và đăng xuất người dùng hiện tại không?",
                "Xác nhận thoát",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _closeConfirmed = true;
            LogoutRequested = true;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _shiftTimer?.Stop();
        _shiftTimer?.Dispose();
        _sessionControl?.Dispose();
        // End our session (unless it was already force-ended by another login).
        if (!_forcedLogout)
        {
            _store.EndSession(_sessionToken, LogoutRequested ? "Đăng xuất" : "Đóng ứng dụng");
            // Session ended → stop counting overtime; a new login needs a fresh punch + approval.
            try { _store.CompleteActiveOvertime(_currentUser.Username); } catch { }
        }
        _avatarImage?.Dispose();
        base.OnFormClosed(e);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════════════════════
    private static Panel BuildPageHeader(string title, string subtitle)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 14, 24, 8)
        };
        var titleLbl = new Label
        {
            Text = title,
            Font = AppTheme.F18B,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 30,
            AutoSize = false,
            TextAlign = ContentAlignment.BottomLeft,
            BackColor = Color.Transparent
        };
        var subLbl = new Label
        {
            Text = subtitle,
            Font = AppTheme.F9,
            ForeColor = AppTheme.TextMuted,
            Dock = DockStyle.Top,
            Height = 20,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            BackColor = Color.Transparent
        };
        panel.Controls.Add(subLbl);
        panel.Controls.Add(titleLbl);
        return panel;
    }

    private static Control BuildPlaceholderPage(string title, string subtitle)
    {
        var page = new Panel { BackColor = AppTheme.Background };
        page.SuspendLayout();
        var hdr = BuildPageHeader(title, subtitle);
        var centerLbl = new Label
        {
            Text = "⚙  Module đang phát triển",
            Font = AppTheme.F14B,
            ForeColor = AppTheme.TextMuted,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        page.Controls.Add(centerLbl);
        page.Controls.Add(hdr);
        page.ResumeLayout(false);
        return page;
    }

    private static RoundedButton MakeToolbarButton(string text, bool isPrimary)
    {
        var btn = new RoundedButton
        {
            Text = text,
            Height = 34,
            AutoSize = false,
            CornerRadius = 8,
            Font = AppTheme.F9B,
            Margin = new Padding(0, 0, 8, 0)
        };
        if (isPrimary)
        {
            btn.BackColor = AppTheme.Accent;
            btn.ForeColor = Color.White;
            btn.BorderColor = Color.Transparent;
        }
        else
        {
            btn.BackColor = AppTheme.Surface;
            btn.ForeColor = AppTheme.TextPrimary;
            btn.BorderColor = AppTheme.Border;
        }
        // Estimate width from text
        using var g = btn.CreateGraphics();
        var sz = TextRenderer.MeasureText(text, AppTheme.F9B);
        btn.Width = sz.Width + 28;
        return btn;
    }


    // ═════════════════════════════════════════════════════════════════════════
    // DISPOSE
    // ═════════════════════════════════════════════════════════════════════════
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var page in _pages.Values)
                page.Dispose();
        }
        base.Dispose(disposing);
    }
}
