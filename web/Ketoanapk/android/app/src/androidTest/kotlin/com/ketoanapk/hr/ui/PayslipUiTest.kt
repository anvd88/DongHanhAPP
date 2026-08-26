package com.ketoanapk.hr.ui

import android.graphics.Bitmap
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.test.core.app.ApplicationProvider
import com.ketoanapk.hr.data.PayLine
import com.ketoanapk.hr.data.PayslipItem
import com.ketoanapk.hr.data.PayslipOvertimeDay
import com.ketoanapk.hr.ui.theme.KetoanTheme
import java.io.File
import java.io.FileOutputStream
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test

class PayslipUiTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun detailedPayslipShowsEnterpriseBreakdown() {
        val payslip = PayslipItem(
            id = "payslip-ui-test",
            period = "2026-07",
            baseSalary = 24_000_000.0,
            allowance = 2_000_000.0,
            overtimePay = 1_200_000.0,
            overtimeHours = 8.0,
            overtimeRate = 150_000.0,
            overtimeDays = listOf(
                PayslipOvertimeDay("2026-07-08", "17:30", "20:30", 180),
                PayslipOvertimeDay("2026-07-19", "08:00", "13:00", 300),
            ),
            workedDays = 24,
            absentDays = 1,
            lateDays = 2,
            totalWorkedHours = 192.0,
            earnings = listOf(
                PayLine("Lương cơ bản", 24_000_000.0),
                PayLine("Phụ cấp trách nhiệm", 2_000_000.0),
                PayLine("Tăng ca (8 giờ)", 1_200_000.0),
                PayLine("Thưởng hiệu suất", 1_500_000.0),
            ),
            deductions = listOf(
                PayLine("Bảo hiểm xã hội", 1_920_000.0),
                PayLine("Bảo hiểm y tế", 360_000.0),
                PayLine("Bảo hiểm thất nghiệp", 240_000.0),
                PayLine("Thuế thu nhập cá nhân", 680_000.0),
            ),
            totalEarnings = 28_700_000.0,
            totalDeductions = 3_200_000.0,
            netPay = 25_500_000.0,
            createdAt = "2026-08-01T08:30:00+07:00",
            note = "Thưởng hiệu suất theo kết quả đánh giá quý II.",
        )

        compose.setContent {
            KetoanTheme(darkTheme = false) {
                MyPayslipsScreen(
                    state = PayslipsUiState(items = listOf(payslip)),
                    openPeriod = payslip.period,
                    username = "ui-test",
                    onOpen = {},
                    onClose = {},
                    onOpenConfirmation = {},
                    onInquiry = { _, _, _ -> },
                    onDownload = {},
                )
            }
        }

        compose.onNodeWithText("25.500.000 ₫").assertIsDisplayed()
        compose.onNodeWithText("Đối soát phiếu lương", ignoreCase = true).assertIsDisplayed()
        compose.onNodeWithText("8 giờ tăng ca").assertIsDisplayed()
        compose.waitForIdle()

        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val output = File(context.getExternalFilesDir(null), "payslip-detail-qa.png")
        FileOutputStream(output).use { stream ->
            compose.onRoot().captureToImage().asAndroidBitmap().compress(Bitmap.CompressFormat.PNG, 100, stream)
        }
        assertTrue(output.exists() && output.length() > 0)
    }

    @Test
    fun requiredConfirmationUsesDedicatedSecureScreenInsteadOfArchiveDetail() {
        val username = "confirmation-ui-${System.nanoTime()}"
        val payslip = PayslipItem(
            id = "required-confirmation-ui",
            period = "2026-08",
            totalEarnings = 20_000_000.0,
            totalDeductions = 2_000_000.0,
            netPay = 18_000_000.0,
            acknowledgementDueAt = "2026-08-17T00:00:00+07:00",
            acknowledgementOverdue = true,
        )

        compose.setContent {
            KetoanTheme(darkTheme = false) {
                PayslipConfirmationScreen(
                    reviewKey = "${payslip.id}:${payslip.updatedAt}",
                    period = payslip.period,
                    dueAt = payslip.acknowledgementDueAt,
                    required = true,
                    remainingOverdueCount = 1,
                    payslip = payslip,
                    loading = false,
                    loadError = null,
                    statusMessage = null,
                    submitting = false,
                    awaitingSync = false,
                    username = username,
                    onRetry = {},
                    onConfirm = {},
                    onInquiry = { _, _, _ -> },
                    onDownload = {},
                    onClose = {},
                )
            }
        }

        assertTrue(compose.onAllNodesWithText("Chi tiết phiếu lương").fetchSemanticsNodes().isEmpty())
        assertTrue(compose.onAllNodesWithText("Tạo mã bảo mật", substring = true).fetchSemanticsNodes().isNotEmpty())
    }
}
