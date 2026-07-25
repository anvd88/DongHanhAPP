package com.ketoanapk.hr

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.ketoanapk.hr.data.AppForeground
import com.ketoanapk.hr.data.AppNotifier
import com.ketoanapk.hr.data.AppUpdater
import com.ketoanapk.hr.data.CallManager
import com.ketoanapk.hr.data.CallNotifier
import com.ketoanapk.hr.ui.HrApp
import com.ketoanapk.hr.ui.HrViewModel

class MainActivity : ComponentActivity() {
    private companion object {
        const val EXTRA_APP_LOGIN_REQUEST = "request_code"
        const val EXTRA_APP_LOGIN_CLIENT_MODE = "client_mode"
    }

    private val viewModel: HrViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        // Tạo channel không làm hiện hộp xin quyền. Người dùng tự bật thông báo trong onboarding/Cài đặt.
        AppNotifier.ensureChannel(this)
        CallManager.init(this)
        AppUpdater.purgeCachedApks(this)
        hideSystemNavBar()
        handleDeepLink(intent)

        setContent { HrApp(viewModel) }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        // Hệ thống tự hiện lại thanh điều hướng sau khi có hộp thoại/bàn phím → giành lại tiêu điểm là ẩn tiếp.
        if (hasFocus) hideSystemNavBar()
    }

    /**
     * Ẩn thanh điều hướng 3 nút của Android (kiểu toàn màn hình như game): người dùng vuốt từ mép dưới
     * để tạm hiện, rồi hệ thống tự ẩn lại. Chỉ ẩn thanh điều hướng, GIỮ thanh trạng thái (giờ/pin/sóng).
     */
    private fun hideSystemNavBar() {
        val controller = WindowInsetsControllerCompat(window, window.decorView)
        controller.hide(WindowInsetsCompat.Type.navigationBars())
        controller.systemBarsBehavior =
            WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleDeepLink(intent)
    }

    override fun onResume() {
        super.onResume()
        AppForeground.isForeground = true
        CallManager.onForegroundChanged()
        // force = false: giữ đúng nhịp tiết chế 10 phút. Trước đây ép force ở mỗi lần mở lại app nên
        // throttle thành vô nghĩa — cứ chuyển app qua lại là gọi mạng và dựng lại bảng nhắc cập nhật.
        viewModel.autoCheckForUpdate()
        viewModel.refreshPushPermissionState()
        viewModel.onAppResumed()
    }

    override fun onPause() {
        super.onPause()
        AppForeground.isForeground = false
        CallManager.onForegroundChanged()
        viewModel.onAppPaused()
    }

    /** Mở đúng nội dung từ notification nghiệp vụ hoặc dựng lại cuộc gọi đến sau cold start. */
    private fun handleDeepLink(intent: Intent?) {
        intent ?: return
        AppDeepLink.mobileAppLoginRequest(
            intent.dataString,
            intent.getStringExtra(EXTRA_APP_LOGIN_REQUEST),
            intent.getStringExtra(EXTRA_APP_LOGIN_CLIENT_MODE),
        )?.let { requestCode ->
            viewModel.receiveMobileAppLoginDeepLink(requestCode)
            return
        }
        AppDeepLink.qrLoginCode(intent.dataString)?.let { code ->
            viewModel.receiveQrLoginDeepLink(code)
            return
        }
        val callId = intent.getStringExtra(CallNotifier.EXTRA_CALL_ID)
        if (!callId.isNullOrBlank()) {
            CallManager.ingestIncomingFromPush(
                callId,
                intent.getStringExtra(CallNotifier.EXTRA_CALL_FROM).orEmpty(),
                intent.getStringExtra(CallNotifier.EXTRA_CALL_NAME).orEmpty(),
                intent.getStringExtra(CallNotifier.EXTRA_CALL_MEDIA).orEmpty(),
            )
            CallNotifier.dismiss(this)
            return
        }
        val target = intent.getStringExtra(AppNotifier.EXTRA_TARGET) ?: return
        viewModel.navigateTo(target, intent.getStringExtra(AppNotifier.EXTRA_ENTITY_ID))
    }
}
