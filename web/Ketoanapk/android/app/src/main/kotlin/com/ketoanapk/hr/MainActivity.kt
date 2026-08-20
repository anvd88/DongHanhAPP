package com.ketoanapk.hr

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
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
        // Không để Android tự chèn scrim hình chữ nhật sau footer trong suốt ở chế độ 3 nút.
        window.isNavigationBarContrastEnforced = false

        // Tạo channel không làm hiện hộp xin quyền. Người dùng tự bật thông báo trong onboarding/Cài đặt.
        AppNotifier.ensureChannel(this)
        CallManager.init(this)
        AppUpdater.purgeCachedApks(this)
        handleDeepLink(intent)

        setContent { HrApp(viewModel) }
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
        // MainActivity phải exported cho app-link đăng nhập. Vì vậy tuyệt đối không tin extras mở
        // notification nếu chúng không vừa đi qua NotificationEntryActivity không-exported.
        if (!NotificationLaunchTrust.consume(intent.getStringExtra(NotificationLaunchTrust.EXTRA_TRUST_TOKEN))) {
            clearNotificationExtras(intent)
            return
        }
        try {
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
            viewModel.navigateTo(
                target = target,
                entityId = intent.getStringExtra(AppNotifier.EXTRA_ENTITY_ID),
                notificationId = intent.getStringExtra(AppNotifier.EXTRA_NOTIFICATION_ID),
                accountScope = intent.getStringExtra(AppNotifier.EXTRA_ACCOUNT_SCOPE),
            )
        } finally {
            // Tránh xử lý lại cùng notification khi Activity bị recreate do đổi cấu hình.
            clearNotificationExtras(intent)
        }
    }

    private fun clearNotificationExtras(intent: Intent) {
        intent.removeExtra(AppNotifier.EXTRA_TARGET)
        intent.removeExtra(AppNotifier.EXTRA_ENTITY_ID)
        intent.removeExtra(AppNotifier.EXTRA_NOTIFICATION_ID)
        intent.removeExtra(AppNotifier.EXTRA_ACCOUNT_SCOPE)
        intent.removeExtra(CallNotifier.EXTRA_CALL_ID)
        intent.removeExtra(CallNotifier.EXTRA_CALL_FROM)
        intent.removeExtra(CallNotifier.EXTRA_CALL_NAME)
        intent.removeExtra(CallNotifier.EXTRA_CALL_MEDIA)
        intent.removeExtra(CallNotifier.EXTRA_CALL_ACTION)
        intent.removeExtra(NotificationLaunchTrust.EXTRA_TRUST_TOKEN)
    }
}
