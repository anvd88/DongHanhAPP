package com.ketoanapk.hr.ui

import android.app.Application
import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.core.app.ApplicationProvider
import com.ketoanapk.hr.data.ReleaseInfo
import org.junit.Rule
import org.junit.Test
import org.junit.Assert.assertTrue

/** Smoke tests for the five release-critical Android surfaces. */
class CoreFlowsUiTest {
    @get:Rule val compose = createComposeRule()

    private fun viewModel() = HrViewModel(ApplicationProvider.getApplicationContext<Application>())

    @Test fun loginRendersCredentialForm() {
        compose.setContent { MaterialTheme { LoginScreen(false, null, false, "", { _, _, _ -> }, { _, _, _, _ -> }) } }
        compose.onNodeWithText("Đăng nhập").assertIsDisplayed()
    }

    @Test fun attendanceRendersConnectionStateWithoutCameraPermissionPromptAtStartup() {
        compose.setContent { MaterialTheme { AttendanceScreen(viewModel()) } }
        compose.onNodeWithText("Chấm công").assertIsDisplayed()
    }

    @Test fun requestsRenderEmptyStateAndCreateAction() {
        compose.setContent { MaterialTheme { RequestsScreen(viewModel()) } }
        compose.onNodeWithText("Tạo đơn mới").assertIsDisplayed()
    }

    @Test fun chatRendersRealInboxEmptyState() {
        compose.setContent { MaterialTheme { RealChatScreen(viewModel()) } }
        compose.onNodeWithText("Chat nội bộ").assertIsDisplayed()
    }

    @Test fun salaryRendersProtectedPayslipList() {
        compose.setContent {
            MaterialTheme {
                MyPayslipsScreen(
                    state = PayslipsUiState(),
                    openPeriod = null,
                    username = "ui-test",
                    onOpen = {},
                    onClose = {},
                    onOpenConfirmation = {},
                    onInquiry = { _, _, _ -> },
                    onDownload = {},
                )
            }
        }
        compose.onNodeWithText("Chưa có phiếu lương").assertIsDisplayed()
    }

    @Test fun updateReminderBarStaysActionable() {
        var opened = false
        compose.setContent {
            MaterialTheme {
                UpdateReminderBar(
                    info = ReleaseInfo(hasUpdate = true, version = "1.4.0", versionCode = 90),
                    onOpen = { opened = true },
                )
            }
        }

        compose.onNodeWithText("Có bản cập nhật 1.4.0").assertIsDisplayed()
        compose.onNodeWithText("Xem ngay").performClick()
        compose.runOnIdle { assertTrue(opened) }
    }
}
