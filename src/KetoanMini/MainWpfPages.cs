using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;
using WpfData = System.Windows.Data;
using WpfInput = System.Windows.Input;
using WpfMedia = System.Windows.Media;

namespace KetoanMini;

internal static class MainWpfPages
{
    public static Wpf.UIElement BuildDashboardPage(AccountingStore store, AppUser user)
    {
        return new DashboardPage(store, user);
    }

    public static Wpf.UIElement BuildKeToanPage(AccountingStore store, Func<bool> create, Action<Document> edit)
    {
        var root = PageRoot();
        root.Children.Add(PageHeader("Kế toán", "Quản lý chứng từ kế toán"));

        var toolbar = Toolbar();
        var btnCreate = WpfUi.FilledButton("＋  Tạo phiếu", WpfTheme.Accent, WpfMedia.Brushes.White);
        btnCreate.Height = 34;
        btnCreate.MinWidth = 120;
        btnCreate.Click += (_, _) => create();
        toolbar.Children.Add(btnCreate);
        root.Children.Add(toolbar);

        var rows = store.Data.Documents
            .OrderByDescending(d => d.Date)
            .Select(d => new DocumentRow(d, d.VoucherNo, d.Date.ToString("dd/MM/yyyy"), d.CustomerName, d.Content, TextUtil.FormatMoney(d.Total)))
            .ToList();
        var grid = DataGrid(rows);
        grid.MouseDoubleClick += (_, _) =>
        {
            if (grid.SelectedItem is DocumentRow row)
                edit(row.Source);
        };
        grid.Columns.Add(TextColumn("VoucherNo", "Số phiếu", 120));
        grid.Columns.Add(TextColumn("Date", "Ngày", 110));
        grid.Columns.Add(TextColumn("Customer", "Khách hàng", 220));
        grid.Columns.Add(TextColumn("Content", "Nội dung", 360));
        grid.Columns.Add(TextColumn("Total", "Tổng tiền", 140));
        root.Children.Add(GridCard(grid));
        return root;
    }

    public static Wpf.UIElement BuildBanHangPage(AccountingStore store)
    {
        var root = PageRoot();
        root.Children.Add(PageHeader("Bán hàng", "Quản lý đơn bán hàng"));
        var rows = store.Data.Documents
            .Where(d => d.Content.Contains("bán", StringComparison.OrdinalIgnoreCase) || d.VoucherNo.StartsWith("BH", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.Date)
            .Select(d => new RecentDocRow(d.VoucherNo, d.Date.ToString("dd/MM/yyyy"), d.CustomerName, d.Content, TextUtil.FormatMoney(d.Total)))
            .ToList();
        var grid = DataGrid(rows);
        grid.Columns.Add(TextColumn("VoucherNo", "Số phiếu", 120));
        grid.Columns.Add(TextColumn("Date", "Ngày", 110));
        grid.Columns.Add(TextColumn("Customer", "Khách hàng", 220));
        grid.Columns.Add(TextColumn("Content", "Nội dung", 360));
        grid.Columns.Add(TextColumn("Total", "Tổng tiền", 140));
        root.Children.Add(GridCard(grid, new Wpf.Thickness(24, 8, 24, 16)));
        return root;
    }

    public static Wpf.UIElement BuildBaoCaoPage(AccountingStore store)
    {
        var root = PageRoot();
        root.Children.Add(PageHeader("Báo cáo", "Tổng hợp số liệu kinh doanh"));

        var toolbar = Toolbar();
        var export = WpfUi.FilledButton("📊  Xuất Excel", WpfTheme.Accent, WpfMedia.Brushes.White);
        export.Height = 34;
        export.MinWidth = 130;
        export.Click += async (_, _) => await ExportAsync(store, "baocao", export);
        toolbar.Children.Add(export);
        root.Children.Add(toolbar);

        var now = DateOnly.FromDateTime(DateTime.Today);
        var cards = new WpfControls.WrapPanel { Margin = new Wpf.Thickness(24, 8, 24, 12) };
        cards.Children.Add(StatCard("Tổng thu chi", TextUtil.FormatMoney(store.Data.Payments.Sum(p => p.Amount)), "Toàn thời gian", "💰", WpfTheme.WarningLight, ""));
        cards.Children.Add(StatCard("Tháng này", TextUtil.FormatMoney(store.Data.Payments.Where(p => p.Date.Year == now.Year && p.Date.Month == now.Month).Sum(p => p.Amount)), $"Tháng {now.Month}/{now.Year}", "📈", WpfTheme.SuccessLight, ""));
        cards.Children.Add(StatCard("Chứng từ", store.Data.Documents.Count.ToString(), "Tổng phiếu", "📄", WpfTheme.AccentLight, ""));
        cards.Children.Add(StatCard("Khách hàng", store.Data.Customers.Count(c => c.IsActive).ToString(), "Đang hoạt động", "👥", WpfTheme.PurpleLight, ""));
        root.Children.Add(cards);

        root.Children.Add(SectionTitle("Tổng hợp theo tháng"));
        var rows = store.Data.Payments
            .GroupBy(p => new { p.Date.Year, p.Date.Month })
            .OrderByDescending(g => g.Key.Year)
            .ThenByDescending(g => g.Key.Month)
            .Take(12)
            .Select(g => new MonthlyRow(
                $"Tháng {g.Key.Month}/{g.Key.Year}",
                store.Data.Documents.Count(d => d.Date.Year == g.Key.Year && d.Date.Month == g.Key.Month),
                g.Count(),
                TextUtil.FormatMoney(g.Sum(p => p.Amount))))
            .ToList();
        var grid = DataGrid(rows);
        grid.Columns.Add(TextColumn("Month", "Tháng", 130));
        grid.Columns.Add(TextColumn("Docs", "Số chứng từ", 120));
        grid.Columns.Add(TextColumn("Payments", "Số giao dịch", 120));
        grid.Columns.Add(TextColumn("Amount", "Tổng tiền", 160));
        root.Children.Add(GridCard(grid));
        return root;
    }

    public static Wpf.UIElement BuildSaoLuuPage(AccountingStore store)
    {
        var root = PageRoot();
        root.Children.Add(PageHeader("Sao lưu", "Sao lưu và xuất dữ liệu"));

        var card = Card();
        var stack = new WpfControls.StackPanel();
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Xuất dữ liệu Excel",
            Foreground = WpfTheme.TextPrimary,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(14)
        });
        stack.Children.Add(new WpfControls.TextBlock
        {
            Text = "Xuất toàn bộ dữ liệu khách hàng, chứng từ và thanh toán ra file Excel.",
            Foreground = WpfTheme.TextSecondary,
            FontSize = WpfTheme.Pt(9),
            Margin = new Wpf.Thickness(0, 8, 0, 14)
        });
        var btn = WpfUi.FilledButton("📥  Xuất dữ liệu Excel", WpfTheme.Accent, WpfMedia.Brushes.White);
        btn.Width = 200;
        btn.Height = 40;
        btn.HorizontalAlignment = Wpf.HorizontalAlignment.Left;
        btn.Click += async (_, _) => await ExportAsync(store, "backup", btn);
        stack.Children.Add(btn);
        card.Child = stack;
        root.Children.Add(card);

        root.Children.Add(SectionTitle("Nhật ký hoạt động"));
        var logs = new List<AuditRow>();
        try
        {
            logs = store.GetAuditLogs()
                .OrderByDescending(l => l.OccurredAt)
                .Take(100)
                .Select(l => new AuditRow(l.OccurredAt.ToString("dd/MM/yyyy HH:mm:ss"), l.Username, l.Action, l.Entity, l.Details))
                .ToList();
        }
        catch { }
        var grid = DataGrid(logs);
        grid.Columns.Add(TextColumn("OccurredAt", "Thời gian", 150));
        grid.Columns.Add(TextColumn("Username", "Người dùng", 130));
        grid.Columns.Add(TextColumn("Action", "Hành động", 120));
        grid.Columns.Add(TextColumn("Entity", "Đối tượng", 120));
        grid.Columns.Add(TextColumn("Details", "Chi tiết", 360));
        root.Children.Add(GridCard(grid));
        return root;
    }

    public static Wpf.UIElement BuildPlaceholderPage(string title, string subtitle)
    {
        var root = PageRoot();
        root.Children.Add(PageHeader(title, subtitle));
        root.Children.Add(new WpfControls.TextBlock
        {
            Text = "⚙  Module đang phát triển",
            Foreground = WpfTheme.TextMuted,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(14),
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Margin = new Wpf.Thickness(0, 120, 0, 0)
        });
        return root;
    }

    private static WpfControls.StackPanel PageRoot() => new()
    {
        Background = WpfTheme.Background
    };

    private static WpfControls.StackPanel PageHeader(string title, string subtitle)
    {
        var header = new WpfControls.StackPanel
        {
            Margin = new Wpf.Thickness(24, 20, 24, 8)
        };
        header.Children.Add(new WpfControls.TextBlock
        {
            Text = title,
            Foreground = WpfTheme.TextPrimary,
            FontWeight = Wpf.FontWeights.Bold,
            FontSize = WpfTheme.Pt(18)
        });
        header.Children.Add(new WpfControls.TextBlock
        {
            Text = subtitle,
            Foreground = WpfTheme.TextMuted,
            FontSize = WpfTheme.Pt(9)
        });
        return header;
    }

    private static WpfControls.TextBlock SectionTitle(string text) => new()
    {
        Text = "  " + text,
        Foreground = WpfTheme.TextPrimary,
        FontWeight = Wpf.FontWeights.Bold,
        FontSize = WpfTheme.Pt(11),
        Margin = new Wpf.Thickness(24, 0, 24, 8)
    };

    private static WpfControls.StackPanel Toolbar() => new()
    {
        Orientation = WpfControls.Orientation.Horizontal,
        Margin = new Wpf.Thickness(24, 8, 24, 8)
    };

    private static WpfControls.Border StatCard(string title, string value, string sub, string icon, WpfMedia.Brush iconBg, string trend)
    {
        var root = new WpfControls.Grid
        {
            Width = 220,
            Height = 110
        };
        root.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(72) });
        root.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new Wpf.GridLength(1, Wpf.GridUnitType.Star) });

        var iconBox = new WpfControls.Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new Wpf.CornerRadius(10),
            Background = iconBg,
            HorizontalAlignment = Wpf.HorizontalAlignment.Center,
            VerticalAlignment = Wpf.VerticalAlignment.Center,
            Child = new WpfControls.TextBlock
            {
                Text = icon,
                FontSize = 22,
                HorizontalAlignment = Wpf.HorizontalAlignment.Center,
                VerticalAlignment = Wpf.VerticalAlignment.Center
            }
        };
        root.Children.Add(iconBox);

        var text = new WpfControls.StackPanel { VerticalAlignment = Wpf.VerticalAlignment.Center };
        text.Children.Add(new WpfControls.TextBlock { Text = title, Foreground = WpfTheme.TextSecondary, FontSize = WpfTheme.Pt(8) });
        text.Children.Add(new WpfControls.TextBlock { Text = value, Foreground = WpfTheme.TextPrimary, FontSize = WpfTheme.Pt(22), FontWeight = Wpf.FontWeights.Bold });
        text.Children.Add(new WpfControls.TextBlock { Text = sub, Foreground = WpfTheme.TextMuted, FontSize = WpfTheme.Pt(8) });
        WpfControls.Grid.SetColumn(text, 1);
        root.Children.Add(text);

        if (!string.IsNullOrWhiteSpace(trend))
        {
            var badge = new WpfControls.Border
            {
                Background = WpfTheme.SuccessLight,
                CornerRadius = new Wpf.CornerRadius(6),
                Padding = new Wpf.Thickness(8, 2, 8, 2),
                HorizontalAlignment = Wpf.HorizontalAlignment.Right,
                VerticalAlignment = Wpf.VerticalAlignment.Bottom,
                Margin = new Wpf.Thickness(0, 0, 12, 10),
                Child = new WpfControls.TextBlock
                {
                    Text = "▲ " + trend,
                    Foreground = WpfTheme.Success,
                    FontSize = WpfTheme.Pt(8),
                    FontWeight = Wpf.FontWeights.Bold
                }
            };
            WpfControls.Grid.SetColumn(badge, 1);
            root.Children.Add(badge);
        }

        return new WpfControls.Border
        {
            Width = 220,
            Height = 110,
            CornerRadius = new Wpf.CornerRadius(8),
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            Margin = new Wpf.Thickness(0, 0, 16, 8),
            Child = root
        };
    }

    private static WpfControls.Border Card() => new()
    {
        Background = WpfTheme.Surface,
        BorderBrush = WpfTheme.Border,
        BorderThickness = new Wpf.Thickness(1),
        CornerRadius = new Wpf.CornerRadius(8),
        Padding = new Wpf.Thickness(20, 16, 20, 16),
        Margin = new Wpf.Thickness(24, 8, 24, 16)
    };

    private static WpfControls.Border GridCard(WpfControls.DataGrid grid, Wpf.Thickness? margin = null)
    {
        return new WpfControls.Border
        {
            Background = WpfTheme.Surface,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Wpf.Thickness(1),
            CornerRadius = new Wpf.CornerRadius(8),
            Padding = new Wpf.Thickness(0),
            Margin = margin ?? new Wpf.Thickness(24, 0, 24, 16),
            MinHeight = 260,
            Child = grid
        };
    }

    private static WpfControls.DataGrid DataGrid(System.Collections.IEnumerable items)
    {
        var grid = new WpfControls.DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            HeadersVisibility = WpfControls.DataGridHeadersVisibility.Column,
            GridLinesVisibility = WpfControls.DataGridGridLinesVisibility.Horizontal,
            RowHeight = 38,
            ColumnHeaderHeight = 36,
            Background = WpfTheme.Surface,
            Foreground = WpfTheme.TextPrimary,
            BorderThickness = new Wpf.Thickness(0),
            HorizontalGridLinesBrush = WpfTheme.GridLine,
            SelectionMode = WpfControls.DataGridSelectionMode.Single,
            SelectionUnit = WpfControls.DataGridSelectionUnit.FullRow,
            FontFamily = WpfTheme.Font,
            FontSize = WpfTheme.Pt(9)
        };
        return grid;
    }

    private static WpfControls.DataGridTextColumn TextColumn(string binding, string header, double width)
    {
        return new WpfControls.DataGridTextColumn
        {
            Binding = new WpfData.Binding(binding),
            Header = header,
            Width = new WpfControls.DataGridLength(width),
            ElementStyle = new Wpf.Style(typeof(WpfControls.TextBlock))
            {
                Setters =
                {
                    new Wpf.Setter(WpfControls.TextBlock.ForegroundProperty, WpfTheme.TextPrimary),
                    new Wpf.Setter(WpfControls.TextBlock.PaddingProperty, new Wpf.Thickness(8, 0, 8, 0)),
                    new Wpf.Setter(WpfControls.TextBlock.VerticalAlignmentProperty, Wpf.VerticalAlignment.Center),
                    new Wpf.Setter(WpfControls.TextBlock.TextTrimmingProperty, Wpf.TextTrimming.CharacterEllipsis)
                }
            }
        };
    }

    private static async Task ExportAsync(AccountingStore store, string prefix, WpfControls.Button button)
    {
        try
        {
            button.IsEnabled = false;
            button.Content = "Đang xuất...";
            var path = Path.Combine(AppContext.BaseDirectory, "exports", $"{prefix}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = store.BuildExportPayload();
            await DirectExcelExporter.ExportCustomerWorkbookAsync(payload, path);
            Wpf.MessageBox.Show($"Đã xuất:\n{path}", "Thành công", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Wpf.MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi", Wpf.MessageBoxButton.OK, Wpf.MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = prefix == "backup" ? "📥  Xuất dữ liệu Excel" : "📊  Xuất Excel";
        }
    }

    private sealed record RecentDocRow(string VoucherNo, string Date, string Customer, string Content, string Total);
    private sealed record DocumentRow(Document Source, string VoucherNo, string Date, string Customer, string Content, string Total);
    private sealed record MonthlyRow(string Month, int Docs, int Payments, string Amount);
    private sealed record AuditRow(string OccurredAt, string Username, string Action, string Entity, string Details);
}
