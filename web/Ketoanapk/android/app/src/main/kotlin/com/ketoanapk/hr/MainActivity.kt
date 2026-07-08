package com.ketoanapk.hr

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import com.ketoanapk.hr.data.AppNotifier
import com.ketoanapk.hr.data.AppUpdater
import com.ketoanapk.hr.ui.HrApp
import com.ketoanapk.hr.ui.HrViewModel

class MainActivity : ComponentActivity() {
    private val viewModel: HrViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)

        AppNotifier.ensureChannel(this)
        // Dọn các APK cập nhật cũ còn sót trong cache (đã cài xong) để app không phình dung lượng.
        AppUpdater.purgeCachedApks(this)
        handleDeepLink(intent)

        setContent {
            HrApp(viewModel)
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleDeepLink(intent)
    }

    override fun onResume() {
        super.onResume()
        // Kiểm tra cập nhật MỖI KHI vào app / quay lại foreground (force = bỏ qua hạn mức 10 phút).
        viewModel.autoCheckForUpdate(force = true)
        viewModel.refreshPushPermissionState()
        // Làm mới ngay + bật vòng poll nhẹ để thấy admin duyệt đơn mà không cần kéo làm mới.
        viewModel.onAppResumed()
    }

    override fun onPause() {
        super.onPause()
        // Dừng vòng poll khi app xuống nền để đỡ tốn pin/mạng (nền đã có WorkManager + push FCM).
        viewModel.onAppPaused()
    }

    /** Mở đúng màn hình khi người dùng bấm vào thông báo hệ thống. */
    private fun handleDeepLink(intent: Intent?) {
        val target = intent?.getStringExtra(AppNotifier.EXTRA_TARGET) ?: return
        viewModel.navigateTo(target)
    }

}
