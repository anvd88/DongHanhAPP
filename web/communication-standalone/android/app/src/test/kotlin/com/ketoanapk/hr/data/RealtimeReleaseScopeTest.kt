package com.ketoanapk.hr.data

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RealtimeReleaseScopeTest {
    @Test
    fun signalRForwardsOnlyCommunicationScope() {
        assertTrue(shouldForwardRealtimeRefreshScope("chat"))
        assertFalse(shouldForwardRealtimeRefreshScope("release"))
        assertFalse(shouldForwardRealtimeRefreshScope("all"))
    }

    @Test
    fun unknownScopeIsIgnored() {
        assertFalse(shouldForwardRealtimeRefreshScope("untrusted-scope"))
    }
}
