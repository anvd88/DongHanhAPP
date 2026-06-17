using System.Globalization;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfDocs = System.Windows.Documents;
using WpfEffects = System.Windows.Media.Effects;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;
using WpfShapes = System.Windows.Shapes;

namespace KetoanMini;

// ============================================================================
// UpdateWindow — popup cập nhật (bản WPF), giao diện card bo góc với logo
// mũi tên + badge "!", pill phiên bản, hộp ghi chú và nút Thoát / Cập nhật ngay.
//   DialogResult == true  => đã chạy setup, app nên thoát để cài.
//   DialogResult != true  => người dùng hoãn (Để sau) / thoát.
// ============================================================================
public sealed class UpdateWindow : Wpf.Window
{
    private readonly AccountingStore _store;
    private readonly AppRelease _release;
    private readonly bool _blocking;

    private WpfControls.Border _card = null!;
    private WpfControls.Button _btnUpdate = null!;
    private WpfControls.Button _btnLater = null!;
    private WpfControls.Grid _actionHost = null!;
    private WpfControls.StackPanel _progressPanel = null!;
    private WpfControls.ProgressBar _progressBar = null!;
    private WpfControls.TextBlock _statusText = null!;

    public UpdateWindow(AccountingStore store, AppRelease release, bool blocking)
    {
        _store = store;
        _release = release;
        _blocking = blocking;
        BuildUi();
    }

    private void BuildUi()
    {
        Title = _blocking ? "Bắt buộc cập nhật" : "Có bản cập nhật mới";
        Width = 720;
        Height = 620;
        WindowStyle = Wpf.WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfMedia.Brushes.Transparent;
        ResizeMode = Wpf.ResizeMode.NoResize;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        FontFamily = WpfTheme.Font;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);

        // Lớp ngoài trong suốt + chừa lề cho đổ bóng.
        var root = new WpfControls.Grid { Margin = new Wpf.Thickness(28) };

        var shadow = new WpfControls.Border
        {
            Background = WpfMedia.Brushes.White,
            CornerRadius = new Wpf.CornerRadius(22),
            Margin = new Wpf.Thickness(4),
            Effect = new WpfEffects.DropShadowEffect
            {
                Color = WpfMedia.Colors.Black,
                BlurRadius = 28,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.22
            }
        };
        root.Children.Add(shadow);

        _card = new WpfControls.Border
        {
            Background = WpfMedia.Brushes.White,
            CornerRadius = new Wpf.CornerRadius(24)
        };
        _card.SizeChanged += (_, _) =>
            _card.Clip = new WpfMedia.RectangleGeometry(
                new Wpf.Rect(0, 0, _card.ActualWidth, _card.ActualHeight), 24, 24);
        root.Children.Add(_card);

        var content = new WpfControls.Grid { Margin = new Wpf.Thickness(34, 26, 34, 26) };
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto }); // title bar
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto }); // logo
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto }); // big title
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto }); // pills
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto }); // notes label
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) }); // notes box
        content.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto }); // actions

        AddAt(content, BuildTitleBar(), 0);
        AddAt(content, BuildLogo(), 1, new Wpf.Thickness(0, 10, 0, 6));
        AddAt(content, BigTitle(), 2);
        AddAt(content, BuildPills(), 3, new Wpf.Thickness(0, 14, 0, 0));
        AddAt(content, BuildNotesLabel(), 4, new Wpf.Thickness(0, 22, 0, 8));
        AddAt(content, BuildNotesBox(), 5);
        AddAt(content, BuildActions(), 6, new Wpf.Thickness(0, 20, 0, 0));

        _card.Child = content;
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == WpfInput.Key.Escape)
            {
                Cancel();
            }
        };
    }

    // ── Title bar (icon + tiêu đề nhỏ trái, nút X phải) ──────────────────────
    private WpfControls.Grid BuildTitleBar()
    {
        var bar = new WpfControls.Grid { Background = WpfMedia.Brushes.Transparent };
        bar.ColumnDefinitions.Add(new WpfControls.ColumnDefinition());
        bar.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = Wpf.GridLength.Auto });
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == WpfInput.MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        var left = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal, VerticalAlignment = Wpf.VerticalAlignment.Center };
        var iconBox = new WpfControls.Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new Wpf.CornerRadius(9),
            Background = WpfTheme.Accent,
            Child = new WpfControls.Viewbox
            {
                Width = 18,
                Height = 18,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center,
                Child = UpArrow(100, WpfMedia.Brushes.White)
            }
        };
        left.Children.Add(iconBox);
        left.Children.Add(new WpfControls.TextBlock
        {
            Text = Title,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(11),
            Foreground = WpfTheme.TextSecondary,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(12, 0, 0, 0)
        });
        WpfControls.Grid.SetColumn(left, 0);
        bar.Children.Add(left);

        var close = new WpfControls.Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new Wpf.CornerRadius(8),
            Background = WpfMedia.Brushes.Transparent,
            Cursor = WpfInput.Cursors.Hand,
            Child = new WpfControls.TextBlock
            {
                Text = "✕",
                FontSize = WpfTheme.Pt(13),
                Foreground = WpfTheme.TextSecondary,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            }
        };
        close.MouseEnter += (_, _) => close.Background = WpfTheme.Brush("#F1F5F9");
        close.MouseLeave += (_, _) => close.Background = WpfMedia.Brushes.Transparent;
        close.MouseLeftButtonUp += (_, _) => Cancel();
        WpfControls.Grid.SetColumn(close, 1);
        bar.Children.Add(close);

        return bar;
    }

    // ── Logo: vòng tròn + mũi tên lên + badge "!" (giống ảnh) ────────────────
    private WpfControls.Grid BuildLogo()
    {
        var logo = new WpfControls.Grid { Width = 140, Height = 140, HorizontalAlignment = Wpf.HorizontalAlignment.Center };

        logo.Children.Add(new WpfShapes.Ellipse
        {
            Width = 132,
            Height = 132,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Fill = WpfTheme.Brush("#EFF5FF"),
            Stroke = WpfTheme.Brush("#DCEAFF"),
            StrokeThickness = 1.5
        });

        var arrowGradient = new WpfMedia.LinearGradientBrush(
            WpfTheme.Color("#EAF3FF"), WpfTheme.Color("#C7DCFF"), 90);
        logo.Children.Add(new WpfControls.Viewbox
        {
            Width = 62,
            Height = 70,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, -2, 0, 0),
            Child = UpArrow(100, arrowGradient, WpfTheme.Brush("#AFCBF5"))
        });

        var badge = new WpfControls.Grid
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
            VerticalAlignment = Wpf.VerticalAlignment.Bottom,
            Margin = new Wpf.Thickness(0, 0, 4, 8)
        };
        badge.Children.Add(new WpfShapes.Ellipse { Width = 46, Height = 46, Fill = WpfMedia.Brushes.White });
        badge.Children.Add(new WpfShapes.Ellipse
        {
            Width = 38,
            Height = 38,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Fill = WpfTheme.Brush("#2F6BFF")
        });
        badge.Children.Add(new WpfControls.TextBlock
        {
            Text = "!",
            Foreground = WpfMedia.Brushes.White,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(16),
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, -1, 0, 0)
        });
        logo.Children.Add(badge);

        return logo;
    }

    private WpfControls.TextBlock BigTitle() => new()
    {
        Text = "Có phiên bản mới",
        FontWeight = Wpf.FontWeights.Bold,
        FontSize = WpfTheme.Pt(24),
        Foreground = WpfTheme.TextPrimary,
        HorizontalAlignment = Wpf.HorizontalAlignment.Center
    };

    private WpfControls.StackPanel BuildPills()
    {
        var row = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        };
        row.Children.Add(Pill("Phiên bản hiện tại: ", AppVersion.CurrentText, accent: false));
        row.Children.Add(new WpfControls.TextBlock
        {
            Text = "→",
            FontSize = WpfTheme.Pt(13),
            Foreground = WpfTheme.TextMuted,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(12, 0, 12, 0)
        });
        row.Children.Add(Pill("Phiên bản mới: ", _release.Version, accent: true));
        return row;
    }

    private static WpfControls.Border Pill(string label, string value, bool accent)
    {
        var text = new WpfControls.TextBlock
        {
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Foreground = accent ? WpfTheme.Accent : WpfTheme.TextSecondary,
            FontSize = WpfTheme.Pt(10)
        };
        text.Inlines.Add(new WpfDocs.Run(label));
        text.Inlines.Add(new WpfDocs.Run(value) { FontWeight = Wpf.FontWeights.Bold });

        return new WpfControls.Border
        {
            Background = accent ? WpfTheme.AccentLight : WpfTheme.Brush("#F1F5F9"),
            CornerRadius = new Wpf.CornerRadius(10),
            Padding = new Wpf.Thickness(16, 8, 16, 8),
            Child = text
        };
    }

    private WpfControls.StackPanel BuildNotesLabel()
    {
        var row = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
        row.Children.Add(new WpfControls.TextBlock
        {
            Text = ((char)0xE8FD).ToString(), // Segoe MDL2 List glyph
            FontFamily = new WpfMedia.FontFamily("Segoe MDL2 Assets"),
            FontSize = WpfTheme.Pt(11),
            Foreground = WpfTheme.Accent,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, 0, 8, 0)
        });
        row.Children.Add(new WpfControls.TextBlock
        {
            Text = "Nội dung cập nhật",
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(11),
            Foreground = WpfTheme.TextPrimary,
            VerticalAlignment = Wpf.VerticalAlignment.Center
        });
        return row;
    }

    private WpfControls.Border BuildNotesBox()
    {
        var hasNotes = !string.IsNullOrWhiteSpace(_release.ReleaseNotes);
        var text = new WpfControls.TextBlock
        {
            Text = hasNotes ? _release.ReleaseNotes : "(Không có ghi chú)",
            TextWrapping = Wpf.TextWrapping.Wrap,
            Foreground = hasNotes ? WpfTheme.TextPrimary : WpfTheme.TextMuted,
            FontSize = WpfTheme.Pt(10)
        };

        return new WpfControls.Border
        {
            Background = WpfMedia.Brushes.White,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(10),
            Padding = new Wpf.Thickness(14, 12, 14, 12),
            MinHeight = 130,
            Child = new WpfControls.ScrollViewer
            {
                VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = WpfControls.ScrollBarVisibility.Disabled,
                Content = text
            }
        };
    }

    // ── Khu vực hành động: nút hoặc thanh tiến trình ─────────────────────────
    private WpfControls.Grid BuildActions()
    {
        _actionHost = new WpfControls.Grid();

        var buttons = new WpfControls.StackPanel
        {
            Orientation = WpfControls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center
        };

        _btnLater = WpfUi.OutlineButton(_blocking ? "✕   Thoát" : "✕   Để sau", WpfTheme.TextPrimary, WpfTheme.Border, cornerRadius: 12, fontPt: 11.5, bold: true);
        _btnLater.Height = 54;
        _btnLater.Width = 200;
        _btnLater.Margin = new Wpf.Thickness(0, 0, 16, 0);
        _btnLater.Click += (_, _) => Cancel();

        _btnUpdate = WpfUi.FilledButton("⬇   Cập nhật ngay", WpfTheme.Accent, WpfMedia.Brushes.White, cornerRadius: 12, fontPt: 11.5);
        _btnUpdate.Height = 54;
        _btnUpdate.Width = 240;
        _btnUpdate.Click += (_, _) => DoUpdate();

        if (!_release.HasSetupSource)
        {
            _btnUpdate.IsEnabled = false;
            _btnUpdate.Content = "Chưa có file setup";
        }

        buttons.Children.Add(_btnLater);
        buttons.Children.Add(_btnUpdate);
        _actionHost.Children.Add(buttons);

        _progressPanel = new WpfControls.StackPanel { Visibility = Wpf.Visibility.Collapsed, VerticalAlignment = Wpf.VerticalAlignment.Center };
        _statusText = new WpfControls.TextBlock
        {
            Text = "",
            FontSize = WpfTheme.Pt(10),
            Foreground = WpfTheme.TextSecondary,
            Margin = new Wpf.Thickness(2, 0, 0, 8)
        };
        _progressBar = new WpfControls.ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Foreground = WpfTheme.Accent,
            Background = WpfTheme.Brush("#EEF2F7"),
            BorderThickness = new Wpf.Thickness(0)
        };
        _progressPanel.Children.Add(_statusText);
        _progressPanel.Children.Add(_progressBar);
        _actionHost.Children.Add(_progressPanel);

        return _actionHost;
    }

    // ── Logic ────────────────────────────────────────────────────────────────
    private void Cancel()
    {
        DialogResult = false;
        Close();
    }

    private async void DoUpdate()
    {
        _btnUpdate.IsEnabled = false;
        _btnLater.IsEnabled = false;
        _progressPanel.Visibility = Wpf.Visibility.Visible;
        _progressBar.Value = 0;
        _statusText.Text = "Đang tải bản cập nhật... 0%";

        var progress = new Progress<double>(p =>
        {
            var pct = (int)Math.Round(Math.Clamp(p, 0, 1) * 100);
            _progressBar.Value = Math.Clamp(pct, 0, 100);
            _statusText.Text = $"Đang tải bản cập nhật... {pct}%";
        });

        try
        {
            var path = await UpdateInstaller.DownloadAsync(_store, _release, progress);
            _progressBar.Value = 100;
            _statusText.Text = "Đang mở trình cài đặt...";
            UpdateInstaller.RunInstaller(path);
            DialogResult = true; // caller thoát app để cài
            Close();
        }
        catch (Exception ex)
        {
            _progressPanel.Visibility = Wpf.Visibility.Collapsed;
            _btnUpdate.IsEnabled = _release.HasSetupSource;
            _btnLater.IsEnabled = true;
            Wpf.MessageBox.Show(
                $"Không tải/chạy được file cập nhật.\n\n{ex.Message}",
                "Lỗi cập nhật",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static void AddAt(WpfControls.Grid grid, Wpf.UIElement element, int row, Wpf.Thickness? margin = null)
    {
        if (margin is { } m && element is Wpf.FrameworkElement fe)
        {
            fe.Margin = m;
        }

        WpfControls.Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    /// <summary>Mũi tên lên dạng vector trong khung [0..box] (dùng cho logo + icon).</summary>
    private static WpfShapes.Path UpArrow(double box, WpfMedia.Brush fill, WpfMedia.Brush? stroke = null)
    {
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var data =
            $"M {N(0.50 * box)},{N(0.16 * box)} " +
            $"L {N(0.84 * box)},{N(0.50 * box)} " +
            $"L {N(0.66 * box)},{N(0.50 * box)} " +
            $"L {N(0.66 * box)},{N(0.84 * box)} " +
            $"L {N(0.34 * box)},{N(0.84 * box)} " +
            $"L {N(0.34 * box)},{N(0.50 * box)} " +
            $"L {N(0.16 * box)},{N(0.50 * box)} Z";

        return new WpfShapes.Path
        {
            Data = WpfMedia.Geometry.Parse(data),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke is null ? 0 : 1.4,
            StrokeLineJoin = WpfMedia.PenLineJoin.Round
        };
    }
}
