using Wpf = System.Windows;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

// ============================================================================
// UpdateSuccessWindow — màn hình "Cập nhật thành công" (WPF/XAML).
//   • Truyền oldVersion / currentVersion / successMessage qua constructor.
//   • Nút "Đóng" và nút X: đóng cửa sổ (DialogResult = false).
//   • Nút "Hoàn tất": gọi FinishUpdate(). Nếu có truyền onFinish thì chạy nó
//     (vd: mở lại trang đăng nhập); nếu không thì hiện MessageBox demo.
// ============================================================================
public partial class UpdateSuccessWindow : Wpf.Window
{
    private readonly Action? _onFinish;

    public UpdateSuccessWindow(
        string oldVersion,
        string currentVersion,
        string? successMessage = null,
        Action? onFinish = null)
    {
        _onFinish = onFinish;

        InitializeComponent();
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        RunOld.Text = string.IsNullOrWhiteSpace(oldVersion) ? "—" : oldVersion;
        RunCurrent.Text = string.IsNullOrWhiteSpace(currentVersion) ? "—" : currentVersion;
        TxtSuccess.Text = string.IsNullOrWhiteSpace(successMessage)
            ? "Cài đặt hoàn tất. Không phát hiện lỗi."
            : successMessage;
    }

    // ── Sự kiện ──────────────────────────────────────────────────────────────
    private void Window_PreviewKeyDown(object sender, WpfInput.KeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            CloseWindow();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, WpfInput.MouseButtonEventArgs e)
    {
        if (e.ButtonState == WpfInput.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, Wpf.RoutedEventArgs e) => CloseWindow();

    private void BtnClose2_Click(object sender, Wpf.RoutedEventArgs e) => CloseWindow();

    private void BtnFinish_Click(object sender, Wpf.RoutedEventArgs e) => FinishUpdate();

    // ── Logic ──────────────────────────────────────────────────────────────
    private void CloseWindow()
    {
        TrySetDialogResult(false);
        Close();
    }

    private void FinishUpdate()
    {
        TrySetDialogResult(true);
        Close();

        if (_onFinish is not null)
        {
            _onFinish(); // vd: mở trang đăng nhập
        }
        else
        {
            Wpf.MessageBox.Show(
                "Cập nhật hoàn tất. Ứng dụng sẵn sàng sử dụng.",
                "Cập nhật",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
    }

    /// <summary>Đặt DialogResult an toàn (chỉ hợp lệ khi mở bằng ShowDialog).</summary>
    private void TrySetDialogResult(bool value)
    {
        try
        {
            DialogResult = value;
        }
        catch (InvalidOperationException)
        {
            // Cửa sổ được mở bằng Show() (không phải ShowDialog) → bỏ qua.
        }
    }
}
