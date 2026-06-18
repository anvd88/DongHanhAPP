using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfData = System.Windows.Data;
using WpfMedia = System.Windows.Media;
using static KetoanMini.DialogGridHelpers;

namespace KetoanMini;

internal abstract class KetoanDialogWindow : Wpf.Window
{
    protected KetoanDialogWindow(string title, double width, double height)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = Math.Min(width, 520);
        MinHeight = Math.Min(height, 360);
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterOwner;
        Background = WpfTheme.Background;
        FontFamily = WpfTheme.Font;
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);
    }

    protected static WpfControls.Border Card(Wpf.UIElement child) => new()
    {
        Background = WpfTheme.Surface,
        BorderBrush = WpfTheme.Border,
        BorderThickness = new Wpf.Thickness(1),
        CornerRadius = new Wpf.CornerRadius(8),
        Padding = new Wpf.Thickness(18),
        Margin = new Wpf.Thickness(16),
        Child = child
    };

    protected static WpfControls.TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = WpfTheme.TextSecondary,
        FontWeight = Wpf.FontWeights.Bold,
        FontSize = WpfTheme.Pt(9),
        VerticalAlignment = Wpf.VerticalAlignment.Center,
        Margin = new Wpf.Thickness(0, 6, 10, 6)
    };

    protected static WpfControls.TextBox TextBox(string text = "") => new()
    {
        Text = text,
        Foreground = WpfTheme.TextPrimary,
        Background = WpfTheme.Surface,
        BorderBrush = WpfTheme.Border,
        BorderThickness = new Wpf.Thickness(1),
        Padding = new Wpf.Thickness(8, 4, 8, 4),
        FontSize = WpfTheme.Pt(9.5),
        MinHeight = 30
    };

    protected static WpfControls.PasswordBox PasswordBox() => new()
    {
        Background = WpfTheme.Surface,
        BorderBrush = WpfTheme.Border,
        BorderThickness = new Wpf.Thickness(1),
        Padding = new Wpf.Thickness(8, 4, 8, 4),
        FontSize = WpfTheme.Pt(9.5),
        MinHeight = 30
    };

    protected static WpfControls.ComboBox ComboBox(bool editable = false) => new()
    {
        IsEditable = editable,
        Background = WpfTheme.Surface,
        Foreground = WpfTheme.TextPrimary,
        BorderBrush = WpfTheme.Border,
        BorderThickness = new Wpf.Thickness(1),
        Padding = new Wpf.Thickness(4, 2, 4, 2),
        MinHeight = 30,
        FontSize = WpfTheme.Pt(9.5)
    };

    protected static WpfControls.Grid FormGrid()
    {
        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(140) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        return grid;
    }

    protected static void AddRow(WpfControls.Grid grid, int row, string label, Wpf.UIElement input)
    {
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        var lbl = Label(label);
        WpfControls.Grid.SetRow(lbl, row);
        WpfControls.Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);
        WpfControls.Grid.SetRow(input, row);
        WpfControls.Grid.SetColumn(input, 1);
        grid.Children.Add(input);
    }

    protected WpfControls.StackPanel ButtonBar()
        => new() { Orientation = WpfControls.Orientation.Horizontal, HorizontalAlignment = Wpf.HorizontalAlignment.Right, Margin = new Wpf.Thickness(0, 14, 0, 0) };

    protected WpfControls.Button CancelButton(string text = "Hủy")
    {
        var btn = WpfUi.OutlineButton(text, WpfTheme.TextPrimary, WpfTheme.Border);
        btn.Width = 90;
        btn.Height = 34;
        btn.Margin = new Wpf.Thickness(8, 0, 0, 0);
        btn.Click += (_, _) => { DialogResult = false; Close(); };
        return btn;
    }

    protected WpfControls.Button PrimaryButton(string text)
    {
        var btn = WpfUi.FilledButton(text, WpfTheme.Accent, WpfMedia.Brushes.White);
        btn.Width = 100;
        btn.Height = 34;
        btn.Margin = new Wpf.Thickness(8, 0, 0, 0);
        return btn;
    }
}

internal sealed class DocumentWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly Document? _existing;
    private readonly WpfControls.TextBox _voucher = TextBox();
    private readonly WpfControls.DatePicker _date = new() { SelectedDate = DateTime.Today, MinHeight = 30 };
    private readonly WpfControls.ComboBox _customer = ComboBox(editable: true);
    private readonly WpfControls.TextBox _contentText = TextBox();
    private readonly ObservableCollection<DocumentLineEdit> _lines = new();

    public string SavedVoucherNo { get; private set; } = "";

    public DocumentWpfWindow(AccountingStore store, Document? existing)
        : base(existing is null ? "Tạo chứng từ mới" : "Chỉnh sửa chứng từ", 820, 600)
    {
        _store = store;
        _existing = existing;
        foreach (var c in _store.ActiveCustomers())
            _customer.Items.Add(c.Name);

        if (existing != null)
        {
            _voucher.Text = existing.VoucherNo;
            _date.SelectedDate = existing.Date.ToDateTime(TimeOnly.MinValue);
            _customer.Text = existing.CustomerName;
            _contentText.Text = existing.Content;
            foreach (var line in existing.Lines)
                _lines.Add(new DocumentLineEdit(line.LineContent, line.Spec, line.Quantity, line.UnitPrice, line.Note));
        }
        if (_lines.Count == 0)
            _lines.Add(new DocumentLineEdit("", "", 0, 0, ""));

        Content = BuildContent();
    }

    private Wpf.UIElement BuildContent()
    {
        var root = new WpfControls.DockPanel();
        var form = FormGrid();
        AddRow(form, 0, "Số phiếu:", _voucher);
        AddRow(form, 1, "Ngày:", _date);
        AddRow(form, 2, "Khách hàng:", _customer);
        AddRow(form, 3, "Nội dung:", _contentText);

        var stack = new WpfControls.StackPanel();
        stack.Children.Add(form);
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Danh sách hàng hóa / dịch vụ",
            Foreground = WpfTheme.TextPrimary,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(11),
            Margin = new Wpf.Thickness(0, 14, 0, 8)
        });

        var grid = EditableGrid(_lines);
        grid.MinHeight = 250;
        grid.Columns.Add(TextColumn("LineContent", "Nội dung dòng", 220));
        grid.Columns.Add(TextColumn("Spec", "Quy cách", 100));
        grid.Columns.Add(TextColumn("Quantity", "Số lượng", 90));
        grid.Columns.Add(TextColumn("UnitPrice", "Đơn giá", 120));
        grid.Columns.Add(TextColumn("Amount", "Thành tiền", 120, true));
        grid.Columns.Add(TextColumn("Note", "Ghi chú", 180));
        stack.Children.Add(grid);

        var add = WpfUi.OutlineButton("＋  Thêm dòng", WpfTheme.Accent, WpfTheme.Border);
        add.Width = 130;
        add.Height = 32;
        add.Margin = new Wpf.Thickness(0, 8, 0, 0);
        add.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        add.Click += (_, _) => _lines.Add(new DocumentLineEdit("", "", 0, 0, ""));
        stack.Children.Add(add);

        var bar = ButtonBar();
        var save = PrimaryButton("Lưu");
        save.Click += (_, _) => Save();
        bar.Children.Add(CancelButton());
        bar.Children.Add(save);
        stack.Children.Add(bar);

        root.Children.Add(Card(stack));
        return root;
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_voucher.Text))
        {
            Wpf.MessageBox.Show(this, "Vui lòng nhập số phiếu.", "Thiếu thông tin", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }

        var lines = _lines
            .Where(l => !string.IsNullOrWhiteSpace(l.LineContent))
            .Select(l => new DocumentLine
            {
                LineContent = l.LineContent.Trim(),
                Spec = l.Spec.Trim(),
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Note = l.Note.Trim()
            })
            .ToList();
        var date = DateOnly.FromDateTime(_date.SelectedDate ?? DateTime.Today);
        var voucherNo = _voucher.Text.Trim();

        if (_existing != null)
        {
            _existing.VoucherNo = voucherNo;
            _existing.Date = date;
            _existing.CustomerName = _customer.Text.Trim();
            _existing.Content = _contentText.Text.Trim();
            _existing.Lines = lines;
            _store.Save();
        }
        else
        {
            _store.AddDocument(voucherNo, date, _customer.Text.Trim(), _contentText.Text.Trim(), "", lines);
        }

        SavedVoucherNo = voucherNo;
        DialogResult = true;
        Close();
    }

    private sealed class DocumentLineEdit
    {
        public DocumentLineEdit(string lineContent, string spec, decimal quantity, decimal unitPrice, string note)
        {
            LineContent = lineContent;
            Spec = spec;
            Quantity = quantity;
            UnitPrice = unitPrice;
            Note = note;
        }

        public string LineContent { get; set; }
        public string Spec { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string Note { get; set; }
        public string Amount => TextUtil.FormatMoney(Quantity * UnitPrice);
    }

    private static WpfControls.DataGrid EditableGrid(System.Collections.IEnumerable items) => new()
    {
        ItemsSource = items,
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = true,
        GridLinesVisibility = WpfControls.DataGridGridLinesVisibility.Horizontal,
        RowHeight = 36,
        ColumnHeaderHeight = 36,
        Background = WpfTheme.Surface,
        Foreground = WpfTheme.TextPrimary,
        BorderBrush = WpfTheme.Border,
        HorizontalGridLinesBrush = WpfTheme.GridLine,
        FontFamily = WpfTheme.Font
    };

    private static WpfControls.DataGridTextColumn TextColumn(string binding, string header, double width, bool readOnly = false) => new()
    {
        Header = header,
        Width = width,
        IsReadOnly = readOnly,
        Binding = new WpfData.Binding(binding)
        {
            UpdateSourceTrigger = WpfData.UpdateSourceTrigger.PropertyChanged,
            StringFormat = binding is "Quantity" or "UnitPrice" ? "N2" : null
        }
    };
}

internal sealed class GiaCongPhieuWpfWindow : KetoanDialogWindow
{
    private readonly GiaCongStore _store;
    private readonly GiaCongPhieu? _editing;
    private readonly string _username;
    private readonly WpfControls.ComboBox _loai = ComboBox();
    private readonly WpfControls.ComboBox _doiTac = ComboBox(editable: true);
    private readonly WpfControls.TextBox _nhanVien = TextBox();
    private readonly WpfControls.DatePicker _ngayLap = new() { SelectedDate = DateTime.Today, MinHeight = 30 };
    private readonly WpfControls.DatePicker _han = new() { SelectedDate = DateTime.Today.AddDays(30), MinHeight = 30, IsEnabled = false };
    private readonly WpfControls.CheckBox _coHan = new() { Content = "Có hạn hoàn thành", Foreground = WpfTheme.TextPrimary };
    private readonly WpfControls.TextBox _ghiChu = TextBox();
    private readonly ObservableCollection<GiaCongLineEdit> _lines = new();

    public string MaPhieu { get; private set; } = "";

    public GiaCongPhieuWpfWindow(GiaCongStore store, GiaCongPhieu? editing, string username)
        : base(editing is null ? "Tạo phiếu gia công mới" : $"Sửa phiếu gia công - {editing.MaPhieu}", 1180, 760)
    {
        _store = store;
        _editing = editing;
        _username = string.IsNullOrWhiteSpace(username) ? Environment.UserName : username;
        _loai.Items.Add("Xuất gia công");
        _loai.Items.Add("Nhập gia công");
        _loai.SelectedIndex = 0;
        _nhanVien.Text = _username;
        _nhanVien.IsReadOnly = true;
        _ghiChu.MaxLength = 255;
        _coHan.Checked += (_, _) => _han.IsEnabled = true;
        _coHan.Unchecked += (_, _) => _han.IsEnabled = false;

        try
        {
            foreach (var name in _store.GetAllPhieu().Select(p => p.DoiTac).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s))
                _doiTac.Items.Add(name);
        }
        catch { }

        if (editing != null)
        {
            _loai.SelectedItem = editing.LoaiPhieu;
            _doiTac.Text = editing.DoiTac;
            _nhanVien.Text = editing.NhanVienPhuTrach;
            _ngayLap.SelectedDate = editing.NgayLap.ToDateTime(TimeOnly.MinValue);
            _coHan.IsChecked = editing.HanHoanThanh.HasValue;
            _han.IsEnabled = editing.HanHoanThanh.HasValue;
            if (editing.HanHoanThanh.HasValue)
                _han.SelectedDate = editing.HanHoanThanh.Value.ToDateTime(TimeOnly.MinValue);
            _ghiChu.Text = editing.GhiChu;
            foreach (var line in editing.HangHoaList)
                _lines.Add(new GiaCongLineEdit(line));
        }
        if (_lines.Count == 0)
            _lines.Add(new GiaCongLineEdit());

        Content = BuildContent();
    }

    private Wpf.UIElement BuildContent()
    {
        var form = FormGrid();
        AddRow(form, 0, "Loại phiếu *", _loai);
        AddRow(form, 1, "Đối tác *", _doiTac);
        AddRow(form, 2, "Nhân viên", _nhanVien);
        AddRow(form, 3, "Ngày lập *", _ngayLap);
        AddRow(form, 4, "", _coHan);
        AddRow(form, 5, "Hạn hoàn thành", _han);
        AddRow(form, 6, "Ghi chú", _ghiChu);

        var stack = new WpfControls.StackPanel();
        stack.Children.Add(form);
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Danh sách hàng hóa",
            Foreground = WpfTheme.TextPrimary,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(12),
            Margin = new Wpf.Thickness(0, 14, 0, 8)
        });

        var grid = new WpfControls.DataGrid
        {
            ItemsSource = _lines,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = true,
            MinHeight = 300,
            RowHeight = 38,
            ColumnHeaderHeight = 38,
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            HorizontalGridLinesBrush = WpfTheme.GridLine,
            GridLinesVisibility = WpfControls.DataGridGridLinesVisibility.Horizontal
        };
        grid.Columns.Add(TextColumn("MaHang", "Mã hàng", 130));
        grid.Columns.Add(TextColumn("TenHang", "Tên hàng", 220));
        grid.Columns.Add(TextColumn("DonViTinh", "ĐVT", 100));
        grid.Columns.Add(TextColumn("SoLuong", "Số lượng", 120));
        grid.Columns.Add(TextColumn("DonGiaGiaCong", "Đơn giá GC", 130));
        grid.Columns.Add(TextColumn("GhiChu", "Ghi chú", 220));
        stack.Children.Add(grid);

        var add = WpfUi.OutlineButton("＋  Thêm dòng", WpfTheme.Accent, WpfTheme.Border);
        add.Width = 130;
        add.Height = 32;
        add.Margin = new Wpf.Thickness(0, 8, 0, 0);
        add.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        add.Click += (_, _) => _lines.Add(new GiaCongLineEdit());
        stack.Children.Add(add);

        var bar = ButtonBar();
        var save = PrimaryButton("Lưu");
        save.Click += (_, _) => Save();
        bar.Children.Add(CancelButton());
        bar.Children.Add(save);
        stack.Children.Add(bar);
        return Card(stack);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_doiTac.Text))
        {
            Wpf.MessageBox.Show(this, "Vui lòng nhập tên đối tác.", "Thiếu thông tin", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }

        var loai = _loai.SelectedItem?.ToString() ?? "Xuất gia công";
        var loaiDong = TextUtil.RemoveDiacritics(loai).Contains("nhap", StringComparison.OrdinalIgnoreCase)
            ? GiaCongLoaiDong.ThanhPham
            : GiaCongLoaiDong.NguyenLieu;
        var lines = _lines
            .Where(l => !string.IsNullOrWhiteSpace(l.TenHang))
            .Select(l => new GiaCongHangHoa
            {
                LoaiDong = loaiDong,
                MaHang = l.MaHang.Trim(),
                TenHang = l.TenHang.Trim(),
                DonViTinh = l.DonViTinh.Trim(),
                SoLuong = l.SoLuong,
                DonGiaGiaCong = l.DonGiaGiaCong,
                GhiChu = l.GhiChu.Trim(),
                TrangThaiDong = GiaCongTrangThaiDong.Cho
            })
            .ToList();
        var han = _coHan.IsChecked == true && _han.SelectedDate.HasValue ? DateOnly.FromDateTime(_han.SelectedDate.Value) : (DateOnly?)null;

        try
        {
            if (_editing == null)
            {
                var created = _store.CreatePhieu(loai, _doiTac.Text.Trim(), _nhanVien.Text.Trim(), DateOnly.FromDateTime(_ngayLap.SelectedDate ?? DateTime.Today), han, _ghiChu.Text.Trim(), lines);
                MaPhieu = created.MaPhieu;
            }
            else
            {
                _store.UpdatePhieu(_editing.Id, loai, _doiTac.Text.Trim(), _nhanVien.Text.Trim(), DateOnly.FromDateTime(_ngayLap.SelectedDate ?? DateTime.Today), han, _ghiChu.Text.Trim(), lines);
                MaPhieu = _editing.MaPhieu;
            }
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    private static WpfControls.DataGridTextColumn TextColumn(string binding, string header, double width) => new()
    {
        Header = header,
        Width = width,
        Binding = new WpfData.Binding(binding)
        {
            UpdateSourceTrigger = WpfData.UpdateSourceTrigger.PropertyChanged,
            StringFormat = binding is "SoLuong" or "DonGiaGiaCong" ? "N2" : null
        }
    };

    private sealed class GiaCongLineEdit
    {
        public GiaCongLineEdit() { }
        public GiaCongLineEdit(GiaCongHangHoa line)
        {
            MaHang = line.MaHang;
            TenHang = line.TenHang;
            DonViTinh = line.DonViTinh;
            SoLuong = line.SoLuong;
            DonGiaGiaCong = line.DonGiaGiaCong;
            GhiChu = line.GhiChu;
        }
        public string MaHang { get; set; } = "";
        public string TenHang { get; set; } = "";
        public string DonViTinh { get; set; } = "";
        public decimal SoLuong { get; set; }
        public decimal DonGiaGiaCong { get; set; }
        public string GhiChu { get; set; } = "";
    }
}

internal sealed class ChangePasswordWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly AppUser _user;
    private readonly WpfControls.PasswordBox _current = PasswordBox();
    private readonly WpfControls.PasswordBox _new = PasswordBox();
    private readonly WpfControls.PasswordBox _confirm = PasswordBox();

    public ChangePasswordWpfWindow(AccountingStore store, AppUser user) : base("Đổi mật khẩu", 430, 300)
    {
        _store = store;
        _user = user;
        var form = FormGrid();
        AddRow(form, 0, "Mật khẩu hiện tại:", _current);
        AddRow(form, 1, "Mật khẩu mới:", _new);
        AddRow(form, 2, "Nhập lại mật khẩu:", _confirm);
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(form);
        var bar = ButtonBar();
        var save = PrimaryButton("Cập nhật");
        save.Click += (_, _) => Save();
        bar.Children.Add(CancelButton());
        bar.Children.Add(save);
        stack.Children.Add(bar);
        Content = Card(stack);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_new.Password))
        {
            Wpf.MessageBox.Show(this, "Vui lòng nhập mật khẩu mới.", "Thiếu thông tin", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }
        if (_new.Password != _confirm.Password)
        {
            Wpf.MessageBox.Show(this, "Mật khẩu nhập lại không khớp.", "Không khớp", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }
        try
        {
            _store.UpdateCurrentUserProfile(_user.FullName, _current.Password, _new.Password);
            Wpf.MessageBox.Show(this, "Đã đổi mật khẩu thành công.", "Thành công");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }
}

internal sealed class ProfileWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly AppUser _user;
    private readonly WpfControls.TextBox _name = TextBox();
    private string? _pickedPath;
    private bool _removed;

    public bool ProfileChanged { get; private set; }

    public ProfileWpfWindow(AccountingStore store, AppUser user) : base("Tùy chỉnh tài khoản", 460, 340)
    {
        _store = store;
        _user = user;
        _name.Text = user.FullName;

        var stack = new WpfControls.StackPanel();
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = TextUtil.Initials(user.DisplayName),
            Width = 96,
            Height = 96,
            TextAlignment = Wpf.TextAlignment.Center,
            FontSize = WpfTheme.Pt(26),
            FontWeight = Wpf.FontWeights.Bold,
            Foreground = WpfMedia.Brushes.White,
            Background = WpfTheme.SidebarActive,
            Padding = new Wpf.Thickness(0, 30, 0, 0),
            Margin = new Wpf.Thickness(0, 0, 0, 12)
        });
        var imageButtons = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
        var choose = WpfUi.OutlineButton("Chọn ảnh...", WpfTheme.TextPrimary, WpfTheme.Border);
        choose.Width = 120;
        choose.Height = 32;
        choose.Click += (_, _) => ChooseImage();
        var remove = WpfUi.OutlineButton("Xóa ảnh", WpfTheme.Danger, WpfTheme.Border);
        remove.Width = 100;
        remove.Height = 32;
        remove.Margin = new Wpf.Thickness(8, 0, 0, 0);
        remove.Click += (_, _) => { _pickedPath = null; _removed = true; };
        imageButtons.Children.Add(choose);
        imageButtons.Children.Add(remove);
        stack.Children.Add(imageButtons);
        stack.Children.Add(Label("Tên hiển thị"));
        stack.Children.Add(_name);

        var bar = ButtonBar();
        var save = PrimaryButton("Lưu");
        save.Click += (_, _) => Save();
        bar.Children.Add(CancelButton());
        bar.Children.Add(save);
        stack.Children.Add(bar);
        Content = Card(stack);
    }

    private void ChooseImage()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh đại diện",
            Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (ofd.ShowDialog(this) == true)
        {
            _pickedPath = ofd.FileName;
            _removed = false;
        }
    }

    private void Save()
    {
        try
        {
            var newName = _name.Text.Trim();
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
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }
}

internal sealed class AddUserWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly WpfControls.TextBox _username = TextBox();
    private readonly WpfControls.TextBox _fullName = TextBox();
    private readonly WpfControls.PasswordBox _password = PasswordBox();

    public AddUserWpfWindow(AccountingStore store, AppUser adminUser) : base("Thêm người dùng mới", 440, 320)
    {
        _store = store;
        var form = FormGrid();
        AddRow(form, 0, "Tên đăng nhập:", _username);
        AddRow(form, 1, "Họ tên:", _fullName);
        AddRow(form, 2, "Mật khẩu:", _password);
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(form);
        var bar = ButtonBar();
        var save = PrimaryButton("Tạo");
        save.Click += (_, _) => Save();
        bar.Children.Add(CancelButton());
        bar.Children.Add(save);
        stack.Children.Add(bar);
        Content = Card(stack);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrWhiteSpace(_password.Password))
        {
            Wpf.MessageBox.Show(this, "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.", "Thiếu thông tin", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
            return;
        }
        try
        {
            _store.AdminCreateUser(_username.Text.Trim(), _fullName.Text.Trim(), _password.Password);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, $"Lỗi tạo người dùng: {ex.Message}", "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }
}

internal sealed class PasswordResetRequestsWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly WpfControls.DataGrid _grid = new();

    public PasswordResetRequestsWpfWindow(AccountingStore store) : base("Yêu cầu đổi mật khẩu", 660, 440)
    {
        _store = store;
        Content = BuildContent();
        Reload();
    }

    private Wpf.UIElement BuildContent()
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Chọn một yêu cầu rồi bấm cấp mã. Mã có hiệu lực 15 phút.",
            Foreground = WpfTheme.TextSecondary,
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 0, 0, 10)
        });
        SetupReadOnlyGrid(_grid);
        _grid.MinHeight = 240;
        _grid.Columns.Add(ReadColumn("Username", "Tên đăng nhập", 160));
        _grid.Columns.Add(ReadColumn("FullName", "Họ tên", 200));
        _grid.Columns.Add(ReadColumn("RequestedAtText", "Thời gian yêu cầu", 170));
        stack.Children.Add(_grid);
        var bar = ButtonBar();
        var gen = PrimaryButton("Cấp mã");
        gen.Click += (_, _) => Generate();
        bar.Children.Add(CancelButton("Đóng"));
        bar.Children.Add(gen);
        stack.Children.Add(bar);
        return Card(stack);
    }

    private void Reload()
    {
        _grid.ItemsSource = _store.GetPendingPasswordResetRequests()
            .Select(r => new ResetRow(r.Username, r.FullName, r.RequestedAt.ToString("dd/MM/yyyy HH:mm")))
            .ToList();
    }

    private void Generate()
    {
        if (_grid.SelectedItem is not ResetRow row)
        {
            Wpf.MessageBox.Show(this, "Hãy chọn một yêu cầu.", "Chưa chọn");
            return;
        }
        try
        {
            var user = _store.GetUsers().FirstOrDefault(u => u.Username == row.Username);
            if (user == null)
            {
                Wpf.MessageBox.Show(this, "Không tìm thấy tài khoản.", "Lỗi");
                return;
            }
            var code = _store.AdminCreatePasswordResetCode(user.Id);
            CodeDisplayWpfWindow.Show(this, row.Username, code);
            Reload();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    private sealed record ResetRow(string Username, string FullName, string RequestedAtText);
}

internal sealed class WorkAccessRequestsWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly WpfControls.DataGrid _grid = new();

    public WorkAccessRequestsWpfWindow(AccountingStore store) : base("Duyệt tăng ca", 760, 460)
    {
        _store = store;
        Content = BuildContent();
        Reload();
    }

    private Wpf.UIElement BuildContent()
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Chọn yêu cầu rồi bấm Duyệt để cho phép nhân viên tăng ca ngoài giờ.",
            Foreground = WpfTheme.TextSecondary,
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 0, 0, 10)
        });
        SetupReadOnlyGrid(_grid);
        _grid.MinHeight = 260;
        _grid.Columns.Add(ReadColumn("Username", "Tên đăng nhập", 130));
        _grid.Columns.Add(ReadColumn("FullName", "Họ tên", 160));
        _grid.Columns.Add(ReadColumn("WorkDateText", "Ngày", 100));
        _grid.Columns.Add(ReadColumn("Reason", "Lý do", 260));
        stack.Children.Add(_grid);

        var bar = ButtonBar();
        var approve = PrimaryButton("Duyệt");
        approve.Click += (_, _) => ApproveSelected();
        var approveAll = WpfUi.OutlineButton("Duyệt tất cả", WpfTheme.TextPrimary, WpfTheme.Border);
        approveAll.Width = 120;
        approveAll.Height = 34;
        approveAll.Margin = new Wpf.Thickness(8, 0, 0, 0);
        approveAll.Click += (_, _) => ApproveAll();
        bar.Children.Add(CancelButton("Đóng"));
        bar.Children.Add(approveAll);
        bar.Children.Add(approve);
        stack.Children.Add(bar);
        return Card(stack);
    }

    private void Reload()
    {
        _grid.ItemsSource = _store.GetPendingWorkAccessRequests()
            .Select(r => new WorkAccessRow(r.Id, r.Username, r.FullName, r.WorkDate.ToString("dd/MM/yyyy"), r.Reason))
            .ToList();
    }

    private void ApproveSelected()
    {
        if (_grid.SelectedItem is not WorkAccessRow row)
        {
            Wpf.MessageBox.Show(this, "Hãy chọn một yêu cầu.", "Chưa chọn");
            return;
        }
        try { _store.ApproveWorkAccessRequests(new[] { row.Id }); Reload(); }
        catch (Exception ex) { Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error); }
    }

    private void ApproveAll()
    {
        var ids = (_grid.ItemsSource as IEnumerable<WorkAccessRow>)?.Select(r => r.Id).ToList() ?? [];
        if (ids.Count == 0) return;
        try { _store.ApproveWorkAccessRequests(ids); Reload(); }
        catch (Exception ex) { Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error); }
    }

    private sealed record WorkAccessRow(long Id, string Username, string FullName, string WorkDateText, string Reason);
}

internal sealed class OvertimeRequestWpfWindow : KetoanDialogWindow
{
    private readonly AccountingStore _store;
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    public OvertimeRequestWpfWindow(AccountingStore store) : base("Chấm công tăng ca", 480, 300)
    {
        _store = store;
        Content = BuildContent();
    }

    private Wpf.UIElement BuildContent()
    {
        WorkAccessRequest? req = null;
        try { req = _store.GetWorkAccessForToday(_today); } catch { }
        var stack = new WpfControls.StackPanel();
        string info;
        var showPunch = false;
        if (req != null && req.IsApproved)
        {
            var baseInfo = req.PunchAt != null ? $"từ lúc chấm công {req.PunchAt:HH:mm}" : $"từ lúc duyệt {req.ApprovedAt:HH:mm}";
            info = $"✓ Tăng ca đã được duyệt.\n\nĐồng hồ trên thẻ \"Ca làm việc\" đang đếm {baseInfo}.";
        }
        else if (req?.PunchAt != null)
        {
            info = $"Đã chấm công lúc {req.PunchAt:HH:mm} ({DateTime.Now:dd/MM/yyyy}).\n\nĐang chờ admin duyệt.";
        }
        else
        {
            info = $"Hiện đang ngoài giờ làm việc ({DateTime.Now:HH:mm}).\n\nBấm \"Chấm công\" để bắt đầu ca tăng ca.";
            showPunch = true;
        }
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = info,
            Foreground = WpfTheme.TextPrimary,
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 0, 0, 16)
        });
        var bar = ButtonBar();
        bar.Children.Add(CancelButton("Đóng"));
        if (showPunch)
        {
            var punch = WpfUi.FilledButton("🕐  Chấm công", WpfTheme.Success, WpfMedia.Brushes.White);
            punch.Width = 140;
            punch.Height = 34;
            punch.Margin = new Wpf.Thickness(8, 0, 0, 0);
            punch.Click += (_, _) => Punch();
            bar.Children.Add(punch);
        }
        stack.Children.Add(bar);
        return Card(stack);
    }

    private void Punch()
    {
        try
        {
            _store.CreateOrGetWorkAccessRequest(DateTime.Now, "Chấm công tăng ca");
            _store.PunchWorkAccess(_today);
            Wpf.MessageBox.Show(this, $"Đã chấm công lúc {DateTime.Now:HH:mm}.\nChờ admin duyệt để được tính tăng ca.", "Đã chấm công");
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(this, ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }
}

internal sealed class CodeDisplayWpfWindow : KetoanDialogWindow
{
    private CodeDisplayWpfWindow(string username, RegistrationCode code) : base("Mã đổi mật khẩu", 430, 250)
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = $"Mã đổi mật khẩu cho tài khoản \"{username}\".\nGửi mã này cho người dùng (hết hạn lúc {code.ExpiresAt:HH:mm}).",
            Foreground = WpfTheme.TextSecondary,
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(new WpfControls.TextBox
        {
            Text = code.Code,
            IsReadOnly = true,
            FontSize = WpfTheme.Pt(18),
            FontWeight = Wpf.FontWeights.Bold,
            TextAlignment = Wpf.TextAlignment.Center,
            Foreground = WpfTheme.TextPrimary,
            Background = WpfTheme.SurfaceAlt,
            BorderBrush = WpfTheme.Border,
            Padding = new Wpf.Thickness(8)
        });
        var bar = ButtonBar();
        var copy = PrimaryButton("Sao chép");
        copy.Click += (_, _) =>
        {
            try { Wpf.Clipboard.SetText(code.Code); copy.Content = "Đã chép"; } catch { }
        };
        bar.Children.Add(CancelButton("Xong"));
        bar.Children.Add(copy);
        stack.Children.Add(bar);
        Content = Card(stack);
    }

    public static void Show(Wpf.Window owner, string username, RegistrationCode code)
    {
        new CodeDisplayWpfWindow(username, code) { Owner = owner }.ShowDialog();
    }
}

internal sealed class DatabaseSetupWpfWindow : KetoanDialogWindow
{
    private readonly string _initialConnectionString;
    private readonly string _initialError;
    private readonly WpfControls.TextBox _server = TextBox();
    private readonly WpfControls.TextBox _database = TextBox();
    private readonly WpfControls.TextBox _user = TextBox();
    private readonly WpfControls.PasswordBox _password = PasswordBox();
    private readonly WpfControls.CheckBox _windowsAuth = new() { Content = "Dùng Windows Authentication", Foreground = WpfTheme.TextPrimary };
    private readonly WpfControls.TextBlock _status = new() { TextWrapping = Wpf.TextWrapping.Wrap, Foreground = WpfTheme.Danger };

    public string SavedConnectionString { get; private set; } = "";

    public DatabaseSetupWpfWindow(string initialConnectionString, string initialError)
        : base("Cấu hình kết nối SQL Server", 640, 460)
    {
        _initialConnectionString = initialConnectionString;
        _initialError = initialError;
        _windowsAuth.Checked += (_, _) => ToggleSqlLoginFields();
        _windowsAuth.Unchecked += (_, _) => ToggleSqlLoginFields();
        PopulateFromConnectionString(initialConnectionString);
        Content = BuildContent();
    }

    private Wpf.UIElement BuildContent()
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Không kết nối được SQL Server",
            Foreground = WpfTheme.TextPrimary,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(14)
        });
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Nhập thông tin máy chủ SQL trên mạng LAN. Cấu hình sẽ được lưu vào thư mục người dùng.",
            Foreground = WpfTheme.TextSecondary,
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 6, 0, 12)
        });

        var form = FormGrid();
        AddRow(form, 0, "Máy chủ / IP:", _server);
        AddRow(form, 1, "Database:", _database);
        AddRow(form, 2, "Tài khoản SQL:", _user);
        AddRow(form, 3, "Mật khẩu:", _password);
        AddRow(form, 4, "", _windowsAuth);
        stack.Children.Add(form);

        _status.Text = $"Lỗi hiện tại: {_initialError}";
        _status.Margin = new Wpf.Thickness(0, 12, 0, 0);
        stack.Children.Add(_status);

        var bar = ButtonBar();
        var save = PrimaryButton("Kiểm tra && lưu");
        save.Width = 140;
        save.Click += (_, _) => TestAndSave();
        bar.Children.Add(CancelButton("Thoát"));
        bar.Children.Add(save);
        stack.Children.Add(bar);
        return Card(stack);
    }

    private void PopulateFromConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            _server.Text = builder.DataSource;
            _database.Text = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "KetoanMini" : builder.InitialCatalog;
            _windowsAuth.IsChecked = builder.IntegratedSecurity;
            _user.Text = builder.UserID;
            _password.Password = builder.Password;
        }
        catch
        {
            _server.Text = "";
            _database.Text = "KetoanMini";
            _user.Text = "ketoan_app";
            _password.Password = "";
        }
        ToggleSqlLoginFields();
    }

    private void ToggleSqlLoginFields()
    {
        var sqlLogin = _windowsAuth.IsChecked != true;
        _user.IsEnabled = sqlLogin;
        _password.IsEnabled = sqlLogin;
    }

    private void TestAndSave()
    {
        if (string.IsNullOrWhiteSpace(_server.Text) || string.IsNullOrWhiteSpace(_database.Text))
        {
            SetStatus("Vui lòng nhập máy chủ và database.", WpfTheme.Warning);
            return;
        }
        if (_windowsAuth.IsChecked != true && (string.IsNullOrWhiteSpace(_user.Text) || string.IsNullOrWhiteSpace(_password.Password)))
        {
            SetStatus("Vui lòng nhập tài khoản và mật khẩu SQL.", WpfTheme.Warning);
            return;
        }

        try
        {
            IsEnabled = false;
            var connectionString = BuildConnectionString();
            _ = new AccountingStore(connectionString);
            DatabaseConnectionConfig.SaveUserConnectionString(connectionString);
            SavedConnectionString = connectionString;
            Wpf.MessageBox.Show(this,
                $"Đã kết nối và lưu cấu hình thành công.\n\nFile cấu hình:\n{DatabaseConnectionConfig.UserConfigPath}",
                "Kết nối thành công",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, WpfTheme.Danger);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _server.Text.Trim(),
            InitialCatalog = _database.Text.Trim(),
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
            ConnectTimeout = 10
        };

        if (_windowsAuth.IsChecked == true)
            builder.IntegratedSecurity = true;
        else
        {
            builder.UserID = _user.Text.Trim();
            builder.Password = _password.Password;
        }
        return builder.ConnectionString;
    }

    private void SetStatus(string text, WpfMedia.Brush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
    }
}

internal static class DialogGridHelpers
{
    public static void SetupReadOnlyGrid(WpfControls.DataGrid grid)
    {
        grid.AutoGenerateColumns = false;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        grid.IsReadOnly = true;
        grid.RowHeight = 38;
        grid.ColumnHeaderHeight = 36;
        grid.Background = WpfTheme.Surface;
        grid.Foreground = WpfTheme.TextPrimary;
        grid.BorderBrush = WpfTheme.Border;
        grid.HorizontalGridLinesBrush = WpfTheme.GridLine;
        grid.GridLinesVisibility = WpfControls.DataGridGridLinesVisibility.Horizontal;
        grid.SelectionMode = WpfControls.DataGridSelectionMode.Single;
    }

    public static WpfControls.DataGridTextColumn ReadColumn(string binding, string header, double width) => new()
    {
        Header = header,
        Width = width,
        Binding = new WpfData.Binding(binding)
    };
}
