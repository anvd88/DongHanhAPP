package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class FormatTest {
    @Test
    fun moneyUsesVietnameseThousandsSeparator() {
        assertEquals("1.250.000 ₫", formatMoney(1_250_000.0))
        assertEquals("0 ₫", formatMoney(0.0))
    }

    @Test
    fun minutesHandleHoursAndBoundaries() {
        assertEquals("0 phút", formatMinutes(-1))
        assertEquals("45 phút", formatMinutes(45))
        assertEquals("1h", formatMinutes(60))
        assertEquals("1h 15p", formatMinutes(75))
    }

    @Test
    fun isoDateFallsBackWithoutCrashing() {
        assertEquals("12/07/2026", formatIsoDate("2026-07-12T08:30:00Z"))
        assertEquals("--", formatIsoDate(null))
        assertEquals("không-hợp-lệ", formatIsoDate("không-hợp-lệ"))
    }

    @Test
    fun initialsUseLastTwoNameParts() {
        assertEquals("VA", initials("Nguyễn Văn An"))
        assertEquals("A", initials("An"))
        assertEquals("?", initials("  "))
    }
}
