using System.Collections.ObjectModel;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfData = System.Windows.Data;
using WpfEffects = System.Windows.Media.Effects;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace KetoanMini;

public sealed class GiaCongWpfPage : WpfControls.UserControl
{
    private readonly GiaCongStore _store;
    private readonly ObservableCollection<GiaCongPhieuRow> _phieuRows = [];
    private readonly ObservableCollection<GiaCongHangHoaRow> _hangHoaRows = [];
    private readonly Dictionary<string, WpfControls.Button> _tabButtons = new();

    private WpfControls.Grid _root = null!;
    private WpfControls.Border _loadingOverlay = null!;
    private WpfControls.TextBox _searchBox = null!;
    private WpfControls.DataGrid _phieuGrid = null!;
    private WpfControls.Grid _detailHost = null!;
    private WpfControls.TextBlock _footerText = null!;
    private WpfControls.ProgressBar _loadingProgress = null!;
    private WpfControls.TextBlock _loadingPercentText = null!;
    private System.Windows.Threading.DispatcherTimer? _loadingTimer;
    private double _loadingValue;
    private GiaCongPhieu? _currentPhieu;
    private List<GiaCongPhieu> _allPhieu = [];
    private string _filter = "all";
    private bool _loaded;
    private bool _initialLoadCompletedRaised;
    private bool _applyingSelection;
    private int _detailLoadVersion;

    private static readonly WpfMedia.Brush BackgroundBrush = Brush("#F3F7FB");
    private static readonly WpfMedia.Brush SurfaceBrush = Brush("#FFFFFF");
    private static readonly WpfMedia.Brush SurfaceAltBrush = Brush("#F8FAFC");
    private static readonly WpfMedia.Brush LineBrush = Brush("#DDE6F2");
    private static readonly WpfMedia.Brush TextBrush = Brush("#0F172A");
    private static readonly WpfMedia.Brush MutedBrush = Brush("#64748B");
    private static readonly WpfMedia.Brush AccentBrush = Brush("#0B5FEA");
    private static readonly WpfMedia.Brush AccentSoftBrush = Brush("#EAF2FF");
    private static readonly WpfMedia.Brush TealBrush = Brush("#0F9F95");
    private static readonly WpfMedia.Brush TealSoftBrush = Brush("#EEFDFC");
    private static readonly WpfMedia.Brush AmberBrush = Brush("#D97706");
    private static readonly WpfMedia.Brush AmberSoftBrush = Brush("#FFF7E6");
    private static readonly WpfMedia.Brush SuccessBrush = Brush("#16A34A");
    private static readonly WpfMedia.Brush DangerBrush = Brush("#DC2626");
    private static readonly WpfMedia.Brush TransparentBrush = WpfMedia.Brushes.Transparent;

    private static readonly Wpf.Style ChromeButtonStyle = CreateButtonStyle();
    private static readonly Wpf.Style DataGridRowChrome = CreateRowStyle();
    private static readonly Wpf.Style DataGridCellChrome = CreateCellStyle();
    private static readonly Wpf.Style DataGridHeaderChrome = CreateHeaderStyle();

    public event EventHandler? CreateRequested;
    public event EventHandler? InitialLoadCompleted;

    public GiaCongWpfPage(GiaCongStore store)
    {
        _store = store;

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
            await RefreshDataAsync(GetSelectedId(), showLoading: true);
        };
    }

    public async void RefreshData()
        => await RefreshDataAsync(GetSelectedId(), showLoading: true);

    private WpfControls.Grid BuildLayout()
    {
        _root = new WpfControls.Grid
        {
            Background = BackgroundBrush,
            Margin = new Wpf.Thickness(16)
        };
        _root.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(42, Wpf.GridUnitType.Star), MinWidth = 410 });
        _root.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(58, Wpf.GridUnitType.Star), MinWidth = 560 });

        var leftCard = BuildLeftCard();
        WpfControls.Grid.SetColumn(leftCard, 0);
        _root.Children.Add(leftCard);

        var rightCard = Card(new Wpf.Thickness(22, 16, 22, 18));
        rightCard.Margin = new Wpf.Thickness(16, 0, 0, 0);
        _detailHost = new WpfControls.Grid();
        rightCard.Child = _detailHost;
        WpfControls.Grid.SetColumn(rightCard, 1);
        _root.Children.Add(rightCard);

        ShowPlaceholder("Chọn một phiếu để xem chi tiết");

        _loadingOverlay = BuildLoadingOverlay();
        WpfControls.Grid.SetColumnSpan(_loadingOverlay, 2);
        WpfControls.Panel.SetZIndex(_loadingOverlay, 10);
        _root.Children.Add(_loadingOverlay);

        return _root;
    }

    private WpfControls.Border BuildLeftCard()
    {
        var card = Card(new Wpf.Thickness(20, 16, 20, 14));
        var grid = new WpfControls.Grid();
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });

        var header = new WpfControls.DockPanel { LastChildFill = true, Margin = new Wpf.Thickness(0, 0, 0, 16) };
        var actions = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right
        };
        var refresh = MakeButton("\uE72C", "Làm mới", primary: false, minWidth: 102);
        refresh.Click += async (_, _) => await RefreshDataAsync(GetSelectedId(), showLoading: true);
        var create = MakeButton("\uE710", "Tạo phiếu", primary: true, minWidth: 112);
        create.Click += (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty);
        actions.Children.Add(refresh);
        actions.Children.Add(create);
        WpfControls.DockPanel.SetDock(actions, WpfControls.Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(HeaderTitle("\uE8FD", "Danh sách phiếu gia công"));
        grid.Children.Add(header);

        var tabs = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            Margin = new Wpf.Thickness(0, 0, 0, 14)
        };
        AddTab(tabs, "all", "\uE8FD", "Tất cả");
        AddTab(tabs, "nhap", "\uE896", "Nhập");
        AddTab(tabs, "xuat", "\uE898", "Xuất");
        AddTab(tabs, GiaCongTrangThai.DangXuLy, "\uE823", "Đang xử lý");
        WpfControls.Grid.SetRow(tabs, 1);
        grid.Children.Add(tabs);

        var searchRow = new WpfControls.Grid { Margin = new Wpf.Thickness(0, 0, 0, 14) };
        searchRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        searchRow.Children.Add(BuildSearchBox());
        var filterButton = MakeButton("\uE71C", "Bộ lọc", primary: false, minWidth: 96);
        filterButton.Margin = new Wpf.Thickness(14, 0, 0, 0);
        filterButton.Click += async (_, _) =>
        {
            _filter = "all";
            _searchBox.Text = "";
            UpdateTabButtons();
            await ApplyFilterAndLoadAsync(null);
        };
        WpfControls.Grid.SetColumn(filterButton, 1);
        searchRow.Children.Add(filterButton);
        WpfControls.Grid.SetRow(searchRow, 2);
        grid.Children.Add(searchRow);

        _phieuGrid = CreateGrid(_phieuRows);
        _phieuGrid.Columns.Add(TextColumn("Mã phiếu", nameof(GiaCongPhieuRow.MaPhieu), 1.05, star: true));
        _phieuGrid.Columns.Add(TextColumn("Đối tác", nameof(GiaCongPhieuRow.DoiTac), 0.85, star: true));
        _phieuGrid.Columns.Add(TextColumn("Loại", nameof(GiaCongPhieuRow.LoaiPhieu), 1.15, star: true));
        _phieuGrid.Columns.Add(TextColumn("Ngày", nameof(GiaCongPhieuRow.NgayLapText), 0.95, star: true));
        _phieuGrid.Columns.Add(TextColumn("SL", nameof(GiaCongPhieuRow.SoMatHangText), 0.45, star: true, alignRight: true));
        _phieuGrid.Columns.Add(TextColumn("Tổng", nameof(GiaCongPhieuRow.TongGiaTriText), 1.0, star: true, alignRight: true));
        _phieuGrid.Columns.Add(StatusColumn("Trạng thái", nameof(GiaCongPhieuRow.TrangThai), 1.0, star: true));
        _phieuGrid.SelectionChanged += async (_, _) =>
        {
            if (!_applyingSelection)
                await LoadSelectedDetailAsync(showLoading: false);
        };
        WpfControls.Grid.SetRow(_phieuGrid, 3);
        grid.Children.Add(_phieuGrid);

        var footerBorder = new WpfControls.Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(0, 1, 0, 0),
            Margin = new Wpf.Thickness(0, 14, 0, 0),
            Padding = new Wpf.Thickness(0, 11, 0, 0)
        };
        _footerText = Text("Hiển thị 0 phiếu", 13, false, MutedBrush);
        footerBorder.Child = _footerText;
        WpfControls.Grid.SetRow(footerBorder, 4);
        grid.Children.Add(footerBorder);

        card.Child = grid;
        UpdateTabButtons();
        return card;
    }

    private WpfControls.Border BuildSearchBox()
    {
        var border = InputFrame();
        var host = new WpfControls.Grid();
        var icon = IconText("\uE721", 17, MutedBrush);
        icon.Margin = new Wpf.Thickness(12, 0, 0, 1);
        icon.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        var placeholder = Text("Tìm kiếm mã phiếu, đối tác, loại...", 13, false, MutedBrush);
        placeholder.Margin = new Wpf.Thickness(42, 0, 10, 0);
        _searchBox = new WpfControls.TextBox
        {
            BorderThickness = new Wpf.Thickness(0),
            Background = TransparentBrush,
            Foreground = TextBrush,
            FontSize = 13,
            Padding = new Wpf.Thickness(42, 0, 10, 1),
            VerticalContentAlignment = Wpf.VerticalAlignment.Center
        };
        _searchBox.TextChanged += async (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrWhiteSpace(_searchBox.Text)
                ? Wpf.Visibility.Visible
                : Wpf.Visibility.Collapsed;
            await ApplyFilterAndLoadAsync(GetSelectedId());
        };

        host.Children.Add(icon);
        host.Children.Add(placeholder);
        host.Children.Add(_searchBox);
        border.Child = host;
        return border;
    }

    private void AddTab(WpfControls.Panel host, string key, string icon, string label)
    {
        var button = MakeButton(icon, label, key == _filter, minWidth: key == GiaCongTrangThai.DangXuLy ? 124 : 90);
        button.Tag = key;
        button.Click += async (_, _) =>
        {
            _filter = key;
            UpdateTabButtons();
            await ApplyFilterAndLoadAsync(GetSelectedId());
        };
        _tabButtons[key] = button;
        host.Children.Add(button);
    }

    private async Task RefreshDataAsync(long? preferredId, bool showLoading)
    {
        if (showLoading)
            ShowLoading(true);

        try
        {
            var list = await Task.Run(() => _store.GetAllPhieu());
            _allPhieu = list;
            await ApplyFilterAndLoadAsync(preferredId);
        }
        catch (Exception ex)
        {
            _allPhieu = [];
            _phieuRows.Clear();
            _footerText.Text = "Không tải được dữ liệu Gia công";
            ShowPlaceholder("Không tải được dữ liệu Gia công");
            Wpf.MessageBox.Show(ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
        finally
        {
            if (showLoading)
                ShowLoading(false);

            RaiseInitialLoadCompletedOnce();
        }
    }

    private async Task ApplyFilterAndLoadAsync(long? preferredId)
    {
        IEnumerable<GiaCongPhieu> filtered = _allPhieu;
        if (_filter == "nhap")
            filtered = filtered.Where(p => Normalize(p.LoaiPhieu).Contains("nhap", StringComparison.Ordinal));
        else if (_filter == "xuat")
            filtered = filtered.Where(p => Normalize(p.LoaiPhieu).Contains("xuat", StringComparison.Ordinal));
        else if (_filter == GiaCongTrangThai.DangXuLy)
            filtered = filtered.Where(p => p.TrangThai == GiaCongTrangThai.DangXuLy);

        var query = Normalize(_searchBox?.Text ?? "");
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(p =>
                Normalize(p.MaPhieu).Contains(query, StringComparison.Ordinal) ||
                Normalize(p.DoiTac).Contains(query, StringComparison.Ordinal) ||
                Normalize(p.LoaiPhieu).Contains(query, StringComparison.Ordinal) ||
                Normalize(p.TrangThai).Contains(query, StringComparison.Ordinal));
        }

        var list = filtered.ToList();
        _applyingSelection = true;
        try
        {
            _phieuRows.Clear();
            foreach (var phieu in list)
                _phieuRows.Add(new GiaCongPhieuRow(phieu));

            _footerText.Text = $"Hiển thị {list.Count} phiếu";

            var selected = preferredId.HasValue
                ? _phieuRows.FirstOrDefault(row => row.Id == preferredId.Value)
                : null;
            selected ??= _phieuRows.FirstOrDefault();
            _phieuGrid.SelectedItem = selected;
        }
        finally
        {
            _applyingSelection = false;
        }

        await LoadSelectedDetailAsync(showLoading: false);
    }

    private async Task LoadSelectedDetailAsync(bool showLoading)
    {
        var version = ++_detailLoadVersion;
        if (_phieuGrid.SelectedItem is not GiaCongPhieuRow row)
        {
            _currentPhieu = null;
            ShowPlaceholder("Chọn một phiếu để xem chi tiết");
            return;
        }

        if (showLoading)
            ShowLoading(true);

        try
        {
            var detail = await Task.Run(() => _store.GetPhieuById(row.Id));
            if (version != _detailLoadVersion)
                return;

            _currentPhieu = detail ?? row.Source;
            ShowDetail(_currentPhieu);
        }
        catch
        {
            if (version != _detailLoadVersion)
                return;

            _currentPhieu = row.Source;
            ShowDetail(_currentPhieu);
        }
        finally
        {
            if (showLoading)
                ShowLoading(false);
        }
    }

    private void ShowPlaceholder(string message)
    {
        _detailHost.Children.Clear();
        _detailHost.Children.Add(new WpfControls.TextBlock
        {
            Text = Clean(message),
            Foreground = MutedBrush,
            FontWeight = Wpf.FontWeights.SemiBold,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        });
    }

    private void ShowDetail(GiaCongPhieu phieu)
    {
        _detailHost.Children.Clear();
        _hangHoaRows.Clear();
        var stt = 1;
        foreach (var hangHoa in phieu.HangHoaList)
            _hangHoaRows.Add(new GiaCongHangHoaRow(stt++, hangHoa));

        var scroll = new WpfControls.ScrollViewer
        {
            VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = WpfControls.ScrollBarVisibility.Disabled,
            CanContentScroll = true
        };

        var stack = new WpfControls.StackPanel();
        scroll.Content = stack;

        var header = new WpfControls.DockPanel { LastChildFill = true, Margin = new Wpf.Thickness(0, 0, 0, 14) };
        var statusButton = MakeButton("\uE895", "Cập nhật trạng thái", primary: true, minWidth: 176);
        statusButton.Click += (_, _) => OpenStatusMenu(statusButton);
        WpfControls.DockPanel.SetDock(statusButton, WpfControls.Dock.Right);
        header.Children.Add(statusButton);
        header.Children.Add(HeaderTitle("\uE8A5", $"Chi tiết phiếu gia công - {Clean(phieu.MaPhieu)}"));
        stack.Children.Add(header);
        stack.Children.Add(Separator());

        stack.Children.Add(BuildInfoGrid(phieu));
        stack.Children.Add(BuildSummaryCards(phieu));
        stack.Children.Add(SectionTitle("Danh sách hàng hóa"));
        stack.Children.Add(BuildHangHoaGrid(phieu.HangHoaList.Count));
        stack.Children.Add(SectionTitle("Tiến độ xử lý"));
        stack.Children.Add(BuildProgress(phieu));
        stack.Children.Add(BuildValueSummary(phieu));
        stack.Children.Add(BuildActions());

        _detailHost.Children.Add(scroll);
    }

    private WpfControls.Grid BuildInfoGrid(GiaCongPhieu phieu)
    {
        var grid = new WpfControls.Grid { Margin = new Wpf.Thickness(0, 14, 0, 10) };
        for (var i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });

        AddInfoCell(grid, 0, 0, "Mã phiếu", Clean(phieu.MaPhieu), TextBrush);
        AddInfoCell(grid, 1, 0, "Ngày lập", phieu.NgayLap.ToString("dd/MM/yyyy"), TextBrush);
        AddInfoCell(grid, 2, 0, "Loại phiếu", Clean(phieu.LoaiPhieu), TextBrush);
        AddInfoCell(grid, 3, 0, "Hạn hoàn thành", phieu.HanHoanThanh?.ToString("dd/MM/yyyy") ?? "Không có", IsOverdue(phieu) ? DangerBrush : TextBrush);
        AddInfoCell(grid, 0, 1, "Đối tác", Clean(phieu.DoiTac), TextBrush);
        AddStatusInfoCell(grid, 1, 1, phieu.TrangThai);
        AddInfoCell(grid, 2, 1, "Nhân viên", Clean(phieu.NhanVienPhuTrach), TextBrush);
        AddInfoCell(grid, 3, 1, "Ghi chú", string.IsNullOrWhiteSpace(phieu.GhiChu) ? "-" : Clean(phieu.GhiChu), TextBrush);
        return grid;
    }

    private static void AddInfoCell(WpfControls.Grid grid, int column, int row, string label, string value, WpfMedia.Brush valueBrush)
    {
        var panel = new WpfControls.StackPanel { Margin = new Wpf.Thickness(0, 0, 22, 18) };
        panel.Children.Add(Text(label, 13, false, MutedBrush));
        var valueText = Text(value, 14, true, valueBrush);
        valueText.Margin = new Wpf.Thickness(0, 8, 0, 0);
        panel.Children.Add(valueText);
        WpfControls.Grid.SetColumn(panel, column);
        WpfControls.Grid.SetRow(panel, row);
        grid.Children.Add(panel);
    }

    private static void AddStatusInfoCell(WpfControls.Grid grid, int column, int row, string status)
    {
        var panel = new WpfControls.StackPanel { Margin = new Wpf.Thickness(0, 0, 22, 18) };
        panel.Children.Add(Text("Trạng thái", 13, false, MutedBrush));
        var pill = StatusPill(status);
        pill.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        pill.Margin = new Wpf.Thickness(0, 8, 0, 0);
        panel.Children.Add(pill);
        WpfControls.Grid.SetColumn(panel, column);
        WpfControls.Grid.SetRow(panel, row);
        grid.Children.Add(panel);
    }

    private WpfControls.Grid BuildSummaryCards(GiaCongPhieu phieu)
    {
        var grid = new WpfControls.Grid { Margin = new Wpf.Thickness(0, 4, 0, 18) };
        for (var i = 0; i < 3; i++)
            grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var totalQty = phieu.HangHoaList.Sum(h => h.SoLuong);
        AddSummaryCard(grid, 0, "▱", $"{phieu.SoMatHang} mặt hàng", AccentBrush, AccentSoftBrush, Brush("#BFD5FF"));
        AddSummaryCard(grid, 1, "☷", $"SL: {totalQty:N2}", TealBrush, TealSoftBrush, Brush("#BFEFED"));
        AddSummaryCard(grid, 2, "₫", $"{TextUtil.FormatMoney(phieu.TongGiaTri)} đ", AmberBrush, AmberSoftBrush, Brush("#F7D596"));
        return grid;
    }

    private static void AddSummaryCard(WpfControls.Grid grid, int column, string icon, string value, WpfMedia.Brush accent, WpfMedia.Brush bg, WpfMedia.Brush border)
    {
        var card = new WpfControls.Border
        {
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(8),
            Padding = new Wpf.Thickness(14, 12, 14, 12),
            Margin = new Wpf.Thickness(column == 0 ? 0 : 10, 0, column == 2 ? 0 : 10, 0)
        };

        var panel = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center
        };
        var iconText = Text(icon, 22, true, accent);
        iconText.Margin = new Wpf.Thickness(0, 0, 12, 0);
        panel.Children.Add(iconText);
        panel.Children.Add(Text(value, 16, true, accent));
        card.Child = panel;
        WpfControls.Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private WpfControls.DataGrid BuildHangHoaGrid(int lineCount)
    {
        var grid = CreateGrid(_hangHoaRows);
        grid.Height = Math.Max(100, Math.Min(250, lineCount * 36 + 42));
        grid.Margin = new Wpf.Thickness(0, 0, 0, 18);
        grid.Columns.Add(TextColumn("#", nameof(GiaCongHangHoaRow.Stt), 46, alignRight: true));
        grid.Columns.Add(TextColumn("Loại hàng", nameof(GiaCongHangHoaRow.LoaiDong), 100));
        grid.Columns.Add(TextColumn("Mã hàng", nameof(GiaCongHangHoaRow.MaHang), 92));
        grid.Columns.Add(TextColumn("Tên hàng", nameof(GiaCongHangHoaRow.TenHang), 1, star: true));
        grid.Columns.Add(TextColumn("ĐVT", nameof(GiaCongHangHoaRow.DonViTinh), 72));
        grid.Columns.Add(TextColumn("SL", nameof(GiaCongHangHoaRow.SoLuongText), 78, alignRight: true));
        grid.Columns.Add(TextColumn("Đơn giá", nameof(GiaCongHangHoaRow.DonGiaText), 104, alignRight: true));
        grid.Columns.Add(TextColumn("Thành tiền", nameof(GiaCongHangHoaRow.ThanhTienText), 112, alignRight: true));
        grid.Columns.Add(StatusColumn("Trạng thái", nameof(GiaCongHangHoaRow.TrangThaiDong), 92));
        return grid;
    }

    private WpfControls.Border BuildProgress(GiaCongPhieu phieu)
    {
        var clampedProgress = Math.Clamp(phieu.TienDo, 0, 100);
        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var circle = new WpfControls.Border
        {
            Width = 46,
            Height = 46,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(3),
            CornerRadius = new Wpf.CornerRadius(23),
            Background = SurfaceBrush,
            Child = new WpfControls.TextBlock
            {
                Text = $"{clampedProgress}%",
                Foreground = TextBrush,
                FontSize = 12,
                FontWeight = Wpf.FontWeights.SemiBold,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            }
        };
        WpfControls.Grid.SetColumn(circle, 0);
        grid.Children.Add(circle);

        var progressPanel = new WpfControls.Grid { Margin = new Wpf.Thickness(18, 0, 0, 0) };
        progressPanel.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        progressPanel.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        progressPanel.Children.Add(Text($"Bước hiện tại: {Math.Clamp(phieu.BuocHienTai, 1, 5)}/5 - Tiến độ {clampedProgress}%", 13, false, MutedBrush));
        var progress = new WpfControls.ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Value = clampedProgress,
            Foreground = AccentBrush,
            Background = Brush("#CBD5E1"),
            Margin = new Wpf.Thickness(0, 12, 0, 0)
        };
        WpfControls.Grid.SetRow(progress, 1);
        progressPanel.Children.Add(progress);
        WpfControls.Grid.SetColumn(progressPanel, 1);
        grid.Children.Add(progressPanel);

        return new WpfControls.Border
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(8),
            Padding = new Wpf.Thickness(18, 14, 18, 14),
            Margin = new Wpf.Thickness(0, 0, 0, 14),
            Child = grid
        };
    }

    private WpfControls.Border BuildValueSummary(GiaCongPhieu phieu)
    {
        var giaNguyenLieu = phieu.HangHoaList.Where(h => h.LoaiDong == GiaCongLoaiDong.NguyenLieu).Sum(h => h.ThanhTien);
        var giaThanhPham = phieu.HangHoaList.Where(h => h.LoaiDong == GiaCongLoaiDong.ThanhPham).Sum(h => h.ThanhTien);
        var chiPhiKhac = Math.Max(0, phieu.TongGiaTri - giaNguyenLieu - giaThanhPham);

        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });

        AddValueRow(grid, 0, "Giá trị NL", $"{TextUtil.FormatMoney(giaNguyenLieu)} đ", strong: false);
        AddValueRow(grid, 1, "Giá trị TP", $"{TextUtil.FormatMoney(giaThanhPham)} đ", strong: false);
        AddValueRow(grid, 2, "Chi phí khác", $"{TextUtil.FormatMoney(chiPhiKhac)} đ", strong: false);

        var line = new WpfControls.Border
        {
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(0, 1, 0, 0),
            Margin = new Wpf.Thickness(0, 8, 0, 12)
        };
        WpfControls.Grid.SetRow(line, 3);
        WpfControls.Grid.SetColumnSpan(line, 2);
        grid.Children.Add(line);

        AddValueRow(grid, 4, "Tổng cộng", $"{TextUtil.FormatMoney(phieu.TongGiaTri)} đ", strong: true);

        return new WpfControls.Border
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(8),
            Padding = new Wpf.Thickness(18, 14, 18, 14),
            Margin = new Wpf.Thickness(0, 0, 0, 18),
            Child = grid
        };
    }

    private static void AddValueRow(WpfControls.Grid grid, int row, string label, string value, bool strong)
    {
        var labelText = Text(label, strong ? 15 : 13, strong, strong ? AccentBrush : MutedBrush);
        labelText.Margin = new Wpf.Thickness(0, 0, 12, strong ? 0 : 10);
        var valueText = Text(value, strong ? 22 : 13, strong, strong ? AccentBrush : TextBrush);
        valueText.HorizontalAlignment = Wpf.HorizontalAlignment.Right;
        valueText.Margin = new Wpf.Thickness(0, 0, 0, strong ? 0 : 10);
        WpfControls.Grid.SetRow(labelText, row);
        WpfControls.Grid.SetColumn(labelText, 0);
        WpfControls.Grid.SetRow(valueText, row);
        WpfControls.Grid.SetColumn(valueText, 1);
        grid.Children.Add(labelText);
        grid.Children.Add(valueText);
    }

    private WpfControls.Grid BuildActions()
    {
        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(2.2, Wpf.GridUnitType.Star) });

        var print = MakeButton("\uE749", "In phiếu", primary: false, minWidth: 120);
        print.Click += (_, _) => Wpf.MessageBox.Show("Tính năng in phiếu đang phát triển.", "Thông báo");
        var excel = MakeExcelButton("Xuất Excel", minWidth: 120);
        excel.Click += (_, _) => Wpf.MessageBox.Show("Tính năng xuất Excel đang phát triển.", "Thông báo");
        var status = MakeButton("\uE895", "Cập nhật trạng thái", primary: true, minWidth: 180);
        status.Click += (_, _) => OpenStatusMenu(status);

        print.Margin = new Wpf.Thickness(0, 0, 10, 0);
        excel.Margin = new Wpf.Thickness(10, 0, 12, 0);
        status.Margin = new Wpf.Thickness(12, 0, 0, 0);
        WpfControls.Grid.SetColumn(print, 0);
        WpfControls.Grid.SetColumn(excel, 1);
        WpfControls.Grid.SetColumn(status, 2);
        grid.Children.Add(print);
        grid.Children.Add(excel);
        grid.Children.Add(status);
        return grid;
    }

    private void OpenStatusMenu(WpfControls.Button target)
    {
        if (_currentPhieu == null)
            return;

        var menu = new WpfControls.ContextMenu
        {
            PlacementTarget = target,
            Placement = WpfPrimitives.PlacementMode.Bottom
        };

        foreach (var status in GiaCongTrangThai.AllValues)
        {
            var captured = status;
            var item = new WpfControls.MenuItem { Header = Clean(captured) };
            item.Click += async (_, _) => await UpdateStatusAsync(captured);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private async Task UpdateStatusAsync(string status)
    {
        if (_currentPhieu == null)
            return;

        ShowLoading(true);
        try
        {
            var phieu = _currentPhieu;
            await Task.Run(() => _store.UpdatePhieuTrangThai(phieu.Id, status, phieu.TienDo, phieu.BuocHienTai));
            await RefreshDataAsync(phieu.Id, showLoading: false);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private long? GetSelectedId()
        => _phieuGrid?.SelectedItem is GiaCongPhieuRow row ? row.Id : _currentPhieu?.Id;

    private void ShowLoading(bool visible)
    {
        if (_loadingOverlay == null)
            return;

        if (visible)
        {
            _loadingOverlay.Visibility = Wpf.Visibility.Visible;
            StartLoadingProgress();
            return;
        }

        FinishLoadingProgress();
        _loadingOverlay.Visibility = Wpf.Visibility.Collapsed;
    }

    private void StartLoadingProgress()
    {
        _loadingValue = 0;
        UpdateLoadingProgress();

        if (_loadingTimer == null)
        {
            _loadingTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(22)
            };
            _loadingTimer.Tick += (_, _) =>
            {
                _loadingValue += _loadingValue < 70 ? 3.5 : 0.9;
                if (_loadingValue > 96)
                    _loadingValue = 96;
                UpdateLoadingProgress();
            };
        }

        _loadingTimer.Stop();
        _loadingTimer.Start();
    }

    private void FinishLoadingProgress()
    {
        _loadingTimer?.Stop();
        _loadingValue = 100;
        UpdateLoadingProgress();
    }

    private void UpdateLoadingProgress()
    {
        if (_loadingProgress == null || _loadingPercentText == null)
            return;

        var value = Math.Clamp((int)Math.Round(_loadingValue), 0, 100);
        _loadingProgress.Value = value;
        _loadingPercentText.Text = $"{value}%";
    }

    private void RaiseInitialLoadCompletedOnce()
    {
        if (_initialLoadCompletedRaised)
            return;

        _initialLoadCompletedRaised = true;
        InitialLoadCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTabButtons()
    {
        foreach (var (key, button) in _tabButtons)
        {
            var active = key == _filter;
            button.Background = active ? AccentBrush : SurfaceBrush;
            button.Foreground = active ? WpfMedia.Brushes.White : MutedBrush;
            button.BorderBrush = active ? AccentBrush : LineBrush;
            SetButtonContentBrushes(button, active);
        }
    }

    private static bool IsOverdue(GiaCongPhieu phieu)
        => phieu.HanHoanThanh.HasValue
           && phieu.HanHoanThanh.Value < DateOnly.FromDateTime(DateTime.Today)
           && phieu.TrangThai != GiaCongTrangThai.HoanThanh;

    private WpfControls.Border BuildLoadingOverlay()
    {
        _loadingProgress = new WpfControls.ProgressBar
        {
            Width = 240,
            Height = 7,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsIndeterminate = false,
            Foreground = AccentBrush,
            Background = Brush("#D8E4F3"),
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
        stack.Children.Add(Text("Đang tải trang Gia công...", 16, true, TextBrush));
        stack.Children.Add(Text("Dữ liệu sẽ hiện ngay sau khi bố cục ổn định", 12, false, MutedBrush));
        stack.Children.Add(_loadingProgress);
        stack.Children.Add(_loadingPercentText);

        return new WpfControls.Border
        {
            Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(247, 243, 247, 251)),
            Child = stack,
            Visibility = Wpf.Visibility.Visible
        };
    }

    private static WpfControls.DockPanel HeaderTitle(string icon, string title)
    {
        var panel = new WpfControls.DockPanel { LastChildFill = true };
        var iconText = IconText(icon, 22, AccentBrush);
        iconText.Margin = new Wpf.Thickness(0, 0, 12, 0);
        WpfControls.DockPanel.SetDock(iconText, WpfControls.Dock.Left);
        panel.Children.Add(iconText);
        panel.Children.Add(Text(title, 20, true, TextBrush));
        return panel;
    }

    private static WpfControls.TextBlock SectionTitle(string text)
    {
        var block = Text(text, 16, true, TextBrush);
        block.Margin = new Wpf.Thickness(0, 0, 0, 10);
        return block;
    }

    private static WpfControls.Border Separator()
        => new()
        {
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(0, 1, 0, 0),
            Margin = new Wpf.Thickness(0, 0, 0, 0)
        };

    private static WpfControls.Border InputFrame()
        => new()
        {
            Height = 44,
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(6)
        };

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
            Height = 40,
            MinWidth = minWidth,
            Padding = new Wpf.Thickness(12, 0, 12, 1),
            Margin = new Wpf.Thickness(0, 0, 10, 0),
            Background = primary ? AccentBrush : SurfaceBrush,
            Foreground = primary ? WpfMedia.Brushes.White : TextBrush,
            BorderBrush = primary ? AccentBrush : LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            FontSize = 13,
            FontWeight = Wpf.FontWeights.SemiBold,
            Cursor = WpfInput.Cursors.Hand,
            Style = ChromeButtonStyle
        };
    }

    private static WpfControls.Button MakeExcelButton(string text, double minWidth)
    {
        var panel = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        var badge = new WpfControls.Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new Wpf.CornerRadius(3),
            Background = Brush("#107C41"),
            Margin = new Wpf.Thickness(0, 0, 8, 0),
            Child = new WpfControls.TextBlock
            {
                Text = "X",
                Foreground = WpfMedia.Brushes.White,
                FontSize = 11,
                FontWeight = Wpf.FontWeights.Bold,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            }
        };
        panel.Children.Add(badge);
        panel.Children.Add(Text(text, 13, true, TextBrush));

        return new WpfControls.Button
        {
            Content = panel,
            Height = 40,
            MinWidth = minWidth,
            Padding = new Wpf.Thickness(12, 0, 12, 1),
            Margin = new Wpf.Thickness(0, 0, 10, 0),
            Background = SurfaceBrush,
            Foreground = TextBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            FontSize = 13,
            FontWeight = Wpf.FontWeights.SemiBold,
            Cursor = WpfInput.Cursors.Hand,
            Style = ChromeButtonStyle
        };
    }

    private static void SetButtonContentBrushes(WpfControls.Button button, bool active)
    {
        if (button.Content is not WpfControls.Panel panel)
            return;

        var index = 0;
        foreach (var child in panel.Children.OfType<WpfControls.TextBlock>())
        {
            child.Foreground = active
                ? WpfMedia.Brushes.White
                : index == 0 ? AccentBrush : TextBrush;
            index++;
        }
    }

    private static WpfControls.Border Card(Wpf.Thickness padding)
        => new()
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(8),
            Padding = padding,
            Effect = new WpfEffects.DropShadowEffect
            {
                BlurRadius = 16,
                Color = WpfMedia.Color.FromRgb(15, 23, 42),
                Direction = 270,
                Opacity = 0.08,
                ShadowDepth = 2
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
            HorizontalGridLinesBrush = Brush("#EEF2F7"),
            VerticalGridLinesBrush = Brush("#EEF2F7"),
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Wpf.Thickness(1),
            RowHeight = 42,
            ColumnHeaderHeight = 40,
            FontSize = 13,
            RowStyle = DataGridRowChrome,
            CellStyle = DataGridCellChrome,
            ColumnHeaderStyle = DataGridHeaderChrome,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };

        WpfControls.ScrollViewer.SetCanContentScroll(grid, true);
        WpfControls.VirtualizingPanel.SetIsVirtualizing(grid, true);
        WpfControls.VirtualizingPanel.SetVirtualizationMode(grid, WpfControls.VirtualizationMode.Recycling);
        return grid;
    }

    private static WpfControls.DataGridTextColumn TextColumn(string header, string binding, double width, bool star = false, bool alignRight = false)
        => new()
        {
            Header = header,
            Binding = new WpfData.Binding(binding),
            Width = star
                ? new WpfControls.DataGridLength(width, WpfControls.DataGridLengthUnitType.Star)
                : new WpfControls.DataGridLength(width),
            ElementStyle = CellTextStyle(alignRight),
            CanUserResize = false,
            CanUserSort = false
        };

    private static WpfControls.DataGridTemplateColumn StatusColumn(string header, string binding, double width, bool star = false)
    {
        var borderFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border));
        borderFactory.SetBinding(WpfControls.Border.BackgroundProperty, new WpfData.Binding(nameof(GiaCongPhieuRow.StatusBackground)));
        borderFactory.SetBinding(WpfControls.Border.BorderBrushProperty, new WpfData.Binding(nameof(GiaCongPhieuRow.StatusBorder)));
        borderFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Wpf.Thickness(1));
        borderFactory.SetValue(WpfControls.Border.CornerRadiusProperty, new Wpf.CornerRadius(6));
        borderFactory.SetValue(WpfControls.Border.PaddingProperty, new Wpf.Thickness(8, 3, 8, 4));
        borderFactory.SetValue(WpfControls.Border.HorizontalAlignmentProperty, Wpf.HorizontalAlignment.Center);
        borderFactory.SetValue(WpfControls.Border.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center);

        var textFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.TextBlock));
        textFactory.SetBinding(WpfControls.TextBlock.TextProperty, new WpfData.Binding(binding));
        textFactory.SetBinding(WpfControls.TextBlock.ForegroundProperty, new WpfData.Binding(nameof(GiaCongPhieuRow.StatusForeground)));
        textFactory.SetValue(WpfControls.TextBlock.FontSizeProperty, 12.0);
        textFactory.SetValue(WpfControls.TextBlock.FontWeightProperty, Wpf.FontWeights.SemiBold);
        borderFactory.AppendChild(textFactory);

        return new WpfControls.DataGridTemplateColumn
        {
            Header = header,
            Width = star
                ? new WpfControls.DataGridLength(width, WpfControls.DataGridLengthUnitType.Star)
                : new WpfControls.DataGridLength(width),
            CellTemplate = new Wpf.DataTemplate { VisualTree = borderFactory },
            CanUserResize = false,
            CanUserSort = false
        };
    }

    private static WpfControls.Border StatusPill(string status)
        => new()
        {
            Background = StatusSoftBrush(status),
            BorderBrush = StatusLineBrush(status),
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(6),
            Padding = new Wpf.Thickness(10, 5, 10, 5),
            Child = Text(status, 13, true, StatusBrush(status))
        };

    private static Wpf.Style CellTextStyle(bool alignRight)
    {
        var style = new Wpf.Style(typeof(WpfControls.TextBlock));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.PaddingProperty, new Wpf.Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.TextTrimmingProperty, Wpf.TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.ForegroundProperty, TextBrush));
        if (alignRight)
            style.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.TextAlignmentProperty, Wpf.TextAlignment.Right));
        return style;
    }

    private static Wpf.Style CreateRowStyle()
    {
        var style = new Wpf.Style(typeof(WpfControls.DataGridRow));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, SurfaceBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, TransparentBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.DataGridRow.SnapsToDevicePixelsProperty, true));

        var selected = new Wpf.Trigger { Property = WpfControls.DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, AccentSoftBrush));
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, AccentBrush));
        style.Triggers.Add(selected);

        var hover = new Wpf.Trigger { Property = WpfControls.DataGridRow.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, Brush("#F7FBFF")));
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
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, SurfaceAltBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, TextBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FontWeightProperty, Wpf.FontWeights.SemiBold));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FontSizeProperty, 13.0));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, LineBrush));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.PaddingProperty, new Wpf.Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.HorizontalContentAlignmentProperty, Wpf.HorizontalAlignment.Left));
        return style;
    }

    private static Wpf.Style CreateButtonStyle()
    {
        var borderFactory = new Wpf.FrameworkElementFactory(typeof(WpfControls.Border));
        borderFactory.SetValue(WpfControls.Border.BackgroundProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BackgroundProperty));
        borderFactory.SetValue(WpfControls.Border.BorderBrushProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BorderBrushProperty));
        borderFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Wpf.TemplateBindingExtension(WpfControls.Control.BorderThicknessProperty));
        borderFactory.SetValue(WpfControls.Border.CornerRadiusProperty, new Wpf.CornerRadius(6));

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

    private static WpfMedia.Brush StatusBrush(string status)
    {
        var clean = Clean(status);
        if (clean == Clean(GiaCongTrangThai.HoanThanh))
            return SuccessBrush;
        if (clean == Clean(GiaCongTrangThai.Huy))
            return DangerBrush;
        if (clean == Clean(GiaCongTrangThai.ChoDauTac) || clean == Clean(GiaCongTrangThaiDong.Cho))
            return AmberBrush;
        return AccentBrush;
    }

    private static WpfMedia.Brush StatusSoftBrush(string status)
    {
        var brush = StatusBrush(status);
        if (brush == SuccessBrush)
            return Brush("#DCFCE7");
        if (brush == DangerBrush)
            return Brush("#FEE2E2");
        if (brush == AmberBrush)
            return Brush("#FEF3C7");
        return AccentSoftBrush;
    }

    private static WpfMedia.Brush StatusLineBrush(string status)
    {
        var brush = StatusBrush(status);
        if (brush == SuccessBrush)
            return Brush("#BBF7D0");
        if (brush == DangerBrush)
            return Brush("#FECACA");
        if (brush == AmberBrush)
            return Brush("#FDE68A");
        return Brush("#BFDBFE");
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

    private sealed class GiaCongPhieuRow(GiaCongPhieu source)
    {
        public GiaCongPhieu Source { get; } = source;
        public long Id => Source.Id;
        public string MaPhieu => Clean(Source.MaPhieu);
        public string DoiTac => Clean(Source.DoiTac);
        public string LoaiPhieu => Clean(Source.LoaiPhieu);
        public string NgayLapText => Source.NgayLap.ToString("dd/MM/yyyy");
        public string SoMatHangText => Source.SoMatHang.ToString();
        public string TongGiaTriText => TextUtil.FormatMoney(Source.TongGiaTri);
        public string TrangThai => Clean(Source.TrangThai);
        public WpfMedia.Brush StatusForeground => StatusBrush(Source.TrangThai);
        public WpfMedia.Brush StatusBackground => StatusSoftBrush(Source.TrangThai);
        public WpfMedia.Brush StatusBorder => StatusLineBrush(Source.TrangThai);
    }

    private sealed class GiaCongHangHoaRow(int stt, GiaCongHangHoa source)
    {
        public int Stt { get; } = stt;
        public string LoaiDong => Clean(source.LoaiDong);
        public string MaHang => Clean(source.MaHang);
        public string TenHang => Clean(source.TenHang);
        public string DonViTinh => Clean(source.DonViTinh);
        public string SoLuongText => source.SoLuong.ToString("N2");
        public string DonGiaText => TextUtil.FormatMoney(source.DonGiaGiaCong);
        public string ThanhTienText => TextUtil.FormatMoney(source.ThanhTien);
        public string TrangThaiDong => Clean(source.TrangThaiDong);
        public WpfMedia.Brush StatusForeground => StatusBrush(source.TrangThaiDong);
        public WpfMedia.Brush StatusBackground => StatusSoftBrush(source.TrangThaiDong);
        public WpfMedia.Brush StatusBorder => StatusLineBrush(source.TrangThaiDong);
    }
}
