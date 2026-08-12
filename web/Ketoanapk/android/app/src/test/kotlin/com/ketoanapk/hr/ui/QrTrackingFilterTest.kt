package com.ketoanapk.hr.ui

import kotlin.math.abs
import kotlin.math.hypot
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class QrTrackingFilterTest {
    @Test
    fun trackingFill_isVisibleTranslucentBlue() {
        assertEquals(0x30, QR_TRACK_FILL_ARGB ushr 24 and 0xff)
        assertEquals(0x4d, QR_TRACK_FILL_ARGB ushr 16 and 0xff)
        assertEquals(0x9e, QR_TRACK_FILL_ARGB ushr 8 and 0xff)
        assertEquals(0xff, QR_TRACK_FILL_ARGB and 0xff)
    }

    @Test
    fun alternatingPerspectiveTilt_isSmootherThanRawDetectorPoints() {
        val filter = QrTrackingFilter()
        val leftTilt = quad(
            96f to 104f,
            304f to 96f,
            298f to 304f,
            102f to 296f,
        )
        val rightTilt = quad(
            104f to 96f,
            296f to 104f,
            302f to 296f,
            98f to 304f,
        )

        var previousRaw = leftTilt
        var previousFiltered = filter.update(leftTilt, nowMs = 0L)
        var rawTravel = 0f
        var filteredTravel = 0f

        repeat(20) { index ->
            // Simulate the detector alternating between two shapes while the phone is tilted.
            val measured = if (index % 2 == 0) rightTilt else leftTilt
            val filtered = filter.update(measured, nowMs = (index + 1L) * 16L)
            rawTravel += maxCornerDistance(previousRaw, measured)
            filteredTravel += maxCornerDistance(previousFiltered, filtered)
            previousRaw = measured
            previousFiltered = filtered
        }

        assertTrue(
            "Filtered corners should travel much less than raw detector points",
            filteredTravel < rawTravel * 0.65f,
        )
        assertTrue(
            "Tilting around the same QR code must not make the frame center drift",
            distance(previousFiltered.center(), TrackPoint(200f, 200f)) < 3f,
        )
    }

    @Test
    fun alternatingApparentSize_dampsBreathingWithoutMovingTheCenter() {
        val filter = QrTrackingFilter()
        val small = square(centerX = 200f, centerY = 200f, edge = 180f)
        val large = square(centerX = 200f, centerY = 200f, edge = 220f)

        var previousRaw = small
        var previousFiltered = filter.update(small, nowMs = 0L)
        var rawSizeTravel = 0f
        var filteredSizeTravel = 0f

        repeat(20) { index ->
            val measured = if (index % 2 == 0) large else small
            val filtered = filter.update(measured, nowMs = (index + 1L) * 16L)
            rawSizeTravel += abs(characteristicSize(measured) - characteristicSize(previousRaw))
            filteredSizeTravel += abs(characteristicSize(filtered) - characteristicSize(previousFiltered))
            previousRaw = measured
            previousFiltered = filtered
        }

        assertTrue(
            "The filtered frame should suppress detector size breathing",
            filteredSizeTravel < rawSizeTravel * 0.55f,
        )
        assertTrue(
            "Size-only noise must not move the frame center",
            distance(previousFiltered.center(), TrackPoint(200f, 200f)) < 0.1f,
        )
    }

    @Test
    fun repeatedLargeTranslation_isRecognisedQuicklyInsteadOfLaggingBehind() {
        val filter = QrTrackingFilter()
        val initial = square(centerX = 200f, centerY = 200f, edge = 200f)
        val moved = square(centerX = 280f, centerY = 200f, edge = 200f)

        filter.update(initial, nowMs = 0L)
        filter.update(initial, nowMs = 16L)
        val firstMovedFrame = filter.update(moved, nowMs = 32L)
        val confirmedMovedFrame = filter.update(moved, nowMs = 48L)

        val firstDistance = firstMovedFrame.center().x - initial.center().x
        val confirmedDistance = confirmedMovedFrame.center().x - initial.center().x
        assertTrue("The frame should react on the first real movement frame", firstDistance > 20f)
        assertTrue("Two frames should cover most of the real movement", confirmedDistance > 48f)
    }

    @Test
    fun oneFrameOutlier_doesNotTeleportTheTrackingFrame() {
        val filter = QrTrackingFilter()
        val stable = square(centerX = 200f, centerY = 200f, edge = 200f)
        val falseDetection = square(centerX = 720f, centerY = 80f, edge = 160f)

        filter.update(stable, nowMs = 0L)
        filter.update(stable, nowMs = 16L)
        val beforeOutlier = filter.update(stable, nowMs = 32L)
        val afterOutlier = filter.update(falseDetection, nowMs = 48L)
        val recovered = filter.update(stable, nowMs = 64L)

        assertTrue(
            "One false detection must not pull the frame away from the QR code",
            distance(beforeOutlier.center(), afterOutlier.center()) < 80f,
        )
        assertTrue(
            "The frame should settle as soon as the detector sees the real code again",
            distance(stable.center(), recovered.center()) < 40f,
        )
    }

    @Test
    fun detectionLoss_holdsBrieflyThenReleasesTheFrame() {
        val filter = QrTrackingFilter()
        filter.update(square(centerX = 200f, centerY = 200f, edge = 200f), nowMs = 1_000L)

        assertEquals(1f, filter.opacity(nowMs = 1_000L + QrTrackingFilter.HOLD_MS), 0.001f)
        val fadeMidpoint = 1_000L + (QrTrackingFilter.HOLD_MS + QrTrackingFilter.RELEASE_MS) / 2L
        assertTrue(filter.opacity(fadeMidpoint) in 0.45f..0.55f)
        assertNotNull("A few missing frames must not make the overlay blink", filter.current(nowMs = 1_519L))
        assertNull("The overlay must release after the fade window", filter.current(nowMs = 1_851L))
        assertEquals(0f, filter.opacity(nowMs = 1_851L), 0.001f)
    }

    @Test
    fun reset_forgetsBothGeometryAndHoldState() {
        val filter = QrTrackingFilter()
        filter.update(square(centerX = 200f, centerY = 200f, edge = 200f), nowMs = 1_000L)

        filter.reset()

        assertNull(filter.current(nowMs = 1_001L))
    }

    private fun square(centerX: Float, centerY: Float, edge: Float): TrackQuad {
        val radius = edge / 2f
        return quad(
            (centerX - radius) to (centerY - radius),
            (centerX + radius) to (centerY - radius),
            (centerX + radius) to (centerY + radius),
            (centerX - radius) to (centerY + radius),
        )
    }

    private fun quad(
        topLeft: Pair<Float, Float>,
        topRight: Pair<Float, Float>,
        bottomRight: Pair<Float, Float>,
        bottomLeft: Pair<Float, Float>,
    ) = TrackQuad(
        topLeft = TrackPoint(topLeft.first, topLeft.second),
        topRight = TrackPoint(topRight.first, topRight.second),
        bottomRight = TrackPoint(bottomRight.first, bottomRight.second),
        bottomLeft = TrackPoint(bottomLeft.first, bottomLeft.second),
    )

    private fun TrackQuad.center() = TrackPoint(
        x = (topLeft.x + topRight.x + bottomRight.x + bottomLeft.x) / 4f,
        y = (topLeft.y + topRight.y + bottomRight.y + bottomLeft.y) / 4f,
    )

    private fun maxCornerDistance(first: TrackQuad, second: TrackQuad) = maxOf(
        distance(first.topLeft, second.topLeft),
        distance(first.topRight, second.topRight),
        distance(first.bottomRight, second.bottomRight),
        distance(first.bottomLeft, second.bottomLeft),
    )

    private fun characteristicSize(quad: TrackQuad) = (
        distance(quad.topLeft, quad.topRight) +
            distance(quad.topRight, quad.bottomRight) +
            distance(quad.bottomRight, quad.bottomLeft) +
            distance(quad.bottomLeft, quad.topLeft)
        ) / 4f

    private fun distance(first: TrackPoint, second: TrackPoint) =
        hypot(first.x - second.x, first.y - second.y)

    private fun assertQuadEquals(expected: TrackQuad, actual: TrackQuad) {
        assertPointEquals(expected.topLeft, actual.topLeft)
        assertPointEquals(expected.topRight, actual.topRight)
        assertPointEquals(expected.bottomRight, actual.bottomRight)
        assertPointEquals(expected.bottomLeft, actual.bottomLeft)
    }

    private fun assertPointEquals(expected: TrackPoint, actual: TrackPoint) {
        assertEquals(expected.x, actual.x, 0.01f)
        assertEquals(expected.y, actual.y, 0.01f)
    }
}
