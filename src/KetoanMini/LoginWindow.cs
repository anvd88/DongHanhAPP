using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfEffects = System.Windows.Media.Effects;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

// ============================================================================
// LoginWindow — bản WPF của LoginForm (màn đăng nhập/đăng ký nền navy)
// Giữ nguyên bố cục & giao diện: card trắng bo góc, header navy, tab bar,
// ô nhập bo góc đổi viền khi focus/lỗi, nút bo góc màu nhấn.
// ============================================================================
public sealed class LoginWindow : Wpf.Window
{
    private readonly AccountingStore _store;
    private int _activeTab;

    public AppUser? AuthenticatedUser { get; private set; }

    // Layout
    private WpfControls.Grid _cardHost = null!;
    private WpfControls.Border _card = null!;
    private WpfControls.Grid _loginPanel = null!;
    private WpfControls.ScrollViewer _registerPanel = null!;

    // Tab bar
    private readonly WpfControls.Border[] _tabBorders = new WpfControls.Border[2];
    private readonly WpfControls.TextBlock[] _tabTexts = new WpfControls.TextBlock[2];
    private readonly WpfControls.Border[] _tabUnderlines = new WpfControls.Border[2];

    // Login controls
    private LoginInputBox _txtUsername = null!;
    private LoginInputBox _txtPassword = null!;
    private WpfControls.TextBlock _lblLoginError = null!;

    // Register controls
    private LoginInputBox _txtRegUser = null!;
    private LoginInputBox _txtRegFullName = null!;
    private LoginInputBox _txtRegPass = null!;
    private LoginInputBox _txtRegConfirm = null!;
    private LoginInputBox _txtRegCode = null!;
    private WpfControls.TextBlock _lblRegError = null!;

    public LoginWindow(AccountingStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        BuildUi();
    }

    private void BuildUi()
    {
        Title = "Đăng nhập - Công ty TNHH Inox Cường Phát";
        Width = 520;
        Height = 600;
        ResizeMode = Wpf.ResizeMode.NoResize;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        FontFamily = WpfTheme.Font;
        Background = new WpfMedia.LinearGradientBrush(WpfTheme.NavyTop, WpfTheme.NavyBottom, 90);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        var root = new WpfControls.Grid();

        // Phiên bản hiện tại (góc dưới trái).
        root.Children.Add(new WpfControls.TextBlock
        {
            Text = $"v{AppVersion.CurrentText}",
            Foreground = WpfTheme.TextMuted,
            FontSize = WpfTheme.Pt(8),
            HorizontalAlignment = Wpf.HorizontalAlignment.Left,
            VerticalAlignment = Wpf.VerticalAlignment.Bottom,
            Margin = new Wpf.Thickness(16, 0, 0, 10)
        });

        // Footer credit (góc dưới phải) — chỉ hiển thị ở màn Login.
        root.Children.Add(new WpfControls.TextBlock
        {
            Text = "Powered by Codex and Claude",
            Foreground = WpfTheme.TextMuted,
            FontSize = WpfTheme.Pt(8),
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
            VerticalAlignment = Wpf.VerticalAlignment.Bottom,
            Margin = new Wpf.Thickness(0, 0, 16, 10)
        });

        // Card: tách lớp đổ bóng ra Border riêng phía sau để KHÔNG rasterize nội dung
        // (đặt Effect trực tiếp lên card sẽ làm mờ toàn bộ chữ bên trong).
        _cardHost = new WpfControls.Grid
        {
            Width = 460,
            Height = 510,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };

        var shadowLayer = new WpfControls.Border
        {
            Background = WpfTheme.Surface,
            CornerRadius = new Wpf.CornerRadius(14),
            // Thụt vào 2px + bo góc nhỏ hơn card → nằm hẳn dưới card, không thò viền
            // trắng ra ở góc. Bóng vẫn lan ra ngoài nhờ BlurRadius.
            Margin = new Wpf.Thickness(2),
            Effect = new WpfEffects.DropShadowEffect
            {
                Color = WpfMedia.Colors.Black,
                BlurRadius = 22,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.28
            }
        };

        _card = new WpfControls.Border
        {
            Background = WpfTheme.Surface,
            CornerRadius = new Wpf.CornerRadius(16)
        };
        // Clip toàn bộ card theo hình chữ nhật bo góc 16 → góc mượt, không bị
        // "vỡ"/seam giữa header navy và card khi hai hình bo góc chồng nhau.
        _card.SizeChanged += (_, _) =>
            _card.Clip = new WpfMedia.RectangleGeometry(
                new Wpf.Rect(0, 0, _card.ActualWidth, _card.ActualHeight), 16, 16);

        var cardGrid = new WpfControls.Grid();
        cardGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(80) });
        cardGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(44) });
        cardGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var header = BuildHeader();
        WpfControls.Grid.SetRow(header, 0);
        cardGrid.Children.Add(header);

        var tabBar = BuildTabBar();
        WpfControls.Grid.SetRow(tabBar, 1);
        cardGrid.Children.Add(tabBar);

        var content = new WpfControls.Grid { Margin = new Wpf.Thickness(32, 16, 32, 16) };
        _loginPanel = BuildLoginPanel();
        _registerPanel = BuildRegisterPanel();
        content.Children.Add(_registerPanel);
        content.Children.Add(_loginPanel);
        WpfControls.Grid.SetRow(content, 2);
        cardGrid.Children.Add(content);

        _card.Child = cardGrid;
        _cardHost.Children.Add(shadowLayer);
        _cardHost.Children.Add(_card);
        root.Children.Add(_cardHost);

        Content = root;

        ShowActiveTab();
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => _txtUsername.FocusInput();
    }

    // ── Header ──────────────────────────────────────────────────────────────
    private WpfControls.Border BuildHeader()
    {
        var header = new WpfControls.Border
        {
            Background = WpfTheme.SidebarBg // navy #0F172A; góc trên do clip của card bo lại
        };

        var grid = new WpfControls.Grid();

        var logo = new WpfControls.Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new Wpf.CornerRadius(22),
            Background = WpfTheme.Accent,
            HorizontalAlignment = Wpf.HorizontalAlignment.Left,
            VerticalAlignment = Wpf.VerticalAlignment.Top,
            Margin = new Wpf.Thickness(20, 18, 0, 0),
            Child = new WpfControls.TextBlock
            {
                Text = "CP",
                Foreground = WpfMedia.Brushes.White,
                FontWeight = Wpf.FontWeights.Bold,
                FontSize = WpfTheme.Pt(13),
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            }
        };

        var company = new WpfControls.TextBlock
        {
            Text = "Công ty TNHH Inox Cường Phát",
            Foreground = WpfMedia.Brushes.White,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(10),
            HorizontalAlignment = Wpf.HorizontalAlignment.Left,
            VerticalAlignment = Wpf.VerticalAlignment.Top,
            Margin = new Wpf.Thickness(72, 16, 0, 0)
        };

        var sub = new WpfControls.TextBlock
        {
            Text = "Hệ thống quản lý kế toán",
            Foreground = WpfTheme.SidebarText,
            FontSize = WpfTheme.Pt(8.5),
            HorizontalAlignment = Wpf.HorizontalAlignment.Left,
            VerticalAlignment = Wpf.VerticalAlignment.Top,
            Margin = new Wpf.Thickness(72, 40, 0, 0)
        };

        grid.Children.Add(logo);
        grid.Children.Add(company);
        grid.Children.Add(sub);
        header.Child = grid;
        return header;
    }

    // ── Tab bar ───────────────────────────────────────────────────────────
    private WpfControls.Grid BuildTabBar()
    {
        var grid = new WpfControls.Grid { Background = WpfTheme.Surface };
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition());
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition());

        // Divider đáy chạy hết chiều ngang
        var divider = new WpfControls.Border
        {
            Height = 1,
            Background = WpfTheme.Border,
            VerticalAlignment = Wpf.VerticalAlignment.Bottom
        };
        WpfControls.Grid.SetColumnSpan(divider, 2);

        string[] titles = { "Đăng nhập", "Đăng ký" };
        for (var i = 0; i < 2; i++)
        {
            var index = i;
            var text = new WpfControls.TextBlock
            {
                Text = titles[i],
                FontFamily = WpfTheme.Font,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            };
            var underline = new WpfControls.Border
            {
                Height = 2.5,
                Background = WpfTheme.Accent,
                VerticalAlignment = Wpf.VerticalAlignment.Bottom,
                Margin = new Wpf.Thickness(14, 0, 14, 0),
                Visibility = Wpf.Visibility.Hidden
            };
            var cell = new WpfControls.Border
            {
                Background = WpfMedia.Brushes.Transparent,
                Cursor = WpfInput.Cursors.Hand,
                Child = new WpfControls.Grid { Children = { text, underline } }
            };
            cell.MouseLeftButtonUp += (_, _) => SwitchTab(index);
            cell.MouseEnter += (_, _) => { if (_activeTab != index) cell.Background = WpfTheme.RowHover; };
            cell.MouseLeave += (_, _) => cell.Background = WpfMedia.Brushes.Transparent;

            WpfControls.Grid.SetColumn(cell, i);
            grid.Children.Add(cell);

            _tabBorders[i] = cell;
            _tabTexts[i] = text;
            _tabUnderlines[i] = underline;
        }

        grid.Children.Add(divider);
        return grid;
    }

    private void SwitchTab(int index)
    {
        if (_activeTab == index)
        {
            return;
        }

        _activeTab = index;
        ShowActiveTab();
    }

    private void ShowActiveTab()
    {
        _loginPanel.Visibility = _activeTab == 0 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
        _registerPanel.Visibility = _activeTab == 1 ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;

        for (var i = 0; i < 2; i++)
        {
            var active = i == _activeTab;
            _tabTexts[i].FontWeight = active ? Wpf.FontWeights.Bold : Wpf.FontWeights.Normal;
            _tabTexts[i].FontSize = WpfTheme.Pt(active ? 9.5 : 9);
            _tabTexts[i].Foreground = active ? WpfTheme.Accent : WpfTheme.TextSecondary;
            _tabUnderlines[i].Visibility = active ? Wpf.Visibility.Visible : Wpf.Visibility.Hidden;
            _tabBorders[i].Background = WpfMedia.Brushes.Transparent;
        }

        _cardHost.Height = _activeTab == 0 ? 510 : 550;
    }

    // ── Login panel ─────────────────────────────────────────────────────────
    private WpfControls.Grid BuildLoginPanel()
    {
        var panel = new WpfControls.StackPanel();

        panel.Children.Add(FieldLabel("Tài khoản"));
        _txtUsername = new LoginInputBox("Nhập tài khoản") { Margin = new Wpf.Thickness(0, 0, 0, 12) };
        panel.Children.Add(_txtUsername);

        panel.Children.Add(FieldLabel("Mật khẩu"));
        _txtPassword = new LoginInputBox("Nhập mật khẩu", isPassword: true) { Margin = new Wpf.Thickness(0, 0, 0, 8) };
        panel.Children.Add(_txtPassword);

        // Hàng: Hiện mật khẩu (trái) + Quên mật khẩu (phải)
        var row = new WpfControls.Grid { Margin = new Wpf.Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new WpfControls.ColumnDefinition());
        row.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });

        var chkShow = new WpfControls.CheckBox
        {
            Content = "Hiện mật khẩu",
            Foreground = WpfTheme.TextSecondary,
            FontSize = WpfTheme.Pt(9),
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        chkShow.Checked += (_, _) => _txtPassword.ShowPassword = true;
        chkShow.Unchecked += (_, _) => _txtPassword.ShowPassword = false;
        WpfControls.Grid.SetColumn(chkShow, 0);

        var forgot = WpfUi.LinkText("Quên mật khẩu?", ShowForgotPassword);
        WpfControls.Grid.SetColumn(forgot, 1);

        row.Children.Add(chkShow);
        row.Children.Add(forgot);
        panel.Children.Add(row);

        _lblLoginError = ErrorLabel();
        panel.Children.Add(_lblLoginError);

        var btnLogin = WpfUi.FilledButton("Đăng nhập", WpfTheme.Accent, WpfMedia.Brushes.White, fontPt: 10);
        btnLogin.Height = 42;
        btnLogin.Margin = new Wpf.Thickness(0, 2, 0, 10);
        btnLogin.Click += (_, _) => Login();
        panel.Children.Add(btnLogin);

        panel.Children.Add(WpfUi.LinkText("Chưa có tài khoản? Chuyển sang tab Đăng ký", () => SwitchTab(1)));

        var host = new WpfControls.Grid();
        host.Children.Add(panel);
        return host;
    }

    // ── Register panel ───────────────────────────────────────────────────────
    private WpfControls.ScrollViewer BuildRegisterPanel()
    {
        var panel = new WpfControls.StackPanel();

        panel.Children.Add(FieldLabel("Tài khoản"));
        _txtRegUser = new LoginInputBox("Nhập tên đăng nhập") { Margin = new Wpf.Thickness(0, 0, 0, 10) };
        panel.Children.Add(_txtRegUser);

        panel.Children.Add(FieldLabel("Họ và tên"));
        _txtRegFullName = new LoginInputBox("Nhập họ và tên đầy đủ") { Margin = new Wpf.Thickness(0, 0, 0, 10) };
        panel.Children.Add(_txtRegFullName);

        panel.Children.Add(FieldLabel("Mật khẩu"));
        _txtRegPass = new LoginInputBox("Nhập mật khẩu", isPassword: true) { Margin = new Wpf.Thickness(0, 0, 0, 10) };
        panel.Children.Add(_txtRegPass);

        panel.Children.Add(FieldLabel("Xác nhận mật khẩu"));
        _txtRegConfirm = new LoginInputBox("Nhập lại mật khẩu", isPassword: true) { Margin = new Wpf.Thickness(0, 0, 0, 10) };
        panel.Children.Add(_txtRegConfirm);

        panel.Children.Add(FieldLabel("Mã kích hoạt (không bắt buộc)"));
        _txtRegCode = new LoginInputBox("Nhập mã kích hoạt nếu có") { Margin = new Wpf.Thickness(0, 0, 0, 8) };
        panel.Children.Add(_txtRegCode);

        _lblRegError = ErrorLabel();
        panel.Children.Add(_lblRegError);

        var btnRegister = WpfUi.FilledButton("Đăng ký", WpfTheme.Accent, WpfMedia.Brushes.White, fontPt: 10);
        btnRegister.Height = 42;
        btnRegister.Margin = new Wpf.Thickness(0, 2, 0, 8);
        btnRegister.Click += (_, _) => Register();
        panel.Children.Add(btnRegister);

        return new WpfControls.ScrollViewer
        {
            // Ẩn thanh cuộn cho gọn; vẫn cuộn được bằng con lăn chuột nếu nội dung dài.
            VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = WpfControls.ScrollBarVisibility.Disabled,
            Content = panel
        };
    }

    private static WpfControls.TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontWeight = Wpf.FontWeights.Bold,
        FontSize = WpfTheme.Pt(9),
        Foreground = WpfTheme.TextPrimary,
        Margin = new Wpf.Thickness(0, 4, 0, 4)
    };

    private static WpfControls.TextBlock ErrorLabel() => new()
    {
        Foreground = WpfTheme.Danger,
        FontSize = WpfTheme.Pt(9),
        TextWrapping = Wpf.TextWrapping.Wrap,
        Margin = new Wpf.Thickness(0, 0, 0, 2),
        Visibility = Wpf.Visibility.Collapsed
    };

    // ── Keyboard ──────────────────────────────────────────────────────────
    private void OnPreviewKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Enter)
        {
            if (_activeTab == 0)
            {
                Login();
            }
            else
            {
                Register();
            }

            e.Handled = true;
        }
        else if (e.Key == WpfInput.Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    // ── Auth ──────────────────────────────────────────────────────────────
    private void Login()
    {
        HideError(_lblLoginError);
        _txtUsername.ErrorState = false;
        _txtPassword.ErrorState = false;

        var username = _txtUsername.Text.Trim();
        var password = _txtPassword.Text;

        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError(_lblLoginError, "Vui lòng nhập tài khoản.");
            _txtUsername.ErrorState = true;
            _txtUsername.FocusInput();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError(_lblLoginError, "Vui lòng nhập mật khẩu.");
            _txtPassword.ErrorState = true;
            _txtPassword.FocusInput();
            return;
        }

        try
        {
            var user = _store.AuthenticateUser(username, password);
            if (user is null)
            {
                ShowError(_lblLoginError, "Tài khoản hoặc mật khẩu không đúng.");
                _txtUsername.ErrorState = true;
                _txtPassword.ErrorState = true;
                return;
            }

            if (user.IsPendingApproval)
            {
                Wpf.MessageBox.Show(
                    "Tài khoản của bạn đang chờ admin phê duyệt.\nVui lòng liên hệ quản trị viên.",
                    "Chờ phê duyệt",
                    Wpf.MessageBoxButton.OK,
                    Wpf.MessageBoxImage.Information);
                return;
            }

            AuthenticatedUser = user;
            DialogResult = true;
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

        var username = _txtRegUser.Text.Trim();
        var fullName = _txtRegFullName.Text.Trim();
        var password = _txtRegPass.Text;
        var confirm = _txtRegConfirm.Text;
        var code = _txtRegCode.Text.Trim();

        if (string.IsNullOrWhiteSpace(username))
        { ShowError(_lblRegError, "Vui lòng nhập tên đăng nhập."); _txtRegUser.FocusInput(); return; }

        if (string.IsNullOrWhiteSpace(fullName))
        { ShowError(_lblRegError, "Vui lòng nhập họ và tên."); _txtRegFullName.FocusInput(); return; }

        if (string.IsNullOrWhiteSpace(password))
        { ShowError(_lblRegError, "Vui lòng nhập mật khẩu."); _txtRegPass.FocusInput(); return; }

        if (password.Length < 6)
        { ShowError(_lblRegError, "Mật khẩu phải có ít nhất 6 ký tự."); _txtRegPass.FocusInput(); return; }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            ShowError(_lblRegError, "Xác nhận mật khẩu không khớp.");
            _txtRegConfirm.ErrorState = true;
            _txtRegConfirm.FocusInput();
            return;
        }

        try
        {
            var user = _store.RegisterUser(username, fullName, password, code);

            var msg = user.IsPendingApproval
                ? $"Đăng ký thành công!\n\nTài khoản \"{username}\" đang chờ admin phê duyệt.\nBạn sẽ đăng nhập được sau khi được duyệt."
                : $"Đăng ký thành công!\n\nTài khoản \"{username}\" đã được kích hoạt.\nBạn có thể đăng nhập ngay.";

            Wpf.MessageBox.Show(msg, "Đăng ký thành công", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);

            ClearRegistrationForm();
            SwitchTab(0);
            _txtUsername.Text = username;
            _txtPassword.FocusInput();
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
        var dlg = new ForgotPasswordWindow(_store) { Owner = this };
        dlg.ShowDialog();
    }

    private void ClearRegistrationForm()
    {
        _txtRegUser.Text = "";
        _txtRegFullName.Text = "";
        _txtRegPass.Text = "";
        _txtRegConfirm.Text = "";
        _txtRegCode.Text = "";
        HideError(_lblRegError);
    }

    private static void ShowError(WpfControls.TextBlock label, string message)
    {
        label.Text = message;
        label.Visibility = Wpf.Visibility.Visible;
    }

    private static void HideError(WpfControls.TextBlock label)
    {
        label.Text = "";
        label.Visibility = Wpf.Visibility.Collapsed;
    }
}

// ============================================================================
// LoginInputBox — ô nhập bo góc với placeholder, đổi viền khi focus/lỗi,
// hỗ trợ mật khẩu + "hiện mật khẩu" (bản WPF của LoginInputWrapPanel).
// ============================================================================
internal sealed class LoginInputBox : WpfControls.Border
{
    private readonly WpfControls.TextBox _text;
    private readonly WpfControls.PasswordBox _pass;
    private readonly WpfControls.TextBlock _placeholder;
    private readonly bool _isPassword;
    private bool _showPassword;
    private bool _focused;
    private bool _error;

    public LoginInputBox(string placeholder, bool isPassword = false)
    {
        _isPassword = isPassword;

        Height = 38;
        Background = WpfTheme.Surface;
        CornerRadius = new Wpf.CornerRadius(6);
        BorderThickness = new Wpf.Thickness(1);
        BorderBrush = WpfTheme.InputBorderNormal;
        Padding = new Wpf.Thickness(10, 0, 10, 0);
        SnapsToDevicePixels = true;

        _text = new WpfControls.TextBox
        {
            BorderThickness = new Wpf.Thickness(0),
            Background = WpfMedia.Brushes.Transparent,
            Foreground = WpfTheme.TextPrimary,
            FontFamily = WpfTheme.Font,
            FontSize = WpfTheme.Pt(10),
            VerticalContentAlignment = Wpf.VerticalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Stretch
        };

        _pass = new WpfControls.PasswordBox
        {
            BorderThickness = new Wpf.Thickness(0),
            Background = WpfMedia.Brushes.Transparent,
            Foreground = WpfTheme.TextPrimary,
            FontFamily = WpfTheme.Font,
            FontSize = WpfTheme.Pt(10),
            VerticalContentAlignment = Wpf.VerticalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Stretch
        };

        _placeholder = new WpfControls.TextBlock
        {
            Text = placeholder,
            Foreground = WpfTheme.TextMuted,
            FontFamily = WpfTheme.Font,
            FontSize = WpfTheme.Pt(10),
            IsHitTestVisible = false,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };

        var grid = new WpfControls.Grid();
        grid.Children.Add(_text);
        grid.Children.Add(_pass);
        grid.Children.Add(_placeholder);
        Child = grid;

        _text.Visibility = isPassword ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
        _pass.Visibility = isPassword ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;

        _text.GotKeyboardFocus += (_, _) => SetFocused(true);
        _text.LostKeyboardFocus += (_, _) => SetFocused(false);
        _text.TextChanged += (_, _) => UpdatePlaceholder();
        _pass.GotKeyboardFocus += (_, _) => SetFocused(true);
        _pass.LostKeyboardFocus += (_, _) => SetFocused(false);
        _pass.PasswordChanged += (_, _) => UpdatePlaceholder();

        UpdatePlaceholder();
    }

    public string Text
    {
        get => ActiveIsPassword ? _pass.Password : _text.Text;
        set
        {
            _text.Text = value ?? "";
            if (_isPassword)
            {
                _pass.Password = value ?? "";
            }

            UpdatePlaceholder();
        }
    }

    private bool ActiveIsPassword => _isPassword && !_showPassword;

    public bool ShowPassword
    {
        set
        {
            if (!_isPassword || _showPassword == value)
            {
                return;
            }

            _showPassword = value;
            if (value)
            {
                _text.Text = _pass.Password;
                _text.Visibility = Wpf.Visibility.Visible;
                _pass.Visibility = Wpf.Visibility.Collapsed;
            }
            else
            {
                _pass.Password = _text.Text;
                _pass.Visibility = Wpf.Visibility.Visible;
                _text.Visibility = Wpf.Visibility.Collapsed;
            }

            UpdatePlaceholder();
        }
    }

    public bool ErrorState
    {
        get => _error;
        set { _error = value; UpdateBorder(); }
    }

    public void FocusInput()
    {
        if (ActiveIsPassword)
        {
            _pass.Focus();
        }
        else
        {
            _text.Focus();
        }
    }

    private void SetFocused(bool focused)
    {
        _focused = focused;
        if (!focused)
        {
            _error = false;
        }

        UpdateBorder();
        UpdatePlaceholder();
    }

    private void UpdateBorder()
    {
        if (_error)
        {
            BorderBrush = WpfTheme.InputBorderError;
            BorderThickness = new Wpf.Thickness(1.5);
        }
        else if (_focused)
        {
            BorderBrush = WpfTheme.InputBorderFocus;
            BorderThickness = new Wpf.Thickness(1.5);
        }
        else
        {
            BorderBrush = WpfTheme.InputBorderNormal;
            BorderThickness = new Wpf.Thickness(1);
        }
    }

    private void UpdatePlaceholder()
    {
        var value = ActiveIsPassword ? _pass.Password : _text.Text;
        var empty = string.IsNullOrEmpty(value);
        _placeholder.Visibility = empty && !_focused ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }
}
