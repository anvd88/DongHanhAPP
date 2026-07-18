package com.ketoanapk.hr.ui

import android.app.Application
import androidx.compose.material3.MaterialTheme
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithText
import androidx.test.core.app.ApplicationProvider
import org.junit.Rule
import org.junit.Test

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
                    onAcknowledge = {},
                    onInquiry = { _, _, _ -> },
                    onDownload = {},
                    onVerifyAccountPassword = { _, result -> result(true, null) },
                )
            }
        }
        compose.onNodeWithText("Chưa có phiếu lương").assertIsDisplayed()
    }
}
