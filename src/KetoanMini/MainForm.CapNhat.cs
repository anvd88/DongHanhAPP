namespace KetoanMini;

public sealed partial class MainForm
{
    // ═════════════════════════════════════════════════════════════════════════
    // CẬP NHẬT PHIÊN BẢN (admin) — trang WPF nhúng qua ElementHost
    // ═════════════════════════════════════════════════════════════════════════
    private Control BuildCapNhatPage()
    {
        var page = new CapNhatWpfPage(_store);
        return new System.Windows.Forms.Integration.ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Child = page
        };
    }
}
