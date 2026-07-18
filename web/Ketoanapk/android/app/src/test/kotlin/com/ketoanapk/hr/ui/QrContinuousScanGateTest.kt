package com.ketoanapk.hr.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class QrContinuousScanGateTest {
    @Test
    fun oneHundredDifferentQrCodes_areAcceptedInOneContinuousSession() {
        val gate = QrContinuousScanGate()

        repeat(100) { index ->
            assertTrue(gate.shouldAccept("qr-$index", nowMs = index * 20L))
        }
    }

    @Test
    fun repeatedFramesOfTheSameVisibleQr_areIgnored() {
        val gate = QrContinuousScanGate(visibleHoldMs = 900L)

        assertTrue(gate.shouldAccept("same-qr", nowMs = 0L))
        assertFalse(gate.shouldAccept("same-qr", nowMs = 200L))
        assertFalse(gate.shouldAccept("same-qr", nowMs = 800L))
    }

    @Test
    fun sameQr_canBeReadAgainAfterItLeavesTheCamera() {
        val gate = QrContinuousScanGate(visibleHoldMs = 900L)

        assertTrue(gate.shouldAccept("same-qr", nowMs = 0L))
        assertTrue(gate.shouldAccept("same-qr", nowMs = 1_000L))
    }

    @Test
    fun overlay_ignoresOtherVisibleQrUntilThatQueuedQrBecomesActive() {
        val selection = QrOverlaySelection()

        assertTrue(selection.activate("qr-a"))
        assertTrue(selection.owns("qr-a"))
        assertFalse(selection.owns("qr-b"))

        // qr-b has been decoded and queued, but qr-a is still the content being processed/displayed.
        assertTrue(selection.owns("qr-a"))
        assertFalse(selection.owns("qr-b"))

        assertTrue(selection.activate("qr-b"))
        assertFalse(selection.owns("qr-a"))
        assertTrue(selection.owns("qr-b"))
        assertFalse(selection.activate("qr-b"))

        selection.clear()
        assertFalse(selection.owns("qr-b"))
    }
}
