using System.Drawing.Drawing2D;

namespace KetoanMini;

// Page builders (Dashboard, Kế toán, Bán hàng, Nhân sự, Báo cáo, Sao lưu) — split out of MainForm.cs.
public sealed partial class MainForm
{
    // ═════════════════════════════════════════════════════════════════════════
    // DASHBOARD PAGE
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildDashboardPage()
    {
        var page = new Panel { BackColor = AppTheme.Background, Padding = new Padding(0) };
        page.SuspendLayout();

        var hdr = BuildPageHeader("Tổng quan", $"Chào mừng trở lại, {_currentUser.DisplayName}");

        // Stat cards row
        var cardsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 130,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 8, 24, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false
        };

        int totalCustomers = _store.Data.Customers.Count(c => c.IsActive);
        int totalDocs = _store.Data.Documents.Count;
        decimal totalPayments = _store.Data.Payments.Sum(p => p.Amount);
        var now = DateOnly.FromDateTime(DateTime.Today);
        decimal monthRevenue = _store.Data.Payments
            .Where(p => p.Date.Year == now.Year && p.Date.Month == now.Month)
            .Sum(p => p.Amount);

        cardsFlow.Controls.Add(MakeStatCard("Khách hàng", totalCustomers.ToString(), "Đang hoạt động", "👥", AppTheme.AccentLight, "+5%", true));
        cardsFlow.Controls.Add(MakeStatCard("Chứng từ", totalDocs.ToString(), "Tổng phiếu", "📄", AppTheme.SuccessLight, "", true));
        cardsFlow.Controls.Add(MakeStatCard("Thu chi", TextUtil.FormatMoney(totalPayments), "Tổng thanh toán", "💰", AppTheme.WarningLight, "", true));
        cardsFlow.Controls.Add(MakeStatCard("Doanh thu tháng", TextUtil.FormatMoney(monthRevenue), $"Tháng {now.Month}/{now.Year}", "📈", AppTheme.PurpleLight, "", true));

        // Recent docs label
        var recentLbl = new Label
        {
            Text = "  Hoạt động gần đây",
            Font = AppTheme.F11B,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        // Recent docs grid
        var gridWrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(24, 0, 24, 24)
        };
        var grid = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VoucherNo", HeaderText = "Số phiếu", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Ngày", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Khách hàng", Width = 200 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Content", HeaderText = "Nội dung", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Tổng tiền", Width = 130, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        var recent = _store.Data.Documents.OrderByDescending(d => d.Date).Take(10).ToList();
        foreach (var doc in recent)
        {
            grid.Rows.Add(doc.VoucherNo, doc.Date.ToString("dd/MM/yyyy"), doc.CustomerName, doc.Content, TextUtil.FormatMoney(doc.Total));
        }

        gridWrap.Controls.Add(grid);

        page.Controls.Add(gridWrap);
        page.Controls.Add(recentLbl);
        page.Controls.Add(cardsFlow);
        page.Controls.Add(hdr);
        page.ResumeLayout(false);
        return page;
    }

    private static StatCard MakeStatCard(string title, string value, string sub, string icon, Color iconBg, string trend, bool trendPos)
    {
        var card = new StatCard
        {
            CardTitle = title,
            ValueText = value,
            SubText = sub,
            IconText = icon,
            IconBg = iconBg,
            TrendText = trend,
            TrendPositive = trendPos,
            Width = 220,
            Height = 110,
            Margin = new Padding(0, 0, 16, 0)
        };
        return card;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // KE TOAN PAGE
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildKeToanPage()
    {
        var page = new Panel { BackColor = AppTheme.Background };
        page.SuspendLayout();

        var hdr = BuildPageHeader("Kế toán", "Quản lý chứng từ kế toán");

        // Toolbar
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 8, 24, 8)
        };
        var btnCreate = MakeToolbarButton("＋ Tạo phiếu", true);
        var btnRefresh = MakeToolbarButton("🔄 Làm mới", false);

        btnRefresh.Click += (s, e) => RefreshDocGrid();
        btnCreate.Click += (s, e) => ShowCreateDocumentForm();

        var tbFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        tbFlow.Controls.Add(btnCreate);
        tbFlow.Controls.Add(btnRefresh);
        toolbar.Controls.Add(tbFlow);

        // Grid wrapper
        var gridWrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(24, 8, 24, 16)
        };

        _docGrid = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(_docGrid);
        _docGrid.ReadOnly = true;
        _docGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Visible = false });
        _docGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VoucherNo", HeaderText = "Số phiếu", Width = 120 });
        _docGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Ngày", Width = 110 });
        _docGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Khách hàng", Width = 220 });
        _docGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Content", HeaderText = "Nội dung", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _docGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Total", HeaderText = "Tổng tiền", Width = 140,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        RefreshDocGrid();

        _docGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && _docGrid.Rows[e.RowIndex].Tag is Document doc)
                ShowEditDocumentForm(doc);
        };

        gridWrap.Controls.Add(_docGrid);

        page.Controls.Add(gridWrap);
        page.Controls.Add(toolbar);
        page.Controls.Add(hdr);
        page.ResumeLayout(false);
        return page;
    }

    private void RefreshDocGrid()
    {
        if (_docGrid == null) return;
        _docGrid.Rows.Clear();
        var docs = _store.Data.Documents.OrderByDescending(d => d.Date).ToList();
        foreach (var doc in docs)
        {
            int idx = _docGrid.Rows.Add(doc.Id.ToString(), doc.VoucherNo, doc.Date.ToString("dd/MM/yyyy"), doc.CustomerName, doc.Content, TextUtil.FormatMoney(doc.Total));
            _docGrid.Rows[idx].Tag = doc;
        }
    }

    private void ShowCreateDocumentForm()
    {
        using var dlg = new DocumentFormDialog(_store, null);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            RefreshDocGrid();
            _store.RecordAudit("Tạo chứng từ", "Document", dlg.SavedVoucherNo, "Tạo chứng từ mới");
        }
    }

    private void ShowEditDocumentForm(Document doc)
    {
        using var dlg = new DocumentFormDialog(_store, doc);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            RefreshDocGrid();
            _store.RecordAudit("Cập nhật chứng từ", "Document", doc.VoucherNo, "Cập nhật chứng từ");
        }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // BAN HANG PAGE
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildBanHangPage()
    {
        var page = new Panel { BackColor = AppTheme.Background };
        page.SuspendLayout();
        var hdr = BuildPageHeader("Bán hàng", "Quản lý đơn bán hàng");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent, Padding = new Padding(24, 8, 24, 8) };
        var btnRefresh = MakeToolbarButton("🔄 Làm mới", false);
        var tbFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        tbFlow.Controls.Add(btnRefresh);
        toolbar.Controls.Add(tbFlow);

        var gridWrap = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(24, 8, 24, 16) };
        var grid = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(grid);
        grid.ReadOnly = true;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "VoucherNo", HeaderText = "Số phiếu", Width = 120 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Ngày", Width = 110 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Customer", HeaderText = "Khách hàng", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Content", HeaderText = "Nội dung", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "Tổng tiền", Width = 140, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        void LoadBanHang()
        {
            grid.Rows.Clear();
            var banHangDocs = _store.Data.Documents
                .Where(d => d.Content.Contains("bán", StringComparison.OrdinalIgnoreCase) || d.VoucherNo.StartsWith("BH", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Date).ToList();
            foreach (var doc in banHangDocs)
                grid.Rows.Add(doc.VoucherNo, doc.Date.ToString("dd/MM/yyyy"), doc.CustomerName, doc.Content, TextUtil.FormatMoney(doc.Total));
        }

        btnRefresh.Click += (s, e) => LoadBanHang();
        LoadBanHang();
        gridWrap.Controls.Add(grid);

        page.Controls.Add(gridWrap);
        page.Controls.Add(toolbar);
        page.Controls.Add(hdr);
        page.ResumeLayout(false);
        return page;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BAO CAO PAGE
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildBaoCaoPage()
    {
        var page = new Panel { BackColor = AppTheme.Background };
        page.SuspendLayout();
        var hdr = BuildPageHeader("Báo cáo", "Tổng hợp số liệu kinh doanh");

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent, Padding = new Padding(24, 8, 24, 8) };
        var tbFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
        var btnExport = MakeToolbarButton("📊 Xuất Excel", true);
        tbFlow.Controls.Add(btnExport);
        toolbar.Controls.Add(tbFlow);

        btnExport.Click += async (s, e) =>
        {
            try
            {
                btnExport.Enabled = false;
                btnExport.Text = "Đang xuất...";
                var path = Path.Combine(AppContext.BaseDirectory, "exports", $"baocao_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var payload = _store.BuildExportPayload();
                await DirectExcelExporter.ExportCustomerWorkbookAsync(payload, path);
                MessageBox.Show($"Đã xuất báo cáo:\n{path}", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất Excel: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnExport.Enabled = true;
                btnExport.Text = "📊 Xuất Excel";
            }
        };

        var cardsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 130,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 8, 24, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var now = DateOnly.FromDateTime(DateTime.Today);
        decimal totalPayments = _store.Data.Payments.Sum(p => p.Amount);
        decimal monthPay = _store.Data.Payments.Where(p => p.Date.Year == now.Year && p.Date.Month == now.Month).Sum(p => p.Amount);
        int totalDocs = _store.Data.Documents.Count;
        int activeCusts = _store.Data.Customers.Count(c => c.IsActive);

        cardsFlow.Controls.Add(MakeStatCard("Tổng thu chi", TextUtil.FormatMoney(totalPayments), "Toàn thời gian", "💰", AppTheme.WarningLight, "", true));
        cardsFlow.Controls.Add(MakeStatCard("Tháng này", TextUtil.FormatMoney(monthPay), $"Tháng {now.Month}/{now.Year}", "📈", AppTheme.SuccessLight, "", true));
        cardsFlow.Controls.Add(MakeStatCard("Chứng từ", totalDocs.ToString(), "Tổng phiếu", "📄", AppTheme.AccentLight, "", true));
        cardsFlow.Controls.Add(MakeStatCard("Khách hàng", activeCusts.ToString(), "Đang hoạt động", "👥", AppTheme.PurpleLight, "", true));

        var sumLbl = new Label
        {
            Text = "  Tổng hợp theo tháng",
            Font = AppTheme.F11B,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        var gridWrap = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(24, 8, 24, 16) };
        var grid = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(grid);
        grid.ReadOnly = true;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Month", HeaderText = "Tháng", Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Docs", HeaderText = "Số chứng từ", Width = 120, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Payments", HeaderText = "Số giao dịch", Width = 120, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Amount", HeaderText = "Tổng tiền", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        var byMonth = _store.Data.Payments
            .GroupBy(p => new { p.Date.Year, p.Date.Month })
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Take(12).ToList();

        foreach (var g2 in byMonth)
        {
            var docCount = _store.Data.Documents.Count(d => d.Date.Year == g2.Key.Year && d.Date.Month == g2.Key.Month);
            grid.Rows.Add($"Tháng {g2.Key.Month}/{g2.Key.Year}", docCount, g2.Count(), TextUtil.FormatMoney(g2.Sum(p => p.Amount)));
        }

        gridWrap.Controls.Add(grid);

        page.Controls.Add(gridWrap);
        page.Controls.Add(sumLbl);
        page.Controls.Add(cardsFlow);
        page.Controls.Add(toolbar);
        page.Controls.Add(hdr);
        page.ResumeLayout(false);
        return page;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SAO LUU PAGE
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildSaoLuuPage()
    {
        var page = new Panel { BackColor = AppTheme.Background };
        page.SuspendLayout();
        var hdr = BuildPageHeader("Sao lưu", "Sao lưu và xuất dữ liệu");

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 16, 24, 24)
        };

        var card = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 200,
            FillColor = AppTheme.Surface,
            CornerRadius = 12,
            ShadowDepth = 2,
            Padding = new Padding(24)
        };

        var cardTitle = new Label
        {
            Text = "Xuất dữ liệu Excel",
            Font = AppTheme.F14B,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 36,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        var cardDesc = new Label
        {
            Text = "Xuất toàn bộ dữ liệu khách hàng, chứng từ và thanh toán ra file Excel. File sẽ được lưu vào thư mục exports trong thư mục ứng dụng.",
            Font = AppTheme.F9,
            ForeColor = AppTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 52,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            BackColor = Color.Transparent
        };

        var btnExport = MakeToolbarButton("📥 Xuất dữ liệu Excel", true);
        btnExport.Width = 200;
        btnExport.Height = 40;
        btnExport.Font = AppTheme.F10B;
        btnExport.Dock = DockStyle.Top;

        var statusLbl = new Label
        {
            Text = "",
            Font = AppTheme.F9,
            ForeColor = AppTheme.Success,
            Dock = DockStyle.Top,
            Height = 28,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        btnExport.Click += async (s, e) =>
        {
            try
            {
                btnExport.Enabled = false;
                btnExport.Text = "Đang xuất...";
                statusLbl.Text = "";
                statusLbl.ForeColor = AppTheme.TextMuted;

                var path = Path.Combine(AppContext.BaseDirectory, "exports", $"backup_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var payload = _store.BuildExportPayload();
                await DirectExcelExporter.ExportCustomerWorkbookAsync(payload, path);
                statusLbl.Text = $"✓ Đã xuất thành công: {path}";
                statusLbl.ForeColor = AppTheme.Success;
                MessageBox.Show($"Đã xuất: {path}", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                statusLbl.Text = $"✗ Lỗi: {ex.Message}";
                statusLbl.ForeColor = AppTheme.Danger;
                MessageBox.Show($"Lỗi xuất dữ liệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnExport.Enabled = true;
                btnExport.Text = "📥 Xuất dữ liệu Excel";
            }
        };

        card.Controls.Add(statusLbl);
        card.Controls.Add(btnExport);
        card.Controls.Add(cardDesc);
        card.Controls.Add(cardTitle);

        // Audit log section
        var auditCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = AppTheme.Surface,
            CornerRadius = 12,
            ShadowDepth = 2,
            Padding = new Padding(16),
            Margin = new Padding(0, 16, 0, 0)
        };

        var auditTitle = new Label
        {
            Text = "Nhật ký hoạt động",
            Font = AppTheme.F11B,
            ForeColor = AppTheme.TextPrimary,
            Dock = DockStyle.Top,
            Height = 32,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };

        var auditGrid = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(auditGrid);
        auditGrid.ReadOnly = true;
        auditGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OccurredAt", HeaderText = "Thời gian", Width = 150 });
        auditGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Username", HeaderText = "Người dùng", Width = 130 });
        auditGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Hành động", Width = 100 });
        auditGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Entity", HeaderText = "Đối tượng", Width = 120 });
        auditGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Chi tiết", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        try
        {
            var logs = _store.GetAuditLogs();
            foreach (var log in logs.OrderByDescending(l => l.OccurredAt).Take(100))
                auditGrid.Rows.Add(log.OccurredAt.ToString("dd/MM/yyyy HH:mm:ss"), log.Username, log.Action, log.Entity, log.Details);
        }
        catch { }

        auditCard.Controls.Add(auditGrid);
        auditCard.Controls.Add(auditTitle);

        content.Controls.Add(auditCard);
        content.Controls.Add(card);

        page.Controls.Add(content);
        page.Controls.Add(hdr);
        page.ResumeLayout(false);
        return page;
    }
}
