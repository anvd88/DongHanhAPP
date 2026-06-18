using System.Collections.ObjectModel;
using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfData = System.Windows.Data;
using WpfEffects = System.Windows.Media.Effects;
using WpfMedia = System.Windows.Media;
using WpfPrimitives = System.Windows.Controls.Primitives;

namespace KetoanMini;

// ============================================================================
// CapNhatWpfPage — trang quản lý cập nhật phiên bản (admin), bản WPF
//   • Bật/tắt chặn đăng nhập bản cũ
//   • Phát hành bản mới (version, bắt buộc, đường dẫn LAN / file nhúng DB, ghi chú)
//   • Lịch sử phát hành + xóa
// ============================================================================
public sealed class CapNhatWpfPage : WpfControls.UserControl
{
    private readonly AccountingStore _store;
    private readonly ObservableCollection<ReleaseRow> _rows = [];

    private WpfControls.CheckBox _chkEnforce = null!;
    private WpfControls.TextBox _txtVersion = null!;
    private WpfControls.CheckBox _chkMandatory = null!;
    private WpfControls.TextBox _txtUnc = null!;
    private WpfControls.TextBox _txtNotes = null!;
    private WpfControls.TextBlock _lblFile = null!;
    private WpfControls.DataGrid _historyGrid = null!;

    private byte[]? _selectedFileBytes;
    private string _selectedFileName = "";

    public CapNhatWpfPage(AccountingStore store)
    {
        _store = store;
        Background = WpfTheme.Background;
        FontFamily = WpfTheme.Font;
        WpfMedia.TextOptions.SetTextFormattingMode(this, WpfMedia.TextFormattingMode.Display);
        WpfMedia.TextOptions.SetTextRenderingMode(this, WpfMedia.TextRenderingMode.ClearType);
        UseLayoutRounding = true;

        Content = BuildLayout();
        ReloadHistory();
        Loaded += (_, _) =>
        {
            try { _chkEnforce.IsChecked = _store.IsUpdateEnforcementEnabled(); } catch { }
        };
    }

    private WpfControls.Grid BuildLayout()
    {
        var root = new WpfControls.Grid { Margin = new Wpf.Thickness(16) };
        root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        root.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var config = BuildConfigCard();
        WpfControls.Grid.SetRow(config, 0);
        root.Children.Add(config);

        var publish = BuildPublishCard();
        publish.Margin = new Wpf.Thickness(0, 16, 0, 0);
        WpfControls.Grid.SetRow(publish, 1);
        root.Children.Add(publish);

        var history = BuildHistoryCard();
        history.Margin = new Wpf.Thickness(0, 16, 0, 0);
        WpfControls.Grid.SetRow(history, 2);
        root.Children.Add(history);

        return root;
    }

    // ── Card cấu hình chặn ──────────────────────────────────────────────────
    private WpfControls.Border BuildConfigCard()
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(Title("Cấu hình cập nhật"));
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = $"Phiên bản ứng dụng hiện tại: {AppVersion.CurrentText}.\n" +
                   "Khi bật, người dùng đang chạy bản cũ hơn bản bắt buộc sẽ buộc phải cập nhật mới đăng nhập được.",
            Foreground = WpfTheme.TextSecondary,
            FontSize = WpfTheme.Pt(9),
            TextWrapping = Wpf.TextWrapping.Wrap,
            Margin = new Wpf.Thickness(0, 6, 0, 12)
        });

        _chkEnforce = new WpfControls.CheckBox
        {
            Content = "Chặn đăng nhập nếu bản đang dùng cũ hơn bản BẮT BUỘC mới nhất",
            FontSize = WpfTheme.Pt(9.5),
            FontWeight = Wpf.FontWeights.Bold,
            Foreground = WpfTheme.TextPrimary
        };
        _chkEnforce.Checked += (_, _) => SaveEnforcement(true);
        _chkEnforce.Unchecked += (_, _) => SaveEnforcement(false);
        stack.Children.Add(_chkEnforce);

        return Card(stack);
    }

    private void SaveEnforcement(bool enabled)
    {
        try
        {
            _store.SetUpdateEnforcementEnabled(enabled);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show($"Không lưu được cấu hình: {ex.Message}", "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    // ── Card phát hành ──────────────────────────────────────────────────────
    private WpfControls.Border BuildPublishCard()
    {
        var grid = new WpfControls.Grid();
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(150) });
        grid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });

        var title = Title("Phát hành bản mới");
        WpfControls.Grid.SetColumnSpan(title, 2);
        WpfControls.Grid.SetRow(title, 0);
        grid.Children.Add(title);

        _txtVersion = NewTextBox();
        _txtVersion.Width = 200;
        _txtVersion.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        AddRow(grid, 1, "Số phiên bản:", Wrap(_txtVersion, width: 200));

        _chkMandatory = new WpfControls.CheckBox
        {
            Content = "Bản bắt buộc cập nhật",
            FontSize = WpfTheme.Pt(9),
            Foreground = WpfTheme.TextPrimary,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, 6, 0, 6)
        };
        AddRow(grid, 2, "", _chkMandatory);

        _txtUnc = NewTextBox();
        AddRow(grid, 3, "Đường dẫn LAN:", WithHint(Wrap(_txtUnc), @"VD: \\SERVER\share\KetoanMiniUpdate-x.y.z-win-x64.zip hoặc KetoanMiniSetup-x.y.z-win-x64.exe"));

        var filePanel = new WpfControls.DockPanel { Margin = new Wpf.Thickness(0, 6, 0, 6) };
        var btnPick = WpfUi.OutlineButton("Chọn file...", WpfTheme.TextPrimary, WpfTheme.Border, fontPt: 9, bold: false);
        btnPick.Height = 30;
        btnPick.Width = 110;
        btnPick.Click += (_, _) => PickFile();
        WpfControls.DockPanel.SetDock(btnPick, WpfControls.Dock.Left);
        _lblFile = new WpfControls.TextBlock
        {
            Text = "(không nhúng file — dùng đường dẫn LAN)",
            Foreground = WpfTheme.TextMuted,
            FontSize = WpfTheme.Pt(9),
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(10, 0, 0, 0),
            TextTrimming = Wpf.TextTrimming.CharacterEllipsis
        };
        filePanel.Children.Add(btnPick);
        filePanel.Children.Add(_lblFile);
        AddRow(grid, 4, "File nhúng DB:", filePanel);

        _txtNotes = NewTextBox();
        _txtNotes.AcceptsReturn = true;
        _txtNotes.TextWrapping = Wpf.TextWrapping.Wrap;
        _txtNotes.VerticalContentAlignment = Wpf.VerticalAlignment.Top;
        _txtNotes.VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto;
        var notesWrap = Wrap(_txtNotes, height: 80);
        AddRow(grid, 5, "Ghi chú:", notesWrap);

        var btnPublish = WpfUi.FilledButton("⬆  Phát hành", WpfTheme.Accent, WpfMedia.Brushes.White, fontPt: 10);
        btnPublish.Height = 38;
        btnPublish.Width = 150;
        btnPublish.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        btnPublish.Margin = new Wpf.Thickness(0, 10, 0, 0);
        btnPublish.Click += (_, _) => Publish(btnPublish);
        WpfControls.Grid.SetRow(btnPublish, 6);
        WpfControls.Grid.SetColumn(btnPublish, 1);
        grid.Children.Add(btnPublish);

        return Card(grid);
    }

    private void PickFile()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file cập nhật để nhúng vào DB",
            Filter = "File cập nhật (*.zip;*.kup;*.exe;*.msi)|*.zip;*.kup;*.exe;*.msi|Tất cả (*.*)|*.*"
        };
        if (ofd.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var info = new FileInfo(ofd.FileName);
            if (info.Length > 500L * 1024 * 1024)
            {
                Wpf.MessageBox.Show("File quá lớn (>500MB). Nên dùng đường dẫn LAN thay vì nhúng vào DB.", "File quá lớn", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
                return;
            }

            _selectedFileBytes = File.ReadAllBytes(ofd.FileName);
            _selectedFileName = Path.GetFileName(ofd.FileName);
            _lblFile.Text = $"✓ {_selectedFileName} ({TextUtil.FormatFileSize(info.Length)})";
            _lblFile.Foreground = WpfTheme.Success;
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show($"Không đọc được file: {ex.Message}", "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    private void Publish(WpfControls.Button button)
    {
        try
        {
            button.IsEnabled = false;
            _store.PublishRelease(
                _txtVersion.Text,
                _txtNotes.Text,
                _txtUnc.Text,
                _chkMandatory.IsChecked == true,
                _selectedFileBytes,
                _selectedFileName);

            Wpf.MessageBox.Show("Đã phát hành phiên bản mới.", "Thành công", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);

            _txtVersion.Text = "";
            _txtUnc.Text = "";
            _txtNotes.Text = "";
            _chkMandatory.IsChecked = false;
            _selectedFileBytes = null;
            _selectedFileName = "";
            _lblFile.Text = "(không nhúng file — dùng đường dẫn LAN)";
            _lblFile.Foreground = WpfTheme.TextMuted;

            ReloadHistory();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(ex.Message, "Không phát hành được", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ── Card lịch sử ────────────────────────────────────────────────────────
    private WpfControls.Border BuildHistoryCard()
    {
        var grid = new WpfControls.Grid();
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = Wpf.GridLength.Auto });
        grid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var header = new WpfControls.DockPanel { Margin = new Wpf.Thickness(0, 0, 0, 12) };
        var btnDelete = WpfUi.OutlineButton("🗑  Xóa bản chọn", WpfTheme.Danger, WpfTheme.Border, fontPt: 9, bold: false);
        btnDelete.Height = 32;
        btnDelete.Width = 140;
        btnDelete.Click += (_, _) => DeleteSelected();
        WpfControls.DockPanel.SetDock(btnDelete, WpfControls.Dock.Right);
        header.Children.Add(btnDelete);
        header.Children.Add(Title("Lịch sử cập nhật"));
        WpfControls.Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _historyGrid = CreateGrid(_rows);
        _historyGrid.Columns.Add(TextColumn("Phiên bản", nameof(ReleaseRow.Version), 100));
        _historyGrid.Columns.Add(TextColumn("Bắt buộc", nameof(ReleaseRow.Mandatory), 80));
        _historyGrid.Columns.Add(TextColumn("Nguồn tải", nameof(ReleaseRow.Source), 130));
        _historyGrid.Columns.Add(TextColumn("Dung lượng", nameof(ReleaseRow.Size), 100, alignRight: true));
        _historyGrid.Columns.Add(TextColumn("Phát hành lúc", nameof(ReleaseRow.PublishedAt), 150));
        _historyGrid.Columns.Add(TextColumn("Bởi", nameof(ReleaseRow.PublishedBy), 110));
        _historyGrid.Columns.Add(TextColumn("Ghi chú", nameof(ReleaseRow.Notes), 1, star: true));
        WpfControls.Grid.SetRow(_historyGrid, 1);
        grid.Children.Add(_historyGrid);

        return Card(grid);
    }

    private void DeleteSelected()
    {
        if (_historyGrid.SelectedItem is not ReleaseRow row)
        {
            Wpf.MessageBox.Show("Vui lòng chọn một bản phát hành để xóa.", "Chưa chọn", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
            return;
        }

        if (Wpf.MessageBox.Show($"Xóa bản phát hành {row.Version} khỏi lịch sử?", "Xác nhận", Wpf.MessageBoxButton.YesNo, Wpf.MessageBoxImage.Warning) != Wpf.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _store.DeleteRelease(row.Id);
            ReloadHistory();
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show(ex.Message, "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    private void ReloadHistory()
    {
        _rows.Clear();
        try
        {
            foreach (var release in _store.GetReleaseHistory())
            {
                var source = (release.HasEmbeddedFile, !string.IsNullOrWhiteSpace(release.SetupPath)) switch
                {
                    (true, true) => "UNC + DB",
                    (true, false) => "File DB",
                    (false, true) => "Đường dẫn LAN",
                    _ => "(chưa có)"
                };

                _rows.Add(new ReleaseRow
                {
                    Id = release.Id,
                    Version = release.Version,
                    Mandatory = release.IsMandatory ? "Có" : "—",
                    Source = source,
                    Size = release.FileSize > 0 ? TextUtil.FormatFileSize(release.FileSize) : "—",
                    PublishedAt = release.PublishedAt.ToString("dd/MM/yyyy HH:mm"),
                    PublishedBy = release.PublishedBy,
                    Notes = (release.ReleaseNotes ?? "").Replace("\r", " ").Replace("\n", " ")
                });
            }
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show($"Không tải được lịch sử cập nhật: {ex.Message}", "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
    }

    // ── Helpers dựng giao diện ──────────────────────────────────────────────
    private static WpfControls.Border Card(Wpf.UIElement child) => new()
    {
        Background = WpfTheme.Surface,
        BorderBrush = WpfTheme.Border,
        BorderThickness = new Wpf.Thickness(1),
        CornerRadius = new Wpf.CornerRadius(8),
        Padding = new Wpf.Thickness(20, 16, 20, 16),
        Child = child,
        Effect = new WpfEffects.DropShadowEffect
        {
            BlurRadius = 16,
            Color = ThemeState.IsDark ? WpfMedia.Colors.Black : WpfMedia.Color.FromRgb(15, 23, 42),
            Direction = 270,
            Opacity = ThemeState.IsDark ? 0.5 : 0.08,
            ShadowDepth = 2
        }
    };

    private static WpfControls.TextBlock Title(string text) => new()
    {
        Text = text,
        FontSize = WpfTheme.Pt(13),
        FontWeight = Wpf.FontWeights.Bold,
        Foreground = WpfTheme.TextPrimary,
        Margin = new Wpf.Thickness(0, 0, 0, 4)
    };

    private static void AddRow(WpfControls.Grid grid, int row, string label, Wpf.UIElement input)
    {
        var lbl = new WpfControls.TextBlock
        {
            Text = label,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(9),
            Foreground = WpfTheme.TextSecondary,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, 0, 10, 0)
        };
        WpfControls.Grid.SetRow(lbl, row);
        WpfControls.Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        WpfControls.Grid.SetRow(input, row);
        WpfControls.Grid.SetColumn(input, 1);
        grid.Children.Add(input);
    }

    private static WpfControls.TextBox NewTextBox() => new()
    {
        BorderThickness = new Wpf.Thickness(0),
        Background = WpfMedia.Brushes.Transparent,
        Foreground = WpfTheme.TextPrimary,
        FontFamily = WpfTheme.Font,
        FontSize = WpfTheme.Pt(10),
        VerticalContentAlignment = Wpf.VerticalAlignment.Center
    };

    private static WpfControls.Border Wrap(WpfControls.Control inner, double height = 34, double width = double.NaN)
        => new()
        {
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(8),
            Padding = new Wpf.Thickness(10, 0, 10, 0),
            Height = height,
            Width = width,
            HorizontalAlignment = double.IsNaN(width) ? Wpf.HorizontalAlignment.Stretch : Wpf.HorizontalAlignment.Left,
            Margin = new Wpf.Thickness(0, 6, 0, 6),
            Child = inner
        };

    private static WpfControls.StackPanel WithHint(Wpf.UIElement input, string hint)
    {
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(input);
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = hint,
            Foreground = WpfTheme.TextMuted,
            FontSize = WpfTheme.Pt(8),
            Margin = new Wpf.Thickness(2, 0, 0, 6)
        });
        return stack;
    }

    // ── DataGrid styling (gọn, đồng bộ với phong cách app) ───────────────────
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
            HorizontalGridLinesBrush = WpfTheme.GridLine,
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            RowHeight = 38,
            ColumnHeaderHeight = 38,
            FontSize = WpfTheme.Pt(9),
            RowStyle = RowStyle(),
            ColumnHeaderStyle = HeaderStyle()
        };
        return grid;
    }

    private static WpfControls.DataGridTextColumn TextColumn(string header, string binding, double width, bool star = false, bool alignRight = false)
    {
        var cell = new Wpf.Style(typeof(WpfControls.TextBlock));
        cell.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.PaddingProperty, new Wpf.Thickness(10, 0, 10, 0)));
        cell.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center));
        cell.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.TextTrimmingProperty, Wpf.TextTrimming.CharacterEllipsis));
        cell.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.ForegroundProperty, WpfTheme.TextPrimary));
        if (alignRight)
            cell.Setters.Add(new Wpf.Setter(WpfControls.TextBlock.TextAlignmentProperty, Wpf.TextAlignment.Right));

        return new WpfControls.DataGridTextColumn
        {
            Header = header,
            Binding = new WpfData.Binding(binding),
            Width = star
                ? new WpfControls.DataGridLength(width, WpfControls.DataGridLengthUnitType.Star)
                : new WpfControls.DataGridLength(width),
            ElementStyle = cell,
            CanUserResize = false,
            CanUserSort = false
        };
    }

    private static Wpf.Style RowStyle()
    {
        var style = new Wpf.Style(typeof(WpfControls.DataGridRow));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, WpfTheme.Surface));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, WpfTheme.TextPrimary));

        var selected = new Wpf.Trigger { Property = WpfControls.DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, WpfTheme.AccentLight));
        selected.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, WpfTheme.Accent));
        style.Triggers.Add(selected);

        var hover = new Wpf.Trigger { Property = WpfControls.DataGridRow.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, WpfTheme.RowHover));
        style.Triggers.Add(hover);
        return style;
    }

    private static Wpf.Style HeaderStyle()
    {
        var style = new Wpf.Style(typeof(WpfPrimitives.DataGridColumnHeader));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BackgroundProperty, WpfTheme.SurfaceAlt));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.ForegroundProperty, WpfTheme.TextSecondary));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.FontWeightProperty, Wpf.FontWeights.Bold));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.PaddingProperty, new Wpf.Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.HorizontalContentAlignmentProperty, Wpf.HorizontalAlignment.Left));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderBrushProperty, WpfTheme.Border));
        style.Setters.Add(new Wpf.Setter(WpfControls.Control.BorderThicknessProperty, new Wpf.Thickness(0, 0, 0, 1)));
        return style;
    }

    private sealed class ReleaseRow
    {
        public long Id { get; init; }
        public string Version { get; init; } = "";
        public string Mandatory { get; init; } = "";
        public string Source { get; init; } = "";
        public string Size { get; init; } = "";
        public string PublishedAt { get; init; } = "";
        public string PublishedBy { get; init; } = "";
        public string Notes { get; init; } = "";
    }
}
