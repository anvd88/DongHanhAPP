using KetoanMini.Api.Services;
using Xunit;

namespace KetoanMini.Api.Tests;

public sealed class QrLoginServiceTests
{
    [Fact]
    public void Session_ExpiresExactlyAfterFiveMinutes()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero));
        using var service = new QrLoginService(clock);
        var created = service.Create("web-test", "test-agent");

        Assert.NotNull(created);
        Assert.Equal(clock.GetUtcNow().AddMinutes(5).UtcDateTime, created!.ExpiresAt);
        Assert.Equal(QrLoginPollState.Pending, service.BeginConsume(created.PollToken).State);
        var scan = service.ScanDetailed(created.QrCode, "employee", "Nhan Vien");
        Assert.Equal(QrLoginScanResult.Scanned, scan.Result);
        Assert.Equal(created.ExpiresAt, scan.ExpiresAt);

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(QrLoginPollState.Expired, service.BeginConsume(created.PollToken).State);
        Assert.Equal(QrLoginConfirmResult.InvalidOrExpired, service.Confirm(created.QrCode, "employee", "Nhan Vien"));
    }

    [Fact]
    public void ConfirmAndConsume_AreAtomicAndOneTime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var service = new QrLoginService(clock);
        var created = service.Create("web-test", "test-agent")!;

        Assert.Equal(QrLoginConfirmResult.Confirmed, service.Confirm(created.QrCode, "employee", "Nhan Vien"));
        Assert.Equal(QrLoginConfirmResult.AlreadyConfirmed, service.Confirm(created.QrCode, "employee", "Nhan Vien"));
        Assert.Equal(QrLoginConfirmResult.InvalidOrExpired, service.Confirm(created.QrCode, "other-user", "Nguoi Khac"));

        var first = service.BeginConsume(created.PollToken);
        Assert.Equal(QrLoginPollState.Ready, first.State);
        Assert.NotNull(first.Session);
        Assert.False(first.Session!.AlreadyAuthorized);

        // Poll đồng thời không thể giành quyền cấp thêm token.
        Assert.Equal(QrLoginPollState.Pending, service.BeginConsume(created.PollToken).State);

        // Lỗi DB tạm thời trả phiên về trạng thái đã xác nhận để lần poll sau thử lại.
        service.CompleteConsume(first.Session!, success: false);
        var retry = service.BeginConsume(created.PollToken);
        Assert.Equal(QrLoginPollState.Ready, retry.State);

        service.CompleteConsume(retry.Session!, success: true);

        // Nếu response authenticated bị mất, poll token vẫn nhận lại kết quả nhưng không lặp side effect.
        var redelivery = service.BeginConsume(created.PollToken);
        Assert.Equal(QrLoginPollState.Ready, redelivery.State);
        Assert.True(redelivery.Session!.AlreadyAuthorized);
        service.CompleteConsume(redelivery.Session, success: true);
        service.Acknowledge(created.PollToken);

        Assert.Equal(QrLoginPollState.Expired, service.BeginConsume(created.PollToken).State);
        Assert.Equal(QrLoginConfirmResult.InvalidOrExpired, service.Confirm(created.QrCode, "employee", "Nhan Vien"));
    }

    [Fact]
    public void Scan_ShowsAccountBeforeConfirm_AndRejectIsTerminal()
    {
        using var service = new QrLoginService();
        var accepted = service.Create("web-test", "test-agent")!;

        Assert.Equal(QrLoginScanResult.Scanned, service.Scan(accepted.QrCode, "employee", "Nhân viên A"));
        Assert.Equal(QrLoginScanResult.AlreadyScanned, service.Scan(accepted.QrCode, "employee", "Nhân viên A"));
        Assert.Equal(QrLoginScanResult.InvalidOrExpired, service.Scan(accepted.QrCode, "EMPLOYEE", "Tài khoản khác"));
        Assert.Equal(QrLoginScanResult.InvalidOrExpired, service.Scan(accepted.QrCode, "other-user", "Người khác"));

        var scanned = service.BeginConsume(accepted.PollToken);
        Assert.Equal(QrLoginPollState.Scanned, scanned.State);
        Assert.Equal("employee", scanned.Account?.Username);
        Assert.Equal("Nhân viên A", scanned.Account?.FullName);
        Assert.Equal("employee", service.GetScannedAccount(accepted.PollToken)?.Username);
        Assert.Null(service.GetScannedAccount("invalid-token"));

        Assert.Equal(QrLoginConfirmResult.InvalidOrExpired, service.Confirm(accepted.QrCode, "EMPLOYEE", "Tài khoản khác"));
        Assert.Equal(QrLoginConfirmResult.Confirmed, service.Confirm(accepted.QrCode, "employee", "Nhân viên A"));
        var ready = service.BeginConsume(accepted.PollToken);
        Assert.Equal(QrLoginPollState.Ready, ready.State);
        service.CompleteConsume(ready.Session!, success: true);
        Assert.Equal("employee", service.GetScannedAccount(accepted.PollToken)?.Username);
        service.Acknowledge(accepted.PollToken);

        var rejected = service.Create("web-test-2", "test-agent")!;
        Assert.Equal(QrLoginScanResult.Scanned, service.Scan(rejected.QrCode, "employee", "Nhân viên A"));
        Assert.False(service.RejectScan(rejected.QrCode, "other-user"));
        Assert.True(service.RejectScan(rejected.QrCode, "employee"));
        Assert.True(service.RejectScan(rejected.QrCode, "employee"));
        Assert.Equal(QrLoginPollState.Rejected, service.BeginConsume(rejected.PollToken).State);
        Assert.Equal(QrLoginConfirmResult.InvalidOrExpired, service.Confirm(rejected.QrCode, "employee", "Nhân viên A"));
        Assert.Equal(QrLoginScanResult.InvalidOrExpired, service.Scan(rejected.QrCode, "employee", "Nhân viên A"));
    }

    [Fact]
    public void Cancel_InvalidatesBothBrowserAndScanTokens()
    {
        using var service = new QrLoginService();
        var created = service.Create("web-test", "test-agent")!;

        service.Cancel(created.PollToken);

        Assert.Equal(QrLoginPollState.Expired, service.BeginConsume(created.PollToken).State);
        Assert.Equal(QrLoginConfirmResult.InvalidOrExpired, service.Confirm(created.QrCode, "employee", "Nhan Vien"));
    }

    [Fact]
    public void NewSessionForSameBrowser_InvalidatesPreviousQr()
    {
        using var service = new QrLoginService();
        var first = service.Create("same-browser", "test-agent")!;

        Assert.Equal(QrLoginPollState.Pending, service.BeginConsume(first.PollToken).State);

        var second = service.Create("same-browser", "test-agent")!;

        Assert.Equal(QrLoginScanResult.InvalidOrExpired, service.Scan(first.QrCode, "employee", "Nhan Vien"));
        Assert.Equal(QrLoginPollState.Expired, service.BeginConsume(first.PollToken).State);
        Assert.Equal(QrLoginScanResult.Scanned, service.Scan(second.QrCode, "employee", "Nhan Vien"));
        Assert.Equal(QrLoginPollState.Scanned, service.BeginConsume(second.PollToken).State);
    }

    [Fact]
    public void MobileAppChannel_IsSeparateFromDesktopQr()
    {
        using var service = new QrLoginService();
        var created = service.Create("mobile-browser", "android-chrome", WebLoginChannel.MobileApp)!;

        Assert.StartsWith(QrLoginService.MobileAppPrefix, created.QrCode);
        Assert.True(QrLoginService.LooksLikeMobileAppLogin(created.QrCode));
        Assert.False(QrLoginService.LooksLikeLoginQr(created.QrCode));
        Assert.Equal(QrLoginScanResult.InvalidOrExpired,
            service.Scan(created.QrCode, "employee", "Nhân viên"));
        Assert.Equal(QrLoginPollState.Expired, service.BeginConsume(created.PollToken).State);

        Assert.Equal(QrLoginScanResult.Scanned,
            service.Scan(created.QrCode, "employee", "Nhân viên", WebLoginChannel.MobileApp));
        Assert.Equal(QrLoginPollState.Scanned,
            service.BeginConsume(created.PollToken, WebLoginChannel.MobileApp).State);
        Assert.Equal(QrLoginConfirmResult.Confirmed,
            service.Confirm(created.QrCode, "employee", "Nhân viên", WebLoginChannel.MobileApp));
        var ready = service.BeginConsume(created.PollToken, WebLoginChannel.MobileApp);
        Assert.Equal(QrLoginPollState.Ready, ready.State);
        Assert.Equal(WebLoginChannel.MobileApp, ready.Session!.Channel);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
