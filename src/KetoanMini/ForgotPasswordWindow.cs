using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

// ============================================================================
// ForgotPasswordWindow — bản WPF của ForgotPasswordDialog
// ============================================================================
internal sealed class ForgotPasswordWindow : Wpf.Window
{
    private readonly AccountingStore _store;
    private LoginInputBox _txtUsername = null!;
    private LoginInputBox _txtCode = null!;
    private LoginInputBox _txtNewPass = null!;
    private WpfControls.TextBlock _lblError = null!;

    public ForgotPasswordWindow(AccountingStore store)
    {
        _store = store;
        Build();
    }

    private void Build()
    {
        Title = "Quên mật khẩu";
        Width = 412;
        SizeToContent = Wpf.SizeToContent.Height;
        ResizeMode = Wpf.ResizeMode.NoResize;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterOwner;
        FontFamily = WpfTheme.Font;
        Background = WpfTheme.Surface;
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);

        var root = new WpfControls.StackPanel();

        // Header navy
        var header = new WpfControls.Border { Background = WpfTheme.SidebarBg, Height = 70 };
        var headerStack = new WpfControls.StackPanel { Margin = new Wpf.Thickness(24, 12, 24, 0) };
        headerStack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Quên mật khẩu",
            Foreground = WpfMedia.Brushes.White,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(12)
        });
        headerStack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Lấy lại mật khẩu bằng mã do admin cấp",
            Foreground = WpfTheme.SidebarText,
            FontSize = WpfTheme.Pt(8.5),
            Margin = new Wpf.Thickness(0, 4, 0, 0)
        });
        header.Child = headerStack;
        root.Children.Add(header);

        // Content
        var content = new WpfControls.StackPanel { Margin = new Wpf.Thickness(28, 18, 28, 18) };

        content.Children.Add(FieldLabel("Tài khoản"));
        _txtUsername = new LoginInputBox("Nhập tài khoản") { Margin = new Wpf.Thickness(0, 0, 0, 12) };
        content.Children.Add(_txtUsername);

        var btnRequest = WpfUi.OutlineButton("📨  Gửi yêu cầu cho admin", WpfTheme.Accent, WpfTheme.Accent, fontPt: 9.5);
        btnRequest.Height = 38;
        btnRequest.Margin = new Wpf.Thickness(0, 0, 0, 16);
        btnRequest.Click += (_, _) => DoRequest();
        content.Children.Add(btnRequest);

        content.Children.Add(new WpfControls.Border { Height = 1, Background = WpfTheme.Border, Margin = new Wpf.Thickness(0, 0, 0, 12) });
        content.Children.Add(new WpfControls.TextBlock
        {
            Text = "Đã có mã từ admin? Nhập bên dưới để đặt lại:",
            Foreground = WpfTheme.TextSecondary,
            FontSize = WpfTheme.Pt(8.5),
            Margin = new Wpf.Thickness(0, 0, 0, 14)
        });

        content.Children.Add(FieldLabel("Mã đặt lại (admin cấp)"));
        _txtCode = new LoginInputBox("") { Margin = new Wpf.Thickness(0, 0, 0, 12) };
        content.Children.Add(_txtCode);

        content.Children.Add(FieldLabel("Mật khẩu mới"));
        _txtNewPass = new LoginInputBox("", isPassword: true) { Margin = new Wpf.Thickness(0, 0, 0, 8) };
        content.Children.Add(_txtNewPass);

        _lblError = new WpfControls.TextBlock
        {
            Foreground = WpfTheme.Danger,
            FontSize = WpfTheme.Pt(8.5),
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 0, 0, 6),
            Visibility = Wpf.Visibility.Collapsed
        };
        content.Children.Add(_lblError);

        var btnReset = WpfUi.FilledButton("Đặt lại mật khẩu", WpfTheme.Accent, WpfMedia.Brushes.White, fontPt: 10);
        btnReset.Height = 42;
        btnReset.Click += (_, _) => DoReset();
        content.Children.Add(btnReset);

        root.Children.Add(content);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == WpfInput.Key.Enter)
            {
                DoReset();
                e.Handled = true;
            }
        };
    }

    private static WpfControls.TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontWeight = Wpf.FontWeights.Bold,
        FontSize = WpfTheme.Pt(9),
        Foreground = WpfTheme.TextPrimary,
        Margin = new Wpf.Thickness(0, 0, 0, 4)
    };

    private void DoRequest()
    {
        _lblError.Visibility = Wpf.Visibility.Collapsed;
        var username = _txtUsername.Text.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            ShowError("Nhập tài khoản để gửi yêu cầu.");
            return;
        }

        try
        {
            _store.CreatePasswordResetRequest(username);
            Wpf.MessageBox.Show(
                "Đã gửi yêu cầu đặt lại mật khẩu.\n\nLiên hệ admin để nhận mã, sau đó nhập mã vào ô \"Mã đặt lại\" bên dưới.",
                "Đã gửi yêu cầu",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi: {ex.Message}");
        }
    }

    private void DoReset()
    {
        _lblError.Visibility = Wpf.Visibility.Collapsed;

        var username = _txtUsername.Text.Trim();
        var code = _txtCode.Text.Trim();
        var newPass = _txtNewPass.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(newPass))
        {
            ShowError("Vui lòng điền đầy đủ thông tin.");
            return;
        }

        if (newPass.Length < 6)
        {
            ShowError("Mật khẩu mới phải có ít nhất 6 ký tự.");
            return;
        }

        try
        {
            _store.ResetPasswordWithCode(username, code, newPass);
            Wpf.MessageBox.Show(
                "Đặt lại mật khẩu thành công!\nBạn có thể đăng nhập với mật khẩu mới.",
                "Thành công",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (InvalidOperationException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        _lblError.Text = message;
        _lblError.Visibility = Wpf.Visibility.Visible;
    }
}
