package com.ketoanapk.hr.data

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RealtimeReleaseScopeTest {
    @Test
    fun releaseScopeIsForwardedToForegroundUpdateFlow() {
        assertTrue(shouldForwardRealtimeRefreshScope("release"))
        assertTrue(shouldForwardRealtimeRefreshScope("all"))
    }

    @Test
    fun unknownScopeIsIgnored() {
        assertFalse(shouldForwardRealtimeRefreshScope("untrusted-scope"))
    }
}
