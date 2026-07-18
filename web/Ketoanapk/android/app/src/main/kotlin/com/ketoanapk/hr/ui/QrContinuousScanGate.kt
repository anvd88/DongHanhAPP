package com.ketoanapk.hr.ui

/**
 * De-duplicates the repeated successful decode frames emitted by continuous ML Kit scanning.
 *
 * A different QR is accepted immediately. The same QR is accepted again only after it has not
 * been decoded for [visibleHoldMs], which covers the natural remove-and-present-again gesture.
 */
internal class QrContinuousScanGate(
    private val visibleHoldMs: Long = DEFAULT_VISIBLE_HOLD_MS,
) {
    private val lastSeenAt = LinkedHashMap<String, Long>()

    fun shouldAccept(value: String, nowMs: Long): Boolean {
        val previous = lastSeenAt.put(value, nowMs)
        prune(nowMs)
        return previous == null || nowMs - previous > visibleHoldMs
    }

    private fun prune(nowMs: Long) {
        val iterator = lastSeenAt.entries.iterator()
        while (iterator.hasNext()) {
            val entry = iterator.next()
            if (nowMs - entry.value > visibleHoldMs) iterator.remove()
        }
    }

    private companion object {
        const val DEFAULT_VISIBLE_HOLD_MS = 900L
    }
}

/** Owns the overlay for exactly one QR, independent from other decoded values waiting in the queue. */
internal class QrOverlaySelection {
    private var activeValue: String? = null

    /** Returns true only when the overlay has switched to another QR. */
    fun activate(value: String): Boolean {
        val changed = activeValue != value
        activeValue = value
        return changed
    }

    fun owns(value: String): Boolean = activeValue == value

    fun clear() {
        activeValue = null
    }
}
