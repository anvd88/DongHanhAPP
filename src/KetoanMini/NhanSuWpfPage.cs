using System.Collections.ObjectModel;
using System.ComponentModel;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfData = System.Windows.Data;
using WpfEffects = System.Windows.Media.Effects;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;
using WpfShapes = System.Windows.Shapes;

namespace KetoanMini;

public sealed class NhanSuPasswordResetRequestedEventArgs(AppUser user) : EventArgs
{
    public AppUser User { get; } = user;
}

/// <summary>Raised when an admin locks or deletes an account, so the host can push an
/// instant force-logout to that user over the LAN.</summary>
public sealed class NhanSuAccountLockedEventArgs(string username) : EventArgs
{
    public string Username { get; } = username;
}

public sealed class NhanSuWpfPage : WpfControls.UserControl
{
    private readonly AccountingStore _store;
    private readonly AppUser _currentUser;
    private readonly ObservableCollection<NhanSuUserRow> _rows = [];

    private WpfControls.Grid _root = null!;
    private WpfControls.TextBox _searchBox = null!;
    private WpfControls.ComboBox _roleFilter = null!;
    private WpfControls.DataGrid _grid = null!;
    private WpfControls.TextBlock _footerText = null!;
    private WpfControls.Border _loadingOverlay = null!;
    private WpfControls.ProgressBar _loadingProgress = null!;
    private WpfControls.TextBlock _loadingPercentText = null!;
    private System.Windows.Threading.DispatcherTimer? _loadingTimer;
    private double _loadingValue;
    private bool _loaded;

    private List<AppUser> _allUsers = [];
    private Dictionary<string, UserPresence> _presence = new(StringComparer.OrdinalIgnoreCase);

    // Bảng màu hỗ trợ Sáng/Tối: mỗi token chọn hex theo ThemeState.IsDark.
    // Giữ nguyên hex chế độ Sáng; bổ sung hex chế độ Tối tương ứng để bật dark mode.
    private static bool Dark => ThemeState.IsDark;
    private static WpfMedia.Brush BackgroundBrush => Bn("#EDF4FC", "#080B10");
    private static WpfMedia.Brush SurfaceBrush => Bn("#BFFFFFFF", "#A6151B25");
    private static WpfMedia.Brush SurfaceAltBrush => Bn("#92FFFFFF", "#8C111720");
    private static WpfMedia.Brush LineBrush => Bn("#CFFFFFFF", "#667B8FA8");
    private static WpfMedia.Brush TextBrush => Bn("#07173A", "#F4F7FC");
    private static WpfMedia.Brush MutedBrush => Bn("#647694", "#A9B5C7");
    private static WpfMedia.Brush AccentBrush => Bn("#086CFF", "#3B8CFF");
    private static WpfMedia.Brush AccentSoftBrush => Bn("#E9F2FF", "#243B5E");
    private static WpfMedia.Brush SuccessBrush => Bn("#10B968", "#27D980");
    private static WpfMedia.Brush SuccessSoftBrush => Bn("#DDF9EC", "#173C2C");
    private static WpfMedia.Brush SuccessLineBrush => Bn("#AFEBCF", "#236B4B");
    private static WpfMedia.Brush WarningBrush => Bn("#D97706", "#F59E0B");
    private static WpfMedia.Brush WarningSoftBrush => Bn("#FEF3C7", "#1C1406");
    private static WpfMedia.Brush WarningLineBrush => Bn("#FDE68A", "#3A2E0A");
    private static WpfMedia.Brush DangerBrush => Bn("#DC2626", "#EF4444");
    private static WpfMedia.Brush DangerSoftBrush => Bn("#FEE2E2", "#1A0C0C");
    private static WpfMedia.Brush DangerLineBrush => Bn("#FECACA", "#3A1414");
    private static WpfMedia.Brush NeutralDotBrush => Bn("#9AA9BD", "#748198");
    private static WpfMedia.Brush TableFrameBrush => Bn("#FFFFFFFF", "#F2141A24");
    private static WpfMedia.Brush TableBodyBrush => Bn("#F6FAFF", "#F2111720");
    private static WpfMedia.Brush TableFooterBrush => Bn("#FBFDFF", "#F2161F2C");
    private static WpfMedia.Brush TableFooterLineBrush => Bn("#D8E5F3", "#45556B");
    private static WpfMedia.Color GlowColor => (WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(Dark ? "#8060A5FA" : "#AA5CA9FF")!;
    private static readonly WpfMedia.Brush TransparentBrush = WpfMedia.Brushes.Transparent;
    private static Dictionary<string, WpfMedia.Brush>? _brushCache;

    private static readonly Wpf.Style ChromeButtonStyle = CreateButtonStyle();
    // Row/Header style nhúng màu theo theme nên dựng lại mỗi lần dùng (theo theme hiện tại).
    private static Wpf.Style DataGridRowChrome => CreateRowStyle();
    private static readonly Wpf.Style DataGridCellChrome = CreateCellStyle();
    private static Wpf.Style DataGridHeaderChrome => CreateHeaderStyle();
    private static readonly Wpf.Style ActionButtonChrome = CreateActionButtonStyle();

    public event EventHandler? AddUserRequested;
    public event EventHandler? ResetRequestsRequested;
    public event EventHandler? OvertimeRequestsRequested;
    public event EventHandler? NotificationsChanged;
    public event EventHandler<NhanSuPasswordResetRequestedEventArgs>? PasswordResetRequested;
    public event EventHandler<NhanSuAccountLockedEventArgs>? AccountLocked;

    public NhanSuWpfPage(AccountingStore store, AppUser currentUser)
    {
        _store = store;
        _currentUser = currentUser;

        Background = BackgroundBrush;
        FontFamily = new WpfMedia.FontFamily("Segoe UI");
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        Content = BuildLayout();
        Loaded += async (_, _) =>
        {
            if (_loaded)
                return;

            _loaded = true;
            await RefreshUsersAsync(null, showLoading: true);
        };
        Unloaded += (_, _) => StopLoadingTimer();
    }

    public async void RefreshUsers()
        => await RefreshUsersAsync(GetSelectedUsername(), showLoading: true);

    public async void RefreshUsersQuiet()
        => await RefreshUsersAsync(GetSelectedUsername(), showLoading: false);

    public void RefreshPresenceOnly()
    {
        try
        {
            _presence = _store.GetUserPresence()
                .ToDictionary(p => p.Username, p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
                row.UpdatePresence(_presence);
        }
        catch
        {
            // Presence is best-effort; a transient DB/network miss should not reset the grid.
        }
    }

    private WpfControls.Grid BuildLayout()
    {
        _root = new WpfControls.Grid
        {
            Background = PageBackgroundBrush(),
            Margin = new Wpf.Thickness(24, 22, 24, 24)
        };
        _root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        _root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var header = BuildHeader();
        WpfControls.Grid.SetRow(header, 0);
        _root.Children.Add(header);

        var card = Card(new Wpf.Thickness(0));
        card.Margin = new Wpf.Thickness(0, 30, 0, 0);
        card.CornerRadius = new Wpf.CornerRadius(28);
        card.Child = BuildUserCard();
        WpfControls.Grid.SetRow(card, 1);
        _root.Children.Add(card);

        _loadingOverlay = BuildLoadingOverlay();
        WpfControls.Grid.SetRowSpan(_loadingOverlay, 2);
        WpfControls.Panel.SetZIndex(_loadingOverlay, 20);
        _root.Children.Add(_loadingOverlay);

        return _root;
    }

    private WpfControls.Grid BuildHeader()
    {
        var header = new WpfControls.Grid { Margin = new Wpf.Thickness(0, 0, 0, 0) };
        header.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        header.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var iconCard = HeaderIconBadge();
        iconCard.Width = 70;
        iconCard.Height = 70;
        iconCard.Child = IconText("\uE716", 31, AccentBrush);
        iconCard.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        WpfControls.Grid.SetColumn(iconCard, 0);
        header.Children.Add(iconCard);

        var textStack = new WpfControls.StackPanel
        {
            Margin = new Wpf.Thickness(20, 5, 0, 0),
            VerticalAlignment = Wpf.VerticalAlignment.Top
        };
        textStack.Children.Add(Text("Quản lý người dùng", 32, true, TextBrush));
        var subtitle = Text("Quản lý tài khoản và thông tin người dùng trong hệ thống.", 15, false, MutedBrush);
        subtitle.Margin = new Wpf.Thickness(0, 6, 0, 0);
        textStack.Children.Add(subtitle);
        WpfControls.Grid.SetColumn(textStack, 1);
        header.Children.Add(textStack);

        return header;
    }

    private static WpfControls.Border HeaderIconBadge()
        => new LiquidGlassBorder
        {
            Background = Bn("#FFFFFFFF", "#F0182433"),
            BaseBorderBrush = Bn("#DCE9F8", "#465A73"),
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(20),
            ClipChildToCornerRadius = true,
            ClipSelfToCornerRadius = true,
            EnableMouseGlow = false
        };

    private WpfControls.Grid BuildUserCard()
    {
        var cardGrid = new WpfControls.Grid();
        cardGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        cardGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var toolbar = BuildToolbar();
        WpfControls.Grid.SetRow(toolbar, 0);
        cardGrid.Children.Add(toolbar);

        var tableFrame = BuildTableFrame();
        WpfControls.Grid.SetRow(tableFrame, 1);
        cardGrid.Children.Add(tableFrame);

        return cardGrid;
    }

    private WpfControls.Border BuildTableFrame()
    {
        var tableGrid = new WpfControls.Grid();
        tableGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        tableGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });

        _grid = CreateGrid(_rows);
        _grid.Columns.Add(TextColumn("Tên đăng nhập", nameof(NhanSuUserRow.Username), 1.1, star: true, minWidth: 130));
        _grid.Columns.Add(TextColumn("Họ tên", nameof(NhanSuUserRow.FullName), 1.1, star: true, minWidth: 130));
        _grid.Columns.Add(PillColumn("Vai trò", nameof(NhanSuUserRow.Role), nameof(NhanSuUserRow.RoleForeground), nameof(NhanSuUserRow.RoleBackground), nameof(NhanSuUserRow.RoleBorder), 112));
        _grid.Columns.Add(PillColumn("Trạng thái", nameof(NhanSuUserRow.Status), nameof(NhanSuUserRow.StatusForeground), nameof(NhanSuUserRow.StatusBackground), nameof(NhanSuUserRow.StatusBorder), 164));
        _grid.Columns.Add(OnlineColumn("Trực tuyến", 1.0, star: true, minWidth: 130));
        _grid.Columns.Add(TextColumn("Phút hôm nay", nameof(NhanSuUserRow.MinutesToday), 118));
        _grid.Columns.Add(TextColumn("Ngày tạo", nameof(NhanSuUserRow.CreatedAtText), 1.0, star: true, minWidth: 152));
        _grid.Columns.Add(ActionColumn());
        _grid.PreviewMouseRightButtonDown += OnGridRightClick;

        var body = new WpfControls.Border
        {
            Background = TableBodyBrush,
            Child = _grid
        };
        WpfControls.Grid.SetRow(body, 0);
        tableGrid.Children.Add(body);

        var footer = BuildFooter();
        WpfControls.Grid.SetRow(footer, 1);
        tableGrid.Children.Add(footer);

        var frame = Card(new Wpf.Thickness(0));
        frame.Margin = new Wpf.Thickness(20, 0, 20, 20);
        frame.CornerRadius = new Wpf.CornerRadius(14);
        frame.Background = TableFrameBrush;
        if (frame is LiquidGlassBorder glass)
        {
            glass.BaseBorderBrush = Bn("#EDF5FF", "#63758D");
            glass.GlowRadiusX = 0.18;
            glass.GlowRadiusY = 0.34;
            glass.ClipChildToCornerRadius = true;
            glass.ClipSelfToCornerRadius = true;
        }
        frame.Child = tableGrid;
        return frame;
    }

    private WpfControls.Grid BuildToolbar()
    {
        var toolbarFrame = Card(new Wpf.Thickness(18));
        toolbarFrame.Margin = new Wpf.Thickness(20, 20, 20, 18);
        toolbarFrame.CornerRadius = new Wpf.CornerRadius(22);

        var toolbar = new WpfControls.Grid();
        toolbar.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star), MinWidth = 260 });
        toolbar.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(252) });
        toolbar.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });

        toolbar.Children.Add(BuildSearchBox());

        _roleFilter = BuildRoleFilter();
        _roleFilter.Margin = new Wpf.Thickness(14, 0, 0, 0);
        _roleFilter.SelectionChanged += (_, _) => ApplyUsers(GetSelectedUsername());
        WpfControls.Grid.SetColumn(_roleFilter, 1);
        toolbar.Children.Add(_roleFilter);

        var actions = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
            Margin = new Wpf.Thickness(14, 0, 0, 0)
        };

        var refresh = MakeButton("\uE72C", "Làm mới", primary: false, minWidth: 138);
        refresh.Click += (_, _) => RefreshUsers();
        actions.Children.Add(refresh);

        if (_currentUser.IsAdmin)
        {
            var more = MakeIconButton("\uE712", "Tác vụ khác");
            more.Click += (_, _) => ShowAdminQuickMenu(more);
            actions.Children.Add(more);

            var add = MakeButton("\uE710", "Thêm người dùng", primary: true, minWidth: 204);
            add.Click += (_, _) => AddUserRequested?.Invoke(this, EventArgs.Empty);
            actions.Children.Add(add);
        }

        WpfControls.Grid.SetColumn(actions, 2);
        toolbar.Children.Add(actions);
        toolbarFrame.Child = toolbar;
        return new WpfControls.Grid { Children = { toolbarFrame } };
    }

    private WpfControls.Border BuildSearchBox()
    {
        var border = InputFrame();
        var host = new WpfControls.Grid();
        var icon = IconText("\uE721", 17, MutedBrush);
        icon.Margin = new Wpf.Thickness(12, 0, 0, 1);
        icon.HorizontalAlignment = Wpf.HorizontalAlignment.Left;

        var placeholder = Text("Tìm kiếm theo tên đăng nhập hoặc họ tên...", 14, false, MutedBrush);
        placeholder.Margin = new Wpf.Thickness(42, 0, 10, 0);

        _searchBox = new WpfControls.TextBox
        {
            BorderThickness = new Wpf.Thickness(0),
            Background = TransparentBrush,
            Foreground = TextBrush,
            FontSize = 14,
            Padding = new Wpf.Thickness(42, 0, 10, 1),
            VerticalContentAlignment = Wpf.VerticalAlignment.Center
        };
        _searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrWhiteSpace(_searchBox.Text)
                ? Wpf.Visibility.Visible
                : Wpf.Visibility.Collapsed;
            ApplyUsers(GetSelectedUsername());
        };

        host.Children.Add(placeholder);
        host.Children.Add(_searchBox);
        host.Children.Add(icon);
        border.Child = host;
        return border;
    }

    private static WpfControls.ComboBox BuildRoleFilter()
    {
        var combo = new WpfControls.ComboBox
        {
            Width = 252,
            Height = 58,
            FontSize = 15,
            Padding = new Wpf.Thickness(16, 0, 10, 0),
            VerticalContentAlignment = Wpf.VerticalAlignment.Center,
            Background = SurfaceAltBrush,
            BorderBrush = LineBrush,
            Foreground = TextBrush
        };
        combo.Items.Add("Tất cả vai trò");
        combo.Items.Add("Admin");
        combo.Items.Add("User");
        combo.Items.Add("Chờ duyệt");
        combo.Items.Add("Đã khóa");
        combo.SelectedIndex = 0;
        return combo;
    }

    private WpfControls.Border BuildFooter()
    {
        var footer = new WpfControls.Border
        {
            BorderBrush = TableFooterLineBrush,
            BorderThickness = new Wpf.Thickness(0, 1, 0, 0),
            Padding = new Wpf.Thickness(24, 16, 24, 16),
            Background = TableFooterBrush
        };

        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });

        _footerText = Text("Hiển thị 0 kết quả", 14, false, MutedBrush);
        grid.Children.Add(_footerText);

        var pager = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right
        };
        pager.Children.Add(MakeFooterBox("10 / trang", 126));
        pager.Children.Add(MakeFooterBox("\uE76B", 44, isIcon: true, muted: true));
        pager.Children.Add(MakeFooterBox("1", 44, accent: true));
        pager.Children.Add(MakeFooterBox("\uE76C", 44, isIcon: true, muted: true));
        WpfControls.Grid.SetColumn(pager, 1);
        grid.Children.Add(pager);

        footer.Child = grid;
        return footer;
    }

    private WpfControls.Border BuildLoadingOverlay()
    {
        _loadingProgress = new WpfControls.ProgressBar
        {
            Width = 290,
            Height = 7,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Foreground = AccentBrush,
            Background = Bn("#D8E4F3", "#1C222B"),
            Margin = new Wpf.Thickness(0, 14, 0, 0)
        };
        _loadingPercentText = Text("0%", 12, true, AccentBrush);
        _loadingPercentText.HorizontalAlignment = Wpf.HorizontalAlignment.Center;
        _loadingPercentText.Margin = new Wpf.Thickness(0, 7, 0, 0);

        var stack = new WpfControls.StackPanel
        {
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        stack.Children.Add(Text("Đang tải trang Nhân sự...", 16, true, TextBrush));
        
        stack.Children.Add(_loadingProgress);
        stack.Children.Add(_loadingPercentText);

        return new WpfControls.Border
        {
            Background = new WpfMedia.SolidColorBrush(Dark
                ? WpfMedia.Color.FromArgb(247, 5, 6, 8)
                : WpfMedia.Color.FromArgb(238, 237, 244, 252)),
            Child = stack,
            Visibility = Wpf.Visibility.Visible
        };
    }

    private async Task RefreshUsersAsync(string? selectedUsername, bool showLoading)
    {
        if (showLoading)
            StartLoading();

        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
        if (showLoading)
            await Task.Delay(90);

        try
        {
            _allUsers = _store.GetUsers().ToList();
            _presence = _store.GetUserPresence()
                .ToDictionary(p => p.Username, p => p, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _allUsers = [];
            _presence = new Dictionary<string, UserPresence>(StringComparer.OrdinalIgnoreCase);
            ShowError(ex);
        }

        ApplyUsers(selectedUsername);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);

        if (showLoading)
            await FinishLoadingAsync();
    }

    private void ApplyUsers(string? selectedUsername)
    {
        if (_grid is null)
            return;

        selectedUsername ??= GetSelectedUsername();
        var query = Normalize(_searchBox?.Text ?? "");
        var filter = _roleFilter?.SelectedItem?.ToString() ?? "Tất cả vai trò";

        IEnumerable<AppUser> filtered = _allUsers;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(u =>
                Normalize(u.Username).Contains(query) ||
                Normalize(u.FullName).Contains(query) ||
                Normalize(u.Role).Contains(query));
        }

        filtered = filter switch
        {
            "Admin" => filtered.Where(u => u.IsAdmin),
            "User" => filtered.Where(u => !u.IsAdmin),
            "Chờ duyệt" => filtered.Where(u => u.IsPendingApproval),
            "Đã khóa" => filtered.Where(u => !u.IsActive),
            _ => filtered
        };

        var rows = filtered
            .OrderByDescending(u => u.IsPendingApproval)
            .ThenByDescending(u => u.IsAdmin)
            .ThenBy(u => Clean(u.Username))
            .Select(u => new NhanSuUserRow(u, _presence))
            .ToList();

        _rows.Clear();
        foreach (var row in rows)
            _rows.Add(row);

        UpdateFooterText();

        if (!string.IsNullOrWhiteSpace(selectedUsername))
        {
            var selected = _rows.FirstOrDefault(r =>
                string.Equals(r.Source.Username, selectedUsername, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _grid.SelectedItem = selected;
                _grid.ScrollIntoView(selected);
            }
        }
    }

    private string? GetSelectedUsername()
        => _grid?.SelectedItem is NhanSuUserRow row ? row.Source.Username : null;

    private void UpdateFooterText()
    {
        if (_footerText is null)
            return;

        _footerText.Text = _rows.Count == 0
            ? "Không có kết quả phù hợp"
            : $"Hiển thị 1 đến {_rows.Count} của {_rows.Count} kết quả";
    }

    private void OnGridRightClick(object sender, WpfInput.MouseButtonEventArgs e)
    {
        var row = FindParent<WpfControls.DataGridRow>(e.OriginalSource as Wpf.DependencyObject);
        if (row?.Item is not NhanSuUserRow userRow)
            return;

        row.IsSelected = true;
        _grid.SelectedItem = userRow;
        ShowUserContextMenu(userRow, row, WpfPrimitives.PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void OnActionButtonClick(object sender, Wpf.RoutedEventArgs e)
    {
        if (sender is not WpfControls.Button button || button.DataContext is not NhanSuUserRow userRow)
            return;

        _grid.SelectedItem = userRow;
        ShowUserContextMenu(userRow, button, WpfPrimitives.PlacementMode.Bottom);
        e.Handled = true;
    }

    private void ShowAdminQuickMenu(WpfControls.Button button)
    {
        var menu = CreateGlassContextMenu();
        var reset = new WpfControls.MenuItem { Header = "Yêu cầu đổi mật khẩu" };
        reset.Click += (_, _) => ResetRequestsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(reset);

        var overtime = new WpfControls.MenuItem { Header = "Duyệt tăng ca" };
        overtime.Click += (_, _) => OvertimeRequestsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(overtime);

        menu.PlacementTarget = button;
        menu.Placement = WpfPrimitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowUserContextMenu(NhanSuUserRow row, WpfControls.Control target, WpfPrimitives.PlacementMode placement)
    {
        if (!_currentUser.IsAdmin)
            return;

        var menu = CreateGlassContextMenu();
        var added = false;

        if (row.Source.IsPendingApproval)
        {
            var approve = new WpfControls.MenuItem { Header = "Duyệt tài khoản" };
            approve.Click += (_, _) => ApproveUser(row);
            menu.Items.Add(approve);
            added = true;
        }

        if (!row.Source.IsAdmin)
        {
            var reset = new WpfControls.MenuItem { Header = "Cấp mã đổi mật khẩu" };
            reset.Click += (_, _) => PasswordResetRequested?.Invoke(this, new NhanSuPasswordResetRequestedEventArgs(row.Source));
            menu.Items.Add(reset);
            added = true;
        }

        if (added)
            menu.Items.Add(new WpfControls.Separator());

        var toggle = new WpfControls.MenuItem
        {
            Header = row.Source.IsActive ? "Khóa tài khoản" : "Kích hoạt tài khoản"
        };
        toggle.Click += (_, _) => ToggleUserActive(row);
        menu.Items.Add(toggle);

        var delete = new WpfControls.MenuItem { Header = "Xóa người dùng" };
        delete.Click += (_, _) =>
        {
            var confirm = Wpf.MessageBox.Show(
                $"Xóa người dùng {row.Source.Username}?",
                "Xác nhận",
                Wpf.MessageBoxButton.YesNo,
                Wpf.MessageBoxImage.Warning);
            if (confirm != Wpf.MessageBoxResult.Yes)
                return;

            DeleteUser(row);
        };
        menu.Items.Add(delete);

        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private static WpfControls.ContextMenu CreateGlassContextMenu()
    {
        return new WpfControls.ContextMenu
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            Padding = new Wpf.Thickness(6),
            FontFamily = new WpfMedia.FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = TextBrush,
            Effect = new WpfEffects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = Dark ? 0.42 : 0.18,
                Color = Dark ? WpfMedia.Colors.Black : WpfMedia.Color.FromRgb(90, 122, 160)
            }
        };
    }

    private void ApproveUser(NhanSuUserRow row)
    {
        try
        {
            _store.AdminApproveUser(row.Source.Id);
            var updated = _store.GetUsers().FirstOrDefault(u => u.Id == row.Source.Id);
            if (updated is not null)
                row.ApplyUser(updated);
            else
                row.ApplyApproval();

            RemoveRowIfFilteredOut(row);
            SelectRow(row);
            UpdateFooterText();
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ToggleUserActive(NhanSuUserRow row)
    {
        try
        {
            var willLock = row.Source.IsActive; // active -> locked after the toggle
            var updated = _store.AdminUpdateUser(
                row.Source.Id,
                row.Source.Username,
                row.Source.FullName,
                "",
                !row.Source.IsActive);

            row.ApplyUser(updated);
            RemoveRowIfFilteredOut(row);
            SelectRow(row);
            UpdateFooterText();
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
            if (willLock)
                AccountLocked?.Invoke(this, new NhanSuAccountLockedEventArgs(updated.Username));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void DeleteUser(NhanSuUserRow row)
    {
        try
        {
            var deletedUsername = row.Source.Username;
            _store.AdminDeleteUser(row.Source.Id);
            AvatarStore.Delete(row.Source.Username);
            _allUsers.RemoveAll(u => u.Id == row.Source.Id);
            _rows.Remove(row);
            UpdateFooterText();
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
            // A deleted account is also disabled (is_active = 0) — kick it off instantly too.
            AccountLocked?.Invoke(this, new NhanSuAccountLockedEventArgs(deletedUsername));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void RemoveRowIfFilteredOut(NhanSuUserRow row)
    {
        if (!UserMatchesCurrentFilter(row.Source))
            _rows.Remove(row);
    }

    private void SelectRow(NhanSuUserRow row)
    {
        if (!_rows.Contains(row))
            return;

        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row);
    }

    private bool UserMatchesCurrentFilter(AppUser user)
    {
        var query = Normalize(_searchBox?.Text ?? "");
        if (!string.IsNullOrWhiteSpace(query)
            && !Normalize(user.Username).Contains(query)
            && !Normalize(user.FullName).Contains(query)
            && !Normalize(user.Role).Contains(query))
        {
            return false;
        }

        var filter = _roleFilter?.SelectedItem?.ToString() ?? "Tất cả vai trò";
        return filter switch
        {
            "Admin" => user.IsAdmin,
            "User" => !user.IsAdmin,
            "Chờ duyệt" => user.IsPendingApproval,
            "Đã khóa" => !user.IsActive,
            _ => true
        };
    }

    private void StartLoading()
    {
        if (_loadingOverlay is null)
            return;

        _loadingValue = 0;
        _loadingProgress.Value = 0;
        _loadingPercentText.Text = "0%";
        _loadingOverlay.Visibility = Wpf.Visibility.Visible;
        StopLoadingTimer();

        _loadingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(22)
        };
        _loadingTimer.Tick += (_, _) =>
        {
            _loadingValue = Math.Min(96, _loadingValue < 70 ? _loadingValue + 5 : _loadingValue + 1.5);
            _loadingProgress.Value = _loadingValue;
            _loadingPercentText.Text = $"{(int)_loadingValue}%";
        };
        _loadingTimer.Start();
    }

    private async Task FinishLoadingAsync()
    {
        StopLoadingTimer();
        _loadingProgress.Value = 100;
        _loadingPercentText.Text = "100%";
        await Task.Delay(110);
        _loadingOverlay.Visibility = Wpf.Visibility.Collapsed;
    }

    private void StopLoadingTimer()
    {
        _loadingTimer?.Stop();
        _loadingTimer = null;
    }

    private static void ShowError(Exception ex)
        => Wpf.MessageBox.Show(ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);

    private static T? FindParent<T>(Wpf.DependencyObject? child) where T : Wpf.DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
                return match;
            child = WpfMedia.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static WpfControls.Button MakeButton(string icon, string text, bool primary, double minWidth)
    {
        var panel = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        var iconText = IconText(icon, 15, primary ? WpfMedia.Brushes.White : AccentBrush);
        iconText.Margin = new Wpf.Thickness(0, 0, 8, 0);
        panel.Children.Add(iconText);
        panel.Children.Add(Text(text, 13, true, primary ? WpfMedia.Brushes.White : TextBrush));

        return new WpfControls.Button
        {
            Content = panel,
            Height = 58,
            MinWidth = minWidth,
            Padding = new Wpf.Thickness(16, 0, 16, 1),
            Margin = new Wpf.Thickness(0, 0, 10, 0),
            Background = primary ? AccentBrush : SurfaceAltBrush,
            Foreground = primary ? WpfMedia.Brushes.White : TextBrush,
            BorderBrush = primary ? AccentBrush : LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            FontSize = 13,
            FontWeight = Wpf.FontWeights.SemiBold,
            Cursor = WpfInput.Cursors.Hand,
            Style = ChromeButtonStyle
        };
    }

    private static WpfControls.Button MakeIconButton(string icon, string tooltip)
        => new()
        {
            Content = IconText(icon, 16, AccentBrush),
            Width = 58,
            Height = 58,
            Margin = new Wpf.Thickness(0, 0, 10, 0),
            Background = SurfaceAltBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            Cursor = WpfInput.Cursors.Hand,
            ToolTip = tooltip,
            Style = ChromeButtonStyle
        };

    private static WpfControls.Border MakeFooterBox(string text, double width, bool isIcon = false, bool accent = false, bool muted = false)
        => new LiquidGlassBorder
        {
            Width = width,
            Height = 48,
            Margin = new Wpf.Thickness(8, 0, 0, 0),
            Background = accent ? AccentBrush : Bn("#F3F8FF", "#1B2534"),
            BaseBorderBrush = accent ? AccentBrush : Bn("#D8E6F6", "#3A4B62"),
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(12),
            GlowColor = GlowColor,
            Child = isIcon
                ? IconText(text, 14, muted ? MutedBrush : TextBrush)
                : Text(text, 14, accent, accent ? WpfMedia.Brushes.White : TextBrush)
        };

    private static WpfControls.Border InputFrame()
        => new LiquidGlassBorder
        {
            Height = 58,
            Background = SurfaceAltBrush,
            BaseBorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(16),
            GlowColor = GlowColor
        };

    private static WpfControls.Border Card(Wpf.Thickness padding)
        => new LiquidGlassBorder
        {
            Background = SurfaceBrush,
            BaseBorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(20),
            Padding = padding,
            GlowColor = GlowColor,
            ClipChildToCornerRadius = true,
            ClipSelfToCornerRadius = true,
            Effect = new WpfEffects.DropShadowEffect
            {
                BlurRadius = 28,
                Color = Dark ? WpfMedia.Colors.Black : WpfMedia.Color.FromRgb(125, 168, 216),
                Direction = 270,
                Opacity = Dark ? 0.38 : 0.22,
                ShadowDepth = 6
            }
        };

    private static WpfControls.TextBlock Text(string value, double size, bool strong, WpfMedia.Brush brush)
        => new()
        {
            Text = Clean(value),
            FontSize = size,
            FontWeight = strong ? Wpf.FontWeights.SemiBold : Wpf.FontWeights.Normal,
            Foreground = brush,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis
        };

    private static WpfControls.TextBlock IconText(string glyph, double size, WpfMedia.Brush brush)
        => new()
        {
            Text = glyph,
            FontFamily = new WpfMedia.FontFamily("Segoe MDL2 Assets"),
            FontSize = size,
            FontWeight = Wpf.FontWeights.Normal,
            Foreground = brush,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            TextTrimming = Wpf.TextTrimming.None
        };

    private static WpfControls.DataGrid CreateGrid(System.Collections.IEnumerable itemsSource)
    {
        var grid = new WpfControls.DataGrid
        {
            ItemsSource = itemsSource,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeColumns = false,
            CanUserResizeRows = false,
            CanUserSortColumns = false,
            IsReadOnly = true,
            SelectionMode = WpfControls.DataGridSelectionMode.Single,
            SelectionUnit = WpfControls.DataGridSelectionUnit.FullRow,
            HeadersVisibility = WpfControls.DataGridHeadersVisibility.Column,
            GridLinesVisibility = WpfControls.DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Bn("#80D7E3F1", "#33405260"),
            VerticalGridLinesBrush = TransparentBrush,
            Background = TransparentBrush,
            BorderBrush = TransparentBrush,
            BorderThickness = new Wpf.Thickness(0),
            RowHeight = 54,
            ColumnHeaderHeight = 50,
            FontSize = 14,
            AlternatingRowBackground = Bn("#30F5F9FF", "#14151B25"),
            RowStyle = DataGridRowChrome,
            CellStyle = DataGridCellChrome,
            ColumnHeaderStyle = DataGridHeaderChrome,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };

        WpfControls.ScrollViewer.SetCanContentScroll(grid, true);
        WpfControls.ScrollViewer.SetHorizontalScrollBarVisibility(grid, WpfControls.ScrollBarVisibility.Auto);
        WpfControls.ScrollViewer.SetVerticalScrollBarVisibility(grid, WpfControls.ScrollBarVisibility.Auto);
        WpfControls.VirtualizingPanel.SetIsVirtualizing(grid, true);
        WpfControls.VirtualizingPanel.SetVirtualizationMode(grid, WpfControls.VirtualizationMode.Recycling);
        return grid;
    }

    private static WpfControls.DataGridTextColumn TextColumn(string header, string binding, double width, bool star = false, double minWidth = 70)
        => new()
        {
            Header = header,
            Binding = new WpfData.Binding(binding),
            Width = star
                ? new WpfControls.DataGridLength(width, WpfControls.DataGridLengthUnitType.Star)
                : new WpfControls.DataGridLength(width),
            MinWidth = minWidth,
            ElementStyle = CellTextStyle(),
            CanUserResize = false,
            CanUserSort = false
        };

    private static WpfControls.DataGridTemplateColumn PillColumn(
        string header,
        string textBinding,
        string foreBinding,
        string backBinding,
        string borderBinding,
        double width)
    {
        var borderFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border));
        borderFactory.SetBinding(WpfControls.Border.BackgroundProperty, new WpfData.Binding(backBinding));
        borderFactory.SetBinding(WpfControls.Border.BorderBrushProperty, new WpfData.Binding(borderBinding));
        borderFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Wpf.Thickness(1));
        borderFactory.SetValue(WpfControls.Border.CornerRadiusProperty, new Wpf.CornerRadius(10));
        borderFactory.SetValue(WpfControls.Border.PaddingProperty, new Wpf.Thickness(12, 5, 12, 6));
        borderFactory.SetValue(WpfControls.Border.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Center);
        borderFactory.SetValue(WpfControls.Border.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);

        var textFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.TextBlock));
        textFactory.SetBinding(WpfControls.TextBlock.TextProperty, new WpfData.Binding(textBinding));
        textFactory.SetBinding(WpfControls.TextBlock.ForegroundProperty, new WpfData.Binding(foreBinding));
        textFactory.SetValue(WpfControls.TextBlock.FontSizeProperty, 13.0);
        textFactory.SetValue(WpfControls.TextBlock.FontWeightProperty, Wpf.FontWeights.SemiBold);
        borderFactory.AppendChild(textFactory);

        return new WpfControls.DataGridTemplateColumn
        {
            Header = header,
            Width = new WpfControls.DataGridLength(width),
            MinWidth = width,
            CellTemplate = new Wpf.DataTemplate { VisualTree = borderFactory },
            CanUserResize = false,
            CanUserSort = false
        };
    }

    private static WpfControls.DataGridTemplateColumn OnlineColumn(string header, double width, bool star = false, double minWidth = 100)
    {
        var gridFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Grid));
        gridFactory.SetValue(WpfControls.Grid.MarginProperty, new Wpf.Thickness(12, 0, 8, 0));

        var dotFactory = new Wpf.FrameworkElementFactory(typeof(WpfShapes.Ellipse));
        dotFactory.SetValue(WpfShapes.Ellipse.WidthProperty, 9.0);
        dotFactory.SetValue(WpfShapes.Ellipse.HeightProperty, 9.0);
        dotFactory.SetValue(WpfShapes.Ellipse.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        dotFactory.SetValue(WpfShapes.Ellipse.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Left);
        dotFactory.SetBinding(WpfShapes.Ellipse.FillProperty, new WpfData.Binding(nameof(NhanSuUserRow.OnlineDotBrush)));
        gridFactory.AppendChild(dotFactory);

        var textFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.TextBlock));
        textFactory.SetValue(WpfControls.TextBlock.MarginProperty, new Wpf.Thickness(18, 0, 0, 0));
        textFactory.SetValue(WpfControls.TextBlock.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        textFactory.SetValue(WpfControls.TextBlock.FontSizeProperty, 14.0);
        textFactory.SetValue(WpfControls.TextBlock.TextTrimmingProperty, Wpf.TextTrimming.CharacterEllipsis);
        textFactory.SetBinding(WpfControls.TextBlock.TextProperty, new WpfData.Binding(nameof(NhanSuUserRow.OnlineText)));
        textFactory.SetBinding(WpfControls.TextBlock.ForegroundProperty, new WpfData.Binding(nameof(NhanSuUserRow.OnlineTextBrush)));
        gridFactory.AppendChild(textFactory);

        return new WpfControls.DataGridTemplateColumn
        {
            Header = header,
            Width = star
                ? new WpfControls.DataGridLength(width, WpfControls.DataGridLengthUnitType.Star)
                : new WpfControls.DataGridLength(width),
            MinWidth = minWidth,
            CellTemplate = new Wpf.DataTemplate { VisualTree = gridFactory },
            CanUserResize = false,
            CanUserSort = false
        };
    }

    private WpfControls.DataGridTemplateColumn ActionColumn()
    {
        var buttonFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Button));
        buttonFactory.SetValue(WpfControls.Button.ContentProperty, "\u22EE");
        buttonFactory.SetValue(WpfControls.Button.WidthProperty, 34.0);
        buttonFactory.SetValue(WpfControls.Button.HeightProperty, 34.0);
        buttonFactory.SetValue(WpfControls.Button.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Center);
        buttonFactory.SetValue(WpfControls.Button.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        buttonFactory.SetValue(WpfControls.Button.ForegroundProperty, Bn("#7084A3", "#A9B5C7"));
        buttonFactory.SetValue(WpfControls.Button.BackgroundProperty, TransparentBrush);
        buttonFactory.SetValue(WpfControls.Button.BorderBrushProperty, TransparentBrush);
        buttonFactory.SetValue(WpfControls.Button.CursorProperty, WpfInput.Cursors.Hand);
        buttonFactory.SetValue(WpfControls.Button.StyleProperty, ActionButtonChrome);
        buttonFactory.AddHandler(WpfControls.Button.ClickEvent, new Wpf.RoutedEventHandler(OnActionButtonClick));

        return new WpfControls.DataGridTemplateColumn
        {
            Header = "",
            Width = new WpfControls.DataGridLength(46),
            MinWidth = 46,
            CellTemplate = new Wpf.DataTemplate { VisualTree = buttonFactory },
            CanUserResize = false,
            CanUserSort = false
        };
    }

    private static Wpf.Style CellTextStyle()
    {
        var style = new Wpf.Style(typeof(WpfControls.TextBlock));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.PaddingProperty, new Wpf.Thickness(20, 0, 20, 0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.TextTrimmingProperty, Wpf.TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.ForegroundProperty, TextBrush));
        return style;
    }

    private static Wpf.Style CreateRowStyle()
    {
        var style = new Wpf.Style(typeof(WpfControls.DataGridRow));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Bn("#54FFFFFF", "#22151B25")));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, Bn("#DCE7F3", "#2A3A4C")));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Wpf.Setter(WpfControls.DataGridRow.SnapsToDevicePixelsProperty, true));

        var selected = new Wpf.Trigger { Property = WpfControls.DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Bn("#9FDCEAFF", "#3345687A")));
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, AccentBrush));
        style.Triggers.Add(selected);

        var hover = new Wpf.Trigger { Property = WpfControls.DataGridRow.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Bn("#75EAF3FF", "#2A223044")));
        style.Triggers.Add(hover);
        return style;
    }

    private static Wpf.Style CreateCellStyle()
    {
        var style = new Wpf.Style(typeof(WpfControls.DataGridCell));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, TransparentBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, TransparentBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.PaddingProperty, new Wpf.Thickness(0)));

        var selected = new Wpf.Trigger { Property = WpfControls.DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, TransparentBrush));
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, TransparentBrush));
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0)));
        style.Triggers.Add(selected);

        var focused = new Wpf.Trigger { Property = WpfControls.DataGridCell.IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, TransparentBrush));
        focused.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0)));
        style.Triggers.Add(focused);
        return style;
    }

    private static Wpf.Style CreateHeaderStyle()
    {
        var style = new Wpf.Style(typeof(WpfPrimitives.DataGridColumnHeader));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Bn("#DAFFFFFF", "#8C111720")));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FontWeightProperty, Wpf.FontWeights.SemiBold));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FontSizeProperty, 14.5));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, LineBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.PaddingProperty, new Wpf.Thickness(20, 0, 20, 0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.HorizontalContentAlignmentProperty, Wpf.HorizontalAlignment.Left));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.TemplateProperty, CreateHeaderTemplate()));
        return style;
    }

    private static WpfControls.ControlTemplate CreateHeaderTemplate()
    {
        var borderFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border));
        borderFactory.SetValue(WpfControls.Border.BackgroundProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BackgroundProperty));
        borderFactory.SetValue(WpfControls.Border.BorderBrushProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BorderBrushProperty));
        borderFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BorderThicknessProperty));

        var presenter = new Wpf.FrameworkElementFactory(typeof(WpfControls.ContentPresenter));
        presenter.SetValue(WpfControls.ContentPresenter.MarginProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.PaddingProperty));
        presenter.SetValue(WpfControls.ContentPresenter.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        presenter.SetValue(WpfControls.ContentPresenter.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Left);
        borderFactory.AppendChild(presenter);

        return new WpfControls.ControlTemplate(typeof(WpfPrimitives.DataGridColumnHeader)) { VisualTree = borderFactory };
    }

    private static Wpf.Style CreateButtonStyle()
    {
        var borderFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border));
        borderFactory.SetValue(WpfControls.Border.BackgroundProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BackgroundProperty));
        borderFactory.SetValue(WpfControls.Border.BorderBrushProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BorderBrushProperty));
        borderFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BorderThicknessProperty));
        borderFactory.SetValue(WpfControls.Border.CornerRadiusProperty, new Wpf.CornerRadius(14));

        var presenter = new Wpf.FrameworkElementFactory(typeof(WpfControls.ContentPresenter));
        presenter.SetValue(WpfControls.ContentPresenter.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Center);
        presenter.SetValue(WpfControls.ContentPresenter.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        borderFactory.AppendChild(presenter);

        var template = new WpfControls.ControlTemplate(typeof(WpfControls.Button)) { VisualTree = borderFactory };
        var style = new Wpf.Style(typeof(WpfControls.Button));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.TemplateProperty, template));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.HorizontalContentAlignmentProperty, Wpf.HorizontalAlignment.Center));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.VerticalContentAlignmentProperty, Wpf.VerticalAlignment.Center));
        return style;
    }

    private static Wpf.Style CreateActionButtonStyle()
    {
        var borderFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border));
        borderFactory.SetValue(WpfControls.Border.BackgroundProperty, TransparentBrush);
        borderFactory.SetValue(WpfControls.Border.BorderBrushProperty, TransparentBrush);
        borderFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Wpf.Thickness(0));

        var presenter = new Wpf.FrameworkElementFactory(typeof(WpfControls.ContentPresenter));
        presenter.SetValue(WpfControls.ContentPresenter.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Center);
        presenter.SetValue(WpfControls.ContentPresenter.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);
        borderFactory.AppendChild(presenter);

        var template = new WpfControls.ControlTemplate(typeof(WpfControls.Button)) { VisualTree = borderFactory };
        var style = new Wpf.Style(typeof(WpfControls.Button));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.TemplateProperty, template));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FocusVisualStyleProperty, null));
        var hover = new Wpf.Trigger { Property = WpfControls.Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Bn("#DDEBFF", "#26364A")));
        hover.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, AccentBrush));
        style.Triggers.Add(hover);
        return style;
    }

    private static WpfMedia.Brush RoleBrush(string role)
        => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? Bn("#0564E8", "#60A5FA") : AccentBrush;

    private static WpfMedia.Brush RoleSoftBrush(string role)
        => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? Bn("#E6F1FF", "#203654") : Bn("#EDF5FF", "#1B2C46");

    private static WpfMedia.Brush RoleLineBrush(string role)
        => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? Bn("#B6D3FF", "#315E92") : Bn("#C6DCFF", "#31506F");

    private static string StatusText(AppUser user)
    {
        if (user.IsPendingApproval)
            return "Chờ duyệt";
        return user.IsActive ? "Đang hoạt động" : "Đã khóa";
    }

    private static WpfMedia.Brush StatusBrush(AppUser user)
    {
        if (user.IsPendingApproval)
            return WarningBrush;
        return user.IsActive ? SuccessBrush : DangerBrush;
    }

    private static WpfMedia.Brush StatusSoftBrush(AppUser user)
    {
        if (user.IsPendingApproval)
            return WarningSoftBrush;
        return user.IsActive ? SuccessSoftBrush : DangerSoftBrush;
    }

    private static WpfMedia.Brush StatusLineBrush(AppUser user)
    {
        if (user.IsPendingApproval)
            return WarningLineBrush;
        return user.IsActive ? SuccessLineBrush : DangerLineBrush;
    }

    // Cache brush theo mã hex để getter màu không tạo lại brush mỗi lần đọc.
    /// <summary>Chọn brush theo theme: hex chế độ Sáng hoặc Tối (đã cache + Freeze).</summary>
    private static WpfMedia.Brush Bn(string light, string dark)
    {
        _brushCache ??= new Dictionary<string, WpfMedia.Brush>(StringComparer.OrdinalIgnoreCase);

        var hex = Dark ? dark : light;
        if (!_brushCache.TryGetValue(hex, out var brush))
        {
            brush = Brush(hex);
            _brushCache[hex] = brush;
        }

        return brush;
    }

    private static WpfMedia.Brush PageBackgroundBrush()
    {
        if (Dark)
        {
            return new WpfMedia.LinearGradientBrush(
                WpfMedia.Color.FromRgb(8, 11, 16),
                WpfMedia.Color.FromRgb(14, 19, 27),
                new Wpf.Point(0, 0),
                new Wpf.Point(1, 1));
        }

        var brush = new WpfMedia.LinearGradientBrush
        {
            StartPoint = new Wpf.Point(0, 0),
            EndPoint = new Wpf.Point(1, 1)
        };
        brush.GradientStops.Add(new WpfMedia.GradientStop(WpfMedia.Color.FromRgb(248, 251, 255), 0));
        brush.GradientStops.Add(new WpfMedia.GradientStop(WpfMedia.Color.FromRgb(238, 246, 255), 0.48));
        brush.GradientStops.Add(new WpfMedia.GradientStop(WpfMedia.Color.FromRgb(229, 240, 252), 1));
        return brush;
    }

    private static WpfMedia.SolidColorBrush Brush(string hex)
    {
        var brush = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    private static string Clean(string value)
        => TextUtil.RepairMojibake(value ?? "");

    private static string Normalize(string value)
        => TextUtil.RemoveDiacritics(Clean(value)).ToLowerInvariant();

    private sealed class NhanSuUserRow : INotifyPropertyChanged
    {
        private UserPresence? _presence;

        public NhanSuUserRow(AppUser source, IReadOnlyDictionary<string, UserPresence> presence)
        {
            Source = source;
            presence.TryGetValue(source.Username, out _presence);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public AppUser Source { get; }
        public string Username => Clean(Source.Username);
        public string FullName => Clean(Source.FullName);
        public string Role => Clean(Source.Role);
        public string Status => StatusText(Source);
        public string CreatedAtText => Source.CreatedAt.ToString("dd/MM/yyyy HH:mm");
        public string OnlineText => IsOnline ? "Trực tuyến" : "Ngoại tuyến";
        public string MinutesToday => $"{(_presence?.MinutesToday ?? 0)} phút";
        public bool IsOnline => _presence?.IsOnline == true;
        public WpfMedia.Brush RoleForeground => RoleBrush(Source.Role);
        public WpfMedia.Brush RoleBackground => RoleSoftBrush(Source.Role);
        public WpfMedia.Brush RoleBorder => RoleLineBrush(Source.Role);
        public WpfMedia.Brush StatusForeground => StatusBrush(Source);
        public WpfMedia.Brush StatusBackground => StatusSoftBrush(Source);
        public WpfMedia.Brush StatusBorder => StatusLineBrush(Source);
        public WpfMedia.Brush OnlineDotBrush => IsOnline ? SuccessBrush : NeutralDotBrush;
        public WpfMedia.Brush OnlineTextBrush => IsOnline ? TextBrush : MutedBrush;

        public void ApplyUser(AppUser updated)
        {
            Source.Username = updated.Username;
            Source.FullName = updated.FullName;
            Source.Role = updated.Role;
            Source.IsActive = updated.IsActive;
            Source.ApprovalStatus = updated.ApprovalStatus;
            Source.ApprovedAt = updated.ApprovedAt;
            Source.ApprovedBy = updated.ApprovedBy;
            Source.ActivationCode = updated.ActivationCode;
            Source.PublicKey = updated.PublicKey;
            Source.IsDeleted = updated.IsDeleted;
            Source.DeletedAt = updated.DeletedAt;
            Source.CreatedAt = updated.CreatedAt;
            NotifyUserChanged();
        }

        public void ApplyApproval()
        {
            Source.IsActive = true;
            Source.ApprovalStatus = "Approved";
            Source.ApprovedAt = DateTime.Now;
            NotifyUserChanged();
        }

        public void UpdatePresence(IReadOnlyDictionary<string, UserPresence> presence)
        {
            presence.TryGetValue(Source.Username, out _presence);
            OnPropertyChanged(nameof(OnlineText));
            OnPropertyChanged(nameof(MinutesToday));
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(OnlineDotBrush));
            OnPropertyChanged(nameof(OnlineTextBrush));
        }

        private void NotifyUserChanged()
        {
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(FullName));
            OnPropertyChanged(nameof(Role));
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CreatedAtText));
            OnPropertyChanged(nameof(RoleForeground));
            OnPropertyChanged(nameof(RoleBackground));
            OnPropertyChanged(nameof(RoleBorder));
            OnPropertyChanged(nameof(StatusForeground));
            OnPropertyChanged(nameof(StatusBackground));
            OnPropertyChanged(nameof(StatusBorder));
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
