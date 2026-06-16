namespace KetoanMini;

public sealed partial class MainForm
{
    private GiaCongWpfPage? _giaCongWpfPage;

    private Control BuildGiaCongPage()
    {
        var container = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background
        };

        var loadingPanel = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background
        };

        var loadingStack = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = AppTheme.Background
        };
        var loadingLabel = new Label
        {
            Text = "Đang tải trang Gia công...",
            AutoSize = false,
            Width = 360,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = AppTheme.F10B,
            ForeColor = AppTheme.TextMuted,
            BackColor = AppTheme.Background
        };
        var loadingProgress = new ProgressBar
        {
            Width = 360,
            Height = 10,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 8, 0, 0)
        };
        var loadingPercent = new Label
        {
            Text = "0%",
            AutoSize = false,
            Width = 360,
            Height = 20,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = AppTheme.F8B,
            ForeColor = AppTheme.Accent,
            BackColor = AppTheme.Background,
            Margin = new Padding(0, 5, 0, 0)
        };
        loadingStack.Controls.Add(loadingLabel);
        loadingStack.Controls.Add(loadingProgress);
        loadingStack.Controls.Add(loadingPercent);
        loadingPanel.Controls.Add(loadingStack);

        void CenterLoadingStack()
        {
            loadingStack.Left = Math.Max(0, (loadingPanel.ClientSize.Width - loadingStack.Width) / 2);
            loadingStack.Top = Math.Max(0, (loadingPanel.ClientSize.Height - loadingStack.Height) / 2);
        }

        loadingPanel.Resize += (_, _) => CenterLoadingStack();
        loadingStack.SizeChanged += (_, _) => CenterLoadingStack();
        CenterLoadingStack();

        var loadingTimer = new System.Windows.Forms.Timer { Interval = 24 };
        loadingTimer.Tick += (_, _) =>
        {
            var next = loadingProgress.Value < 70 ? loadingProgress.Value + 4 : loadingProgress.Value + 1;
            loadingProgress.Value = Math.Min(96, next);
            loadingPercent.Text = $"{loadingProgress.Value}%";
        };
        loadingTimer.Start();
        loadingPanel.Disposed += (_, _) => loadingTimer.Dispose();
        container.Controls.Add(loadingPanel);

        var hosted = false;
        void EnsureWpfHost()
        {
            if (hosted || container.IsDisposed)
                return;

            hosted = true;
            _giaCongWpfPage = new GiaCongWpfPage(_giaCongStore);
            _giaCongWpfPage.CreateRequested += (_, _) =>
            {
                ShowCreateGiaCongForm();
                _giaCongWpfPage?.RefreshData();
            };
            _giaCongWpfPage.InitialLoadCompleted += (_, _) =>
            {
                if (container.IsDisposed || !container.IsHandleCreated)
                    return;

                container.BeginInvoke(new Action(() =>
                {
                    loadingTimer.Stop();
                    loadingProgress.Value = 100;
                    loadingPercent.Text = "100%";
                    loadingLabel.Text = "Đã tải xong";

                    var hideTimer = new System.Windows.Forms.Timer { Interval = 120 };
                    hideTimer.Tick += (_, _) =>
                    {
                        hideTimer.Stop();
                        hideTimer.Dispose();
                        loadingPanel.Visible = false;
                    };
                    hideTimer.Start();
                }));
            };

            var host = new System.Windows.Forms.Integration.ElementHost
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Child = _giaCongWpfPage
            };

            container.Controls.Add(host);
            host.SendToBack();
            loadingPanel.BringToFront();
        }

        container.HandleCreated += (_, _) => container.BeginInvoke(new Action(EnsureWpfHost));
        return container;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);

    private const int WM_SETREDRAW = 0x000B;

    private static void SuspendDraw(Control c)
    {
        if (c.IsHandleCreated)
            SendMessage(c.Handle, WM_SETREDRAW, false, 0);
    }

    private static void ResumeDraw(Control c)
    {
        if (!c.IsHandleCreated)
            return;

        SendMessage(c.Handle, WM_SETREDRAW, true, 0);
        c.Invalidate(true);
    }

    private void ShowCreateGiaCongForm()
    {
        using var dlg = new GiaCongFormDialog(_giaCongStore);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _store.RecordAudit("Tạo phiếu gia công", "GiaCongPhieu", dlg.MaPhieu, "Tạo phiếu gia công mới");
    }
}
