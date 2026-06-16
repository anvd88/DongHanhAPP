namespace KetoanMini;

public sealed partial class MainForm
{
    private NhanSuWpfPage? _nhanSuWpfPage;

    private Control BuildNhanSuProPage()
    {
        var container = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background
        };

        var page = new NhanSuWpfPage(_store, _currentUser);
        _nhanSuWpfPage = page;
        Action reload = page.RefreshUsersQuiet;
        Action refreshPresence = page.RefreshPresenceOnly;

        page.AddUserRequested += (_, _) =>
        {
            if (!_currentUser.IsAdmin)
            {
                MessageBox.Show("Bạn không có quyền thực hiện thao tác này.", "Từ chối",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new AddUserDialog(_store, _currentUser);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                page.RefreshUsers();
                RefreshNhanSuNotificationsAndBaseline();
            }
        };

        page.ResetRequestsRequested += (_, _) =>
        {
            OpenResetRequestsDialog();
            page.RefreshPresenceOnly();
        };

        page.OvertimeRequestsRequested += (_, _) =>
        {
            OpenWorkAccessRequestsDialog();
            page.RefreshPresenceOnly();
        };

        page.PasswordResetRequested += (_, e) =>
        {
            try
            {
                var code = _store.AdminCreatePasswordResetCode(e.User.Id);
                CodeDisplayDialog.Show(this, e.User.Username, code);
                page.RefreshPresenceOnly();
                RefreshNhanSuNotificationsAndBaseline();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        page.NotificationsChanged += (_, _) => RefreshNhanSuNotificationsAndBaseline();

        // Admin locked/deleted an account -> push an instant force-logout to that user
        // over the LAN (the DB heartbeat also catches it as a fallback).
        page.AccountLocked += (_, e) => _sessionControl?.BroadcastAccountLocked(e.Username);

        var host = new System.Windows.Forms.Integration.ElementHost
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Child = page
        };

        container.Controls.Add(host);
        _nhanSuReload = reload;
        _nhanSuPresence = refreshPresence;
        container.Disposed += (_, _) =>
        {
            if (ReferenceEquals(_nhanSuWpfPage, page))
                _nhanSuWpfPage = null;
            if (ReferenceEquals(_nhanSuReload, reload))
                _nhanSuReload = null;
            if (ReferenceEquals(_nhanSuPresence, refreshPresence))
                _nhanSuPresence = null;
        };

        return container;
    }

    private void RefreshNhanSuNotificationsAndBaseline()
    {
        RefreshNotifCount();
        try { _usersToken = _store.GetUsersChangeToken(); } catch { }
    }
}
