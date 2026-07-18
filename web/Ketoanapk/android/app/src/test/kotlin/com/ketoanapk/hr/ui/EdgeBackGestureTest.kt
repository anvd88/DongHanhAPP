package com.ketoanapk.hr.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class EdgeBackGestureTest {
    @Test
    fun sufficientLeftDragCommitsBack() {
        assertTrue(
            shouldCommitEdgeBack(
                edge = BackEdge.Right,
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
            shouldCommitEdgeBack(
                edge = BackEdge.Right,
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
            shouldCommitEdgeBack(
                edge = BackEdge.Right,
                dragOffsetPx = -40f,
                widthPx = 1_000f,
                velocityXPx = -200f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }

    @Test
    fun sufficientRightDragFromLeftEdgeCommitsBack() {
        assertTrue(
            shouldCommitEdgeBack(
                edge = BackEdge.Left,
                dragOffsetPx = 240f,
                widthPx = 1_000f,
                velocityXPx = 0f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }

    @Test
    fun fastRightFlingFromLeftEdgeCommitsEvenWhenDistanceIsShort() {
        assertTrue(
            shouldCommitEdgeBack(
                edge = BackEdge.Left,
                dragOffsetPx = 50f,
                widthPx = 1_000f,
                velocityXPx = 1_200f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }

    @Test
    fun shortSlowDragFromLeftEdgeSpringsBack() {
        assertFalse(
            shouldCommitEdgeBack(
                edge = BackEdge.Left,
                dragOffsetPx = 40f,
                widthPx = 1_000f,
                velocityXPx = 200f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }

    @Test
    fun dragAgainstEdgeDirectionNeverCommits() {
        assertFalse(
            shouldCommitEdgeBack(
                edge = BackEdge.Left,
                dragOffsetPx = -400f,
                widthPx = 1_000f,
                velocityXPx = -1_500f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
        assertFalse(
            shouldCommitEdgeBack(
                edge = BackEdge.Right,
                dragOffsetPx = 400f,
                widthPx = 1_000f,
                velocityXPx = 1_500f,
                minimumDistancePx = 64f,
                minimumVelocityXPx = 900f,
            ),
        )
    }
}
