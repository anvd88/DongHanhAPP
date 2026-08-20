package com.ketoanapk.hr.ui

import com.ketoanapk.hr.data.PayLine
import com.ketoanapk.hr.data.PayslipItem
import com.ketoanapk.hr.data.PayslipRequirement
import com.ketoanapk.hr.data.PayslipRequirementItem
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.time.ZoneId
import java.time.ZonedDateTime

class PayslipPresentationTest {
    @Test
    fun legacyPayslipBuildsReadableIncomeLines() {
        val payslip = PayslipItem(
            baseSalary = 18_000_000.0,
            allowance = 1_500_000.0,
            overtimePay = 750_000.0,
            overtimeHours = 5.0,
        )

        assertEquals(
            listOf(
                PayLine("Lương cơ bản", 18_000_000.0),
                PayLine("Phụ cấp", 1_500_000.0),
                PayLine("Tăng ca (5 giờ)", 750_000.0),
            ),
            payslipDisplayEarnings(payslip),
        )
    }

    @Test
    fun detailedLinesRemainAuthoritative() {
        val serverLines = listOf(PayLine("Lương theo hợp đồng", 20_000_000.0))
        val payslip = PayslipItem(baseSalary = 18_000_000.0, earnings = serverLines)

        assertEquals(serverLines, payslipDisplayEarnings(payslip))
    }

    @Test
    fun balanceDifferenceDetectsMismatchedPayslip() {
        assertEquals(
            250_000.0,
            payslipBalanceDifference(
                PayslipItem(totalEarnings = 20_000_000.0, totalDeductions = 2_000_000.0, netPay = 17_750_000.0),
            ),
            0.001,
        )
    }

    @Test
    fun overtimeRateFallsBackForLegacyPayslip() {
        assertEquals(
            150_000.0,
            payslipResolvedOvertimeRate(PayslipItem(overtimeHours = 5.0, overtimePay = 750_000.0)),
            0.001,
        )
        assertEquals(
            175_000.0,
            payslipResolvedOvertimeRate(PayslipItem(overtimeRate = 175_000.0, overtimeHours = 5.0, overtimePay = 750_000.0)),
            0.001,
        )
    }

    @Test
    fun pendingPayslipReminderShowsDynamicDeadline() {
        val line = payslipReminderLine(
            PayslipRequirement(
                pendingCount = 1,
                payslip = PayslipRequirementItem(
                    period = "2026-08",
                    acknowledgementDueAt = "2026-08-14T00:00:00+07:00",
                ),
            ),
        ).orEmpty()

        assertTrue(line.contains("tháng 8/2026"))
        assertTrue(line.contains("00:00 ngày 14/08/2026"))
    }

    @Test
    fun overduePayslipReminderExplainsApplicationLock() {
        val line = payslipReminderLine(
            PayslipRequirement(
                pendingCount = 2,
                overdueCount = 1,
                mustAcknowledge = true,
                payslip = PayslipRequirementItem(period = "2026-07", overdue = true),
            ),
        ).orEmpty()

        assertTrue(line.contains("quá hạn xác nhận"))
        assertTrue(line.contains("tiếp tục sử dụng ứng dụng"))
        assertTrue(line.contains("2 phiếu chưa xác nhận"))
    }

    @Test
    fun knownPendingPayslipLocksAtItsDynamicDeadlineEvenOffline() {
        val pending = PayslipRequirement(
            pendingCount = 1,
            payslip = PayslipRequirementItem(
                period = "2026-08",
                acknowledgementDueAt = "2026-08-14T00:00:00+07:00",
            ),
        )

        assertTrue(!payslipRequirementAt(pending, Instant.parse("2026-08-13T16:59:59Z")).mustAcknowledge)
        val overdue = payslipRequirementAt(pending, Instant.parse("2026-08-13T17:00:00Z"))
        assertTrue(overdue.mustAcknowledge)
        assertEquals(1, overdue.overdueCount)
        assertTrue(overdue.payslip?.overdue == true)
    }

    @Test
    fun confirmationUsesServerIdAndNeverFallsBackToReissuedSlip() {
        val old = PayslipItem(id = "old-id", period = "2026-08")
        val reissued = PayslipItem(id = "new-id", period = "2026-08")

        assertEquals(old, findPayslipForConfirmation(listOf(reissued, old), "old-id", "2026-08"))
        assertEquals(null, findPayslipForConfirmation(listOf(reissued), "old-id", "2026-08"))
    }

    @Test
    fun confirmationFallsBackToPeriodOnlyForLegacyRequirementWithoutId() {
        val acknowledged = PayslipItem(id = "done", period = "2026-08", acknowledgedAt = "2026-08-03T08:00:00Z")
        val pending = PayslipItem(id = "pending", period = "2026-08")

        assertEquals(pending, findPayslipForConfirmation(listOf(acknowledged, pending), null, "2026-08"))
    }

    @Test
    fun pendingPayslipIsTheFirstHomeTickerMessage() {
        val requirement = PayslipRequirement(
            pendingCount = 1,
            payslip = PayslipRequirementItem(
                period = "2026-08",
                acknowledgementDueAt = "2026-08-14T00:00:00+07:00",
            ),
        )

        val messages = prioritizedHomeTickerMessages(
            greeting = "Chào buổi sáng",
            notices = listOf("Nhắc việc khác", payslipReminderLine(requirement).orEmpty()),
            requirement = requirement,
        )

        assertTrue(messages.first().contains("Phiếu lương tháng 8/2026"))
        assertEquals(1, messages.count { it.contains("Phiếu lương tháng 8/2026") })
    }

    @Test
    fun weekendGreetingStartsSaturdayAfternoonAndContinuesSunday() {
        val zone = ZoneId.of("Asia/Ho_Chi_Minh")
        fun at(value: String) = ZonedDateTime.parse(value).withZoneSameInstant(zone)

        assertEquals(null, weekendGreeting(at("2026-08-15T11:59:00+07:00")))
        assertTrue(weekendGreeting(at("2026-08-15T12:00:00+07:00")).orEmpty().contains("cuối tuần", ignoreCase = true))
        assertTrue(weekendGreeting(at("2026-08-16T23:59:00+07:00")).orEmpty().contains("cuối tuần", ignoreCase = true))
        assertEquals(null, weekendGreeting(at("2026-08-17T00:00:00+07:00")))
    }
}
