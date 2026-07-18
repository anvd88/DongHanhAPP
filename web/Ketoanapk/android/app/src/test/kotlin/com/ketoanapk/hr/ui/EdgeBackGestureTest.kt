package com.ketoanapk.hr.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class EdgeBackGestureTest {
    @Test
    fun sufficientLeftDragCommitsBack() {
        assertTrue(
            shouldCommitRightEdgeBack(
                dragOffsetPx = -240f,
                widthPx = 1_000f,
                velocityXPx = 0f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }

    @Test
    fun fastLeftFlingCommitsEvenWhenDistanceIsShort() {
        assertTrue(
            shouldCommitRightEdgeBack(
                dragOffsetPx = -50f,
                widthPx = 1_000f,
                velocityXPx = -1_200f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }

    @Test
    fun shortSlowDragSpringsBack() {
        assertFalse(
            shouldCommitRightEdgeBack(
                dragOffsetPx = -40f,
                widthPx = 1_000f,
                velocityXPx = -200f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }
}
