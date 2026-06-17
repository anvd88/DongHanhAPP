using Wpf = System.Windows;
using WpfImaging = System.Windows.Media.Imaging;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

// ============================================================================
// UpdateWindow — popup cập nhật (bản WPF/XAML), giao diện card bo góc với logo
// mũi tên + badge "!", pill phiên bản, hộp ghi chú và nút Thoát / Cập nhật ngay.
//   DialogResult == true  => đã chạy setup, app nên thoát để cài.
//   DialogResult != true  => người dùng hoãn (Để sau) / thoát.
//
// Giao diện được khai báo trong UpdateWindow.xaml. Hai icon trang trí (ảnh hero
// và icon cạnh tiêu đề) được nạp từ file PNG trong thư mục assets nếu có; nếu
// thiếu file thì tự động hiển thị icon vector dự phòng để cửa sổ luôn chạy được.
// ============================================================================
public partial class UpdateWindow : Wpf.Window
{
    private readonly AccountingStore _store;
    private readonly AppRelease _release;
    private readonly bool _blocking;

    public UpdateWindow(AccountingStore store, AppRelease release, bool blocking)
    {
        _store = store;
        _release = release;
        _blocking = blocking;

        InitializeComponent();
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        PopulateContent();
        LoadIcons();
    }

    // ── Đổ dữ liệu phiên bản / ghi chú vào giao diện ─────────────────────────
    private void PopulateContent()
    {
        RunCurrent.Text = AppVersion.CurrentText;
        RunNew.Text = _release.Version;

        var hasNotes = !string.IsNullOrWhiteSpace(_release.ReleaseNotes);
        TxtNotes.Text = hasNotes ? _release.ReleaseNotes : "(Không có ghi chú)";
        TxtNotes.Foreground = WpfTheme.Brush(hasNotes ? "#071338" : "#94A3B8");

        // Bản bắt buộc thì nút trái là "Thoát"; bản tùy chọn thì là "Để sau".
        TxtLater.Text = _blocking ? "Thoát" : "Để sau";

        if (!_release.HasSetupSource)
        {
            BtnUpdate.IsEnabled = false;
            TxtUpdate.Text = "Chưa có file setup";
        }
    }

    // ── Nạp 2 icon PNG (ảnh 2 = hero, ảnh 3 = icon tiêu đề) nếu có ───────────
    private void LoadIcons()
    {
        var hero = TryLoadAsset("update_hero_icon.png");
        if (hero is not null)
        {
            HeroImage.Source = hero;
            HeroImage.Visibility = Wpf.Visibility.Visible;
            HeroFallback.Visibility = Wpf.Visibility.Collapsed;
        }

        var title = TryLoadAsset("update_title_icon.png");
        if (title is not null)
        {
            TitleIconImage.Source = title;
            TitleIconImage.Visibility = Wpf.Visibility.Visible;
            TitleIconFallback.Visibility = Wpf.Visibility.Collapsed;
        }
    }

    /// <summary>Nạp ảnh từ thư mục assets cạnh file thực thi; trả về null nếu thiếu/đọc lỗi.</summary>
    private static WpfImaging.BitmapImage? TryLoadAsset(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var bmp = new WpfImaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = WpfImaging.BitmapCacheOption.OnLoad;
            bmp.CreateOptions = WpfImaging.BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ── Sự kiện ──────────────────────────────────────────────────────────────
    private void Window_PreviewKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            Cancel();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, WpfInput.MouseButtonEventArgs e)
    {
        if (e.ButtonState == WpfInput.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, Wpf.RoutedEventArgs e) => Cancel();

    private void BtnLater_Click(object sender, Wpf.RoutedEventArgs e) => Cancel();

    private void BtnUpdate_Click(object sender, Wpf.RoutedEventArgs e) => DoUpdate();

    // ── Logic ──────────────────────────────────────────────────────────────
    private void Cancel()
    {
        DialogResult = false;
        Close();
    }

    // Chuyển sang màn hình "Đang cập nhật": mở UpdateProgressWindow (tự tải + cài
    // im lặng) ĐÈ LÊN cửa sổ này — không gọi Hide()/Show() vì cửa sổ đang ở chế độ
    // modal (ShowDialog), gọi Hide()/Show() sẽ ném InvalidOperationException.
    // Cửa sổ tiến trình cùng kích thước/căn giữa nên che kín cửa sổ này.
    private void DoUpdate()
    {
        var progress = new UpdateProgressWindow(_store, _release, _blocking) { Owner = this };
        if (progress.ShowDialog() != true)
        {
            // Người dùng hủy lúc đang tải: cửa sổ này vẫn hiển thị bên dưới.
            return;
        }

        // Cập nhật xong -> hiện màn "Cập nhật thành công" (onFinish rỗng để không bật MessageBox demo).
        var success = new UpdateSuccessWindow(
            oldVersion: AppVersion.CurrentText,
            currentVersion: _release.Version,
            successMessage: null,
            onFinish: () => { }) { Owner = this };
        success.ShowDialog();

        DialogResult = true; // báo caller: đã cập nhật -> chuyển tới đăng nhập
        Close();
    }
}
