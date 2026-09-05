package com.ketoanapk.hr.data

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.StringReader

class SseRealtimeClientTest {
    @Test
    fun parserHandlesCommentsIdsEventTypesAndMultilineData() = runBlocking {
        val frames = mutableListOf<SseFrame>()
        val wire = """
            retry: 3000

            : heartbeat

            id: 41
            event: invalidated
            data: {"scope":"hr",
            data: "reason":"changed"}

            id: 42
            event: session.revoked
            data: {"scope":"access"}

        """.trimIndent() + "\n"

        parseSseFrames(StringReader(wire).buffered(), onFrame = frames::add)

        assertEquals(2, frames.size)
        assertEquals(SseFrame(41, "invalidated", "{\"scope\":\"hr\",\n\"reason\":\"changed\"}"), frames[0])
        assertEquals(SseFrame(42, "session.revoked", "{\"scope\":\"access\"}"), frames[1])
    }

    @Test
    fun reconnectBackoffIsExponentialCappedAndJitterBounded() {
        assertEquals(1_000L, sseReconnectDelayMs(0, 0.0))
        assertEquals(8_000L, sseReconnectDelayMs(3, 0.0))
        assertEquals(30_000L, sseReconnectDelayMs(20, 0.0))
        assertTrue(sseReconnectDelayMs(20, 1.0) <= 40_000L)
    }

    @Test
    fun freshDeviceAsksForTheBootstrapInsteadOfReplayingTheWholeRetentionWindow() {
        assertEquals("https://x/api/realtime/stream", sseStreamUrl("https://x/", 0))
        assertEquals("https://x/api/realtime/stream", sseStreamUrl("https://x", -1))
        assertEquals("https://x/api/realtime/stream?after=77", sseStreamUrl("https://x/", 77))
    }

    @Test
    fun parserStopsBeforeDispatchWhenConnectionEpochChanges() = runBlocking {
        var active = true
        val frames = mutableListOf<SseFrame>()
        parseSseFrames("id: 1\ndata: {}\n\n".reader().buffered(), { active }) {
            frames += it
            active = false
        }
        assertEquals(1, frames.size)
    }
}
