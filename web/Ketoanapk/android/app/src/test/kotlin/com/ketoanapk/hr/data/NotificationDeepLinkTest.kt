package com.ketoanapk.hr.data

import java.time.LocalDate
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationDeepLinkTest {
    @Test
    fun extractsRequestIdFromRequestAndInboxNotifications() {
        assertEquals("request-123", requestIdFromNotificationId("req:request-123:approved"))
        assertEquals("request-456", requestIdFromNotificationId("inbox:request-456"))
    }

    @Test
    fun ignoresUnrelatedOrMalformedNotifications() {
        assertNull(requestIdFromNotificationId("pen:123"))
        assertNull(requestIdFromNotificationId("inbox:"))
        assertNull(requestIdFromNotificationId(""))
    }

    @Test
    fun missingCheckoutReminderTargetsPrefilledForgotCheckinForm() {
        val sheet = Timesheet(
            period = "2026-08",
            days = listOf(
                TimesheetDay(
                    date = "2026-08-01",
                    checkIn = "08:02",
                    checkOut = null,
                    status = "Thiếu giờ ra",
                ),
            ),
        )

        val reminder = missedCheckoutNotification(
            sheet = sheet,
            today = LocalDate.of(2026, 8, 2),
            createdAt = 123L,
        )

        requireNotNull(reminder)
        assertEquals("attendance:missing-checkout:2026-08-01", reminder.id)
        assertEquals(NotificationKind.Attendance, reminder.kind)
        assertEquals("Requests", reminder.target)
        assertEquals("forgot-checkout:2026-08-01", reminder.entityId)
        assertTrue(reminder.body.contains("1/8/2026"))
        assertEquals("2026-08-01", missedCheckoutDateFromEntityId(reminder.entityId))
        assertEquals(reminder.entityId, entityIdFromNotificationId(reminder.id))
    }

    @Test
    fun missingCheckoutReminderIgnoresCompletedOrNonPreviousDays() {
        val complete = Timesheet(
            period = "2026-08",
            days = listOf(TimesheetDay(date = "2026-08-01", checkIn = "08:00", checkOut = "17:00")),
        )
        val olderMissing = Timesheet(
            period = "2026-08",
            days = listOf(TimesheetDay(date = "2026-07-31", checkIn = "08:00", checkOut = null)),
        )

        assertNull(missedCheckoutNotification(complete, LocalDate.of(2026, 8, 2)))
        assertNull(missedCheckoutNotification(olderMissing, LocalDate.of(2026, 8, 2)))
        assertNull(missedCheckoutDateFromEntityId("forgot-checkout:not-a-date"))
    }

    @Test
    fun installedReleaseNotificationIsObsoleteButNewerReleaseIsNot() {
        val installed = AppNotification(
            id = "release:91",
            kind = NotificationKind.System,
            title = "Có bản cập nhật",
            body = "",
            createdAt = 0,
            target = APP_UPDATE_NOTIFICATION_TARGET,
        )
        assertEquals(91, appUpdateVersionCode(installed))
        assertTrue(isObsoleteAppUpdateNotification(installed, installedVersionCode = 91))
        assertFalse(isObsoleteAppUpdateNotification(installed.copy(id = "release:92"), installedVersionCode = 91))
    }

    @Test
    fun noUpdateResponseExpiresLegacyUpdateOnly() {
        val legacyUpdate = AppNotification(
            id = "legacy-update",
            kind = NotificationKind.System,
            title = "Có bản cập nhật",
            body = "",
            createdAt = 0,
            target = APP_UPDATE_NOTIFICATION_TARGET,
        )
        assertTrue(isObsoleteAppUpdateNotification(legacyUpdate, 91, noUpdateAvailable = true))
        assertFalse(isObsoleteAppUpdateNotification(legacyUpdate.copy(target = "Requests"), 91, noUpdateAvailable = true))
        assertFalse(
            isObsoleteAppUpdateNotification(
                legacyUpdate.copy(id = "release:92"),
                installedVersionCode = 91,
                noUpdateAvailable = true,
            ),
        )
    }
}
