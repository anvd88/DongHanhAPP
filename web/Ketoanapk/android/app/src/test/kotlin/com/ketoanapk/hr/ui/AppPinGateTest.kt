package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Test

class AppPinGateTest {
    @Test
    fun waitTimeIsReadable() {
        assertEquals("30 giây", formatPinWait(30))
        assertEquals("1 phút", formatPinWait(60))
        assertEquals("2 phút", formatPinWait(61))
    }
}
