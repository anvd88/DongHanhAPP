package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class CallHistoryTest {
    @Test fun formatsDuration() {
        assertEquals("00:00", formatCallSeconds(0))
        assertEquals("01:05", formatCallSeconds(65))
    }

    @Test fun mapsOutcomes() {
        assertEquals("Gọi nhỡ", callOutcomeLabel("missed"))
        assertEquals("Mất kết nối", callOutcomeLabel("disconnected"))
        assertEquals("Đã kết thúc", callOutcomeLabel("ended"))
    }
}
