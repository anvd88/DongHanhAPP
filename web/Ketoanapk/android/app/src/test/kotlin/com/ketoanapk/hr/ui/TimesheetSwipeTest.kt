package com.ketoanapk.hr.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class TimesheetSwipeTest {
    @Test
    fun swipeLeftMovesToNextMonth() {
        assertEquals(1, timesheetMonthOffsetForSwipe(dragDistancePx = -120f, thresholdPx = 80f))
    }

    @Test
    fun swipeRightMovesToPreviousMonth() {
        assertEquals(-1, timesheetMonthOffsetForSwipe(dragDistancePx = 120f, thresholdPx = 80f))
    }

    @Test
    fun shortDragDoesNotChangeMonth() {
        assertNull(timesheetMonthOffsetForSwipe(dragDistancePx = 30f, thresholdPx = 80f))
    }
}
