using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfShapes = System.Windows.Shapes;
using WpfThreading = System.Windows.Threading;

namespace KetoanMini;

public sealed class MainWindow : Wpf.Window
{
    private readonly AccountingStore _store;
    private readonly GiaCongStore _giaCongStore;
    private readonly AppUser _currentUser;
    private readonly Dictionary<string, Wpf.UIElement> _pages = new();
    private WpfControls.ContentControl _content = null!;
    private SidebarMenu _sidebar = null!;
    private WpfControls.TextBlock _periodText = null!;
    private WpfControls.TextBlock _notifBadge = null!;
    private ThemeToggle _themeToggle = null!;
    private WorkShiftWpfCard _workCard = null!;
    private WpfControls.TextBlock _userNameText = null!;

    private WpfThreading.DispatcherTimer? _shiftTimer;
    private SessionControlService? _sessionControl;
    private string _sessionToken = "";
    private string _activeKey = "dashboard";
    private bool _forcedLogout;
    private bool _closeConfirmed;
    private int _shiftSeconds;
    private DateTime? _otApprovedAt;
    private int _usersToken = int.MinValue;
    private AppRelease? _pendingUpdate;
    private NhanSuWpfPage? _nhanSuWpfPage;
    private GiaCongWpfPage? _giaCongWpfPage;

    private static readonly TimeSpan WorkStart = new(8, 0, 0);
    private static readonly TimeSpan WorkEnd = new(17, 0, 0);

    public bool LogoutRequested { get; private set; }

    public MainWindow(AccountingStore store, AppUser user)
    {
        _store = store;
        _currentUser = user;
        _giaCongStore = new GiaCongStore(store.DatabasePath);
        _giaCongStore.EnsureGiaCongTables();

        try { _sessionToken = _store.StartSession(Environment.MachineName); } catch { _sessionToken = ""; }
        _sessionControl = new SessionControlService();
        _sessionControl.ForceLogout += OnRemoteForceLogout;
        _sessionControl.Start(_currentUser.Username, _sessionToken);
        if (!string.IsNullOrEmpty(_sessionToken))
            _sessionControl.BroadcastLoginTakeover(_currentUser.Username, _sessionToken);

        Title = "Công ty TNHH Inox Cường Phát";
        WindowState = Wpf.WindowState.Maximized;
        MinWidth = 1200;
        MinHeight = 700;
        FontFamily = WpfTheme.Font;
        Background = WpfTheme.Background;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        Content = BuildShell();
        Loaded += (_, _) =>
        {
            RefreshNotifCount();
            Navigate("dashboard");
            BeginCheckForUpdates();
            StartWorkShiftTimer();
        };
    }

    private WpfControls.Grid BuildShell()
    {
        var root = new WpfControls.Grid();
        root.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(260) });
        root.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        _sidebar = new SidebarMenu { IsAdmin = _currentUser.IsAdmin };
        _sidebar.NavigationRequested += (_, e) => Navigate(e.Key);
        WpfControls.Grid.SetColumn(_sidebar, 0);
        root.Children.Add(_sidebar);

        var main = new WpfControls.Grid();
        main.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(82) });
        main.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        WpfControls.Grid.SetColumn(main, 1);
        root.Children.Add(main);

        var header = BuildHeader();
        WpfControls.Grid.SetRow(header, 0);
        main.Children.Add(header);

        _content = new WpfControls.ContentControl
        {
            Background = WpfTheme.Background,
            Focusable = false
        };
        WpfControls.Grid.SetRow(_content, 1);
        main.Children.Add(_content);

        return root;
    }

    private WpfControls.Border BuildHeader()
    {
        var grid = new WpfControls.Grid
        {
            Background = WpfTheme.Surface,
            Margin = new Wpf.Thickness(0),
            SnapsToDevicePixels = true
        };
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(292) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(230) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(270) });

        var company = new WpfControls.StackPanel
        {
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(24, 0, 0, 0)
        };
        company.Children.Add(new WpfControls.TextBlock
        {
            Text = "Công ty TNHH Inox Cường Phát",
            FontWeight = Wpf.FontWeights.Bold,
            Foreground = WpfTheme.TextPrimary,
            FontSize = 17
        });
        company.Children.Add(new WpfControls.TextBlock
        {
            Text = "Hệ thống kế toán doanh nghiệp",
            Foreground = WpfTheme.TextMuted,
            FontSize = 13
        });
        grid.Children.Add(company);

        var search = new WpfControls.Border
        {
            CornerRadius = new Wpf.CornerRadius(10),
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            Background = WpfTheme.SurfaceAlt,
            Height = 40,
            Margin = new Wpf.Thickness(8, 0, 8, 0),
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Child = BuildSearchBoxContent()
        };
        WpfControls.Grid.SetColumn(search, 1);
        grid.Children.Add(search);

        _workCard = new WorkShiftWpfCard { Margin = new Wpf.Thickness(8, 5, 8, 5) };
        _workCard.MouseLeftButtonUp += (_, _) => OnWorkCardClick();
        WpfControls.Grid.SetColumn(_workCard, 2);
        grid.Children.Add(_workCard);

        var userPanel = new WpfControls.Grid { Margin = new Wpf.Thickness(4, 0, 16, 0) };
        userPanel.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(58) });
        userPanel.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(74) });
        userPanel.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(34) });
        userPanel.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(36) });
        userPanel.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        _periodText = new WpfControls.TextBlock
        {
            Foreground = WpfTheme.TextSecondary,
            FontSize = WpfTheme.Pt(8),
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Text = DateTime.Now.ToString("MM/yyyy")
        };
        userPanel.Children.Add(_periodText);

        _themeToggle = new ThemeToggle { VerticalAlignment = Wpf.VerticalAlignment.Center };
        _themeToggle.ToggleRequested += (_, _) => ToggleTheme();
        WpfControls.Grid.SetColumn(_themeToggle, 1);
        userPanel.Children.Add(_themeToggle);

        var notif = HeaderIconButton("M12,22 C13.1,22 14,21.1 14,20 L10,20 C10,21.1 10.9,22 12,22 M5,17 L19,17 L17,14 L17,10 C17,7.1 15.3,4.7 13,4.1 L13,3 C13,2.4 12.6,2 12,2 C11.4,2 11,2.4 11,3 L11,4.1 C8.7,4.7 7,7.1 7,10 L7,14 Z");
        notif.Click += (_, _) => ShowNotificationsMenu(notif);
        WpfControls.Grid.SetColumn(notif, 2);
        userPanel.Children.Add(notif);

        _notifBadge = new WpfControls.TextBlock
        {
            Foreground = WpfMedia.Brushes.White,
            Background = WpfTheme.Danger,
            FontSize = WpfTheme.Pt(7),
            FontWeight = Wpf.FontWeights.Bold,
            Width = 16,
            Height = 16,
            TextAlignment = Wpf.TextAlignment.Center,
            Visibility = Wpf.Visibility.Collapsed,
            Margin = new Wpf.Thickness(18, 12, 0, 0)
        };
        WpfControls.Grid.SetColumn(_notifBadge, 2);
        userPanel.Children.Add(_notifBadge);

        var avatar = new WpfControls.Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new Wpf.CornerRadius(17),
            Background = WpfTheme.Accent,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Child = new WpfControls.TextBlock
            {
                Text = TextUtil.Initials(_currentUser.DisplayName),
                Foreground = WpfMedia.Brushes.White,
                FontWeight = Wpf.FontWeights.Bold,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            }
        };
        WpfControls.Grid.SetColumn(avatar, 3);
        userPanel.Children.Add(avatar);

        _userNameText = new WpfControls.TextBlock
        {
            Text = _currentUser.DisplayName.Split(' ').LastOrDefault() ?? _currentUser.DisplayName,
            Foreground = WpfTheme.TextPrimary,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(8),
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis,
            Cursor = WpfInput.Cursors.Hand
        };
        _userNameText.MouseLeftButtonUp += (_, _) => ShowUserMenu(_userNameText);
        WpfControls.Grid.SetColumn(_userNameText, 4);
        userPanel.Children.Add(_userNameText);

        WpfControls.Grid.SetColumn(userPanel, 3);
        grid.Children.Add(userPanel);

        return new WpfControls.Border
        {
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static WpfControls.Grid BuildSearchBoxContent()
    {
        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(40) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(72) });

        grid.Children.Add(new WpfShapes.Path
        {
            Data = WpfMedia.Geometry.Parse("M10,18 A8,8 0 1 1 10,2 A8,8 0 1 1 10,18 M16,16 L22,22"),
            Stroke = WpfTheme.TextMuted,
            StrokeThickness = 2,
            StrokeStartLineCap = WpfMedia.PenLineCap.Round,
            StrokeEndLineCap = WpfMedia.PenLineCap.Round,
            Width = 18,
            Height = 18,
            Stretch = WpfMedia.Stretch.Uniform,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        });

        var placeholder = new WpfControls.TextBlock
        {
            Text = "Nhập để tìm kiếm...",
            Foreground = WpfTheme.TextMuted,
            FontSize = 13,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        WpfControls.Grid.SetColumn(placeholder, 1);
        grid.Children.Add(placeholder);

        var shortcut = new WpfControls.Border
        {
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(7),
            Padding = new Wpf.Thickness(9, 3, 9, 3),
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Child = new WpfControls.TextBlock
            {
                Text = "Ctrl + K",
                Foreground = WpfTheme.TextSecondary,
                FontSize = 11,
                FontWeight = Wpf.FontWeights.SemiBold
            }
        };
        WpfControls.Grid.SetColumn(shortcut, 2);
        grid.Children.Add(shortcut);
        return grid;
    }

    private static WpfControls.Button HeaderIconButton(string pathData)
    {
        var button = WpfUi.OutlineButton("", WpfTheme.TextSecondary, WpfTheme.Border);
        button.Width = 34;
        button.Height = 34;
        button.Margin = new Wpf.Thickness(3, 0, 3, 0);
        button.VerticalAlignment = Wpf.VerticalAlignment.Center;
        button.Background = WpfTheme.SurfaceAlt;
        button.Content = new WpfShapes.Path
        {
            Data = WpfMedia.Geometry.Parse(pathData),
            Stroke = WpfTheme.TextSecondary,
            StrokeThickness = 1.8,
            StrokeStartLineCap = WpfMedia.PenLineCap.Round,
            StrokeEndLineCap = WpfMedia.PenLineCap.Round,
            StrokeLineJoin = WpfMedia.PenLineJoin.Round,
            Width = 18,
            Height = 18,
            Stretch = WpfMedia.Stretch.Uniform
        };
        return button;
    }

    private void Navigate(string key)
    {
        if ((key == "nhansu" || key == "capnhat") && !_currentUser.IsAdmin)
            key = "dashboard";

        _activeKey = key;
        _sidebar?.SetActive(key);

        if (!_pages.TryGetValue(key, out var page))
        {
            page = BuildPage(key);
            _pages[key] = page;
        }

        _content.Content = page;
    }

    private Wpf.UIElement BuildPage(string key) => key switch
    {
        "dashboard" => MainWpfPages.BuildDashboardPage(_store, _currentUser),
        "ketoan" => MainWpfPages.BuildKeToanPage(_store, ShowCreateDocumentWindow, ShowEditDocumentWindow),
        "giacong" => BuildGiaCongPage(),
        "banhang" => MainWpfPages.BuildBanHangPage(_store),
        "nhansu" => BuildNhanSuPage(),
        "baocao" => MainWpfPages.BuildBaoCaoPage(_store),
        "saoluu" => MainWpfPages.BuildSaoLuuPage(_store),
        "capnhat" => new CapNhatWpfPage(_store),
        "congno" => MainWpfPages.BuildPlaceholderPage("Công nợ", "Theo dõi công nợ khách hàng và nhà cung cấp"),
        "lichhen" => MainWpfPages.BuildPlaceholderPage("Lịch hẹn", "Lịch công việc và nhắc việc"),
        "tichhop" => MainWpfPages.BuildPlaceholderPage("Tích hợp", "Kết nối dịch vụ và tiện ích mở rộng"),
        "kho" => MainWpfPages.BuildPlaceholderPage("Hàng tồn kho", "Quản lý hàng hóa trong kho"),
        "muahang" => MainWpfPages.BuildPlaceholderPage("Mua hàng", "Quản lý đơn mua hàng"),
        "taisan" => MainWpfPages.BuildPlaceholderPage("Tài sản cố định", "Quản lý tài sản"),
        "danhmuc" => MainWpfPages.BuildPlaceholderPage("Danh mục", "Danh mục hệ thống"),
        "caidat" => MainWpfPages.BuildPlaceholderPage("Cài đặt", "Cài đặt ứng dụng"),
        _ => MainWpfPages.BuildPlaceholderPage(key, "")
    };

    private Wpf.UIElement BuildGiaCongPage()
    {
        var page = new GiaCongWpfPage(_giaCongStore);
        _giaCongWpfPage = page;
        page.CreateRequested += (_, _) =>
        {
            if (ShowCreateGiaCongWindow())
                page.RefreshDataQuiet();
        };
        return page;
    }

    private Wpf.UIElement BuildNhanSuPage()
    {
        var page = new NhanSuWpfPage(_store, _currentUser);
        _nhanSuWpfPage = page;
        page.AddUserRequested += (_, _) =>
        {
            if (!_currentUser.IsAdmin)
            {
                Wpf.MessageBox.Show(this, "Bạn không có quyền thực hiện thao tác này.", "Từ chối", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                return;
            }

            var win = new AddUserWpfWindow(_store, _currentUser) { Owner = this };
            if (win.ShowDialog() == true)
            {
                page.RefreshUsers();
                RefreshNhanSuNotificationsAndBaseline();
            }
        };
        page.ResetRequestsRequested += (_, _) =>
        {
            OpenResetRequestsWindow();
            page.RefreshPresenceOnly();
        };
        page.OvertimeRequestsRequested += (_, _) =>
        {
            OpenWorkAccessRequestsWindow();
            page.RefreshPresenceOnly();
        };
        page.PasswordResetRequested += (_, e) =>
        {
            try
            {
                var code = _store.AdminCreatePasswordResetCode(e.User.Id);
                CodeDisplayWpfWindow.Show(this, e.User.Username, code);
                page.RefreshPresenceOnly();
                RefreshNhanSuNotificationsAndBaseline();
            }
            catch (Exception ex)
            {
                Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
            }
        };
        page.NotificationsChanged += (_, _) => RefreshNhanSuNotificationsAndBaseline();
        page.AccountLocked += (_, e) => _sessionControl?.BroadcastAccountLocked(e.Username);
        return page;
    }

    private bool ShowCreateDocumentWindow()
    {
        var win = new DocumentWpfWindow(_store, null) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _store.RecordAudit("Tạo chứng từ", "Document", win.SavedVoucherNo, "Tạo chứng từ mới");
            InvalidatePage("ketoan");
            InvalidatePage("dashboard");
            return true;
        }
        return false;
    }

    private void ShowEditDocumentWindow(Document doc)
    {
        var win = new DocumentWpfWindow(_store, doc) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _store.RecordAudit("Cập nhật chứng từ", "Document", doc.VoucherNo, "Cập nhật chứng từ");
            InvalidatePage("ketoan");
            InvalidatePage("dashboard");
        }
    }

    private bool ShowCreateGiaCongWindow()
    {
        var win = new GiaCongPhieuWpfWindow(_giaCongStore, null, _currentUser.Username) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _store.RecordAudit("Tạo phiếu gia công", "GiaCongPhieu", win.MaPhieu, "Tạo phiếu gia công mới");
            return true;
        }
        return false;
    }

    private void InvalidatePage(string key)
    {
        _pages.Remove(key);
        if (_activeKey == key)
            Navigate(key);
    }

    private void ToggleTheme()
    {
        ThemeState.Toggle();
        ThemeState.Save();
        WpfTheme.ApplyCurrentTheme();
        _pages.Clear();
        Content = BuildShell();
        RefreshNotifCount();
        Navigate(_activeKey);
        UpdateWorkShift();
    }

    private void ShowUserMenu(Wpf.FrameworkElement anchor)
    {
        var menu = new WpfControls.ContextMenu { FontFamily = WpfTheme.Font };
        menu.Items.Add(new WpfControls.MenuItem { Header = $"{_currentUser.DisplayName}  ({_currentUser.Role})", IsEnabled = false });
        menu.Items.Add(new WpfControls.Separator());
        var profile = new WpfControls.MenuItem { Header = "Tùy chỉnh tài khoản" };
        profile.Click += (_, _) => OpenProfileWindow();
        var change = new WpfControls.MenuItem { Header = "Đổi mật khẩu" };
        change.Click += (_, _) => OpenChangePasswordWindow();
        var update = new WpfControls.MenuItem { Header = "Kiểm tra cập nhật" };
        update.Click += (_, _) => CheckForUpdatesInteractive();
        var logout = new WpfControls.MenuItem { Header = "Đăng xuất" };
        logout.Click += (_, _) => PerformLogout();
        menu.Items.Add(profile);
        menu.Items.Add(change);
        menu.Items.Add(update);
        menu.Items.Add(new WpfControls.Separator());
        menu.Items.Add(logout);
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void ShowNotificationsMenu(Wpf.FrameworkElement anchor)
    {
        var menu = new WpfControls.ContextMenu { FontFamily = WpfTheme.Font };
        if (_pendingUpdate is not null)
        {
            var item = new WpfControls.MenuItem { Header = $"Có bản cập nhật mới {_pendingUpdate.Version}" };
            item.Click += (_, _) => OpenUpdateDialog(_pendingUpdate);
            menu.Items.Add(item);
        }

        if (_currentUser.IsAdmin)
        {
            try
            {
                foreach (var user in _store.GetUsers().Where(u => u.IsPendingApproval && !u.IsAdmin))
                {
                    var captured = user;
                    var item = new WpfControls.MenuItem { Header = $"Duyệt tài khoản: {captured.Username}" };
                    item.Click += (_, _) =>
                    {
                        try
                        {
                            _store.AdminApproveUser(captured.Id);
                            Wpf.MessageBox.Show(this, $"Đã duyệt tài khoản \"{captured.Username}\".", "Thành công");
                            _nhanSuWpfPage?.RefreshUsers();
                            RefreshNotifCount();
                        }
                        catch (Exception ex)
                        {
                            Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
                        }
                    };
                    menu.Items.Add(item);
                }

                var resets = _store.GetPendingPasswordResetRequests();
                if (resets.Count > 0)
                {
                    var item = new WpfControls.MenuItem { Header = $"Yêu cầu đổi mật khẩu ({resets.Count})" };
                    item.Click += (_, _) => OpenResetRequestsWindow();
                    menu.Items.Add(item);
                }

                var overtime = _store.GetPendingWorkAccessRequests();
                if (overtime.Count > 0)
                {
                    var item = new WpfControls.MenuItem { Header = $"Yêu cầu tăng ca ({overtime.Count})" };
                    item.Click += (_, _) => OpenWorkAccessRequestsWindow();
                    menu.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                menu.Items.Add(new WpfControls.MenuItem { Header = "Lỗi tải thông báo: " + ex.Message, IsEnabled = false });
            }
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new WpfControls.MenuItem { Header = "Không có thông báo mới", IsEnabled = false });

        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void OpenProfileWindow()
    {
        var win = new ProfileWpfWindow(_store, _currentUser) { Owner = this };
        if (win.ShowDialog() == true && win.ProfileChanged)
        {
            _userNameText.Text = _currentUser.DisplayName.Split(' ').LastOrDefault() ?? _currentUser.DisplayName;
            _nhanSuWpfPage?.RefreshUsersQuiet();
        }
    }

    private void OpenChangePasswordWindow()
    {
        new ChangePasswordWpfWindow(_store, _currentUser) { Owner = this }.ShowDialog();
    }

    private void OpenResetRequestsWindow()
    {
        new PasswordResetRequestsWpfWindow(_store) { Owner = this }.ShowDialog();
        RefreshNotifCount();
    }

    private void OpenWorkAccessRequestsWindow()
    {
        new WorkAccessRequestsWpfWindow(_store) { Owner = this }.ShowDialog();
        RefreshNotifCount();
        UpdateWorkShift();
    }

    private void OnWorkCardClick()
    {
        if (_currentUser.IsAdmin)
        {
            OpenWorkAccessRequestsWindow();
            return;
        }

        var now = DateTime.Now.TimeOfDay;
        var isWorkTime = now >= WorkStart && now < WorkEnd;
        if (isWorkTime)
        {
            Wpf.MessageBox.Show(this, $"Bạn đang trong ca làm việc ({WorkStart:hh\\:mm} - {WorkEnd:hh\\:mm}).", "Ca làm việc");
            return;
        }

        bool overtimeApproved = false;
        try { overtimeApproved = _store.HasApprovedWorkAccess(DateOnly.FromDateTime(DateTime.Now)); } catch { }
        if (overtimeApproved)
        {
            Wpf.MessageBox.Show(this, "Bạn đã được duyệt tăng ca cho hôm nay. Trạng thái: Tăng ca.", "Tăng ca");
            return;
        }

        var win = new OvertimeRequestWpfWindow(_store) { Owner = this };
        if (win.ShowDialog() == true)
            UpdateWorkShift();
    }

    private void StartWorkShiftTimer()
    {
        RefreshOvertimeFlag();
        UpdateWorkShift();
        _shiftTimer = new WpfThreading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _shiftTimer.Tick += (_, _) =>
        {
            _shiftSeconds++;
            if (_shiftSeconds % 15 == 0 && !CheckSessionAlive()) return;
            if (_shiftSeconds % 30 == 0) RefreshOvertimeFlag();
            if (_currentUser.IsAdmin)
            {
                if (_shiftSeconds % 4 == 0) CheckUsersChanged();
                if (_shiftSeconds % 20 == 0 && _activeKey == "nhansu")
                    _nhanSuWpfPage?.RefreshPresenceOnly();
            }
            _periodText.Text = DateTime.Now.ToString("MM/yyyy");
            UpdateWorkShift();
        };
        _shiftTimer.Start();
    }

    private void RefreshOvertimeFlag()
    {
        _otApprovedAt = null;
        if (_currentUser.IsAdmin) return;
        try
        {
            var req = _store.GetApprovedWorkAccess(DateOnly.FromDateTime(DateTime.Now));
            if (req != null) _otApprovedAt = req.PunchAt ?? req.ApprovedAt ?? req.RequestedAt;
        }
        catch { _otApprovedAt = null; }
    }

    private void UpdateWorkShift()
    {
        if (_workCard is null) return;

        var now = DateTime.Now;
        var t = now.TimeOfDay;
        bool isWorkTime = t >= WorkStart && t < WorkEnd;
        string time = $"{WorkStart:hh\\:mm} - {WorkEnd:hh\\:mm}";
        string status;
        WpfMedia.Brush color;

        if (isWorkTime)
        {
            status = "Đang làm việc";
            color = WpfTheme.Success;
        }
        else if (_currentUser.IsAdmin)
        {
            status = "Ngoài giờ";
            color = WpfTheme.TextMuted;
        }
        else if (_otApprovedAt is DateTime since)
        {
            status = "Tăng ca";
            color = WpfTheme.Warning;
            var elapsed = now - since;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            time = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }
        else
        {
            status = "Hết giờ";
            color = WpfTheme.Danger;
        }

        _workCard.SetStatus("Ca làm việc", status, time, color);
    }

    private bool CheckSessionAlive()
    {
        if (string.IsNullOrEmpty(_sessionToken)) return true;
        AccountingStore.SessionStatus status;
        try { status = _store.CheckSession(_sessionToken); }
        catch { return true; }
        if (status == AccountingStore.SessionStatus.Alive) return true;

        var reason = status == AccountingStore.SessionStatus.AccountLocked
            ? "Tài khoản của bạn đã bị khoá."
            : "Tài khoản của bạn vừa đăng nhập ở một máy khác.\nPhiên làm việc tại đây đã kết thúc.";
        ForceLogoutNow(reason);
        return false;
    }

    private void OnRemoteForceLogout(object? sender, SessionControlEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnRemoteForceLogout(sender, e));
            return;
        }
        ForceLogoutNow(e.Reason);
    }

    private void ForceLogoutNow(string reason)
    {
        if (_forcedLogout) return;
        _forcedLogout = true;
        _shiftTimer?.Stop();
        Wpf.MessageBox.Show(this, reason, "Đăng xuất", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
        LogoutRequested = true;
        _closeConfirmed = true;
        Close();
    }

    private void CheckUsersChanged()
    {
        try
        {
            var token = _store.GetUsersChangeToken();
            if (_usersToken == int.MinValue) { _usersToken = token; return; }
            if (token == _usersToken) return;
            _usersToken = token;
            RefreshNotifCount();
            if (_activeKey == "nhansu") _nhanSuWpfPage?.RefreshUsersQuiet();
        }
        catch { }
    }

    private void RefreshNhanSuNotificationsAndBaseline()
    {
        RefreshNotifCount();
        try { _usersToken = _store.GetUsersChangeToken(); } catch { }
    }

    private void RefreshNotifCount()
    {
        var count = _pendingUpdate is null ? 0 : 1;
        if (_currentUser.IsAdmin)
        {
            try
            {
                count += _store.GetUsers().Count(u => u.IsPendingApproval && !u.IsAdmin);
                count += _store.GetPendingPasswordResetRequests().Count;
                count += _store.GetPendingWorkAccessRequests().Count;
            }
            catch { }
        }

        if (_notifBadge is null) return;
        _notifBadge.Text = count > 9 ? "9+" : count.ToString();
        _notifBadge.Visibility = count > 0 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }

    private void BeginCheckForUpdates()
    {
        Task.Run(() =>
        {
            AppRelease? latest = null;
            try
            {
                var result = _store.CheckVersion();
                latest = result.UpdateAvailable ? result.Latest : null;
            }
            catch { }

            Dispatcher.Invoke(() =>
            {
                _pendingUpdate = latest;
                RefreshNotifCount();
            });
        });
    }

    private void CheckForUpdatesInteractive()
    {
        AppRelease? latest;
        try
        {
            var result = _store.CheckVersion();
            latest = result.UpdateAvailable ? result.Latest : null;
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, "Không kiểm tra được phiên bản.\n\n" + ex.Message, "Kiểm tra cập nhật", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }

        _pendingUpdate = latest;
        RefreshNotifCount();
        if (latest is null)
        {
            Wpf.MessageBox.Show(this, $"Bạn đang dùng phiên bản mới nhất ({AppVersion.CurrentText}).", "Kiểm tra cập nhật");
            return;
        }
        OpenUpdateDialog(latest);
    }

    private void OpenUpdateDialog(AppRelease release)
    {
        var win = new UpdateWindow(_store, release, blocking: false) { Owner = this };
        win.ShowDialog();
    }

    private void PerformLogout()
    {
        if (Wpf.MessageBox.Show(this, "Đăng xuất khỏi tài khoản hiện tại?", "Đăng xuất",
                Wpf.MessageBoxButton.YesNo, Wpf.MessageBoxImage.Question) != Wpf.MessageBoxResult.Yes)
            return;
        LogoutRequested = true;
        _closeConfirmed = true;
        Close();
    }

    public void LogoutToLogin()
    {
        LogoutRequested = true;
        _closeConfirmed = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forcedLogout && !LogoutRequested && !_closeConfirmed)
        {
            var result = Wpf.MessageBox.Show(this,
                "Bạn có chắc chắn muốn thoát và đăng xuất người dùng hiện tại không?",
                "Xác nhận thoát",
                Wpf.MessageBoxButton.YesNo,
                Wpf.MessageBoxImage.Question);
            if (result != Wpf.MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _closeConfirmed = true;
            LogoutRequested = true;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _shiftTimer?.Stop();
        _sessionControl?.Dispose();
        if (!_forcedLogout)
        {
            _store.EndSession(_sessionToken, LogoutRequested ? "Đăng xuất" : "Đóng ứng dụng");
            try { _store.CompleteActiveOvertime(_currentUser.Username); } catch { }
        }
        base.OnClosed(e);
    }
}

internal sealed class WorkShiftWpfCard : WpfControls.Border
{
    private readonly WpfControls.TextBlock _label = new();
    private readonly WpfControls.TextBlock _status = new();
    private readonly WpfControls.TextBlock _time = new();

    public WorkShiftWpfCard()
    {
        Background = WpfTheme.WorkCardBg;
        BorderBrush = WpfTheme.AccentLight;
        BorderThickness = new Wpf.Thickness(1);
        CornerRadius = new Wpf.CornerRadius(8);
        Cursor = WpfInput.Cursors.Hand;
        Padding = new Wpf.Thickness(12, 4, 12, 4);

        var grid = new WpfControls.Grid();
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(20) });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(24) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });

        _label.Foreground = WpfTheme.SidebarText;
        _label.FontSize = WpfTheme.Pt(8);
        _status.FontSize = WpfTheme.Pt(8);
        _status.FontWeight = Wpf.FontWeights.Bold;
        _time.Foreground = WpfMedia.Brushes.White;
        _time.FontSize = WpfTheme.Pt(9);
        _time.FontWeight = Wpf.FontWeights.Bold;

        WpfControls.Grid.SetRow(_label, 0);
        grid.Children.Add(_label);
        WpfControls.Grid.SetRow(_status, 0);
        WpfControls.Grid.SetColumn(_status, 1);
        grid.Children.Add(_status);
        WpfControls.Grid.SetRow(_time, 1);
        WpfControls.Grid.SetColumnSpan(_time, 2);
        grid.Children.Add(_time);
        Child = grid;
    }

    public void SetStatus(string label, string status, string time, WpfMedia.Brush statusBrush)
    {
        _label.Text = label;
        _status.Text = status;
        _status.Foreground = statusBrush;
        _time.Text = "◷  " + time + "      ˅";
    }
}
