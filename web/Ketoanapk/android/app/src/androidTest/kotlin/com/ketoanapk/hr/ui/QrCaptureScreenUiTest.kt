package com.ketoanapk.hr.ui

import android.app.Activity
import android.content.Intent
import android.view.View
import android.widget.TextView
import androidx.lifecycle.Lifecycle
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.rule.GrantPermissionRule
import com.ketoanapk.hr.R
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Màn quét QR gần như không kiểm được bằng unit test: nó phụ thuộc theme trong manifest, việc inflate
 * layout và cụm CameraX. Đúng những thứ đó lại vừa bị đổi (theme riêng thay cho @style/zxing_CaptureTheme
 * đã bỏ cùng thư viện, và bind qua UseCaseGroup + ViewPort), nên test này chạy màn quét THẬT để bắt
 * kiểu lỗi chỉ lộ lúc chạy — inflate hỏng, thiếu style, hoặc bind camera ném ngoại lệ.
 */
@RunWith(AndroidJUnit4::class)
class QrCaptureScreenUiTest {
    @get:Rule
    val cameraPermission: GrantPermissionRule = GrantPermissionRule.grant(android.Manifest.permission.CAMERA)

    private fun intentFor(mode: QrCaptureMode, prompt: String? = null) =
        QrCaptureContract().createIntent(
            ApplicationProvider.getApplicationContext(),
            QrCaptureRequest(mode, prompt),
        ).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

    @Test
    fun scannerReachesResumedWithItsOwnThemeAndCameraBinding() {
        ActivityScenario.launch<Activity>(intentFor(QrCaptureMode.Resolve)).use { scenario ->
            scenario.moveToState(Lifecycle.State.RESUMED)
            scenario.onActivity { activity ->
                // Layout inflate được = theme QrCaptureTheme tồn tại và hợp lệ.
                assertNotNull(activity.findViewById<View>(R.id.qr_preview))
                assertNotNull(activity.findViewById<View>(R.id.qr_overlay))
                // Bảng kết quả phải ẩn lúc mới mở, chưa quét được gì.
                assertEquals(View.GONE, activity.findViewById<View>(R.id.qr_result_sheet).visibility)
            }
        }
    }

    @Test
    fun rawModeShowsTheCallerPrompt() {
        val prompt = "Quét mã QR chấm công"
        ActivityScenario.launch<Activity>(intentFor(QrCaptureMode.Raw, prompt)).use { scenario ->
            scenario.moveToState(Lifecycle.State.RESUMED)
            scenario.onActivity { activity ->
                assertEquals(prompt, activity.findViewById<TextView>(R.id.qr_status_view).text.toString())
            }
        }
    }
}
