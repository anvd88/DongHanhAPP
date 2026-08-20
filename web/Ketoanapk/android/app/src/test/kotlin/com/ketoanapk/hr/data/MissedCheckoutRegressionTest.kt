package com.ketoanapk.hr.data

import java.time.LocalDate
import java.time.ZoneId
import java.time.ZonedDateTime
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class MissedCheckoutRegressionTest {
    private val vietnam = ZoneId.of("Asia/Ho_Chi_Minh")

    @Test
    fun monthBackfillIncludesEveryMonthTouchedByLookbackAcrossYearBoundary() {
        assertEquals(
            listOf("2025-12"),
            missedCheckoutMonthKeys(LocalDate.of(2026, 1, 1), lookbackDays = 31),
        )
        assertEquals(
            listOf("2026-02", "2026-01"),
            missedCheckoutMonthKeys(LocalDate.of(2026, 3, 1), lookbackDays = 31),
        )
        assertTrue(missedCheckoutMonthKeys(LocalDate.of(2026, 8, 18), lookbackDays = 0).isEmpty())
    }

    @Test
    fun backfillFindsMissingCheckoutsOnBothSidesOfMonthBoundary() {
        val now = ZonedDateTime.of(2026, 8, 2, 9, 0, 0, 0, vietnam)
        val sheets = listOf(
            sheet(
                "2026-07",
                TimesheetDay(date = "2026-07-31", checkIn = "08:00", shiftStart = "08:00", shiftEnd = "17:00"),
            ),
            sheet(
                "2026-08",
                TimesheetDay(date = "2026-08-01", checkIn = "08:01", shiftStart = "08:00", shiftEnd = "17:00"),
            ),
        )

        val reminders = missedCheckoutNotifications(sheets, now, lookbackDays = 31, createdAt = 123L)

        assertEquals(
            listOf(
                "attendance:missing-checkout:2026-08-01",
                "attendance:missing-checkout:2026-07-31",
            ),
            reminders.map { it.id },
        )
        reminders.forEach {
            assertEquals(NotificationKind.Attendance, it.kind)
            assertEquals("Requests", it.target)
            assertEquals(123L, it.createdAt)
            assertFalse(it.read)
            assertFalse(it.systemDelivered)
        }
    }

    @Test
    fun scanUsesStructuredCheckInAndCheckOutInsteadOfLocalizedStatus() {
        val now = ZonedDateTime.of(2026, 8, 10, 9, 0, 0, 0, vietnam)
        val sheets = listOf(
            sheet(
                "2026-08",
                // Status text is deliberately misleading: structured timestamps are authoritative.
                TimesheetDay(date = "2026-08-09", checkIn = "08:00", checkOut = null, status = "Đủ công"),
                TimesheetDay(date = "2026-08-08", checkIn = "08:00", checkOut = "17:00", status = "Thiếu giờ ra"),
                TimesheetDay(date = "2026-08-07", checkIn = null, checkOut = "17:00", status = "Thiếu giờ ra"),
            ),
        )

        val reminders = missedCheckoutNotifications(sheets, now, createdAt = 456L)

        assertEquals(listOf("attendance:missing-checkout:2026-08-09"), reminders.map { it.id })
    }

    @Test
    fun openOrCompletedForgotCheckoutRequestSuppressesDuplicateReminder() {
        val now = ZonedDateTime.of(2026, 8, 2, 9, 0, 0, 0, vietnam)
        for (status in listOf("Pending", "Approved", "Resolved", "Completed")) {
            val day = TimesheetDay(
                date = "2026-08-01",
                checkIn = "08:00",
                missingCheckoutRequestStatus = status,
                missingCheckoutRequestId = "request-$status",
            )
            assertTrue(
                "Status $status must suppress a duplicate reminder",
                missedCheckoutNotifications(listOf(sheet("2026-08", day)), now).isEmpty(),
            )
        }

        val aliasOnly = TimesheetDay(
            date = "2026-08-01",
            checkIn = "08:00",
            hasOpenCheckoutRequest = true,
        )
        assertTrue(missedCheckoutNotifications(listOf(sheet("2026-08", aliasOnly)), now).isEmpty())
    }

    @Test
    fun rejectedRequestCreatesNewGenerationButKeepsCanonicalDeepLinkEntity() {
        val now = ZonedDateTime.of(2026, 8, 2, 9, 0, 0, 0, vietnam)
        val rejected = TimesheetDay(
            date = "2026-08-01",
            checkIn = "08:00",
            missingCheckoutRequestStatus = "Rejected",
            missingCheckoutRequestId = "req-7",
        )

        val reminder = missedCheckoutNotifications(
            listOf(sheet("2026-08", rejected)),
            now,
            createdAt = 789L,
        ).single()

        assertEquals("attendance:missing-checkout:2026-08-01:retry:req-7", reminder.id)
        assertEquals("forgot-checkout:2026-08-01", reminder.entityId)
        assertEquals(reminder.entityId, entityIdFromNotificationId(reminder.id))
    }

    @Test
    fun lookbackBoundaryIsInclusiveAndOlderDaysAreIgnored() {
        val now = ZonedDateTime.of(2026, 8, 18, 9, 0, 0, 0, vietnam)
        val within = now.toLocalDate().minusDays(31)
        val outside = now.toLocalDate().minusDays(32)
        val reminders = missedCheckoutNotifications(
            sheets = listOf(
                sheet(
                    "2026-07",
                    TimesheetDay(date = within.toString(), checkIn = "08:00"),
                    TimesheetDay(date = outside.toString(), checkIn = "08:00"),
                ),
            ),
            nowVietnam = now,
            lookbackDays = 31,
        )

        assertEquals(listOf("attendance:missing-checkout:$within"), reminders.map { it.id })
    }

    @Test
    fun overnightShiftWaitsUntilEndPlusGraceOnFollowingDay() {
        val workDate = LocalDate.of(2026, 8, 1)
        val sheet = sheet(
            "2026-08",
            TimesheetDay(
                date = workDate.toString(),
                shiftStart = "22:00",
                shiftEnd = "06:00",
                isOvernight = true,
                checkoutGraceMinutes = 120,
                checkIn = "22:00",
            ),
        )

        val tooEarly = ZonedDateTime.of(2026, 8, 2, 7, 59, 0, 0, vietnam)
        val eligible = ZonedDateTime.of(2026, 8, 2, 8, 0, 0, 0, vietnam)

        assertTrue(missedCheckoutNotifications(listOf(sheet), tooEarly).isEmpty())
        assertEquals(
            listOf("attendance:missing-checkout:2026-08-01"),
            missedCheckoutNotifications(listOf(sheet), eligible).map { it.id },
        )
    }

    @Test
    fun absentShiftMetadataUsesSafeNextMorningFallback() {
        val sheet = sheet(
            "2026-08",
            TimesheetDay(date = "2026-08-01T00:00:00", checkIn = "22:00", shiftStart = "?", shiftEnd = "?"),
        )
        val tooEarly = ZonedDateTime.of(2026, 8, 2, 5, 59, 0, 0, vietnam)
        val eligible = ZonedDateTime.of(2026, 8, 2, 6, 0, 0, 0, vietnam)

        assertTrue(missedCheckoutNotifications(listOf(sheet), tooEarly).isEmpty())
        assertEquals(1, missedCheckoutNotifications(listOf(sheet), eligible).size)
    }

    @Test
    fun accountScopeIsNormalizedOpaqueAndDifferentBetweenAccounts() {
        val alice = notificationAccountScope(" Alice.Example ")
        val sameAlice = notificationAccountScope("alice.example")
        val bob = notificationAccountScope("bob.example")

        assertEquals(alice, sameAlice)
        assertFalse(alice == bob)
        assertTrue(alice.matches(Regex("[0-9a-f]{32}")))
        assertFalse(alice.contains("alice", ignoreCase = true))
    }

    @Test
    fun attendancePushFailsClosedWhenRecipientScopeIsMissingOrBelongsToAnotherAccount() {
        val current = notificationAccountScope("alice.example")

        assertTrue(notificationRecipientMatches(NotificationKind.Attendance, current, "  ${current.uppercase()}  "))
        assertFalse(
            notificationRecipientMatches(
                NotificationKind.Attendance,
                current,
                notificationAccountScope("bob.example"),
            ),
        )
        assertFalse(notificationRecipientMatches(NotificationKind.Attendance, current, null))
        assertTrue(notificationRecipientMatches(NotificationKind.System, current, null))
        assertFalse(notificationRecipientMatches(NotificationKind.System, "", current))
    }

    @Test
    fun reminderEntityAcceptsOnlyCanonicalDateAndMapsBackFromNotificationId() {
        assertEquals("2026-08-01", missedCheckoutDateFromEntityId("forgot-checkout:2026-08-01"))
        assertEquals(
            "forgot-checkout:2026-08-01",
            entityIdFromNotificationId("attendance:missing-checkout:2026-08-01"),
        )
        assertEquals(
            "forgot-checkout:2026-08-01",
            entityIdFromNotificationId("attendance:missing-checkout:2026-08-01:retry:req-7"),
        )
        assertNull(missedCheckoutDateFromEntityId("forgot-checkout:2026-02-30"))
        assertNull(missedCheckoutDateFromEntityId("forgot-checkout:2026-08-01:out"))
        assertNull(entityIdFromNotificationId("attendance:missing-checkout:not-a-date"))
    }

    @Test
    fun seenRetentionEvictsOldestSignaturesAndKeepsNewestInInsertionOrder() {
        val signatures = (0 until 405).mapTo(linkedSetOf()) { "sig:$it" }

        val retained = retainedSeenSignatures(signatures)

        assertEquals(400, retained.size)
        assertEquals("sig:5", retained.first())
        assertEquals("sig:404", retained.last())
    }

    private fun sheet(period: String, vararg days: TimesheetDay) =
        Timesheet(period = period, days = days.toList())
}
